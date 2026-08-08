using CommandLine;
using SheetMan.Recipe;
using SheetMan.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SheetMan.Helpers;
using System.Collections.Generic;
using SheetMan.Targets;

namespace SheetMan.Exporters;

[SheetManTarget("binary", TargetKind.Export, Section = "Exports.Binary", Order = 10)]
public class BinaryExporter : Target<RecipeModel.ExportRecipeGroup.BinaryRecipe>
{
    private Manifest _manifest;

    /// <summary>
    /// A record group is already expressible: it is one fixed-array column per member.
    /// </summary>
    /// <remarks>
    /// Nothing was added to the format for it. Because the file is column oriented, an
    /// array of records is a struct of arrays - and `KindFixedArray` with the member's
    /// element type is exactly that. So no new kind, no version bump, and the column
    /// encodings keep applying per member, which storing a record as one blob would have
    /// defeated. spec/nested-fields.md has the layout.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    protected override void Run(TargetContext context, RecipeModel.ExportRecipeGroup.BinaryRecipe binaryRecipe)
    {
        // An entry left in the recipe with a blank path is treated as switched off.
        if (string.IsNullOrEmpty(binaryRecipe.Path))
            return;

        // Before anything is written: a schema change that would break a reader already
        // out there stops the run here, with nothing exported and the reason named.
        if (!string.IsNullOrEmpty(binaryRecipe.SchemaBaseline))
        {
            SchemaBaseline.Check(
                binaryRecipe.SchemaBaseline, context.Model, binaryRecipe.AcceptSchemaChanges);
        }

        string manifestFilename = Path.Combine(binaryRecipe.Path, "manifest-binary.json");

        _manifest = Manifest.Load(manifestFilename);

        // Before anything is written, while the ledger is still the previous run's: a
        // table renamed or removed leaves its file behind otherwise, and a stale .scb is
        // worse than a stale source file - it ships, and a build still asking for the old
        // name reads it.
        if (binaryRecipe.Sweep)
            _manifest.PruneStaleFiles(binaryRecipe.Path);

        // context.Model is already narrowed to this entry's target side.
        foreach (var table in context.Model.Tables)
            ExportTable(binaryRecipe, table);

        _manifest.BuildAndWriteToFile(manifestFilename);
    }

    private void ExportTable(RecipeModel.ExportRecipeGroup.BinaryRecipe recipe, Table table)
    {
        ScbWriter writer = new ScbWriter();

        // Wire columns, not serial fields. They are the same list for every table written
        // before records existed, and differ for a record group: it stores one column per
        // member, so the file is a struct of arrays where the API is an array of structs.
        var columns = table.WireColumns;

        // Every column is encoded into its own buffer before a byte of the file
        // exists, because the descriptor states each block's encoding and length up
        // front - and which encoding wins is only known once the candidates have
        // actually been written out and measured.
        var blocks = new ColumnBlock[columns.Count];

        for (int at = 0; at < columns.Count; at++)
            blocks[at] = EncodeColumn(table, columns[at]);

        writer.Write(ScbFormat.Version);
        writer.Write((byte)0);                      // flags: no compression, no encryption
        writer.WriteCounter32(table.Data.Count);

        // The descriptors: one per logical column, so the file says what it holds. A
        // reader matches columns by tag rather than position, skips a tag it does not
        // know by the block's byte length, and refuses a wire it cannot read by name -
        // which between them is the whole of what makes schema changes survivable.
        writer.WriteCounter32(columns.Count);

        for (int at = 0; at < columns.Count; at++)
        {
            var column = columns[at];

            writer.WriteCounter32(column.TagCarrier.Tag.Value);
            writer.Write(ScbFormat.Wire(ScbFormat.ElementFor(column), ScbFormat.KindFor(column)));
            writer.Write(blocks[at].Encoding);
            writer.WriteCounter32(ScbFormat.CountFor(column));
            writer.Write((uint)blocks[at].Payload.Length);
        }

        // Column-oriented: each column's rows are one contiguous block. That is what
        // lets an unknown column be skipped in a single advance, with no per-type skip
        // logic for thirteen readers to each get subtly wrong.
        for (int at = 0; at < columns.Count; at++)
            writer.Write(blocks[at].Payload.WrittenSpan);

        var filename = Path.Combine(recipe.Path, table.Name + recipe.FileExtension);
        filename = Path.GetFullPath(filename);

        // A view over the writer's buffer, not a copy: this is the biggest allocation
        // in the export and there is no reason to make it twice.
        Log.Information($"Exporting binary file '{filename}' ({writer.Length} bytes)");
        string stagingFilename = StagingFiles.WriteAllBytesToFile(filename, writer.WrittenSpan);

        _manifest.Add(table.Name + recipe.FileExtension, stagingFilename);
    }

