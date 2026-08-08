using System.Collections.Generic;
using System.Linq;

namespace SheetMan.Models;

/// <summary>
/// One column of a binary file: the unit a wire tag identifies and a reader skips past.
/// </summary>
/// <remarks>
/// Not the same unit as a <see cref="SerialField"/>, and that is the whole reason this
/// type exists.
///
///   * a scalar group is one wire column, however many sheet columns folded into it -
///     `Text1`/`Text2` is a single fixed-array column;
///   * a record group is one wire column **per member**, because the file stores a struct
///     of arrays where the API presents an array of structs.
///
/// Keeping the difference in one place is what stops the writer, the tag assignment and
/// the baseline check from each deciding it separately. They disagreed once already: tag
/// assignment assumed one tag per group, which is right for every table written before
/// records existed and wrong the moment one is not.
///
/// spec/nested-fields.md has the layout and why it is a struct of arrays.
/// </remarks>
public sealed class WireColumn
{
    /// <summary>The group this column belongs to.</summary>
    public SerialField Group { get; init; }

    /// <summary>
    /// The member this column holds, or null when <see cref="Group"/> is a scalar one.
    /// </summary>
    public RecordMember Member { get; init; }

    /// <summary>
    /// How this column is named in a diagnostic and in the baseline: the group's name, or
    /// `Group.Member` for a record's.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The cells this column reads from each row, in element order. One entry for a
    /// scalar, one per element for a fixed array.
    /// </summary>
    public IReadOnlyList<Field> Cells { get; init; }

    /// <summary>
    /// The column that carries the wire tag, which is the first of <see cref="Cells"/>.
    /// </summary>
    public Field TagCarrier => Cells[0];

    /// <summary>Whether this column's values are references stored as a target's index.</summary>
    public bool IsRef { get; init; }

    /// <summary>The type of one value.</summary>
    public ValueType ElementType { get; init; }

    /// <summary>
    /// The declared type, which is the array type itself for a delimited cell. Only
    /// <see cref="ElementType"/> differs from it, and only for those.
    /// </summary>
    public ValueType Type { get; init; }

    /// <summary>
    /// Whether each row carries its own length, which is true only of a delimited array
    /// cell. A record's member cannot be one: nesting an array inside a record is a shape
    /// the notation refuses.
    /// </summary>
    public bool IsVariableLengthArray { get; init; }

    /// <summary>Whether every row holds the same number of elements, known at generation time.</summary>
    public bool IsFixedArray => !IsVariableLengthArray && Cells.Count > 1;

    /// <summary>
    /// The wire columns of a table, in the order they are written to a file.
    /// </summary>
    /// <remarks>
    /// Group order, and within a record group, member order - so a table's layout follows
    /// the sheet and adding a member appends rather than inserts.
    /// </remarks>
    public static List<WireColumn> Of(Table table)
    {
        var result = new List<WireColumn>();

        foreach (var group in table.SerialFields)
        {
            if (!group.IsRecord)
            {
                result.Add(new WireColumn
                {
                    Group = group,
                    Member = null,
                    Name = group.Name,
                    Cells = group.Fields,
                    IsRef = group.IsRef,
                    ElementType = group.ElementType,
                    Type = group.Type,
                    IsVariableLengthArray = group.IsVariableLengthArray,
                });

                continue;
            }

            foreach (var member in group.Members)
            {
                result.Add(new WireColumn
                {
                    Group = group,
                    Member = member,
                    Name = $"{group.Name}{Helpers.NestedName.MemberSeparator}{member.Name}",
                    Cells = member.Fields,
                    IsRef = member.IsRef,
                    ElementType = member.ElementType,
                    Type = member.Type,

                    // A member is one value per element by construction. The notation has
                    // no way to write an array inside a record, and refuses the name that
                    // would try.
                    IsVariableLengthArray = false,
                });
            }
        }

        return result;
    }
}
