using System;
using System.Collections.Generic;
using System.Linq;

namespace SheetMan.Models;

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
/// What one entry of a table holds: a scalar, or a record built from several columns.
///
/// The distinction is what separates `Slot1`/`Slot2` - two numbers folded into one
/// `int[]` - from `Slot1.Id`/`Slot1.Count`/`Slot2.Id`/`Slot2.Count`, which is an array of
/// two records. spec/nested-fields.md has the notation and why it looks like that.
/// </summary>
public enum SerialFieldKind
{
    /// <summary>One value per element. Every table written before nesting existed.</summary>
    Scalar,

    /// <summary>
    /// Several named values per element, each from its own column with its own type.
    /// The members are in <see cref="SerialField.Members"/> and
    /// <see cref="SerialField.Fields"/> is not used.
    /// </summary>
    Record,
}

/// <summary>
/// One member of a record group: its name, and the column filling it in each element.
/// </summary>
public class RecordMember
{
    /// <summary>Member name as generated code sees it, Pascal cased.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The column holding this member, one per element, in element order.
    ///
    /// Its length is the array length, and it is the same for every member of a group -
    /// the folding requires that, because an element missing one member would generate a
    /// record with a value nothing ever writes.
    /// </summary>
    public List<Field> Fields { get; set; } = new List<Field>();

    /// <summary>The member's column in the first element, which carries the shared properties.</summary>
    public Field FirstField => (Fields.Count > 0) ? Fields[0] : null;

    /// <summary>Type of this member. The folding has already required the elements to agree.</summary>
    public ValueType Type => (Fields.Count > 0) ? Fields[0].Type : ValueType.None;

    /// <summary>Element type behind this member, looking through the array kinds.</summary>
    public ValueType ElementType => (Fields.Count > 0) ? ValueTypes.ElementOf(Fields[0].Type) : ValueType.None;

    /// <summary>Whether this member references another table.</summary>
    public bool IsRef => (Fields.Count > 0) && Fields[0].IsRef;
}

/// <summary>
/// How a table's columns are presented to the exporters and generators.
///
/// Every column belongs to exactly one of these. Most are a group of one, but
/// consecutively numbered columns fold into a single array-valued entry - so
/// `Text1`, `Text2` become one `TextArray` rather than two fields, which is what
/// makes them usable as an array in generated code.
///
/// A group's element is a scalar or a record; see <see cref="SerialFieldKind"/>.
/// </summary>
public class SerialField
{
    /// <summary>Whether one element of this group is a scalar or a record.</summary>
    public SerialFieldKind Kind { get; set; } = SerialFieldKind.Scalar;

    /// <summary>
    /// Members of the record, in the order their columns appear in the sheet. Empty
    /// unless <see cref="Kind"/> is Record.
    /// </summary>
    public List<RecordMember> Members { get; set; } = new List<RecordMember>();

    /// <summary>Whether one element of this group is a record rather than a scalar.</summary>
    public bool IsRecord => Kind == SerialFieldKind.Record;

    /// <summary>
    /// How many elements a record group has, which is how many columns each of its
    /// members has. Zero for a scalar group, which reports its length through
    /// <see cref="Fields"/> instead.
    /// </summary>
    public int RecordElementCount => (Members.Count > 0) ? Members[0].Fields.Count : 0;

    /// <summary>
    /// Every column this group covers, whichever kind it is. For the passes that need to
    /// reach each underlying column - tag assignment, target-side filtering, the data
    /// walk - and should not have to know which shape they are looking at.
    /// </summary>
    public IEnumerable<Field> AllFields
        => IsRecord ? Members.SelectMany(m => m.Fields) : Fields;

