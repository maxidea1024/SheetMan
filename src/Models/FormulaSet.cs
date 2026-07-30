using Newtonsoft.Json;
using System.Collections.Generic;

namespace SheetMan.Models
{
    /// <summary>
    /// Reserved for a future `formula` entity: expressions the sheets declare and the
    /// game evaluates at run time, rather than values fixed at conversion time.
    ///
    /// Nothing parses these yet - the cooker recognizes only table, enum and const - so
    /// no instance is ever created. Kept because the shape is settled and removing it
    /// would only have to be undone.
    /// </summary>
    public class FormulaSet
    {
        /// <summary>One named expression.</summary>
        public class Formula
        {
            /// <summary>Cell the formula was declared in.</summary>
            [JsonIgnore]
            public Location Location { get; set; }

            /// <summary>Name the formula is referenced by.</summary>
            public string Key { get; set; }

            /// <summary>The expression itself, unparsed.</summary>
            public string FormulaString { get; set; }

            /// <summary>Description from the sheet.</summary>
            public string Comment { get; set; }
        }

        /// <summary>Cell holding the entity marker that declared this set.</summary>
        [JsonIgnore]
        public Location Location { get; set; }

        /// <summary>Target side filtering option</summary>
        public TargetSide TargetSide { get; set; }

        /// <summary>Name exactly as written in the sheet.</summary>
        public string RawName { get; set; }

        /// <summary>Name normalized to Pascal case.</summary>
        public string Name { get; set; }

        /// <summary>The formulas, in declaration order.</summary>
        public List<Formula> Formulas { get; set; } = new List<Formula>();

        /// <summary>Description from the sheet.</summary>
        public string Comment { get; set; }
    }
}
