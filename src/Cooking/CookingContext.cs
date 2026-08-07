using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SheetMan.Extensions;
using SheetMan.Models;
using SheetMan.Recipe;

namespace SheetMan.Cooking;

/// <summary>
/// The model being built, and everything a layout parser needs that is not about layout.
/// </summary>
/// <remarks>
/// Reading a cell as an `int`, recognizing an enum name, deciding what a boolean spelling
/// means, numbering wire tags - none of that depends on where in a sheet the column was
/// found. It lives here so that a second layout is a second way of locating rows and
/// columns, and not a second answer to what a value means. Two parsers disagreeing about
/// whether `1,000` is a thousand is precisely the failure this tool exists to prevent.
/// </remarks>
public sealed class CookingContext
{
    /// <summary>Number formats accepted in an integer cell.</summary>
    private const NumberStyles IntegerStyles = NumberStyles.Integer | NumberStyles.AllowThousands;

    /// <summary>Number formats accepted in a float or double cell.</summary>
    private const NumberStyles DecimalStyles = NumberStyles.Float | NumberStyles.AllowThousands;

    public CookingContext(Model model, RecipeModel recipe)
    {
        Model = model;
        ArrayDelimiter = ResolveArrayDelimiter(recipe);
        AutoInsertEnumNoneLabel = recipe.AutoInsertEnumNoneLabel;
    }

    /// <summary>The model every parser adds to.</summary>
    public Model Model { get; }

    /// <summary>Separator for array cells, taken from the recipe.</summary>
    public char ArrayDelimiter { get; }

    /// <summary>Whether to give an enum a zero label it did not declare.</summary>
    public bool AutoInsertEnumNoneLabel { get; }

    /// <summary>
    /// Reads the array delimiter from the recipe, rejecting anything that is not exactly
    /// one character.
    /// </summary>
    private static char ResolveArrayDelimiter(RecipeModel recipeModel)
    {
        string delimiter = recipeModel.ArrayDelimiter;

        if (string.IsNullOrEmpty(delimiter) || delimiter.Length != 1)
        {
            throw new SheetManException(
                $"Recipe setting `ArrayDelimiter` is `{delimiter}`, but it must be exactly one character.");
        }

        return delimiter[0];
    }


    #region Names

    /// <summary>Whether a name marks its row or column as commented out.</summary>
    public bool IsIgnorantName(string name)
    {
        return name.StartsWith("#") || name.StartsWith("//");
    }

    public void RequiresIdentifier(string name, Location location)
    {
        if (!name.IsValidIdentifier())
            throw new SheetManException(location, $"`{name}` is not a valid dentifier.");
    }

    public void RequiresValidTypeName(string typeName, Location location)
    {
        if (IsValidTypeName(typeName))
            return;

        throw new SheetManException(location, $"type `{typeName}` is an unrecognized type.");
    }

    /// <summary>
    /// Whether a name is one of the types a sheet may declare.
    /// </summary>
    /// <remarks>
    /// The non-throwing half of <see cref="RequiresValidTypeName"/>, for the callers that
    /// are deciding rather than checking - a layout working out whether a sheet is a table
    /// at all cannot use an exception to find out.
    /// </remarks>
    public bool IsValidTypeName(string typeName)
    {
        if (typeName == null)
            return false;

        // `int[]`, `string[]` and so on: one cell holding several delimited
        // values. Validity of the element name is the same question as for a
        // scalar, so strip the brackets and ask that.
        if (typeName.EndsWith("[]"))
            typeName = typeName.Substring(0, typeName.Length - 2).Trim();

        switch (typeName)
        {
            case "string":
            case "bool":
            case "int":
            case "bigint":
            case "float":
            case "double":
            case "datetime":
            case "timespan":
            case "uuid":

            // Also foreign, enum
            case "foreign":
            case "enum":
                return true;
        }

        return false;
    }