    /// <summary>
    /// One column's data block: the buffer its values were encoded into, and which
    /// encoding that buffer uses - what the descriptor states and the file carries.
    /// </summary>
    private readonly struct ColumnBlock
    {
        public ColumnBlock(byte encoding, ScbWriter payload)
        {
            Encoding = encoding;
            Payload = payload;
        }

        public byte Encoding { get; }
        public ScbWriter Payload { get; }
    }

    /// <summary>
    /// Encodes one column into its own buffer and says which encoding it chose.
    ///
    /// No statistics, no heuristics: every applicable candidate is written out in
    /// full and the smallest kept, ties going to the lowest encoding number. Encode
    /// time is the one resource this format's design does not care about, and a
    /// measured byte count is the one selector that is never wrong. The candidates
    /// and their layouts are spec/scb-v102-column-encoding.md.
    /// </summary>
    private static ColumnBlock EncodeColumn(Table table, WireColumn column)
    {
        var raw = new ScbWriter();

        foreach (var row in table.Data)
        {
            if (column.IsVariableLengthArray)
            {
                ExportArrayValue(raw, row[column.TagCarrier.Index].Value, column.TagCarrier);
                continue;
            }

            foreach (var field in column.Cells)
                ExportValue(raw, row[field.Index].Value, field);
        }

        var best = new ColumnBlock(ScbFormat.EncodingRaw, raw);

        // Only a scalar column repeats itself in a way the encodings model. An array's
        // rows differ in length as well as value, and encoding that would put a second
        // dimension into every candidate for the 1.8 percent of bytes it holds.
        if (ScbFormat.KindFor(column) != ScbFormat.KindScalar)
            return best;

        switch (ScbFormat.ElementFor(column))
        {
            case ScbFormat.ElementI32:
            {
                int[] values = CollectInt32Column(table, column);

                best = Smaller(best, ScbFormat.EncodingVarint, EncodeVarint(values));
                best = Smaller(best, ScbFormat.EncodingDelta, EncodeDelta(values));
                best = Smaller(best, ScbFormat.EncodingRle, EncodeRle(values));
                best = Smaller(best, ScbFormat.EncodingDeltaRle, EncodeDeltaRle(values));
                break;
            }

            case ScbFormat.ElementVarint:
            {
                // Raw already is a varint stream, so of the integer candidates only
                // run-length encoding can say anything raw does not.
                best = Smaller(best, ScbFormat.EncodingRle, EncodeRle(CollectInt32Column(table, column)));
                break;
            }

            case ScbFormat.ElementBool:
            {
                var values = new int[table.Data.Count];
                int index = column.TagCarrier.Index;

                for (int at = 0; at < values.Length; at++)
                    values[at] = (bool)table.Data[at][index].Value ? 1 : 0;

                best = Smaller(best, ScbFormat.EncodingRle, EncodeRle(values));
                break;
            }

            case ScbFormat.ElementString:
            {
                string[] values = CollectStringColumn(table, column);

                best = Smaller(best, ScbFormat.EncodingDict, EncodeDict(values));
                best = Smaller(best, ScbFormat.EncodingDictRle, EncodeDictRle(values));
                best = Smaller(best, ScbFormat.EncodingDictFront, EncodeDictFront(values, false));
                best = Smaller(best, ScbFormat.EncodingDictFrontRle, EncodeDictFront(values, true));
                break;
            }

            // The dictionary is parameterized by element, so a column of floats or ticks
            // reaches it with nothing added to the format: the entries are simply four or
            // eight bytes instead of a length and some UTF-8. Worth reaching, because a
            // float column in design data is a handful of values repeated - the measured
            // set had 18,718 floats among 1,065 distinct ones.
            case ScbFormat.ElementF32:
            {
                var values = CollectRawColumn(table, column, 4);

                best = Smaller(best, ScbFormat.EncodingDict, EncodeValueDict(values, false));
                best = Smaller(best, ScbFormat.EncodingDictRle, EncodeValueDict(values, true));
                break;
            }

            case ScbFormat.ElementI64:
            case ScbFormat.ElementF64:
            {
                var values = CollectRawColumn(table, column, 8);

                best = Smaller(best, ScbFormat.EncodingDict, EncodeValueDict(values, false));
                best = Smaller(best, ScbFormat.EncodingDictRle, EncodeValueDict(values, true));
                break;
            }
        }

        return best;
    }

