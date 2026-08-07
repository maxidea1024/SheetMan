using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Serilog;
using SheetMan.Extensions;
using SheetMan.Models;
using SheetMan.Models.Raw;

namespace SheetMan.Cooking.Layouts;

/// <summary>
/// The layout SheetMan defines: entities are declared with `~~type:Name~~` markers and can
/// sit anywhere on a sheet.
/// </summary>
/// <remarks>
/// Row layout of each entity, top to bottom. The first two rows are the same for
/// all three; what follows differs.
///
/// <code>
///     table  (at least 3 columns wide)
///        ~~table:Name[:side]~~
///        table description
///        field names          &lt;- a `*` prefix marks a secondary index
///        field descriptions
///        field types
///        field detail types   &lt;- enum name, or reference target. Blank otherwise.
///        target sides
///        data rows...
///
///     enum  (3 columns)
///        ~~enum:Name[:side]~~
///        enum description
///        column captions      &lt;- for human readers only; skipped when parsing
///        label | value | description ...
///
///     const  (5 columns)
///        ~~const:Name[:side]~~
///        set description
///        column captions      &lt;- for human readers only; skipped when parsing
///        name | type | detail type | value | description ...
/// </code>
///
/// A definition extends downward while the cell in its first column is non-empty,
/// and rightward while cells are non-empty, so an entity is bounded by blank cells
/// rather than by a declared size. The minimum heights in _possibleEntities are
/// the body only - hence the `- 2`, which drops the marker and description rows.
/// </remarks>
[SheetManLayout("sheetman",
    Summary = "Entities declared with `~~table:Name~~` markers, several to a sheet.")]
public sealed class SheetManLayoutParser : ILayoutParser
{
    public class Size
    {
        public int width;
        public int height;
    }

    private readonly Dictionary<string, Size> _possibleEntities = new Dictionary<string, Size> {
        { "table", new Size{ width = 3, height = 7 - 2 } },
        { "enum", new Size{ width = 3, height = 3 - 2 } },
        { "const", new Size{ width = 5, height = 3 - 2 } },
    };

    private class DefinitionRect
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    private class EntityDefinition
    {
        [JsonIgnore] public RawSheet rawSheet;
        [JsonIgnore] public Location location;
        public string rawName;
        public string name;
        public string type;
        public string comment;
        public TargetSide targetSide;
        public DefinitionRect rect;
    }

    private CookingContext _context;

    /// <summary>
    /// What the marker scan found, kept between the two passes so the sheets are walked
    /// once rather than once per entity kind.
    /// </summary>
    private List<EntityDefinition> _definitions;

    private Model Model => _context.Model;

    public void ParseDeclarations(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;
        _definitions = ScanEntityDefinitions(sheets);

        // Since const and enum have a reference relationship, they must be parsed first.

        foreach (var def in _definitions)
        {
            if (def.type == "enum")
                Model.Enums.Add(ParseEnum(def));
            else if (def.type == "const")
                Model.ConstantSets.Add(ParseConstantSet(def));
        }
    }

    public void ParseTables(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;

        foreach (var def in _definitions)
        {
            if (def.type == "table")
                Model.Tables.Add(ParseTable(def));
        }
    }

