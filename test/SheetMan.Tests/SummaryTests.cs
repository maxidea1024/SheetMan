using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SheetMan.History;
using Xunit;

using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.Tests;

/// <summary>
/// The document every view of a conversion is rendered from.
///
/// Its byte-level shape is already held by the golden trees, which the three core
/// scenarios record a summary into. What is here is the part a golden tree cannot
/// state: that the three of them describe the same thing, and that the counting is
/// counting what it claims to.
/// </summary>
public class SummaryTests
{
    private static JsonElement Read(string scenario)
    {
        string path = Path.Combine(RepoLayout.OutputDir(scenario), "summary", "summary.json");

        Assert.True(File.Exists(path), $"Scenario `{scenario}` produced no summary at {path}.");

        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static JsonElement Convert(string scenario)
    {
        var conversion = SheetManRunner.Convert(scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        return Read(scenario);
    }

    /// <summary>
    /// The gate for TargetContext.FullModel, and the reason it exists.
    ///
    /// core, core-client and core-server convert the same workbook into three different
    /// cuts: the client build leaves out the server's tables and the server build
    /// leaves out the client's. Their summaries must nonetheless be identical, because
    /// a summary describes what the sheets declared rather than what one entry emitted.
    ///
    /// Getting this wrong does not fail. It records the client's build as having
    /// deleted every server table, and the next server build as having added them all
    /// back - a history of edits nobody made, repeating on every alternating build.
    /// </summary>
    [Fact]
    public void A_narrowed_run_still_describes_the_whole_model()
    {
        var whole = Convert("core");
        var client = Convert("core-client");
        var server = Convert("core-server");

        // The model fingerprint first, because it is one string and says the most.
        string hash = whole.GetProperty("data").GetProperty("hash").GetString();

        Assert.Equal(hash, client.GetProperty("data").GetProperty("hash").GetString());
        Assert.Equal(hash, server.GetProperty("data").GetProperty("hash").GetString());

        // And the tables by name, so a failure says which one went missing rather than
        // only that two hashes differ.
        var expected = TableNames(whole);

        Assert.Equal(expected, TableNames(client));
        Assert.Equal(expected, TableNames(server));

        // Not a vacuous comparison: the fixture has a table on each side, and the
        // outputs beside the summary really do leave them out.
        Assert.Contains("ServerTuning", expected);
        Assert.Contains("ClientStrings", expected);

        Assert.False(File.Exists(Path.Combine(
            RepoLayout.OutputDir("core-client"), "binary", "ServerTuning.scb")));

        Assert.False(File.Exists(Path.Combine(
            RepoLayout.OutputDir("core-server"), "binary", "ClientStrings.scb")));
    }

    /// <summary>
    /// What the run was narrowed to is recorded, so a reader of the document can tell
    /// that the outputs beside it are a cut even though this is not.
    /// </summary>
    [Fact]
    public void The_side_the_run_was_narrowed_to_is_recorded()
    {
        var conversion = SheetManRunner.Convert("core", null, "--target-side", "server");

        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var summary = Read("core");

        Assert.Equal("s", summary.GetProperty("run").GetProperty("requestedTargetSide").GetString());

        // Still everything, though. The run was narrowed; the description was not.
        Assert.Contains("ClientStrings", TableNames(summary));
    }

    /// <summary>
    /// Two conversions of the same sheets describe them identically, or a golden
    /// comparison would be measuring the clock.
    /// </summary>
    [Fact]
    public void The_description_does_not_change_between_runs()
    {
        string first = JsonSerializer.Serialize(Convert("core").GetProperty("data"));
        string second = JsonSerializer.Serialize(Convert("core").GetProperty("data"));

        Assert.Equal(first, second);
    }

    // ------------------------------------------------------------- counting

    private static SummaryDocument Describe(SheetMan.Models.Model model)
        => SummaryBuilder.Build(model, UnidentifiedCommit(), null);

    private static CommitInfo UnidentifiedCommit()
        => CommitInfo.Resolve(new Options { Repository = Path.GetTempPath() }, new SheetMan.Recipe.RecipeModel());

    private static readonly (string, ValueType)[] Columns =
    {
        ("id", ValueType.Int32),
        ("name", ValueType.String),
        ("grade", ValueType.Int32),
    };

    [Fact]
    public void A_blank_cell_is_counted_as_empty_and_an_empty_string_is_not()
    {
        var document = Describe(ModelFactory.Of(ModelFactory.Table("Item", Columns,
            new object[] { 1, null, 10 },
            new object[] { 2, "", 20 })));

        var name = document.Data.Tables[0].Fields[1];

        Assert.Equal(1, name.EmptyCount);

        // Blank and empty are different values of the column, which is why the history
        // reports a change when one becomes the other.
        Assert.Equal(2, name.DistinctCount);
    }

    [Fact]
    public void Content_bytes_measure_the_values_rather_than_the_cells()
    {
        var document = Describe(ModelFactory.Of(ModelFactory.Table("Item", Columns,
            new object[] { 1, "한", 10 })));

        // "1" + three UTF-8 bytes for the Korean character + "10".
        Assert.Equal(1 + 3 + 2, document.Data.Tables[0].ContentBytes);
    }

    [Fact]
    public void A_length_is_reported_only_where_a_length_means_something()
    {
        var document = Describe(ModelFactory.Of(ModelFactory.Table("Item", Columns,
            new object[] { 1048576, "Sword", 10 })));

        var fields = document.Data.Tables[0].Fields;

        Assert.Equal(5, fields[1].MaxLength);

        // The width of `1048576` is a fact about the number, not about the column.
        Assert.Null(fields[0].MaxLength);
    }

    [Fact]
    public void The_first_column_is_the_index()
    {
        var fields = Describe(ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }))).Data.Tables[0].Fields;

