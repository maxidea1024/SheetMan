using Newtonsoft.Json;
using System.Collections.Generic;
using SheetMan.Models.Raw;
using SheetMan.Helpers;
using Serilog;

namespace SheetMan.Models
{
    /// <summary>
    /// Table model
    /// </summary>
    public class Table
    {
        /// <summary>Where the table is defined.</summary>
        [JsonIgnore]
        public Location Location { get; set; }

        /// <summary>Target side filtering option</summary>
        public TargetSide TargetSide { get; set; }

        /// <summary></summary>
        public string RawName { get; set; }
        /// <summary>Table name</summary>
        public string Name { get; set; }

        /// <summary>Field list</summary>
        public List<Field> Fields { get; set; } = new List<Field>();

        /// <summary>Data row list</summary>
        public List<List<Cell>> Data { get; set; } = new List<List<Cell>>();

        /// <summary>Comment</summary>
        public string Comment { get; set; }

        /// <summary>Serial column list</summary>
        [JsonIgnore]
        public List<SerialField> SerialFields
        {
            get
            {
                if (_serialFields == null)
                    _serialFields = BuildSerialFieldsFromPlainFields(Fields);

                return _serialFields;
            }
        }
        private List<SerialField> _serialFields;

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
        /// Checks whether the specified field has the specified data in the row.
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

        private bool NextSerialField(SerialField output, List<Field> fields, int index)
        {
            if (output.Pattern == SerialFieldPattern.None)
                return false;

            if (output.Fields.Count == 0)
                return false;

            var field = fields[index];
            var fieldName = field.Name;

            string namePart = Helper.StripNumber(fieldName);
            if (namePart != output.NamePart)
                return false;

            var pattern = GetSerialFieldPattern(fieldName);
            if (pattern != output.Pattern)
                return false;

            string numberPart = Helper.ExtractNumber(fieldName);
            int number = int.Parse(numberPart);
            string prevNumberPart = Helper.ExtractNumber(output.Fields[^1].Name);
            int prevNumber = int.Parse(prevNumberPart);
            //if (number <= prevNumber) // 같은 경우는 있을 수 없음.  컬럼명 유니크 체크가 먼저 일어나므로, 이전에 오류처리 되었을것이므로..
            if (number < prevNumber)
            {
                //TODO 경고 핸들링..
                //string text = "";
                //text += "\n";
                //text += "연속된 컬럼 배열로 취급하기 모호점이 발견되었습니다.";
                //text += "\n뒤이은 컬럼의 번호가 앞에 있는 컬럼의 번호보다 작습니다.";
                //text += "\n아래 위치의 내용을 확안하세요.";
                //text += $"\n이전 컬럼명: `{output.Fields[output.Fields.Count - 1].Name}`, 현재 컬럼명: `{field.Name}`";
                //text += $"\n- {field.NameLocation}";
                //Utils.Logging.LogWarning(text);

                string message = "";
                message += "An ambiguity was found in treating as a contiguous array of columns.\n";
                message += "The number of the subsequent column is less than the number of the preceding column.\n";
                message += "Please confirm the contents of the location below.\n";
                message += $"PreviousField: `{output.Fields[^1].Name}`, CurrentField: `field.Name`\n";
                message += $"    at {field.NameLocation}";
                Log.Warning(message);
            }

            var expectedType = output.Fields[0].Type;
            if (field.Type != expectedType)
            {
                //string text = $"연속된 컬럼명 규칙이 적용되었으나, 컬럼의 타입이 서로일치 하지 않습니다. ({field.Index}의 타입은 {expectedType}이어야합니다.";
                string message = $"The consecutive column name rules are applied, but the column types do not match each other. (The type of {field.Index} must be {expectedType}.";
                throw new SheetManException(field.NameLocation, message);
            }

            if (output.Fields.Count == 1)
                output.Name = output.NamePart + "_array";

            output.Fields.Add(field);

            return true;
        }

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

            // Check if the sequence number is the last type.
            for (int i = name.Length - 1; i >= 0; --i)
            {
                if (char.IsDigit(name[i]))
                    return SerialFieldPattern.TrailingNumber;
            }

            // Check if the sequence number is the middle type.
            // Due to the nature of the column name, a number cannot appear at the beginning.
            // Separately here because it is verified in the process of reading the table layout
            // Does not check, only the first character is excluded from the check target.
            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsDigit(name[i]))
                    return SerialFieldPattern.MiddleNumber;
            }

            return SerialFieldPattern.None;
        }
        #endregion
    }
}
