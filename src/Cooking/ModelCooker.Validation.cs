using System.Collections.Generic;
using SheetMan.Models.Raw;
using SheetMan.Models;

namespace SheetMan.Cooking
{
    // 셀 데이터를 임포트한 형태로 불러들이는게 좋을듯..
    // 단순히 검증만 하는것보다는..

    public partial class ModelCooker
    {
        private void ValidateModel(Model model)
        {
            /*
            // Validate values.
            foreach (var table in model.Tables)
            {
                foreach (var field in table.Fields)
                {
                    var fieldType = field.Type;
                    if (fieldType == ValueType.Enum)
                    {
                        foreach (var row in table.Data)
                        {
                            var cell = row[field.Index];
                            ValidateEnumValue(cell.Value, field.Enum, cell.Location);
                        }
                    }
                    else if (fieldType == ValueType.DateTime)
                    {
                        foreach (var row in table.Data)
                        {
                            var cell = row[field.Index];
                            ValidateDateTimeValue(cell.Value, cell.Location);
                        }
                    }
                    else if (fieldType == ValueType.Bool)
                    {
                        foreach (var row in table.Data)
                        {
                            var cell = row[field.Index];
                            ValidateBoolValue(cell.Value, cell.Location);
                        }
                    }
                    //todo integer나 float/double도 유효한 값인지 체크하자.
                    //overflow도 체크하자.
                    //경고로 해야하나? 에러로 해야하나? struct모드로 설정할수 있게 하자.
                }
            }
            */

            // Validate unique
            foreach (var table in model.Tables)
            {
                foreach (var field in table.Fields)
                {
                    if (field.Indexing)
                        continue;

                    foreach (var row in table.Data)
                    {
                        var cell = row[field.Index];
                        var locations = new List<Location>();
                        CollectConflitedIndices(table, field.Index, cell.Value, ref locations);
                        //todo 이걸 최종적으로 모아서 하는게 좋을까?
                        if (locations.Count > 1)
                        {
                            foreach (var location in locations)
                            {
                                //todo 예외 타입을 하나더 만들어서 처리할까?
                                //details에 넣어주어야하나..?
                                //Utils.Logging.LogError($"duplicated index `{cell.Value}` at {location}");
                            }
                        }
                    }
                }
            }

            // Validate references
            List<string> errors = new List<string>();
            foreach (var table in model.Tables)
            {
                for (int fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
                {
                    var field = table.Fields[fieldIndex];

                    if (!field.IsRef)
                        continue;

                    // 우선 참조하는 테이블이 유효한지 체크.
                    var foreignTable = model.FindTable(field.RefTableName);
                    if (foreignTable == null)
                    {
                        //참조하는 대상의 테이블이 존재하지 않습니다.
                        errors.Add($"참조의 대상이 되는 테이블이 존재하지 않습니다.\n참조하는곳:{field.TypeLocation}, 피참조테이블: {field.RefTableName}");
                        continue;
                    }

                    // 참조하고 있는 테이블에 해당 컬럼이 존재하는지 체크.
                    string foreignFieldName = "index";
                    if (!string.IsNullOrEmpty(field.RefFieldName))
                        foreignFieldName = field.RefFieldName;

                    var foreignField = foreignTable.FindField(foreignFieldName);
                    if (foreignField == null)
                    {
                        //TODO 오류 메시지 적용
                        //error += "참조의 대상이되는 테이블 컬럼이 존재하지 않습니다.\n";
                        //error += string.Format("  선언된곳: {0}, 참조테이블: {1}, 참조컬럼: {2}\n\n", field.Location, field.refTable, foreignFieldName);
                        continue;
                    }

                    // 참조하고 있는 테이블에 레코드가 존재하는지 체크.
                    for (int rowIndex = 0; rowIndex < table.Data.Count; rowIndex++)
                    {
                        var cell = table.Data[rowIndex][fieldIndex];
                        var key = cell.Value;
                        if (!foreignTable.ContainsValueAt(0, key))
                        {
                            //TODO
                            //if (key != "0") // 임시로 "0" 재낌.. 이건 참조 무효를 나타내는 상황인가? null ref?
                            {
                                //TODO 오류 메시지 적용
                                //error += "참조의 대상이되는 테이블 데이터가 존재하지 않습니다.\n";
                                //error += string.Format("  선언된곳: {0}, 참조테이블: {1}, 참조컬럼: {2}, 참조키: {3}\n\n", cell.Location, field.refTable, foreignFieldName, key);
                            }
                        }
                    }
                }
            }
        }

        private void CollectConflitedIndices(Table table, int fieldIndex, object value, ref List<Location> locations)
        {
            foreach (var row in table.Data)
            {
                var cell = row[fieldIndex];
                if (cell.Value != value)
                    continue;

                bool alreadyConflicted = false;
                foreach (var l in locations)
                {
                    if (l == cell.RawCell.Location)
                    {
                        alreadyConflicted = true;
                        break;
                    }
                }

                if (!alreadyConflicted)
                    locations.Add(cell.RawCell.Location);
            }
        }

        private void ValidateValueType(string typeName, Location location)
        {
            if (!IsAcceptableValueType(typeName))
                throw new SheetManException(location, $"'{typeName}' type is undefined.");
        }

        private bool IsAcceptableValueType(string typeName)
        {
            switch (typeName)
            {
                case "string": return true;
                case "bool": return true;
                case "int": return true;
                case "bigint": return true;
                case "float": return true;
                case "double": return true;
                case "datetime": return true;
                case "timespan": return true;
                case "uuid": return true;
            }

            // Also enum
            if (Model.Current.ContainsEnum(typeName))
                return true;

            return false;
        }

        private void ValidateEnumValue(string labelOrValue, Enum enumm, Location location)
        {
            if (!enumm.Contains(labelOrValue))
                throw new SheetManException(location, $"'{labelOrValue}' is not a label that exists in enum '{enumm.Name}'.");
        }

        private void ValidateDateTimeValue(string value, Location location)
        {
            //TODO 살짝 완화하도록 하자.
            //15자리 혹은 13자리를 지원하고
            //구분자 문자를 '_' 뿐만 아니라 공백 문자 ' '도 지원하는게 좋을듯.
            if (value.Length != 15 || value[8] != '_')
                throw new SheetManException(location, $"'{value}' is not legal date-time format. you should take the following format: 'YYYYMMDD_hhmmss'");
        }

        private static void ValidateBoolValue(string value, Location location)
        {
            //How to?
        }
    }
}