    private Models.Enum ParseEnum(EntityDefinition def)
    {
        var result = new Models.Enum
        {
            Location = def.location,
            TargetSide = def.targetSide,
            RawName = def.rawName,
            Name = def.name,
            Comment = def.comment
        };

        Log.Information($"Parsing enum `{result.Name}`. ({result.Location})");

        int dataRowStart = def.rect.y + 1; // skip header row
        int dataRowEnd = def.rect.y + def.rect.height;
        int dataColStart = def.rect.x;

        result.Labels = new List<Models.Enum.Label>();

        for (int rowIdx = dataRowStart; rowIdx < dataRowEnd; rowIdx++)
        {
            var row = def.rawSheet.Rows[rowIdx];

            var nameCol = row[dataColStart + 0];
            var valueCol = row[dataColStart + 1];
            var descCol = row[dataColStart + 2];

            // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
            string rawName = nameCol.Value;
            string name = rawName.ToPascalCase();

            // Skip if marked with comments.
            if (_context.IsIgnorantName(name))
                continue;

            // Ensure identifier
            _context.RequiresIdentifier(name, nameCol.Location);

            // Check if the label is already defined.
            if (result.Contains(name))
                throw new SheetManException(nameCol.Location, $"Label '{name}' is already defined in enum '{result.Name}'.");

            if (!int.TryParse(valueCol.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int labelValue))
            {
                throw new SheetManException(valueCol.Location,
                    $"Label '{name}' in enum '{result.Name}' has value `{valueCol.Value}`, which is not an integer.");
            }

            // Add a label.
            var label = new Models.Enum.Label
            {
                Location = nameCol.Location,
                RawName = rawName,
                Name = name,
                Value = labelValue,
                Comment = descCol.Value
            };
            result.Labels.Add(label);
        }

        // An enum with no zero entry gives every unassigned field of that type a
        // value with no name, so one is supplied unless the recipe says otherwise.
        _context.ApplyAutoNoneLabel(result, def.location);

        return result;
    }

    private Models.ConstantSet ParseConstantSet(EntityDefinition def)
    {
        Log.Information($"Parsing constant-set `{def.name}`. ({def.location})");

        var result = new Models.ConstantSet
        {
            Location = def.location,
            TargetSide = def.targetSide,
            RawName = def.rawName,
            Name = def.name,
            Comment = def.comment
        };

        int dataRowStart = def.rect.y + 1; // skip header row
        int dataRowEnd = def.rect.y + def.rect.height;
        int dataColStart = def.rect.x;

        result.Constants = new List<Models.ConstantSet.Constant>();

        for (int rowIdx = dataRowStart; rowIdx < dataRowEnd; rowIdx++)
        {
            var row = def.rawSheet.Rows[rowIdx];

            var nameCol = row[dataColStart + 0];
            var typeCol = row[dataColStart + 1];
            var detailTypeCol = row[dataColStart + 2];
            var valueCol = row[dataColStart + 3];
            var descCol = row[dataColStart + 4];

            // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
            string rawName = nameCol.Value;
            string name = rawName.ToPascalCase();

            // Skip if marked with comments.
            if (_context.IsIgnorantName(name))
                continue;

            // Ensure identifier
            _context.RequiresIdentifier(name, nameCol.Location);

            // Whether the name collides with a keyword is asked per language rather than
            // here, because the answer differs per language: LanguageProfile carries each
            // one's reserved words and how it gets out of their way. The reserved-words
            // fixture compiles a table whose columns are named `class`, `delete` and
            // `operator` in every target.
            // Check if the label is already defined.
            if (result.ContainsConstant(name))
            {
                throw new SheetManException(nameCol.Location,
                    $"Constant '{name}' is already defined in constant-set '{result.Name}'.");
            }

            string typeName = typeCol.Value.ToLowerInvariant(); // normalize

            _context.RequiresValidTypeName(typeName, typeCol.Location);

            Models.Enum enumm = null;
            if (typeName == "enum")
            {
                if (detailTypeCol.Value == "")
                    throw new SheetManException(detailTypeCol.Location, $"In case of enum type, enum name must be specified in detail-type.");

                typeName = detailTypeCol.Value;

                enumm = Model.GetEnum(typeName, detailTypeCol.Location);
            }

            // Add a constant.
            var constant = new Models.ConstantSet.Constant
            {
                Location = nameCol.Location,
                RawName = rawName,
                Name = name,
                TypeName = typeName,
                Type = _context.ParseValueType(typeName, enumm != null ? detailTypeCol.Location : typeCol.Location), // an enum names its type in the detail cell, so point there
                Enum = enumm,
                Comment = descCol.Value,
                ValueString = valueCol.Value
            };

            constant.Value = _context.ParseValue(constant.Type, constant.Enum, valueCol.Value, valueCol.Location);

            result.Constants.Add(constant);
        }

        return result;
    }

