using System;
using System.Collections.Generic;

namespace SheetMan.Models
{
    /// <summary>
    /// Where the sequence number sits in a serial field's column names.
    ///
    /// Columns only fold together when they agree on this, so `Text1`/`Text2` and
    /// `Item1Bonus`/`Item2Bonus` form separate groups rather than one confused one.
    /// </summary>
    public enum SerialFieldPattern
    {
        /// <summary>Not a serial column: no digits, or more than one run of them.</summary>
        None,

        /// <summary>The name ends in the number, as in `Text1`.</summary>
        TrailingNumber,

        /// <summary>The number sits inside the name, as in `Item1Bonus`.</summary>
        MiddleNumber,
    }

    /// <summary>
    /// How a table's columns are presented to the exporters and generators.
    ///
    /// Every column belongs to exactly one of these. Most are a group of one, but
    /// consecutively numbered columns fold into a single array-valued entry - so
    /// `Text1`, `Text2` become one `TextArray` rather than two fields, which is what
    /// makes them usable as an array in generated code.
    /// </summary>
    public class SerialField
    {
        /// <summary>
        /// Name this group is exposed under. The field's own name for a group of one, or
        /// the shared stem with `_array` appended when several columns folded together.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>Columns in this group, in ascending order of their sequence number.</summary>
        public List<Field> Fields { get; set; } = new List<Field>();

        /// <summary>The column name with its digits removed, which is what groups them.</summary>
        public string NamePart { get; set; } = "";

        /// <summary>Where the sequence number sits. Columns only fold together when this matches.</summary>
        public SerialFieldPattern Pattern { get; set; } = SerialFieldPattern.None;

        /// <summary>
        /// Forces a single column to be presented as a one-element array.
        ///
        /// For a table that will grow more numbered columns later: without it, `Text1`
        /// alone would be a scalar and adding `Text2` would change the generated API
        /// from a value to an array.
        /// </summary>
        public bool TreatAsArrayEvenIfSingleItem { get; set; } = false;

        /// <summary>
        /// Whether this group is an index, so its values must be unique.
        ///
        /// Arrays are excluded: uniqueness of a list of values is not a useful key, and
        /// none of the generated lookups can index by one.
        /// </summary>
        public bool IsIndexer => (Fields.Count > 0) && !IsArray && FirstField.Indexing;

        /// <summary>Whether this group references another table.</summary>
        public bool IsRef => (Fields.Count > 0 ) ? Fields[0].IsRef : false;

        /// <summary>
        /// Whether consumers should see this as an array, from either cause: several
        /// numbered columns folded together, or a single column holding a delimited
        /// list.
        /// </summary>
        public bool IsArray => Fields.Count > 1
                            || (Fields.Count == 1 && TreatAsArrayEvenIfSingleItem)
                            || IsVariableLengthArray;

        /// <summary>
        /// Whether the length varies per row, which is true only of delimited array
        /// cells.
        ///
        /// This is what separates the two array kinds on the wire. A serial field has
        /// as many elements as it has columns, so the count is known at generation
        /// time and nothing needs to be written. A delimited cell has to carry its
        /// length, and its reader has to allocate per row.
        /// </summary>
        public bool IsVariableLengthArray => Fields.Count == 1 && FirstField != null && FirstField.IsArray;

        /// <summary>
        /// Element type behind this field, looking through both array kinds.
        /// </summary>
        public ValueType ElementType => (Fields.Count > 0) ? ValueTypes.ElementOf(Fields[0].Type) : ValueType.None;

        /// <summary>
        /// Type of the group's columns, which the cooker has already required to agree.
        /// This is the array type itself for a delimited column - see
        /// <see cref="ElementType"/> for the type of one value.
        /// </summary>
        public ValueType Type => (Fields.Count > 0 ) ? Fields[0].Type : ValueType.None;

        /// <summary>Target side of the group's columns.</summary>
        public TargetSide TargetSide => (Fields.Count > 0 ) ? Fields[0].TargetSide : TargetSide.Both;

        /// <summary>
        /// First column of the group, which carries the properties shared by all of them.
        /// Null only for an empty group, which should not occur.
        /// </summary>
        public Field FirstField => (Fields.Count > 0) ? Fields[0] : null;
    }
}
