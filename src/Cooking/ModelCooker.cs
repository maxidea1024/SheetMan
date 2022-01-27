/*

    1. 첫번째 필드는 무조건 index 필드여야함.
       이름은 index 여야하고, 타입은 int32여야한다. (이름은 상관 없나?)

    2. 필드 이름 중복 체크

    3. 각 엔티티 이름 중복 체크 (엔티티 종류가 다를 경우에는 중복을 허용해도 되려나?)

    4. 각 셀 값들이 정상적인지 체크.(모호한 값일 경우에는 에러내지 경고, 옵션에 따라 달리 동작)

    5. target-side 마스킹 대응.

    6. 기타 오류 사항 꼼꼼히 체크. (케이스별 확인)
*/

using System.Collections.Generic;
using SheetMan.Models.Raw;
using SheetMan.Models;
using System;
using Newtonsoft.Json;
using SheetMan.Recipe;
using Serilog;
using SheetMan.Extensions;
using System.Linq;

namespace SheetMan.Cooking
{
    public partial class ModelCooker
    {
        /*

        table: 3x7
           marker
           table-comment
           field names
           field comments
           field types
           field detail types (foreign/enum only)
           target side

        enum: 3x3
           marker
           comment
           header
           row=> 0=definition, 1=value, 2=label-comment

        const: 5x3
           marker
           comment
           header
           row=> 0=definition, 1=type, 2=detail-type, 3=value, 4=cosntant-comment

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

        public Model Cook(Options options, RecipeModel recipeModel, RawModel rawModel)
        {
            //foreach (var rawSheet in rawModel.Sheets)
            //{
            //    Log.Information($"RawSheet: Name={rawSheet.Location.Sheet}, Width={rawSheet.ColumnCount}, Height={rawSheet.Rows.Count}");
            //    Log.Information($"   LastRowCell: {rawSheet.Rows[^1][0].Value}");
            //
            //    foreach (var row in rawSheet.Rows)
            //        Log.Information($"...{row[0].Value} {row[0].Location}");
            //}

            var result = new Model();

            _model = result;

            ParseRawModel(rawModel, result);

            result.SolveTableCrossReferencings();

            return result;
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
                // Future feature
                //else if (def.type == "var")
                //{
                //    //Log.Debug("Parsed VAR:");
                //    //Log.Debug("  => " + JsonConvert.SerializeObject(ParseVariableSet(def)));
                //    var variableSet = ParseVariableSet(def);
                //    targetModel.VariableSets.Add(variableSet);
                //}
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

            //헤더 로우로 placeholder에 해당하므로 무시(단순 표기 용도임)
            //var headerRow = def.rawSheet.Rows[def.rect.y];
            //string headerNameCol = headerRow[def.rect.x + 0];
            //string headerValueCol = headerRow[def.rect.x + 1];
            //string headerDescCol = headerRow[def.rect.x + 2];

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

                // Add a label.
                var label = new Models.Enum.Label
                {
                    Location = nameCol.Location,
                    RawName = rawName,
                    Name = name,
                    Value = int.Parse(valueCol.Value),
                    Comment = descCol.Value
                };
                result.Labels.Add(label);
            }

            // If the label "None" is not defined and there is no entry corresponding to the number 0,
            // Automatically add "None" or number 0 entry.

            //TODO 옵션으로 처리하는게 어떠려나?
            if (!result.Contains("None") && !result.Contains(0))
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

            //헤더 로우로 placeholder에 해당하므로 무시(단순 표기 용도임)
            //var headerRow = def.rawSheet.Rows[def.rect.y];
            //string headerNameCol = headerRow[def.rect.x + 0];
            //string headerTypeCol = headerRow[def.rect.x + 1];
            //string headerValueCol = headerRow[def.rect.x + 2];
            //string headerDescCol = headerRow[def.rect.x + 3];

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

                string typeName = typeCol.Value.ToLower(); // normalize

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
                var fieldName = rawFieldName.ToPascalCase(); //이렇게 되면 serial field naming 규칙에 문제가 없나?

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
                    TargetSide = ParseTargetSide(fieldTargetSideCell.Value.ToLower(), fieldTargetSideCell.Location),
                    Index = table.Fields.Count,
                    Comment = fieldCommentCell.Value
                };

                bool indexing = false;
                if (fieldName.StartsWith("*"))
                {
                    //연이어 있는것들도 제거하는게 좋을까?
                    //아니면 한개만 지정하라고 하는게 좋을까? (까탈스럽나?)
                    fieldName = fieldName[1..].Trim();
                    indexing = true;
                }
                field.Indexing = (colIdx == def.rect.x) || indexing;

                // Ensure identifier
                RequiresIdentifier(fieldName, fieldNameCell.Location);

                // Check duplicated name
                if (table.ContainsField(fieldName))
                    throw new SheetManException(fieldNameCell.Location, $"Field name `{fieldName}` is a duplicated.");

                field.RawName = rawFieldName;
                field.Name = fieldName;

                var fieldType = fieldTypeCell.Value.ToLower();
                RequiresValidTypeName(fieldType, fieldTypeCell.Location);

                if (fieldType == "enum")
                {
                    if (fieldDetailTypeCell.Value == "")
                        throw new SheetManException(fieldDetailTypeCell.Location, $"In case of enum type, enum name must be specified in detail-type.");

                    fieldType = fieldDetailTypeCell.Value;
                }

                if (fieldType == "foreign")
                {
                    // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                    string detailTypeName = fieldDetailTypeCell.Value.ToPascalCase();

                    if (detailTypeName == "")
                        throw new SheetManException(fieldDetailTypeCell.Location, $"In case of foreign type, `RefTable[.RefFieldName]` must be specified in detail-type.");

                    field.TypeName = "$Unresolved$";
                    field.Type = Models.ValueType.Unresolved;

                    int dot = detailTypeName.IndexOf(".");
                    if (dot < 0)
                    {
                        // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                        field.RefTableName = detailTypeName.ToPascalCase();
                        field.RefFieldName = null;

                        field.Type = Models.ValueType.Int32;
                    }
                    else
                    {
                        // Names that can be used as variable or class names are normalized to Pascal case at the time of calling.
                        field.RefTableName = detailTypeName.Substring(0, dot - 1).ToPascalCase();
                        field.RefFieldName = detailTypeName.Substring(dot + 1).ToPascalCase();

                        //TODO
                        // .index는 레코드를 의미하므로 레코드 자체를 가리키도록 무효화시킴.
                        // 하지만, 좀더 생각해볼 필요가 있음.
                        if (field.RefFieldName.ToLower() == "index")
                            field.RefFieldName = "";
                    }
                }
                else
                {
                    field.TypeName = fieldType;
                    field.Type = ParseValueType(fieldType, fieldTypeCell.Location);
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

                    if (field.Indexing)
                    {
                        //TODO optimization
                        //여러개의 오류를 한번에 트래킹 하는게 좋을듯 하다.
                        for (int yy = 0; yy < table.Data.Count; yy++)
                        {
                            if (value.Equals(table.Data[yy][i].Value)) //object끼리 비교시에는 object.Equals 메소드를 통해야만함!
                                throw new SheetManException(rawCell.Location, $"Value `{value}` is duplicated. The data in the field specified by the index must be unique.");
                        }
                    }

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
            outType = tokens[0].ToLower();

            // Check if it is a recognizable entity type.
            if (!_possibleEntities.TryGetValue(outType, out outMinSize))
                return false;

            // Name
            outRawName = tokens[1];
            outName = outRawName.ToPascalCase();

            // TargetSide
            if (tokens.Length > 2)
                outTargetSide = tokens[2].ToLower();

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
            try
            {
                switch (type)
                {
                    case Models.ValueType.String:
                        return rawValue;

                    case Models.ValueType.Bool:
                        //return bool.Parse(rawValue);
                        return ParseBool(rawValue);

                    case Models.ValueType.Int32:
                        return int.Parse(rawValue);

                    case Models.ValueType.Int64:
                        return long.Parse(rawValue);

                    case Models.ValueType.Float:
                        return float.Parse(rawValue);

                    case Models.ValueType.Double:
                        return double.Parse(rawValue);

                    case Models.ValueType.TimeSpan:
                        return TimeSpan.Parse(rawValue);

                    case Models.ValueType.DateTime:
                        return DateTime.Parse(rawValue);
                        //return ParseDateTime(rawValue);

                    case Models.ValueType.Uuid:
                        return Guid.Parse(rawValue);

                    case Models.ValueType.Enum:
                        return enumm.GetLabel(rawValue, location).Value;

                    case Models.ValueType.ForeignRecord:
                        return int.Parse(rawValue);

                    default:
                        throw new Exception($"not implemented value type {type}");
                }
            }
            catch (Exception ex)
            {
                throw new SheetManException(location, $"Cannot parse `{rawValue}` as a value of type `{type}`. ({ex.Message})");
            }
        }

        private bool ParseBool(string value)
        {
            if (value.Length == 0) // 비워두면 false로 인식함.
            {
                //strict 모드에서는 오류로 간주하는게 좋을듯..
                return false;
            }

            value = value.ToUpper();
            if (value == "N" || value == "NO" || value == "FALSE" || value == "0")
                return false;

            if (value == "Y" || value == "YES" || value == "TRUE" || value == "1")
                return true;

            //TODO 좀 모호한 상태가 아닐런지? 걍 오류 처리하는게 좋을듯도? 아니면 strict 모드에서만??
            if (double.TryParse(value, out double i))
                return i != 0.0;

            return false;
        }

        private System.DateTime ParseDateTime(string value)
        {
            // Seconds part is omittable.
            if (value.Length != 15 || value.Length != 13)
                throw new SheetManException("datetime format must be like 'YYYYmmdd_HHMMSS' or 'YYYYmmdd_HHMM'");

            var year = value.Substring(0, 4);
            var month = value.Substring(4, 2);
            var day = value.Substring(6, 2);
            var hour = value.Substring(9, 2);
            var min = value.Substring(11, 2);

            string sec;
            if (value.Length == 15)
                sec = value.Substring(13, 2);
            else
                sec = "0"; // Seconds part is omitted.

            //TODO 이 값이 유효한지를 체크하는 기능을 넣어주는게 좋을듯..

            return new System.DateTime(
                int.Parse(year),
                int.Parse(month),
                int.Parse(day),
                int.Parse(hour),
                int.Parse(min),
                int.Parse(sec));
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
