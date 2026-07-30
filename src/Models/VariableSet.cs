using Newtonsoft.Json;
using System.Collections.Generic;

namespace SheetMan.Models
{
    /// <summary>
    /// Reserved for a future `var` entity: values the game may change at run time, as
    /// opposed to the read-only constants a `const` entity declares.
    ///
    /// Nothing parses these yet - the cooker recognizes only table, enum and const, and
    /// the branch that would build one is commented out in ParseRawModel.
    /// </summary>
    public class VariableSet
    {
        /// <summary>One named variable.</summary>
        public class Variable
        {
            /// <summary>변수가 정의된 위치</summary>
            [JsonIgnore]
            public Location Location { get; set; }

            /// <summary>Name normalized to Pascal case.</summary>
            public string Name { get; set; }

            /// <summary>Name exactly as written in the sheet.</summary>
            public string RawName { get; set; }

            /// <summary>Type as written in the sheet.</summary>
            public string TypeName { get; set; }

            /// <summary>Resolved type.</summary>
            public ValueType Type { get; set; }

            // Both the text and the parsed value are kept, matching what ConstantSet
            // settled on: the parsed value is what gets used, and the original text is
            // what a generator shows so the reader sees what the author wrote.

            /// <summary>The value cell's text.</summary>
            public string ValueString { get; set; }

            /// <summary>The parsed value, boxed. Its runtime type follows <see cref="Type"/>.</summary>
            public object Value { get; set; }

            /// <summary>주석</summary>
            public string Comment { get; set; }
        }

        /// <summary>Cell holding the entity marker that declared this set.</summary>
        public Location Location { get; set; }

        /// <summary>Target side filtering option.</summary>
        public TargetSide TargetSide { get; set; }

        /// <summary>Name normalized to Pascal case.</summary>
        public string Name { get; set; }

        /// <summary>Name exactly as written in the sheet.</summary>
        public string RawName { get; set; }

        /// <summary>정의된 변수 목록</summary>
        public List<Variable> Variables { get; set; } = new List<Variable>();

        /// <summary>주석</summary>
        public string Comment { get; set; }
    }
}
