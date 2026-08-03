using System.Collections.Generic;
using SheetMan.Models.Raw;
using SheetMan.Models;
using System;
using Newtonsoft.Json;
using SheetMan.Recipe;
using Serilog;
using SheetMan.Extensions;
using System.Linq;
using System.Globalization;
using SheetMan.Targets;

namespace SheetMan.Cooking
{
    public partial class ModelCooker
    {
        /*
            Row layout of each entity, top to bottom. The first two rows are the same for
            all three; what follows differs.

            table  (at least 3 columns wide)
               ~~table:Name[:side]~~
               table description
               field names          <- a `*` prefix marks a secondary index
               field descriptions
               field types
               field detail types   <- enum name, or reference target. Blank otherwise.
               target sides
               data rows...

            enum  (3 columns)
               ~~enum:Name[:side]~~
               enum description
               column captions      <- for human readers only; skipped when parsing
               label | value | description ...

            const  (5 columns)
               ~~const:Name[:side]~~
               set description
               column captions      <- for human readers only; skipped when parsing
               name | type | detail type | value | description ...

            A definition extends downward while the cell in its first column is non-empty,
            and rightward while cells are non-empty, so an entity is bounded by blank cells
            rather than by a declared size. The minimum heights in _possibleEntities are
            the body only - hence the `- 2`, which drops the marker and description rows.
        */

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

        private Model _model;

        /// <summary>Separator for array cells, taken from the recipe.</summary>
        private char _arrayDelimiter = ';';

        /// <summary>Whether to give an enum a zero label it did not declare.</summary>
        private bool _autoInsertEnumNoneLabel = true;

        /// <summary>Number formats accepted in an integer cell.</summary>
        private const NumberStyles IntegerStyles = NumberStyles.Integer | NumberStyles.AllowThousands;

        /// <summary>Number formats accepted in a float or double cell.</summary>
        private const NumberStyles DecimalStyles = NumberStyles.Float | NumberStyles.AllowThousands;

        public Model Cook(Options options, RecipeModel recipeModel, RawModel rawModel)
        {
            var result = new Model();

            _model = result;
            _arrayDelimiter = ResolveArrayDelimiter(recipeModel);
            _autoInsertEnumNoneLabel = recipeModel.AutoInsertEnumNoneLabel;

            ParseRawModel(rawModel, result);

            // Resolution and validation share one collector, so a workbook comes back
            // with everything wrong with it rather than one problem per run.
            var diagnostics = new Diagnostics();

            result.SolveTableCrossReferencings(diagnostics);

            // Runs after resolution: validation follows references to check that what
            // they point at exists.
            //
            // The requested side is passed in so a narrowed run is checked against what it
            // will actually build. Without it, `--target-side client` could fail on a
            // problem that only exists in the server cut it is not producing.
            ValidateModel(result, recipeModel, CommandLineTargetSide.Of(options), diagnostics);

            diagnostics.ThrowIfAny("The workbook did not pass validation.");

            return result;
        }

        /// <summary>
        /// Reads the array delimiter from the recipe, rejecting anything that is not
        /// exactly one character.
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

        private void ParseRawModel(RawModel rawModel, Model targetModel)
        {
            Log.Information("Parsing raw-model...");

            var entityDefinitions = ScanEntityDefinitions(rawModel);

            // Since const and enum have a reference relationship, they must be parsed first.

            foreach (var def in entityDefinitions)
            {
                if (def.type == "enum")
                {
                    var enumm = ParseEnum(def);
                    targetModel.Enums.Add(enumm);
                }
                else if (def.type == "const")
                {
                    var constantSet = ParseConstantSet(def);
                    targetModel.ConstantSets.Add(constantSet);
                }
            }

            foreach (var def in entityDefinitions)
            {
                if (def.type == "table")
                {
                    var table = ParseTable(def);
                    targetModel.Tables.Add(table);
                }
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
                if (IsIgnorantName(name))
                    continue;

                // Ensure identifier
                RequiresIdentifier(name, nameCol.Location);

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
            //
            // Only when the sheet declares neither the name nor the value: an enum that
            // already has something at zero is left exactly as written.
            if (_autoInsertEnumNoneLabel && !result.Contains("None") && !result.Contains(0))
            {
                var noneLabel = new Models.Enum.Label
                {
                    Location = def.location,
                    RawName = "None",
                    Name = "None",
                    Value = 0,
                    Comment = "None (automatically inserted by SheetMan)"
                };
                result.Labels.Insert(0, noneLabel);
            }

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

            //TODO detail type이 따로 있으므로 따로 처리해야함.

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
                if (IsIgnorantName(name))
                    continue;

                // Ensure identifier
                RequiresIdentifier(name, nameCol.Location);

                //TODO Identifier인지 체크. 예약어인지 체크? 이걸 여기서 하는게 맞으려나?
                //해당 언어출력시에 체크하는게 조금더 자유도를 줄수도 있을듯 싶은데?
                //C#의 경우에는 문제가 없는데..
                //typescript, C/C++에서 camel-case를 사용하게 되면 컴파일이 안되는 문제가 있을수 있다.

                // Check if the label is already defined.
                if (result.ContainsConstant(name))
                {
                    throw new SheetManException(nameCol.Location,
                        $"Constant '{name}' is already defined in constant-set '{result.Name}'.");
                }

                string typeName = typeCol.Value.ToLowerInvariant(); // normalize

                RequiresValidTypeName(typeName, typeCol.Location);

                Models.Enum enumm = null;
                if (typeName == "enum")
                {
                    if (detailTypeCol.Value == "")
                        throw new SheetManException(detailTypeCol.Location, $"In case of enum type, enum name must be specified in detail-type.");

                    typeName = detailTypeCol.Value;

                    enumm = _model.GetEnum(typeName, detailTypeCol.Location);
                }

                // Add a constant.
                var constant = new Models.ConstantSet.Constant
                {
                    Location = nameCol.Location,
                    RawName = rawName,
                    Name = name,
                    TypeName = typeName,
                    Type = ParseValueType(typeName, enumm != null ? detailTypeCol.Location : typeCol.Location), // enum의 경우 detailTypeCol.Location으로.
                    Enum = enumm,
                    Comment = descCol.Value,
                    ValueString = valueCol.Value
                };

                constant.Value = ParseValue(constant.Type, constant.Enum, valueCol.Value, valueCol.Location);

                result.Constants.Add(constant);
            }

            return result;
        }

