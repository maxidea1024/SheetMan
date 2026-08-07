using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using SheetMan.Models.Raw;
using SheetMan.Helpers;
using Serilog;
using System.Globalization;

namespace SheetMan.Models;

/// <summary>
/// A table declared with a `~~table:Name~~` marker: a field list and its rows.
/// </summary>
public class Table
{
    /// <summary>Cell holding the entity marker that declared this table.</summary>
    [JsonIgnore]
    public required Location Location { get; set; }

    /// <summary>Target side filtering option</summary>
    public TargetSide TargetSide { get; set; }

    /// <summary>Name exactly as written in the sheet.</summary>
    public required string RawName { get; set; }

    /// <summary>Name normalized to Pascal case, which is what generated code uses.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Columns of the table, excluding any commented out with `#`.
    ///
    /// Narrowed by target-side filtering without the rows being touched, so a field's
    /// Index still addresses the right cell of every row.
    /// </summary>
    public List<Field> Fields { get; set; } = new List<Field>();

    /// <summary>
    /// Wire tags reserved by `#`-excluded columns (`#OldColor@4`).
    /// </summary>
    /// <remarks>
    /// A deleted column's tombstone. Its tag must never be handed to another column: a file
    /// written before the deletion still carries data under that tag, and a reader built
    /// after a reuse would read that data as the new column - the silent-wrong-value failure
    /// the tags exist to prevent. AssignTags refuses a duplicate against this list.
    /// </remarks>
    /// <summary>
    /// Whether the columns spell their tags out with `@N` rather than taking them from
    /// their position.
    ///
    /// It is what decides how much a schema change can be trusted: with explicit tags a
    /// column keeps its identity through a rename, a reorder and a deletion, and without
    /// them a deletion shifts every tag after it.
    /// </summary>
    public bool HasExplicitTags { get; set; }

    public List<int> ReservedTags { get; set; } = new List<int>();

    /// <summary>
    /// Rows, each a flat list of cells addressed by <see cref="Field.Index"/>.
    ///
    /// Always holds every column the sheet declared, even where the field list has
    /// been narrowed - which is why readers must go through a field's Index rather
    /// than walking a row positionally.
    /// </summary>
    public List<List<Cell>> Data { get; set; } = new List<List<Cell>>();

    /// <summary>Description from the sheet, emitted as a doc comment.</summary>
    public required string Comment { get; set; }

    /// <summary>
    /// Whether consecutively numbered columns fold into one array-valued entry.
    /// </summary>
    /// <remarks>
    /// On for a table authored in SheetMan's own layout, where `Text1`/`Text2` next to each
    /// other is how an array is written and the columns are therefore expected to agree on
    /// a type.
    ///
    /// Off for a layout adopted from a project that never had the convention. There the
    /// numbers are just part of the names - `Condition_1`, `Condition_2` and `Condition_3`
    /// of one real workbook are three different enums - and folding them is not a nicer API
    /// but a wrong one, which the type check turns into a conversion that refuses to run.
    /// </remarks>
    [JsonIgnore]
    public bool FoldSerialFields { get; set; } = true;

    /// <summary>
    /// The fields as the exporters and generators see them, with consecutively
    /// numbered columns folded into single array-valued entries.
    ///
    /// Computed once and cached, since the folding walks every field pair.
    /// </summary>
    [JsonIgnore]
    public List<SerialField> SerialFields
    {
        get
        {
            if (_serialFields == null)
            {
                _serialFields = FoldSerialFields
                    ? BuildSerialFieldsFromPlainFields(Fields)
                    : Fields.Select(OneColumnSerialField).ToList();
            }

            return _serialFields;
        }
    }
    private List<SerialField> _serialFields;

    /// <summary>
    /// Presents one column as its own group, for a table that does not fold.
    /// </summary>
    private static SerialField OneColumnSerialField(Field field)
    {
        // Pattern deliberately None: it is what stops NextSerialField taking anything,
        // and a group of one is what every non-folding column is anyway.
        return new SerialField
        {
            Name = field.Name,
            NamePart = field.Name,
            Pattern = SerialFieldPattern.None,
            Fields = new List<Field> { field },
        };
    }

    /// <summary>
    /// Checks whether the specified field exists. It is not case sensitive.
    /// </summary>
    public bool ContainsField(string nameToFind) => FindField(nameToFind) != null;

    /// <summary>
    /// Get the specified field. Throws a SheetManException if not found.
    /// </summary>
    public Field GetField(string nameToFind, Location callerLocation)
    {
        var found = FindField(nameToFind);
        if (found == null)
            throw new SheetManException(callerLocation, $"No found field '{nameToFind}' in table '{Name}'");

        return found;
    }

    /// <summary>
    /// Find the specified field. Returns null if not found.
    /// </summary>
    public Field FindField(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            return null;

        return Fields.Find(x => x.Name == fieldName);
    }

    /// <summary>
    /// Whether any row holds this value in the given column.
    /// </summary>
    public bool ContainsValueAt(int fieldIndex, object value)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
            return false;

        for (int rowIndex = 0; rowIndex < Data.Count; rowIndex++)
        {
            if (Data[rowIndex][fieldIndex].Value.Equals(value))
                return true;
        }

