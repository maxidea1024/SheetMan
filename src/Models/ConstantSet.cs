using Newtonsoft.Json;
using System.Collections.Generic;

namespace SheetMan.Models
{
    /// <summary>
    ///
    /// </summary>
    public class ConstantSet
    {
        /// <summary>
        ///
        /// </summary>
        public class Constant
        {
            /// <summary></summary>
            [JsonIgnore]
            public Location Location { get; set; }

            /// <summary></summary>
            public string Name { get; set; }
            public string RawName { get; set; }

            /// <summary></summary>
            public string TypeName { get; set; }

            /// <summary></summary>
            public ValueType Type { get; set; }

            /// <summary></summary>
            public Enum Enum { get; set; }

            /// <summary></summary>
            public string ValueString { get; set; } // original value string

            /// <summary></summary>
            public object Value { get; set; } // imported value

            /// <summary></summary>
            public string Comment { get; set; }
        }

        /// <summary></summary>
        [JsonIgnore]
        public Location Location { get; set; }

        /// <summary>Target side filtering option./summary>
        public TargetSide TargetSide { get; set; }

        /// <summary></summary>
        public string Name { get; set; }
        public string RawName { get; set; }

        /// <summary></summary>
        public List<Constant> Constants { get; set; } = new List<Constant>();

        /// <summary></summary>
        public string Comment { get; set; }

        /// <summary>
        ///
        /// </summary>
        public bool ContainsConstant(string constantName) => FindConstant(constantName) != null;

        /// <summary>
        ///
        /// </summary>
        public Constant GetConstant(string constantName, Location callerLocation)
        {
            var result = FindConstant(constantName);
            if (result == null)
                throw new SheetManException(callerLocation, $"constant `{constantName}` was not found in the constant-set `{Name}`");

            return result;
        }

        /// <summary>
        ///
        /// </summary>
        public Constant FindConstant(string constantName) => Constants.Find(x => x.Name == constantName);
    }
}