    public TargetSide ParseTargetSide(string value, Location location)
    {
        switch (value)
        {
            case "":
            case "cs": return TargetSide.Both;
            case "s": return TargetSide.ServerOnly;
            case "c": return TargetSide.ClientOnly;
        }

        throw new SheetManException(location, $"Illegal target-side '{value}'");
    }

    #endregion


    #region Types and values

    public Models.ValueType ParseValueType(string typeName, Location location)
    {
        if (typeName.EndsWith("[]"))
        {
            string elementName = typeName.Substring(0, typeName.Length - 2).Trim();
            var elementType = ParseValueType(elementName, location);

            var arrayType = Models.ValueTypes.ArrayOf(elementType);
            if (arrayType == Models.ValueType.None)
                throw new SheetManException(location, $"type `{elementName}` cannot be used as an array element.");

            return arrayType;
        }

        // Primitive types.
        switch (typeName)
        {
            case "string": return Models.ValueType.String;
            case "bool": return Models.ValueType.Bool;
            case "int": return Models.ValueType.Int32;
            case "bigint": return Models.ValueType.Int64;
            case "float": return Models.ValueType.Float;
            case "double": return Models.ValueType.Double;
            case "datetime": return Models.ValueType.DateTime;
            case "timespan": return Models.ValueType.TimeSpan;
            case "uuid": return Models.ValueType.Uuid;
        }

        // Also enum.
        if (Model.ContainsEnum(typeName))
            return Models.ValueType.Enum;

        throw new SheetManException(location, $"unsupported type '{typeName}'");
    }

    public object ParseValue(Models.ValueType type, Models.Enum enumm, string rawValue, Location location)
    {
        if (Models.ValueTypes.IsArray(type))
            return ParseArrayValue(type, enumm, rawValue, location);

        try
        {
            switch (type)
            {
                case Models.ValueType.String:
                    return rawValue;

                case Models.ValueType.Bool:
                    return ParseBool(rawValue, location);

                // Thousands separators are accepted on the numeric types, because a
                // designer reading a column of large numbers writes `1,000,000`. This
                // is only unambiguous under an invariant culture, where a comma can
                // never be the decimal point.
                case Models.ValueType.Int32:
                    return int.Parse(rawValue, IntegerStyles, CultureInfo.InvariantCulture);

                case Models.ValueType.Int64:
                    return long.Parse(rawValue, IntegerStyles, CultureInfo.InvariantCulture);

                case Models.ValueType.Float:
                    return float.Parse(rawValue, DecimalStyles, CultureInfo.InvariantCulture);

                case Models.ValueType.Double:
                    return double.Parse(rawValue, DecimalStyles, CultureInfo.InvariantCulture);

                case Models.ValueType.TimeSpan:
                    return TimeSpan.Parse(rawValue, CultureInfo.InvariantCulture);

                case Models.ValueType.DateTime:
                    return DateTime.Parse(rawValue, CultureInfo.InvariantCulture);

                case Models.ValueType.Uuid:
                    return Guid.Parse(rawValue);

                case Models.ValueType.Enum:
                    return enumm.GetLabel(rawValue, location).Value;

                case Models.ValueType.ForeignRecord:
                    return int.Parse(rawValue, IntegerStyles, CultureInfo.InvariantCulture);

                default:
                    throw new Exception($"not implemented value type {type}");
            }
        }
        catch (SheetManException)
        {
            // Already carries its own message and location - an enum label that does
            // not exist, or a boolean spelling that is not recognized. Wrapping it
            // would restate the obvious around a better explanation.
            throw;
        }
        catch (Exception ex)
        {
            // Whatever the framework parsers throw: FormatException, OverflowException
            // and friends, whose messages name the problem but not the cell.
            throw new SheetManException(location, $"Cannot parse `{rawValue}` as a value of type `{type}`. ({ex.Message})");
        }
    }

