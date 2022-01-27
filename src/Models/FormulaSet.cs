using Newtonsoft.Json;
using System.Collections.Generic;

namespace SheetMan.Models
{
    /// <summary>
    ///
    /// </summary>
    public class FormulaSet
    {
        /// <summary>
        ///
        /// </summary>
        public class Formula
        {
            /// <summary></summary>
            [JsonIgnore]
            public Location Location { get; set; }

            /// <summary></summary>
            public string Key { get; set; }

            /// <summary></summary>
            public string FormulaString { get; set; }

            /// <summary></summary>
            public string Comment { get; set; }
        }

        /// <summary></summary>
        [JsonIgnore]
        public Location Location { get; set; }

        /// <summary>Target side filtering option</summary>
        public TargetSide TargetSide { get; set; }

        /// <summary></summary>
        public string RawName { get; set; }
        /// <summary></summary>
        public string Name { get; set; }

        /// <summary></summary>
        public List<Formula> Formulas { get; set; } = new List<Formula>();

        /// <summary></summary>
        public string Comment { get; set; }
    }
}