    private Models.Table ParseTable(EntityDefinition def)
    {
        Log.Information($"Parsing table `{def.name}`. ({def.location})");

        var result = new Models.Table
        {
            Location = def.location,
            TargetSide = def.targetSide,
            RawName = def.rawName,
            Name = def.name,
            Comment = def.comment
        };

        var dataColumnOffsets = ParseTableFields(result, def);

        ParseTableData(result, def, dataColumnOffsets);

        _context.AssignTags(result);

        return result;
    }

    private List<int> ParseTableFields(Models.Table table, EntityDefinition def)
    {
        var dataColumnOffsets = new List<int>();

        var fieldNameRow = def.rawSheet.Rows[def.rect.y + 0];
        var fieldCommentRow = def.rawSheet.Rows[def.rect.y + 1];
        var fieldTypeRow = def.rawSheet.Rows[def.rect.y + 2];
        var fieldDetailTypeRow = def.rawSheet.Rows[def.rect.y + 3];
        var fieldTargetSideRow = def.rawSheet.Rows[def.rect.y + 4];

        // Field declarations first; the data pass below needs the types.
        for (int colIdx = def.rect.x; colIdx < def.rect.x + def.rect.width; colIdx++)
        {
            var fieldCommentCell = fieldCommentRow[colIdx];
            var fieldNameCell = fieldNameRow[colIdx];
            var fieldTypeCell = fieldTypeRow[colIdx];
            var fieldDetailTypeCell = fieldDetailTypeRow[colIdx];
            var fieldTargetSideCell = fieldTargetSideRow[colIdx];

            // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
            var rawFieldName = fieldNameCell.Value;

            // The wire tag comes off first: `Price@3` is the name `Price` with tag 3.
            // Before Pascal-casing, which would not survive the `@`.
            (rawFieldName, int? wireTag) = ParseWireTag(rawFieldName, fieldNameCell.Location);

            // Pascal-casing before the serial-field rules see the name is fine: the
            // rules look at where the digits are, and casing moves neither the digits
            // nor their order. `text_en_1` becomes `TextEn1`, which still reads as
            // stem `TextEn` and number 1.
            var fieldName = rawFieldName.ToPascalCase();

            if (_context.IsIgnorantName(fieldName))
            {
                // The primary index is what every row is addressed by, so it cannot be
                // the column somebody commented out.
                if (colIdx == def.rect.x)
                    throw new SheetManException(fieldNameCell.Location, $"The primary index field cannot be omitted.");

                // A tagged tombstone: the column is gone from the model, but its tag
                // stays reserved so it can never identify different data.
                if (wireTag != null)
                    table.ReservedTags.Add(wireTag.Value);

                continue;
            }

            dataColumnOffsets.Add(colIdx);

            var field = new Field
            {
                OwnerTable = table,
                NameLocation = fieldNameCell.Location,
                TypeLocation = fieldTypeCell.Location,
                DetailTypeLocation = fieldDetailTypeCell.Location,
                TargetSideLocation = fieldTargetSideCell.Location,
                TargetSide = _context.ParseTargetSide(fieldTargetSideCell.Value.ToLowerInvariant(), fieldTargetSideCell.Location),
                Index = table.Fields.Count,
                Comment = fieldCommentCell.Value
            };

            // A single leading `*` marks a secondary index.
            bool indexing = false;
            if (fieldName.StartsWith("*"))
            {
                fieldName = fieldName[1..].Trim();
                indexing = true;

                // Exactly one, not "one or more". Stripping every `*` would quietly
                // accept `**Name` as a typo for `*Name`, and leaving the extras in
                // place produced `` `*Name` is not a valid identifier `` - a message
                // that names the symptom rather than the mistake.
                if (fieldName.StartsWith("*"))
                {
                    throw new SheetManException(fieldNameCell.Location,
                        $"Field name `{rawFieldName}` has more than one leading `*`. " +
                        $"Use a single `*` to mark a secondary index field.");
                }
            }
            field.Indexing = (colIdx == def.rect.x) || indexing;

            // Ensure identifier
            _context.RequiresIdentifier(fieldName, fieldNameCell.Location);

            // Check duplicated name
            if (table.ContainsField(fieldName))
                throw new SheetManException(fieldNameCell.Location, $"Field name `{fieldName}` is a duplicated.");

            field.RawName = rawFieldName;
            field.Name = fieldName;
            field.Tag = wireTag;

            var fieldType = fieldTypeCell.Value.ToLowerInvariant();
            _context.RequiresValidTypeName(fieldType, fieldTypeCell.Location);

            // `int[]`, `string[]`, `enum[]`: one cell holding a delimited list.
            // The bracket suffix is peeled off here so the element name goes
            // through exactly the same handling as a scalar, and put back when the
            // type is finally resolved.
            bool isArrayField = fieldType.EndsWith("[]");
            if (isArrayField)
                fieldType = fieldType.Substring(0, fieldType.Length - 2).Trim();

            if (fieldType == "enum")
            {
                if (fieldDetailTypeCell.Value == "")
                    throw new SheetManException(fieldDetailTypeCell.Location, $"In case of enum type, enum name must be specified in detail-type.");

                fieldType = fieldDetailTypeCell.Value;
            }

            if (isArrayField && fieldType == "foreign")
            {
                // Deliberately unsupported rather than half-supported. An array of
                // references means resolving a variable number of targets per row,
                // which the generated readers have no shape for; letting it parse
                // would produce code that silently never resolves.
                throw new SheetManException(fieldTypeCell.Location,
                    "`foreign[]` is not supported. Use a serial field (Ref1, Ref2, ...) for a fixed " +
                    "number of references, or a plain `foreign` for a single one.");
            }

            if (fieldType == "foreign")
            {
                // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                string detailTypeName = fieldDetailTypeCell.Value.ToPascalCase();

                if (detailTypeName == "")
                    throw new SheetManException(fieldDetailTypeCell.Location, $"In case of foreign type, `RefTable[.RefFieldName]` must be specified in detail-type.");

                field.TypeName = "$Unresolved$";

                // Whichever form the reference takes, the cell itself holds the
                // referenced table's index, so that is what the data pass must
                // parse. SolveTableCrossReferencings overwrites Type afterwards
                // with the type the generated code should expose, and
                // BinaryExporter forces Int32 back for any IsRef field when it
                // writes. Leaving it Unresolved here made the dotted form die in
                // ParseValue before resolution ever ran.
                field.Type = Models.ValueType.Int32;

                int dot = detailTypeName.IndexOf(".");
                if (dot < 0)
                {
                    // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                    field.RefTableName = detailTypeName.ToPascalCase();
                    field.RefFieldName = null;
                }
                else
                {
                    // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                    field.RefTableName = detailTypeName.Substring(0, dot).ToPascalCase();
                    field.RefFieldName = detailTypeName.Substring(dot + 1).ToPascalCase();

                    // `Table.Index` names the row's own key, which is the row - so it is
                    // cleared and the reference resolves to the record rather than to the
                    // integer, which is what the writer meant either way.
                    if (field.RefFieldName.ToLowerInvariant() == "index")
                        field.RefFieldName = "";
                }
            }
            else
            {
                // TypeName stays the element's name - for an enum array that is the
                // enum declaration to look up, and the generators append the
                // brackets themselves.
                field.TypeName = fieldType;

                var elementType = _context.ParseValueType(fieldType, fieldTypeCell.Location);

                if (isArrayField)
                {
                    var arrayType = Models.ValueTypes.ArrayOf(elementType);
                    if (arrayType == Models.ValueType.None)
                    {
                        throw new SheetManException(fieldTypeCell.Location,
                            $"type `{fieldType}` cannot be used as an array element.");
                    }

                    field.Type = arrayType;
                }
                else
                {
                    field.Type = elementType;
                }
            }

            table.Fields.Add(field);
        }

        _context.CheckPrimaryIndexValidity(table.Fields[0]);

        return dataColumnOffsets;
    }

