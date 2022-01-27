using Newtonsoft.Json;
using System.Collections.Generic;

namespace SheetMan.Models
{
    /// <summary>
    ///
    /// </summary>
    public class Enum
    {
        /// <summary>
        ///
        /// </summary>
        public class Label
        {
            /// <summary></summary>
            [JsonIgnore] public Location Location { get; set; }

            /// <summary></summary>
            public string RawName { get; set; }
            /// <summary></summary>
            public string Name { get; set; }

            /// <summary></summary>
            //public string RawValue { get; set; }
            /// <summary></summary>
            public int Value { get; set; }

            /// <summary></summary>
            public string Comment { get; set; }
        }

        /// <summary></summary>
        [JsonIgnore] public Location Location { get; set; }

        /// <summary>Target side filtering option</summary>
        public TargetSide TargetSide { get; set; }

        /// <summary></summary>
        public string RawName { get; set; }
        /// <summary></summary>
        public string Name { get; set; }

        /// <summary></summary>
        public List<Label> Labels { get; set; } = new List<Label>();

        /// <summary></summary>
        public string Comment { get; set; }

        /// <summary>
        ///
        /// </summary>
        public bool Contains(object labelNameOrValue) => FindLabel(labelNameOrValue) != null;

        /// <summary>
        ///
        /// </summary>
        public Label GetLabel(object labelNameOrValue, Location callerLocation)
        {
            var found = FindLabel(labelNameOrValue);
            if (found == null)
            {
                if (labelNameOrValue is string name)
                    throw new SheetManException(callerLocation, $"Label '{name}' was not found in the enum '{Name}'");
                else if (labelNameOrValue is int value)
                    throw new SheetManException(callerLocation, $"Value '{value}' was not found in the enum '{Name}'");
                else
                    throw new SheetManException();
            }

            return found;
        }

        /// <summary>
        ///
        /// </summary>
        public Label FindLabel(object labelNameOrValue)
        {
            //TODO string 타입이라고 해도 integer이면 integer로 다루게 해주는게 좋을듯?
            if (labelNameOrValue is string label)
                return FindLabelByName(label);
            else if (labelNameOrValue is int value)
                return FindLabelByValue(value);
            else
                throw new SheetManException();
        }

        /// <summary>
        ///
        /// </summary>
        public Label FindLabelByName(string name) => Labels.Find(x => x.Name == name);

        /// <summary>
        ///
        /// </summary>
        public Label FindLabelByValue(int value) => Labels.Find(x => x.Value == value);
    }
}