    /// <summary>
    /// Splits a delimited cell and parses each element.
    ///
    /// An empty cell is an empty array rather than an error: a row that simply has
    /// no values for the column is the common case, and rejecting it would force
    /// designers to invent a placeholder.
    ///
    /// Elements are trimmed, so `1; 2 ;3` reads the same as `1;2;3`.
    /// </summary>
    private object ParseArrayValue(Models.ValueType arrayType, Models.Enum enumm, string rawValue, Location location)
    {
        var elementType = Models.ValueTypes.ElementOf(arrayType);

        if (string.IsNullOrWhiteSpace(rawValue))
            return System.Array.CreateInstance(ElementClrType(elementType, enumm), 0);

        var parts = rawValue.Split(ArrayDelimiter);
        var result = System.Array.CreateInstance(ElementClrType(elementType, enumm), parts.Length);

        for (int i = 0; i < parts.Length; i++)
            result.SetValue(ParseValue(elementType, enumm, parts[i].Trim(), location), i);

        return result;
    }

    /// <summary>
    /// The CLR element type to allocate an array of.
    ///
    /// Typed rather than object[]: the exporters cast each element to its concrete
    /// type, and JSON serialization of an object[] would render enums as bare
    /// integers inconsistently with the scalar path.
    /// </summary>
    private static System.Type ElementClrType(Models.ValueType elementType, Models.Enum enumm)
    {
        switch (elementType)
        {
            case Models.ValueType.String: return typeof(string);
            case Models.ValueType.Bool: return typeof(bool);
            case Models.ValueType.Int32: return typeof(int);
            case Models.ValueType.Int64: return typeof(long);
            case Models.ValueType.Float: return typeof(float);
            case Models.ValueType.Double: return typeof(double);
            case Models.ValueType.TimeSpan: return typeof(System.TimeSpan);
            case Models.ValueType.DateTime: return typeof(System.DateTime);
            case Models.ValueType.Uuid: return typeof(System.Guid);
            // Enum labels and record references are both stored as their integer.
            case Models.ValueType.Enum: return typeof(int);
            case Models.ValueType.ForeignRecord: return typeof(int);
            default: return typeof(object);
        }
    }

    /// <summary>
    /// Reads a boolean cell.
    ///
    /// Several spellings are accepted because designers reach for whichever reads
    /// best in the sheet: Y/N, YES/NO, TRUE/FALSE, 1/0. Case does not matter.
    ///
    /// An empty cell is false. That is deliberate - a blank means "not set" and
    /// false is the useful reading of that - and it is the one lenient case here.
    ///
    /// Anything else is an error. It used to fall through to false, so `Yes please`
    /// or a misspelled `Ture` became false silently: exactly the human mistake this
    /// tool exists to catch, turned into wrong data instead of a message.
    /// </summary>
    private bool ParseBool(string value, Location location)
    {
        if (value.Length == 0)
            return false;

        switch (value.ToUpperInvariant())
        {
            case "N":
            case "NO":
            case "FALSE":
                return false;

            case "Y":
            case "YES":
            case "TRUE":
                return true;
        }

        // Numeric spellings, so a column of counts can be read as flags: zero is
        // false and anything else is true, as in C.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return number != 0.0;

        throw new SheetManException(location,
            $"`{value}` is not a boolean. Use Y/N, YES/NO, TRUE/FALSE, 1/0, or leave the cell empty for false.");
    }

    #endregion


    #region Enums

    /// <summary>
    /// Gives an enum a zero label when it declared neither the name nor the value, so a
    /// default-constructed field of that type means something.
    /// </summary>
    /// <remarks>
    /// Every layout wants this and for the same reason, so it is asked here rather than
    /// left to each parser to remember. An enum that already has something at zero is left
    /// exactly as written.
    /// </remarks>
    public void ApplyAutoNoneLabel(Models.Enum enumm, Location location)
    {
        if (!AutoInsertEnumNoneLabel)
            return;

        if (enumm.Contains("None") || enumm.Contains(0))
            return;

        enumm.Labels.Insert(0, new Models.Enum.Label
        {
            Location = location,
            RawName = "None",
            Name = "None",
            Value = 0,
            Comment = "None (automatically inserted by SheetMan)"
        });
    }