    /// <summary>
    /// The incumbent, unless the challenger is strictly smaller. Candidates arrive in
    /// ascending encoding order, so a tie keeps the lower number - which keeps the
    /// choice deterministic and the golden trees still.
    /// </summary>
    private static ColumnBlock Smaller(ColumnBlock incumbent, byte encoding, ScbWriter challenger)
        => challenger.Length < incumbent.Payload.Length
            ? new ColumnBlock(encoding, challenger)
            : incumbent;

    // ------------------------------------------------- column value collection

    /// <summary>A scalar int32 column's values: ints, enums and reference indexes alike.</summary>
    private static int[] CollectInt32Column(Table table, WireColumn column)
    {
        int index = column.TagCarrier.Index;
        var values = new int[table.Data.Count];

        for (int at = 0; at < values.Length; at++)
            values[at] = (int)table.Data[at][index].Value;

        return values;
    }

    /// <summary>
    /// A fixed-width scalar column's values as the raw bytes they were written as.
    ///
    /// Sliced back out of the raw block rather than re-encoded from the model, so a
    /// dictionary entry is by construction the same bytes the raw layout would have
    /// written - a float's exact bit pattern, ticks, either of them - and no second
    /// encoding path exists to disagree with the first.
    /// </summary>
    private static byte[][] CollectRawColumn(Table table, WireColumn column, int width)
    {
        var scratch = new ScbWriter();
        var field = column.TagCarrier;

        foreach (var row in table.Data)
            ExportValue(scratch, row[field.Index].Value, field);

        var span = scratch.WrittenSpan;
        var values = new byte[table.Data.Count][];

        for (int at = 0; at < values.Length; at++)
            values[at] = span.Slice(at * width, width).ToArray();

        return values;
    }

    private static string[] CollectStringColumn(Table table, WireColumn column)
    {
        int index = column.TagCarrier.Index;
        var values = new string[table.Data.Count];

        for (int at = 0; at < values.Length; at++)
            values[at] = (string)table.Data[at][index].Value ?? string.Empty;

        return values;
    }

    // -------------------------------------------------------------- encoders

    private static ScbWriter EncodeVarint(int[] values)
    {
        var payload = new ScbWriter();

        foreach (int value in values)
            payload.WriteOptimalInt32(value);

        return payload;
    }

    /// <summary>
    /// The first value, then each step from its predecessor.
    ///
    /// The subtraction wraps on purpose: two int32s can be further apart than an int32
    /// holds, and two's-complement wrapping makes the round trip exact for every pair
    /// anyway. Readers add the delta back with the same wrapping.
    /// </summary>
    private static ScbWriter EncodeDelta(int[] values)
    {
        var payload = new ScbWriter();

        if (values.Length == 0)
            return payload;

        payload.WriteOptimalInt32(values[0]);

        for (int at = 1; at < values.Length; at++)
            payload.WriteOptimalInt32(unchecked(values[at] - values[at - 1]));

        return payload;
    }

    /// <summary>(run length, value) pairs whose run lengths sum to the row count.</summary>
    private static ScbWriter EncodeRle(int[] values)
    {
        var payload = new ScbWriter();

        for (int at = 0; at < values.Length;)
        {
            int run = 1;
            while (at + run < values.Length && values[at + run] == values[at])
                run++;

            payload.WriteCounter32(run);
            payload.WriteOptimalInt32(values[at]);

            at += run;
        }

        return payload;
    }