    private void ParseTableData(Models.Table table, EntityDefinition def, List<int> dataColumnOffsets)
    {
        int dataRowStart = def.rect.y + 5; // skip header rows(field name, comment, type, detail-type, target-side)
        int dataRowEnd = def.rect.y + def.rect.height;

        for (int rowIdx = dataRowStart; rowIdx < dataRowEnd; rowIdx++)
        {
            var row = new List<Cell>();

            for (int i = 0; i < table.Fields.Count; i++)
            {
                var field = table.Fields[i];

                var rawCell = def.rawSheet.Rows[rowIdx][dataColumnOffsets[i]];
                var value = _context.ParseValue(field.Type, field.EnumOrNull, rawCell.Value, rawCell.Location);

                // Index uniqueness is checked in ValidateModel rather than here.
                // Doing it inline compared each new value against every row read
                // so far - quadratic - and threw on the first duplicate, so a sheet
                // with several had to be fixed one error per run.

                row.Add(new Cell
                {
                    RawCell = rawCell,
                    Value = value
                });
            }

            table.Data.Add(row);
        }
    }

    private List<EntityDefinition> ScanEntityDefinitions(IReadOnlyList<RawSheet> sheets)
    {
        var entityDefinitions = new List<EntityDefinition>();

        foreach (var rawSheet in sheets)
        {
            for (int rowIndex = 0; rowIndex < rawSheet.Rows.Count; rowIndex++)
            {
                var rawRow = rawSheet.Rows[rowIndex];

                for (int colIndex = 0; colIndex < rawRow.Count; colIndex++)
                {
                    var rawCell = rawRow[colIndex];

                    if (ParseEntityMarker(rawCell.Value, out string entityType, out string rawEntityName, out string entityName, out string entityTargetSide, out Size entityMinSize))
                    {
                        // Ensure valid identifier
                        _context.RequiresIdentifier(entityName, rawCell.Location);

                        // Check duplicated name
                        if (entityDefinitions.Where(x => x.name == entityName).Count() > 0)
                            throw new SheetManException(rawCell.Location, $"Entity {entityType}'s name `{entityName}` is a duplicated.");

                        var commentRow = rawSheet.Rows[rowIndex + 1];

                        var entity = new EntityDefinition
                        {
                            rawSheet = rawSheet,
                            location = rawCell.Location,
                            type = entityType,
                            rawName = rawEntityName,
                            name = entityName,
                            comment = commentRow[colIndex].Value,
                            targetSide = _context.ParseTargetSide(entityTargetSide, rawCell.Location),
                            rect = ParseDefinitionRect(rawSheet, rawCell.Location, entityType, entityName, colIndex, rowIndex + 2, entityMinSize) // ignore marker and comment rows
                        };
                        entityDefinitions.Add(entity);
                    }
                }
            }
        }

        return entityDefinitions;
    }