    #endregion


    #region Tables

    public void CheckPrimaryIndexValidity(Models.Field field)
    {
        // The name is not fixed to `index`. Reference resolution reads the target
        // table's own primary-index function name, so a sheet whose first column is
        // called something else resolves the same way.
        if (field.Type != Models.ValueType.Int32)
            throw new SheetManException(field.TypeLocation, $"The type of the index field must be `int`, but type `{field.Type}` is specified.");

        if (field.TargetSide != Models.TargetSide.Both)
            throw new SheetManException(field.TargetSideLocation, $"The target-side of the index field must be set to CS.");
    }

    /// <summary>
    /// Gives every logical column its wire tag, checking the sheet's own against each other.
    /// </summary>
    /// <remarks>
    /// A logical column is a serial field - `Ref1..Ref3` is one column with one tag, carried
    /// on its first member.
    ///
    /// Two modes, decided per table and never mixed. If no field carries a tag, the ordinal
    /// position is the tag: the file is still self-describing, but only appending columns is
    /// safe, because an insertion shifts every ordinal after it. The moment any field carries
    /// one, all of them must - a half-tagged table gets neither mode's guarantees - and then
    /// the tags are checked unique, including against the tombstones' reserved ones.
    /// </remarks>
    public void AssignTags(Models.Table table)
    {
        var serials = table.SerialFields;

        // A serial field is one logical column; the tag goes on its first member.
        foreach (var sf in serials)
        {
            foreach (var extra in sf.Fields.Skip(1))
            {
                if (extra.Tag != null)
                {
                    throw new SheetManException(extra.NameLocation,
                        $"Field `{table.Name}.{extra.Name}` is part of the serial field " +
                        $"`{sf.Name}` and carries wire tag {extra.Tag}. A serial field is one " +
                        "column on the wire, so the tag goes on its first member only.");
                }
            }
        }

        var tagged = serials.Where(sf => sf.FirstField.Tag != null).ToList();

        if (tagged.Count == 0)
        {
            if (table.ReservedTags.Count > 0)
            {
                throw new SheetManException(table.Location,
                    $"Table `{table.Name}` has a `#`-excluded column reserving a wire tag, but " +
                    "no live field carries one. Tags are all-or-none per table: give every " +
                    "field its `@N`, or drop the tag from the tombstone.");
            }

            // Ordinal mode: the tag is the column's position, which is safe to append
            // to and nothing else. Recorded as such, because it is what decides how much
            // of a schema change the baseline check can let through.
            table.HasExplicitTags = false;

            for (int position = 0; position < serials.Count; position++)
                serials[position].FirstField.Tag = position + 1;

            return;
        }

        if (tagged.Count != serials.Count)
        {
            var untagged = serials.Where(sf => sf.FirstField.Tag == null).Select(sf => sf.Name);

            throw new SheetManException(table.Location,
                $"Table `{table.Name}` tags some fields and not others: " +
                $"{string.Join(", ", untagged)} carry no `@N`. Tags are all-or-none per " +
                "table, because a half-tagged table gets neither mode's guarantees.");
        }

        var seen = new Dictionary<int, string>();

        foreach (int reserved in table.ReservedTags)
            seen[reserved] = "a `#`-excluded column";

        foreach (var sf in serials)
        {
            int tag = sf.FirstField.Tag.Value;

            if (seen.TryGetValue(tag, out string holder))
            {
                throw new SheetManException(sf.FirstField.NameLocation,
                    $"Field `{table.Name}.{sf.Name}` declares wire tag {tag}, which {holder} " +
                    "already holds. A tag identifies a column for the life of the data, so it " +
                    "can never be shared or reused.");
            }

            seen[tag] = $"field `{sf.Name}`";
        }

        table.HasExplicitTags = true;
    }

    #endregion
}
