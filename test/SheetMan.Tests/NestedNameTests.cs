using SheetMan.Helpers;
using Xunit;

namespace SheetMan.Tests;

/// <summary>
/// Splitting a column name written in the `Group.Member` notation.
///
/// The split happens before Pascal-casing, which is what makes it safe: the separator is
/// gone by the time the case conversion sees either part, so no rule about `_` or about
/// runs of capitals can produce or swallow one. The notation itself is spelled out in
/// spec/nested-fields.md.
/// </summary>
public class NestedNameTests
{
    [Theory]
    // A record with no serial number: one record, not an array.
    [InlineData("Pos.X", "Pos", "X")]
    [InlineData("Pos.Y", "Pos", "Y")]
    // A serial number on the group is what makes it an array. The number stays on the
    // group part, because that is the part the existing folding rules read.
    [InlineData("Slot1.Id", "Slot1", "Id")]
    [InlineData("Slot12.Count", "Slot12", "Count")]
    // Digits in the member are just part of its name and say nothing about folding.
    [InlineData("Slot1.Value2", "Slot1", "Value2")]
    // Neither part is normalized here, so what goes in comes out.
    [InlineData("slot_1.item_id", "slot_1", "item_id")]
    // Spaces around the separator are the kind of thing a spreadsheet cell collects.
    [InlineData("Slot1 . Id", "Slot1", "Id")]
    public void Splits_a_group_from_its_member(string raw, string group, string member)
    {
        Assert.True(NestedName.TrySplit(raw, out string g, out string m, out string problem));
        Assert.Null(problem);
        Assert.Equal(group, g);
        Assert.Equal(member, m);
    }

    [Theory]
    // The ordinary case. Not nested is not a failure - it reports itself with a null
    // member, because every existing column in every existing sheet arrives here.
    [InlineData("Index")]
    [InlineData("Text1")]
    [InlineData("Item1Bonus")]
    [InlineData("already_snake")]
    [InlineData("")]
    [InlineData(null)]
    public void Reports_a_plain_column_by_leaving_the_member_null(string raw)
    {
        Assert.True(NestedName.TrySplit(raw, out string group, out string member, out string problem));
        Assert.Null(problem);
        Assert.Null(member);
        Assert.Equal(raw, group);
    }

    [Theory]
    // Depth beyond one is refused rather than flattened: a member cannot be a group.
    [InlineData("A.B.C")]
    [InlineData("Slot1.Inner.Id")]
    // An empty part either side is a typo far more often than an intent, and letting it
    // through produces a group or member with no name that fails much later.
    [InlineData(".Id")]
    [InlineData("Slot1.")]
    [InlineData(".")]
    [InlineData("Slot1. ")]
    public void Refuses_a_name_it_cannot_support(string raw)
    {
        Assert.False(NestedName.TrySplit(raw, out _, out _, out string problem));
        Assert.NotNull(problem);
        // The message is the middle of a sentence so the caller can put the cell in front
        // of it. If it ever starts with a capital, the diagnostics read wrong.
        Assert.False(char.IsUpper(problem[0]));
    }

    [Theory]
    [InlineData("Slot1.Id", true)]
    [InlineData("A.B.C", true)]
    [InlineData(".", true)]
    [InlineData("Slot1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Tells_a_nested_looking_name_from_a_plain_one(string raw, bool expected)
    {
        Assert.Equal(expected, NestedName.LooksNested(raw));
    }
}