    /// <summary>
    /// The first value, then the delta stream of <see cref="EncodeDelta"/> run-length
    /// encoded - which is what flattens an id column stepping by one into a few bytes.
    /// </summary>
    private static ScbWriter EncodeDeltaRle(int[] values)
    {
        var payload = new ScbWriter();

        if (values.Length == 0)
            return payload;

        payload.WriteOptimalInt32(values[0]);

        var deltas = new int[values.Length - 1];
        for (int at = 0; at < deltas.Length; at++)
            deltas[at] = unchecked(values[at + 1] - values[at]);

        for (int at = 0; at < deltas.Length;)
        {
            int run = 1;
            while (at + run < deltas.Length && deltas[at + run] == deltas[at])
                run++;

            payload.WriteCounter32(run);
            payload.WriteOptimalInt32(deltas[at]);

            at += run;
        }

        return payload;
    }

    /// <summary>
    /// The distinct strings once, in first-appearance order, then an index per row.
    ///
    /// First-appearance order rather than sorted: one pass builds it, and the output
    /// stays deterministic without saying anything about collation.
    /// </summary>
    private static ScbWriter EncodeDict(string[] values)
    {
        var payload = new ScbWriter();
        int[] indexes = BuildDictionary(values, payload);

        foreach (int index in indexes)
            payload.WriteCounter32(index);

        return payload;
    }

    /// <summary>The dictionary of <see cref="EncodeDict"/>, with the index stream run-length encoded.</summary>
    private static ScbWriter EncodeDictRle(string[] values)
    {
        var payload = new ScbWriter();
        int[] indexes = BuildDictionary(values, payload);

        for (int at = 0; at < indexes.Length;)
        {
            int run = 1;
            while (at + run < indexes.Length && indexes[at + run] == indexes[at])
                run++;

            payload.WriteCounter32(run);
            payload.WriteCounter32(indexes[at]);

            at += run;
        }

        return payload;
    }

    /// <summary>
    /// A dictionary of fixed-width values, indexes plain or run-length encoded.
    ///
    /// The same shape as the string dictionary; only an entry's bytes differ, which is
    /// the whole of what "parameterized by element" means here.
    /// </summary>
    private static ScbWriter EncodeValueDict(byte[][] values, bool runLength)
    {
        var payload = new ScbWriter();

        var seen = new Dictionary<string, int>();
        var entries = new List<byte[]>();
        var indexes = new int[values.Length];

        for (int at = 0; at < values.Length; at++)
        {
            // Keyed by the bytes themselves, so two values are the same entry exactly
            // when they were written the same - which for a float is what equality has
            // to mean here, NaN and negative zero included.
            string key = Convert.ToBase64String(values[at]);

            if (!seen.TryGetValue(key, out int index))
            {
                index = entries.Count;
                seen.Add(key, index);
                entries.Add(values[at]);
            }

            indexes[at] = index;
        }

        payload.WriteCounter32(entries.Count);

        foreach (var entry in entries)
            payload.Write(entry);

        WriteIndexes(payload, indexes, runLength);

        return payload;
    }

    /// <summary>
    /// A sorted string dictionary, each entry stating only what it does not share with
    /// the entry before it.
    /// </summary>
    /// <remarks>
    /// Sorted by UTF-8 bytes rather than by anything a locale has an opinion about, so
    /// every language's writer would produce the same order from the same values.
    /// </remarks>
    private static ScbWriter EncodeDictFront(string[] values, bool runLength)
    {
        var payload = new ScbWriter();

        var encoded = new Dictionary<string, byte[]>();

        foreach (string value in values)
        {
            if (!encoded.ContainsKey(value))
                encoded.Add(value, Encoding.UTF8.GetBytes(value));
        }

        var entries = new List<byte[]>(encoded.Values);
        entries.Sort(CompareBytes);

        var order = new Dictionary<string, int>(encoded.Count);
        var position = new Dictionary<string, int>(entries.Count);

        for (int at = 0; at < entries.Count; at++)
            position[Convert.ToBase64String(entries[at])] = at;

        foreach (var pair in encoded)
            order[pair.Key] = position[Convert.ToBase64String(pair.Value)];

        var indexes = new int[values.Length];
        for (int at = 0; at < values.Length; at++)
            indexes[at] = order[values[at]];

        payload.WriteCounter32(entries.Count);

        var previous = Array.Empty<byte>();

        foreach (var entry in entries)
        {
            int shared = 0;
            int limit = Math.Min(previous.Length, entry.Length);

            while (shared < limit && previous[shared] == entry[shared])
                shared++;

            payload.WriteCounter32(shared);
            payload.WriteCounter32(entry.Length - shared);
            payload.Write(entry.AsSpan(shared));

            previous = entry;
        }

        WriteIndexes(payload, indexes, runLength);

        return payload;
    }