    /// <summary>
    /// The columns this group occupies in a binary file, each represented by the
    /// <see cref="Field"/> that carries its wire tag.
    /// </summary>
    /// <remarks>
    /// One for a scalar group - `Text1`/`Text2` is a single fixed-array column - and
    /// **one per member** for a record group, because the file stores a struct of arrays
    /// where the API presents an array of structs. That is what keeps the column
    /// encodings working per member and makes adding a member an additive change rather
    /// than a reinterpretation of an existing column.
    /// </remarks>
    public IEnumerable<Field> WireColumns
    {
        get
        {
            if (IsRecord)
                return Members.Select(m => m.FirstField);

            return (FirstField is null) ? Enumerable.Empty<Field>() : new[] { FirstField };
        }
    }

    /// <summary>
    /// Columns that must not carry a tag of their own, because another column of the same
    /// wire column already does.
    /// </summary>
    public IEnumerable<Field> NonTagCarryingFields
        => IsRecord ? Members.SelectMany(m => m.Fields.Skip(1)) : Fields.Skip(1);

    /// <summary>
    /// How this group's wire column is named in a diagnostic: the group for a scalar one,
    /// and `Group.Member` for a record's.
    /// </summary>
    public string WireColumnName(Field tagCarrier)
    {
        if (!IsRecord)
            return Name;

        var member = Members.FirstOrDefault(m => ReferenceEquals(m.FirstField, tagCarrier));
        return (member is null) ? Name : $"{Name}{Helpers.NestedName.MemberSeparator}{member.Name}";
    }

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
    /// <remarks>
    /// A record group is never one. There is nothing to be unique about - the key would
    /// have to be the whole record - and none of the generated lookups can index by one.
    /// </remarks>
    public bool IsIndexer => !IsRecord && (Fields.Count > 0) && !IsArray && FirstField.Indexing;

    /// <summary>Whether this group references another table.</summary>
    /// <remarks>
    /// False for a record group: a reference belongs to a member rather than to the
    /// record, so the question is asked of <see cref="RecordMember.IsRef"/> instead.
    /// </remarks>
    public bool IsRef => !IsRecord && (Fields.Count > 0) && Fields[0].IsRef;

    /// <summary>
    /// Whether consumers should see this as an array, from either cause: several
    /// numbered columns folded together, or a single column holding a delimited
    /// list.
    /// </summary>
    /// <remarks>
    /// For a record group the count of elements decides it, exactly as the count of
    /// columns does for a scalar group: `Pos.X`/`Pos.Y` is one record and
    /// `Slot1.Id`/`Slot2.Id` is two.
    /// </remarks>
    public bool IsArray => IsRecord
                        ? (RecordElementCount > 1 || (RecordElementCount == 1 && TreatAsArrayEvenIfSingleItem))
                        : Fields.Count > 1
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
    public bool IsVariableLengthArray => Fields.Count == 1 && FirstField is not null && FirstField.IsArray;

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
    /// <remarks>
    /// Taken from the first column whichever kind the group is. The folding requires a
    /// record group's members to agree on it, because a record half of whose members are
    /// absent from a build is not a shape any generator has.
    /// </remarks>
    public TargetSide TargetSide
    {
        get
        {
            var first = AnyField;
            return (first is not null) ? first.TargetSide : TargetSide.Both;
        }
    }

    /// <summary>
    /// First column of the group, which carries the properties shared by all of them.
    /// Null only for an empty group, which should not occur.
    /// </summary>
    /// <remarks>
    /// Scalar groups only. A record group has no single column that speaks for it, so it
    /// answers null here on purpose - a caller reaching for this on a record is asking a
    /// question that has no answer, and null surfaces that rather than hiding it behind
    /// one arbitrary member's column. Use <see cref="AnyField"/> for the properties every
    /// column of the group does share, such as target side.
    /// </remarks>
    public Field FirstField => (!IsRecord && Fields.Count > 0) ? Fields[0] : null;

    /// <summary>
    /// Some column of this group, for the properties every column shares regardless of
    /// kind. Null only for an empty group.
    /// </summary>
    public Field AnyField => IsRecord ? Members.FirstOrDefault()?.FirstField : FirstField;
}