    private DefinitionRect ParseDefinitionRect(RawSheet rawSheet, Location location, string entityType, string entityName, int x, int y, Size minSize)
    {
        // Checks bounds.
        //
        // An empty rectangle used to come back here, and an entity with one silently
        // disappeared: the conversion succeeded, the sheet's marker was still there, and
        // the table was simply not in the output. That is the same shape as every other
        // defect this codebase has had to hunt - not a failure, a different answer - and
        // the minimum-size check immediately below always threw for its own case, so the
        // two disagreed about what an unusable rectangle deserves.
        if (y < 0 || y >= rawSheet.Rows.Count || x < 0 || x >= rawSheet.ColumnCount)
        {
            throw new SheetManException(location,
                $"Entity `{entityType}:{entityName}` starts outside the sheet: its marker points at " +
                $"column {x + 1}, row {y + 1}, and the sheet holds {rawSheet.ColumnCount} column(s) " +
                $"and {rawSheet.Rows.Count} row(s). A marker in the last cell with nothing after it " +
                $"does this.");
        }

        // Check the minimum required size.
        int availWidth = rawSheet.ColumnCount - x;
        int availHeight = rawSheet.Rows.Count - y;
        if (availWidth < minSize.width || availHeight < minSize.height)
        {
            throw new SheetManException(location,
                    $"Entity `{entityType}:{entityName}` must have cells of at least {minSize.width}x{minSize.height} size. " +
                    $"The size of the currently accessible cell is {availWidth}x{availHeight}.");
        }

        // Greedy manner scanning.

        int maxWidth = 0;
        int height = 0;

        for (int rowIdx = y; rowIdx < rawSheet.Rows.Count; rowIdx++)
        {
            var rawCell = rawSheet.Rows[rowIdx][x];

            if (height >= minSize.height) // Since the minimum size has already been met, it stops when an empty cell or entity-marker is encountered.
            {
                if (rawCell.Value == "" || IsEntityMarkerPattern(rawCell.Value))
                    break;
            }
            else
            {
                // If the minimum size has not yet been met and an entity-marker comes, the rule is violated.
                if (IsEntityMarkerPattern(rawCell.Value))
                    throw new SheetManException(rawCell.Location, $"Unexpected entity-marker `{rawCell.Value}`");
            }

            height++;
        }

        for (int rowIdx = y; rowIdx < y + height; rowIdx++)
        {
            var row = rawSheet.Rows[rowIdx];

            int width = 0;
            for (int colIdx = x; colIdx < row.Count; colIdx++)
            {
                var rawCell = row[colIdx];

                if (width >= minSize.width) // Since the minimum size has already been met, it stops when an empty cell or entity-marker is encountered.
                {
                    if (rawCell.Value == "" || IsEntityMarkerPattern(rawCell.Value))
                        break;
                }
                else
                {
                    // If the minimum size has not yet been met and an entity-marker comes, the rule is violated.
                    if (IsEntityMarkerPattern(rawCell.Value))
                        throw new SheetManException(rawCell.Location, $"Unexpected entity-marker `{rawCell.Value}`");
                }

                width++;
            }

            if (width > maxWidth)
                maxWidth = width;
        }

        return new DefinitionRect { x = x, y = y, width = maxWidth, height = height };
    }

