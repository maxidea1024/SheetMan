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
        var serials = table.SerialFields;

        // Every column is encoded into its own buffer before a byte of the file
        // exists, because the descriptor states each block's encoding and length up
        // front - and which encoding wins is only known once the candidates have
        // actually been written out and measured.
        var blocks = new ColumnBlock[serials.Count];

        for (int at = 0; at < serials.Count; at++)
            blocks[at] = EncodeColumn(table, serials[at]);

        writer.Write(ScbFormat.Version);
        writer.Write((byte)0);                      // flags: no compression, no encryption
        writer.WriteCounter32(table.Data.Count);

        // The descriptors: one per logical column, so the file says what it holds. A
        // reader matches columns by tag rather than position, skips a tag it does not
        // know by the block's byte length, and refuses a wire it cannot read by name -
        // which between them is the whole of what makes schema changes survivable.
        writer.WriteCounter32(serials.Count);

        for (int at = 0; at < serials.Count; at++)
        {
            var sf = serials[at];

            writer.WriteCounter32(sf.FirstField.Tag.Value);
            writer.Write(ScbFormat.Wire(ScbFormat.ElementFor(sf), ScbFormat.KindFor(sf)));
            writer.Write(blocks[at].Encoding);
            writer.WriteCounter32(ScbFormat.CountFor(sf));
            writer.Write((uint)blocks[at].Payload.Length);
        }

        // Column-oriented: each column's rows are one contiguous block. That is what
        // lets an unknown column be skipped in a single advance, with no per-type skip
        // logic for thirteen readers to each get subtly wrong.
        for (int at = 0; at < serials.Count; at++)
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
    private ColumnBlock EncodeColumn(Table table, SerialField sf)
    {
        var raw = new ScbWriter();

        foreach (var row in table.Data)
        {
            if (sf.IsVariableLengthArray)
            {
                ExportArrayValue(raw, row[sf.FirstField.Index].Value, sf.FirstField);
                continue;
            }

            foreach (var field in sf.Fields)
                ExportValue(raw, row[field.Index].Value, field);
        }

        var best = new ColumnBlock(ScbFormat.EncodingRaw, raw);

        // Only a scalar column repeats itself in a way the encodings model; arrays
        // stay raw, as do the elements whose blocks are all but noise (i64, floats,
        // bool, uuid - together under two percent of a real dataset's bytes).
        if (ScbFormat.KindFor(sf) != ScbFormat.KindScalar)
            return best;

        switch (ScbFormat.ElementFor(sf))
        {
            case ScbFormat.ElementI32:
            {
                int[] values = CollectInt32Column(table, sf);

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
                best = Smaller(best, ScbFormat.EncodingRle, EncodeRle(CollectInt32Column(table, sf)));
                break;
            }

            case ScbFormat.ElementString:
            {
                string[] values = CollectStringColumn(table, sf);

                best = Smaller(best, ScbFormat.EncodingDict, EncodeDict(values));
                best = Smaller(best, ScbFormat.EncodingDictRle, EncodeDictRle(values));
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
    private static int[] CollectInt32Column(Table table, SerialField sf)
    {
        int index = sf.FirstField.Index;
        var values = new int[table.Data.Count];

        for (int at = 0; at < values.Length; at++)
            values[at] = (int)table.Data[at][index].Value;

        return values;
    }

    private static string[] CollectStringColumn(Table table, SerialField sf)
    {
        int index = sf.FirstField.Index;
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
    private void ExportArrayValue(ScbWriter writer, object value, Field field)
    {
        var elements = (System.Array)value;
        int length = elements?.Length ?? 0;

        writer.WriteCounter32(length);

        for (int i = 0; i < length; i++)
            ExportValue(writer, elements.GetValue(i), field);
    }

    private void ExportValue(ScbWriter writer, object value, Field field)
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