        private Models.Table ParseTable(EntityDefinition def)
        {
            Log.Information($"Parsing table `{def.name}`. ({def.location})");

            //한번에 하나의 오류가 아닌 여러개의 오류를 트래킹하기 위해서
            var detailErrors = new List<SheetManException.Detail>();

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

            // 우선 필드 정의를 처리하자.
            for (int colIdx = def.rect.x; colIdx < def.rect.x + def.rect.width; colIdx++)
            {
                var fieldCommentCell = fieldCommentRow[colIdx];
                var fieldNameCell = fieldNameRow[colIdx];
                var fieldTypeCell = fieldTypeRow[colIdx];
                var fieldDetailTypeCell = fieldDetailTypeRow[colIdx];
                var fieldTargetSideCell = fieldTargetSideRow[colIdx];

                // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                var rawFieldName = fieldNameCell.Value;
                // Pascal-casing before the serial-field rules see the name is fine: the
                // rules look at where the digits are, and casing moves neither the digits
                // nor their order. `text_en_1` becomes `TextEn1`, which still reads as
                // stem `TextEn` and number 1.
                var fieldName = rawFieldName.ToPascalCase();

                if (IsIgnorantName(fieldName))
                {
                    // primary 인덱스 필드의 경우에는 주석으로 마킹되어 있으면 안됨.
                    if (colIdx == def.rect.x)
                        throw new SheetManException(fieldNameCell.Location, $"The primary index field cannot be omitted.");

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
                    TargetSide = ParseTargetSide(fieldTargetSideCell.Value.ToLowerInvariant(), fieldTargetSideCell.Location),
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
                RequiresIdentifier(fieldName, fieldNameCell.Location);

                // Check duplicated name
                if (table.ContainsField(fieldName))
                    throw new SheetManException(fieldNameCell.Location, $"Field name `{fieldName}` is a duplicated.");

                field.RawName = rawFieldName;
                field.Name = fieldName;

                var fieldType = fieldTypeCell.Value.ToLowerInvariant();
                RequiresValidTypeName(fieldType, fieldTypeCell.Location);

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

                        //TODO
                        // .index는 레코드를 의미하므로 레코드 자체를 가리키도록 무효화시킴.
                        // 하지만, 좀더 생각해볼 필요가 있음.
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

                    var elementType = ParseValueType(fieldType, fieldTypeCell.Location);

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

            CheckPrimaryIndexValidity(table.Fields[0]);

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
                    var value = ParseValue(field.Type, field.EnumOrNull, rawCell.Value, rawCell.Location);

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

        private List<EntityDefinition> ScanEntityDefinitions(RawModel rawModel)
        {
            var entityDefinitions = new List<EntityDefinition>();

            foreach (var rawSheet in rawModel.Sheets)
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
                            RequiresIdentifier(entityName, rawCell.Location);

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
                                targetSide = ParseTargetSide(entityTargetSide, rawCell.Location),
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
            // Checks bounds
            if (y < 0 || y >= rawSheet.Rows.Count || x < 0 || x >= rawSheet.ColumnCount)
            {
                //TODO 예외를 던져야하는거 아닐까?
                return new DefinitionRect { x = 0, y = 0, width = 0, height = 0 };
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

        private TargetSide ParseTargetSide(string value, Location location)
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

        private bool IsIgnorantName(string name)
        {
            return name.StartsWith("#") || name.StartsWith("//");
        }

        private Models.ValueType ParseValueType(string typeName, Location location)
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
            if (_model.ContainsEnum(typeName))
                return Models.ValueType.Enum;

            throw new SheetManException(location, $"unsupported type '{typeName}'");
        }

        private object ParseValue(Models.ValueType type, Models.Enum enumm, string rawValue, Location location)
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

            var parts = rawValue.Split(_arrayDelimiter);
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

        private void CheckPrimaryIndexValidity(Models.Field field)
        {
            //TODO 인덱스 이름도 "index'로 고정 시켜야하나?

            if (field.Type != Models.ValueType.Int32)
                throw new SheetManException(field.TypeLocation, $"The type of the index field must be `int`, but type `{field.Type}` is specified.");

            if (field.TargetSide != Models.TargetSide.Both)
                throw new SheetManException(field.TargetSideLocation, $"The target-side of the index field must be set to CS.");
        }

        private void RequiresIdentifier(string name, Location location)
        {
            if (!name.IsValidIdentifier())
                throw new SheetManException(location, $"`{name}` is not a valid dentifier.");
        }

        private void RequiresValidTypeName(string typeName, Location location)
        {
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
                    return;
            }

            throw new SheetManException(location, $"type `{typeName}` is an unrecognized type.");
        }
    }
}
