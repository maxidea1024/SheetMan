using System;
using System.Linq;
using SheetMan.History;
using SheetMan.Models;
using Xunit;

using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.Tests
{
    /// <summary>
    /// Reading the history back.
    ///
    /// The half a designer actually uses. What is checked here is mostly about answers that
    /// mislead rather than answers that fail: a range whose ends are off by one snapshot, a
    /// truncated list that does not admit it, a changeset spanning six people's commits
    /// presented as one person's work.
    /// </summary>
    [Collection("databases")]
    public class HistoryQueryTests
    {
        private readonly string _project = "q" + Guid.NewGuid().ToString("N").Substring(0, 12);
        private readonly string _connectionString;

        private static readonly (string, ValueType)[] Columns =
        {
            ("id", ValueType.Int32),
            ("name", ValueType.String),
            ("power", ValueType.Int32),
        };

        public HistoryQueryTests() => _connectionString = HistoryTestBed.EnsureDatabase();

        // ------------------------------------------------------------- fixtures

        private static Model Items(params object[][] rows)
            => ModelFactory.Of(ModelFactory.Table("Item", Columns, rows));

        private static CommitInfo Commit(string hash, string author, int minute)
            => CommitInfo.Resolve(
                new Options
                {
                    Repository = System.IO.Path.GetTempPath(),
                    Commit = hash,
                    Branch = "main",
                    CommitAuthor = $"{author} <{author.ToLowerInvariant()}@example.com>",
                    CommitDate = $"2026-08-03T10:{minute:00}:00+09:00",
                },
                new SheetMan.Recipe.RecipeModel());

        private void Record(Model model, CommitInfo commit)
        {
            using var store = HistoryStore.Open(_connectionString, _project, "main");

            HistoryRecorder.Record(
                store, SummaryBuilder.Build(model, commit, null), ModelFingerprint.Of(model),
                commit, new HistoryRecipe(), out _);
        }

        private HistoryQuery Query() => HistoryQuery.Open(_connectionString);

        /// <summary>
        /// Three commits by three people, each changing one cell. Small enough to state the
        /// expected answer exactly, which is what makes a range test worth anything.
        /// </summary>
        private void ThreeCommits()
        {
            Record(Items(new object[] { 1, "Sword", 10 }), Commit("aaaaaaaa1111", "Kim", 0));
            Record(Items(new object[] { 1, "Sword", 20 }), Commit("bbbbbbbb2222", "Park", 5));
            Record(Items(new object[] { 1, "Sword", 30 }), Commit("cccccccc3333", "Lee", 10));
        }

        // ---------------------------------------------------------------- tests

        [Fact]
        public void A_range_reports_who_changed_what()
        {
            ThreeCommits();

            using var query = Query();

            var document = query.Diff(_project, "main", from: "aaaaaaaa1111", to: "cccccccc3333");

            Assert.Equal(2, document.Snapshots.Count);

            // Oldest first, which is the direction changes are read in.
            Assert.Equal(new[] { "bbbbbbbb2222", "cccccccc3333" },
                document.Snapshots.Select(s => s.Commit));

            var second = document.Snapshots[0];

            Assert.Equal("Park", second.AuthorName);

            var cell = Assert.Single(second.Cells);
            Assert.Equal("Item", cell.Table);
            Assert.Equal("power", cell.Field);
            Assert.Equal("10", cell.Before);
            Assert.Equal("20", cell.After);
        }

        /// <summary>
        /// `from` is the state compared from, so its own changes belong to the range before
        /// this one. Getting this off by one puts somebody else's edit in your report.
        /// </summary>
        [Fact]
        public void The_start_of_a_range_is_exclusive_and_the_end_is_inclusive()
        {
            ThreeCommits();

            using var query = Query();

            var document = query.Diff(_project, "main", from: "bbbbbbbb2222", to: "cccccccc3333");

            var only = Assert.Single(document.Snapshots);

            Assert.Equal("cccccccc3333", only.Commit);
            Assert.Equal("Lee", only.AuthorName);
        }

        [Fact]
        public void A_range_with_no_ends_covers_the_whole_branch()
        {
            ThreeCommits();

            using var query = Query();

            Assert.Equal(3, query.Diff(_project, "main").Snapshots.Count);
        }

        [Fact]
        public void A_commit_can_be_named_by_a_prefix()
        {
            ThreeCommits();

            using var query = Query();

            Assert.Single(query.Diff(_project, "main", from: "bbbb", to: "cccc").Snapshots);
        }

        [Fact]
        public void An_ambiguous_prefix_is_refused_rather_than_guessed()
        {
            Record(Items(new object[] { 1, "Sword", 10 }), Commit("ffff1111", "Kim", 0));
            Record(Items(new object[] { 1, "Sword", 20 }), Commit("ffff2222", "Park", 5));

            using var query = Query();

            var ex = Assert.Throws<SheetManException>(() => query.Diff(_project, "main", to: "ffff"));

            Assert.Contains("matches 2 commits", ex.Message);
        }

        [Fact]
        public void A_commit_the_history_does_not_hold_is_reported_as_such()
        {
            ThreeCommits();

            using var query = Query();

            var ex = Assert.Throws<SheetManException>(() => query.Diff(_project, "main", to: "9999"));

            Assert.Contains("no snapshot", ex.Message);
        }

        [Fact]
        public void A_range_the_wrong_way_round_is_refused()
        {
            ThreeCommits();

            using var query = Query();

            var ex = Assert.Throws<SheetManException>(
                () => query.Diff(_project, "main", from: "cccccccc3333", to: "aaaaaaaa1111"));

            Assert.Contains("comes after", ex.Message);
        }

        [Fact]
        public void A_report_can_be_narrowed_to_one_person()
        {
            ThreeCommits();

            using var query = Query();

            var document = query.Diff(_project, "main", author: "Park");

            var only = Assert.Single(document.Snapshots);
            Assert.Equal("Park", only.AuthorName);
        }

        [Fact]
        public void A_report_can_be_narrowed_to_one_table()
        {
            Record(ModelFactory.Of(
                ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 10 }),
                ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 30 })),
                Commit("aaaa1111", "Kim", 0));

            Record(ModelFactory.Of(
                ModelFactory.Table("Item", Columns, new object[] { 1, "Sword", 12 }),
                ModelFactory.Table("Armour", Columns, new object[] { 1, "Plate", 33 })),
                Commit("bbbb2222", "Park", 5));

            using var query = Query();

            var document = query.Diff(_project, "main", from: "aaaa1111", table: "Item");

            var cell = Assert.Single(document.Snapshots[0].Cells);
            Assert.Equal("Item", cell.Table);
        }

        /// <summary>
        /// A cut list that does not say it was cut reads as a complete one, and the
        /// conclusion drawn from it - "nothing else changed" - is wrong.
        /// </summary>
        [Fact]
        public void A_truncated_report_says_how_much_it_left_out()
        {
            var first = Enumerable.Range(1, 20).Select(i => new object[] { i, "n" + i, i }).ToArray();
            var second = Enumerable.Range(1, 20).Select(i => new object[] { i, "n" + i, i * 2 }).ToArray();

            Record(Items(first), Commit("aaaa1111", "Kim", 0));
            Record(Items(second), Commit("bbbb2222", "Park", 5));

            using var query = Query();

            var document = query.Diff(_project, "main", from: "aaaa1111", limit: 5);

            Assert.True(document.Query.Truncated);
            Assert.Equal(5, document.Totals.Cells + document.Totals.Rows + document.Totals.Schema);

            // 20 rows changed, so 20 cell changes and 20 row changes; five were reported.
            Assert.Equal(35, document.Query.Omitted);
        }

        [Fact]
        public void An_untruncated_report_says_so_too()
        {
            ThreeCommits();

            using var query = Query();

            var document = query.Diff(_project, "main");

            Assert.False(document.Query.Truncated);
            Assert.Equal(0, document.Query.Omitted);
        }

        /// <summary>
        /// A snapshot recorded from an identifier git knows nothing about cannot be shown to
        /// follow its parent - and claiming a gap that may not exist would put a warning on
        /// a clean report. The unknown case reads as "follows".
        /// </summary>
        [Fact]
        public void A_snapshot_whose_ancestry_cannot_be_checked_is_not_reported_as_a_gap()
        {
            ThreeCommits();

            using var query = Query();

            Assert.Equal(0, query.Diff(_project, "main").Totals.Gaps);
        }

        // ------------------------------------------------------------ statistics

        [Fact]
        public void Statistics_are_read_back_as_the_conversion_recorded_them()
        {
            Record(Items(
                new object[] { 1, "Sword", 10 },
                new object[] { 2, "Shield", 20 }), Commit("aaaa1111", "Kim", 0));

            using var query = Query();

            var summary = query.Stats(_project, "main");

            Assert.Equal(1, summary.Data.Totals.Tables);
            Assert.Equal(2, summary.Data.Totals.Rows);
            Assert.Equal("aaaa1111", summary.Run.Commit.Hash);
        }

        /// <summary>
        /// An old commit's statistics describe that commit, not today's workbook - which is
        /// why they are stored rather than recomputed.
        /// </summary>
        [Fact]
        public void Statistics_of_an_older_commit_describe_that_commit()
        {
            Record(Items(new object[] { 1, "Sword", 10 }), Commit("aaaa1111", "Kim", 0));

            Record(Items(
                new object[] { 1, "Sword", 10 },
                new object[] { 2, "Shield", 20 },
                new object[] { 3, "Bow", 15 }), Commit("bbbb2222", "Park", 5));

            using var query = Query();

            Assert.Equal(1, query.Stats(_project, "main", "aaaa1111").Data.Totals.Rows);
            Assert.Equal(3, query.Stats(_project, "main").Data.Totals.Rows);
        }

        [Fact]
        public void A_trend_runs_oldest_first()
        {
            Record(Items(new object[] { 1, "Sword", 10 }), Commit("aaaa1111", "Kim", 0));

            Record(Items(
                new object[] { 1, "Sword", 10 },
                new object[] { 2, "Shield", 20 }), Commit("bbbb2222", "Park", 5));

            using var query = Query();

            var trend = query.Trend(_project, "main", "rows");

            Assert.Equal(new long[] { 1, 2 }, trend.Select(p => p.Value));
            Assert.Equal("aaaa1111", trend[0].Commit);
        }

        [Fact]
        public void A_metric_that_is_not_a_metric_is_refused()
        {
            ThreeCommits();

            using var query = Query();

            Assert.Throws<SheetManException>(() => query.Trend(_project, "main", "vibes"));
        }

        [Fact]
        public void Authors_are_summarised_over_a_range()
        {
            ThreeCommits();

            using var query = Query();

            var authors = query.Authors(_project, "main", from: "aaaaaaaa1111");

            Assert.Equal(2, authors.Count);
            Assert.All(authors, a => Assert.Equal(1, a.Snapshots));
            Assert.Contains(authors, a => a.Name == "Park");
            Assert.DoesNotContain(authors, a => a.Name == "Kim");
        }

        /// <summary>
        /// The question a designer actually asks: this number is wrong, when did it become
        /// this, and who made it so.
        /// </summary>
        [Fact]
        public void One_cells_whole_history_can_be_followed()
        {
            ThreeCommits();

            using var query = Query();

            var entries = query.CellHistory(_project, "main", "Item", rowKey: "1", field: "power");

            // Newest first: the question starts from the value that is wrong now.
            Assert.Equal(new[] { "Lee", "Park", "Kim" }, entries.Select(e => e.AuthorName));
            Assert.Equal(new[] { "30", "20", "10" }, entries.Select(e => e.After));
            Assert.Equal(new[] { "20", "10", null }, entries.Select(e => e.Before));
        }

        [Fact]
        public void Branches_and_tables_can_be_listed()
        {
            ThreeCommits();

            using var query = Query();

            Assert.Contains(_project, query.Projects());
            Assert.Equal(new[] { "main" }, query.Branches(_project));
            Assert.Equal(new[] { "Item" }, query.Tables(_project, "main"));
            Assert.Equal("main", query.DefaultBranch(_project));
        }

        [Fact]
        public void Snapshots_can_be_listed_newest_first_with_their_counts()
        {
            ThreeCommits();

            using var query = Query();

            var snapshots = query.Snapshots(_project, "main");

            Assert.Equal(new[] { "cccccccc3333", "bbbbbbbb2222", "aaaaaaaa1111" },
                snapshots.Select(s => s.Commit));

            Assert.Equal(1, snapshots[0].Counts.Cells);
        }
    }
}
