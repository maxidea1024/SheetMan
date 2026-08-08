using System;
using Xunit;

namespace SheetMan.Tests;

/// <summary>
/// End-to-end regression tests: convert a fixture workbook and compare every
/// produced artifact against a committed golden tree.
///
/// These exist so the .NET 10 port and the feature work that follows it have a
/// way to tell "I changed the output" from "I broke the output". A port must
/// leave every golden file untouched; a deliberate fix updates specific goldens
/// and the resulting diff is the review artifact.
/// </summary>
public class ConversionGoldenTests
{
    [Theory]
    // Everything SheetMan handles: all primitive types, enums (both an explicit
    // zero entry and an auto-inserted one), constants, cross-table record
    // references, serial fields and per-field target sides.
    [InlineData("core")]
    // Values entered as real Excel types rather than text - notably genuine date
    // cells, which are numbers carrying a date format.
    [InlineData("excel-typed")]
    // Leading blank rows and columns, a ragged interior blank row, and two
    // entities on one sheet.
    [InlineData("layout-edge")]
    // The `RefTable.RefFieldName` foreign form, which resolves to the referenced
    // field's type while storing the target's index.
    [InlineData("foreign-field")]
    // The same workbook built for one side only, so entities and fields marked
    // for the other side must be absent from every artifact.
    [InlineData("core-client")]
    [InlineData("core-server")]
    // The `Group.Member` notation: a record, an array of records whose members are of
    // different types, and a scalar serial field beside them that must still fold the way
    // it always has. JSON only for now - the code targets refuse a record by name until
    // each one learns the shape, which NestedTargetSupportTests covers.
    [InlineData("nested")]
    public void Fixture_matches_golden(string scenario)
    {
        var result = SheetManRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{result.Describe()}");

        GoldenComparer.Verify(scenario);
    }
}