    private bool IsEntityMarkerPattern(string marker)
    {
        return ParseEntityMarker(marker, out _, out _, out _, out _, out _);
    }

    private bool ParseEntityMarker(string marker, out string outType, out string outRawName, out string outName, out string outTargetSide, out Size outMinSize)
    {
        outType = "";
        outRawName = "";
        outName = "";
        outTargetSide = "";
        outMinSize = new Size { width = 0, height = 0 };

        if (marker.Length == 0)
            return false;

        if (!marker.StartsWith("~~"))
            return false;
        marker = marker.Substring(2).Trim();

        if (!marker.EndsWith("~~"))
            return false;
        marker = marker.Substring(0, marker.Length - 2).Trim();

        if (!marker.Contains(":"))
            return false;

        var tokens = marker.Split(":");
        for (int i = 0; i < tokens.Length; i++)
            tokens[i] = tokens[i].Trim();

        // Type
        outType = tokens[0].ToLowerInvariant();

        // Check if it is a recognizable entity type.
        if (!_possibleEntities.TryGetValue(outType, out outMinSize))
            return false;

        // Name
        outRawName = tokens[1];
        outName = outRawName.ToPascalCase();

        // TargetSide
        if (tokens.Length > 2)
            outTargetSide = tokens[2].ToLowerInvariant();

        return true;
    }

    /// <summary>
    /// Splits a field name's `@N` wire-tag suffix off, when it has one.
    /// </summary>
    /// <remarks>
    /// The tag identifies the column in a binary file instead of its position, which is
    /// what lets a reader built from one generation of the model read a file written from
    /// another. `Price@3` is the field `Price` with tag 3; a name with no `@` has no
    /// explicit tag and AssignTags decides what that means for the table.
    /// </remarks>
    private static (string name, int? tag) ParseWireTag(string rawName, Location location)
    {
        int at = rawName.LastIndexOf('@');

        if (at < 0)
            return (rawName, null);

        string digits = rawName.Substring(at + 1).Trim();

        if (digits.Length == 0 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int tag))
        {
            throw new SheetManException(location,
                $"Field name `{rawName}` has an `@` where a wire tag goes, but `{digits}` is not a " +
                "positive integer. A tag is written as `Name@3`.");
        }

        if (tag < 1)
        {
            throw new SheetManException(location,
                $"Field `{rawName}` declares wire tag {tag}, but a tag starts at 1.");
        }

        return (rawName.Substring(0, at).Trim(), tag);
    }
}
