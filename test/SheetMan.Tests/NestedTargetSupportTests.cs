using Xunit;

namespace SheetMan.Tests;

/// <summary>
/// What happens at the edges of the `Group.Member` notation: a target that does not
/// understand a record, and two sheets whose grouping cannot be made into one.
///
/// All three are refusals, and each is here because the alternative is worse than a
/// failure. A target reaching for the single column a record group does not have would
/// fail somewhere that says nothing about the cause; a sheet with a hole in a group would
/// generate a record carrying a value nothing writes, which reads as a deliberate default.
///
/// spec/nested-fields.md has the rules. The shapes that do work are pinned by the `nested`
/// golden.
/// </summary>
public class NestedTargetSupportTests
{
    /// <summary>
    /// A code target refuses by name, and names the table and the field.
    /// </summary>
    /// <remarks>
    /// Delete this test when every target supports records. Until then it is what keeps
    /// the twelve that do not from emitting output that differs from the one that does for
    /// reasons nobody can see.
    /// </remarks>
    [Fact]
    public void A_target_that_does_not_support_records_refuses_by_name()
    {
        var result = SheetManRunner.Convert("nested-unsupported");

        Assert.False(result.Succeeded, $"Expected a refusal.\n{result.Describe()}");

        string output = result.StdOut + result.StdErr;

        // The target, so it is clear which of the thirteen is the one that cannot.
        Assert.Contains("csharp", output);
        Assert.Contains("does not support nested fields", output);

        // And the table and the first group it could not take, so it is clear what to
        // change if the answer is to write the columns flat. `Pos` rather than `Slot`
        // because it is the first record group in the sheet and the check stops there -
        // listing all of them would bury the one to look at.
        Assert.Contains("Loadout", output);
        Assert.Contains("Pos", output);
        Assert.Contains("record group", output);
    }

    /// <summary>
    /// An element that does not declare every member stops the conversion.
    /// </summary>
    [Fact]
    public void An_element_missing_a_member_is_refused()
    {
        var result = SheetManRunner.Convert("nested-hole");

        Assert.False(result.Succeeded, $"Expected a refusal.\n{result.Describe()}");

        string output = result.StdOut + result.StdErr;

        Assert.Contains("Holed", output);
        Assert.Contains("Label", output);
        Assert.Contains("every member", output);
    }

    /// <summary>
    /// A member that is itself a group is refused while the name is being read, before any
    /// of it reaches the model.
    /// </summary>
    [Fact]
    public void A_member_that_is_itself_a_group_is_refused()
    {
        var result = SheetManRunner.Convert("nested-deep");

        Assert.False(result.Succeeded, $"Expected a refusal.\n{result.Describe()}");

        string output = result.StdOut + result.StdErr;

        Assert.Contains("Slot1.Inner.Id", output);
        Assert.Contains("more than once", output);
    }
}