    /// <summary>
    /// Orders two entries by their bytes.
    /// </summary>
    /// <remarks>
    /// By the bytes and not by the string, because C#'s ordinal comparison orders UTF-16
    /// code units: a surrogate pair sorts below U+E000 there and above it in UTF-8. The
    /// spec says the order is the bytes', so that is what this compares - and every other
    /// language's writer reaches the same order without being told about UTF-16 at all.
    /// </remarks>
    private static int CompareBytes(byte[] left, byte[] right)
    {
        int limit = Math.Min(left.Length, right.Length);

        for (int at = 0; at < limit; at++)
        {
            if (left[at] != right[at])
                return left[at] < right[at] ? -1 : 1;
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>An index stream, plainly or as runs, shared by every dictionary encoding.</summary>
    private static void WriteIndexes(ScbWriter payload, int[] indexes, bool runLength)
    {
        if (!runLength)
        {
            foreach (int index in indexes)
                payload.WriteCounter32(index);

            return;
        }

        for (int at = 0; at < indexes.Length;)
        {
            int run = 1;
            while (at + run < indexes.Length && indexes[at + run] == indexes[at])
                run++;

            payload.WriteCounter32(run);
            payload.WriteCounter32(indexes[at]);

            at += run;
        }
    }

    /// <summary>
    /// Writes the dictionary block - entry count, then the entries - and hands back
    /// each row's index into it.
    /// </summary>
    private static int[] BuildDictionary(string[] values, ScbWriter payload)
    {
        var seen = new Dictionary<string, int>();
        var entries = new List<string>();
        var indexes = new int[values.Length];

        for (int at = 0; at < values.Length; at++)
        {
            if (!seen.TryGetValue(values[at], out int index))
            {
                index = entries.Count;
                seen.Add(values[at], index);
                entries.Add(values[at]);
            }

            indexes[at] = index;
        }

        payload.WriteCounter32(entries.Count);

        foreach (string entry in entries)
            payload.Write(entry);

        return indexes;
    }

    /// <summary>
    /// Writes a delimited array cell: element count first, then the elements.
    /// </summary>
    private static void ExportArrayValue(ScbWriter writer, object value, Field field)
    {
        var elements = (System.Array)value;
        int length = elements?.Length ?? 0;

        writer.WriteCounter32(length);

        for (int i = 0; i < length; i++)
            ExportValue(writer, elements.GetValue(i), field);
    }

    private static void ExportValue(ScbWriter writer, object value, Field field)
    {
        // Element type, so the same switch serves a scalar field and one element
        // of an array field.
        Models.ValueType valueType = field.ElementType;

        // A reference is stored as the target's primary index, which is always an
        // int32: the cooker rejects a table whose index column is any other type, so
        // there is no case here for the index being something else.
        if (field.IsRef)
            valueType = Models.ValueType.Int32;

        switch (valueType)
        {
            case Models.ValueType.String:
                writer.Write((string)value);
                break;
            case Models.ValueType.Bool:
                writer.Write((bool)value);
                break;
            case Models.ValueType.Int32:
                writer.Write((int)value);
                break;
            case Models.ValueType.Int64:
                writer.Write((long)value);
                break;
            case Models.ValueType.Float:
                writer.Write((float)value);
                break;
            case Models.ValueType.Double:
                writer.Write((double)value);
                break;
            case Models.ValueType.DateTime:
                writer.Write((DateTime)value);
                break;
            case Models.ValueType.TimeSpan:
                writer.Write((TimeSpan)value);
                break;
            case Models.ValueType.Uuid:
                writer.Write((Guid)value);
                break;
            case Models.ValueType.Enum:
                writer.WriteOptimalInt32((int)value);
                break;
            case Models.ValueType.ForeignRecord:
                writer.Write((int)value);
                break;
            default:
                throw new SheetManException($"unsupported type  `{valueType}`");
        }
    }
}