        return false;
    }


    #region Serial Fields

    /// <summary>
    /// Folds consecutively numbered columns into array-valued groups.
    ///
    /// Each unclaimed field opens a group, and every later field that shares its stem
    /// and numbering pattern joins it - so the columns of a group need not be adjacent
    /// in the sheet.
    /// </summary>
    private List<SerialField> BuildSerialFieldsFromPlainFields(List<Field> fields)
    {
        var result = new List<SerialField>();

        var visits = new bool[fields.Count];
        for (int i = 0; i < visits.Length; i++)
            visits[i] = false;

        for (int i = 0; i < fields.Count; i++)
        {
            if (visits[i])
                continue;

            var serialField = BeginSerialField(fields, i);
            if (serialField != null)
            {
                for (int j = i + 1; j < fields.Count; j++)
                {
                    if (NextSerialField(serialField, fields, j))
                        visits[j] = true;
                }
            }

            result.Add(serialField);
        }

        return result;
    }

    /// <summary>
    /// Opens a group around one field, which is also the answer for a column that
    /// turns out to have no siblings.
    /// </summary>
    private SerialField BeginSerialField(List<Field> fields, int index)
    {
        var field = fields[index];
        var fieldName = field.Name;

        var result = new SerialField
        {
            Name = fieldName,
            NamePart = Helper.StripNumber(fieldName),
            Pattern = GetSerialFieldPattern(fieldName),
            Fields = new List<Field>()
        };
        result.Fields.Add(field);

        return result;
    }

    /// <summary>
    /// Adds a field to a group if it belongs there.
    /// </summary>
    /// <returns>True when the field was taken, so the caller can mark it claimed.</returns>
    private bool NextSerialField(SerialField output, List<Field> fields, int index)
    {
        if (output.Pattern == SerialFieldPattern.None)
            return false;

        if (output.Fields.Count == 0)
            return false;

        var field = fields[index];
        var fieldName = field.Name;

        // Two delimited-array columns must not fold into one serial field: the
        // result would be an array of arrays, which no exporter or generator has
        // a shape for.
        if (field.IsArray || output.FirstField.IsArray)
            return false;

        string namePart = Helper.StripNumber(fieldName);
        if (namePart != output.NamePart)
            return false;

        var pattern = GetSerialFieldPattern(fieldName);
        if (pattern != output.Pattern)
            return false;

        string numberPart = Helper.ExtractNumber(fieldName);
        int number = int.Parse(numberPart, CultureInfo.InvariantCulture);
        string prevNumberPart = Helper.ExtractNumber(output.Fields[^1].Name);
        int prevNumber = int.Parse(prevNumberPart, CultureInfo.InvariantCulture);
        // Strictly less than, not less than or equal: two columns cannot carry the
        // same number, because duplicate field names are rejected before this runs.
        if (number < prevNumber)
        {
            // A warning rather than an error: the columns still fold into an array,
            // just in an order the sheet does not read in. Whether that is a mistake
            // depends on intent, so it is reported and left to the author.
            //
            // `{field.Name}`, not `field.Name` - the placeholder used to be written
            // without braces, so every one of these warnings named the literal text
            // "field.Name" instead of the column it was about.
            Log.Warning(
                $"Columns folded into an array are numbered out of order in table `{Name}`.\n" +
                $"`{field.Name}` follows `{output.Fields[^1].Name}` but carries a lower number, " +
                $"so the array elements will not be in sheet order.\n" +
                $"    at {field.NameLocation}");
        }

        var expectedType = output.Fields[0].Type;
        if (field.Type != expectedType)
        {
            string message = $"The consecutive column name rules are applied, but the column types do not match each other. (The type of {field.Index} must be {expectedType}.";
            throw new SheetManException(field.NameLocation, message);
        }

        if (output.Fields.Count == 1)
            output.Name = output.NamePart + "_array";

        output.Fields.Add(field);

        return true;
    }

    /// <summary>
    /// Classifies where a column name's sequence number sits, or reports that it has
    /// no usable one.
    /// </summary>
    private SerialFieldPattern GetSerialFieldPattern(string name)
    {
        if (string.IsNullOrEmpty(name))
            return SerialFieldPattern.None;

        // If there is no number pattern or more than once, it is not recognized.
        // ex) "item", "item01_1"
        int toggles = 0;
        bool digit = false;
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsDigit(name[i]))
            {
                if (!digit)
                    toggles++;
                digit = true;
            }
            else
            {
                digit = false;
            }
        }

        if (toggles == 0 || toggles > 1)
            return SerialFieldPattern.None;

        // Trailing when the name ends in the digit run, as in `Text1`.
        //
        // Only the last character is examined. This used to scan backwards over the
        // whole name and report TrailingNumber on finding a digit anywhere, which -
        // since reaching here means there is exactly one digit run - was always. So
        // `Item1Bonus` was classified as trailing and MiddleNumber was unreachable.
        if (char.IsDigit(name[name.Length - 1]))
            return SerialFieldPattern.TrailingNumber;

        // Otherwise the digits sit in the middle, as in `Item1Bonus`. A column name
        // cannot begin with a digit - the identifier check upstream rejects that -
        // so anything left is a middle run.
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsDigit(name[i]))
                return SerialFieldPattern.MiddleNumber;
        }

        return SerialFieldPattern.None;
    }
    #endregion
}