        Assert.True(fields[0].IsIndex);
        Assert.False(fields[1].IsIndex);
    }

    /// <summary>
    /// Counting distinct values costs a set of them, so it stops - and says that it
    /// stopped, rather than reporting the number it happened to reach as if it were
    /// the answer.
    /// </summary>
    [Fact]
    public void Counting_distinct_values_gives_up_out_loud()
    {
        var rows = Enumerable.Range(0, 12_000)
                             .Select(i => new object[] { i, "row-" + i, i })
                             .ToArray();

        var field = Describe(ModelFactory.Of(ModelFactory.Table("Item", Columns, rows)))
                    .Data.Tables[0].Fields[1];

        Assert.True(field.DistinctCapped);
        Assert.Equal(10_000, field.DistinctCount);
    }

    [Fact]
    public void A_column_with_few_values_is_reported_as_having_few()
    {
        var rows = Enumerable.Range(0, 500)
                             .Select(i => new object[] { i, i % 3 == 0 ? "a" : "b", i })
                             .ToArray();

        var field = Describe(ModelFactory.Of(ModelFactory.Table("Item", Columns, rows)))
                    .Data.Tables[0].Fields[1];

        Assert.False(field.DistinctCapped);
        Assert.Equal(2, field.DistinctCount);
    }

    /// <summary>
    /// An unidentified conversion says so rather than leaving the question open, and
    /// nothing downstream may credit its changes to anyone.
    /// </summary>
    [Fact]
    public void A_conversion_nothing_identified_is_marked_unattributable()
    {
        var commit = Describe(ModelFactory.Of(
            ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }))).Run.Commit;

        Assert.Null(commit.Hash);
        Assert.False(commit.Attributable);
        Assert.Equal("none", commit.Origin);
    }

    private static IReadOnlyList<string> TableNames(JsonElement summary)
        => summary.GetProperty("data").GetProperty("tables")
                  .EnumerateArray()
                  .Select(t => t.GetProperty("name").GetString())
                  .OrderBy(n => n, StringComparer.Ordinal)
                  .ToList();
}
