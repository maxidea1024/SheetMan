using System.Collections.Generic;

namespace SheetMan.History
{
    /// <summary>
    /// The answer to a range query: who changed what, between two commits.
    ///
    /// The same shape whether it was asked for on the command line or over HTTP. Two
    /// renderings of one question drift, and the one that is wrong looks exactly like the
    /// one that is right - so there is one, and both entry points serialise it.
    /// </summary>
    public sealed class HistoryDocument
    {
        public int SchemaVersion { get; set; } = 1;

        public HistoryQueryInfo Query { get; set; }

        /// <summary>Oldest first, so a reader follows the changes forwards.</summary>
        public IReadOnlyList<HistorySnapshotView> Snapshots { get; set; }

        public HistoryTotals Totals { get; set; }
    }

    /// <summary>What was asked, echoed back so a stored answer explains itself.</summary>
    public sealed class HistoryQueryInfo
    {
        public string Project { get; set; }
        public string Branch { get; set; }

        /// <summary>
        /// The commit the range starts after.
        ///
        /// Exclusive: it is the state being compared from, so its own changes belong to the
        /// range before this one. Null means from the beginning of the branch.
        /// </summary>
        public string From { get; set; }

        /// <summary>The commit the range ends at, inclusive. Null means the branch's head.</summary>
        public string To { get; set; }

        public string Table { get; set; }
        public string Field { get; set; }
        public string Author { get; set; }

        public string GeneratedAt { get; set; }

        /// <summary>How many changes were asked for at most.</summary>
        public int Limit { get; set; }

        /// <summary>
        /// Whether the answer was cut short.
        ///
        /// Said out loud rather than left to be noticed. A truncated list that does not
        /// admit it reads as a complete one, and the conclusion drawn from it - "nothing
        /// else changed" - is wrong.
        /// </summary>
        public bool Truncated { get; set; }

        /// <summary>How many changes were left out by the limit.</summary>
        public long Omitted { get; set; }
    }

    /// <summary>One snapshot, and what changed to reach it.</summary>
    public sealed class HistorySnapshotView
    {
        public long Id { get; set; }
        public long Seq { get; set; }

        public string Commit { get; set; }
        public string ShortCommit { get; set; }
        public string Branch { get; set; }
        public string AuthorName { get; set; }
        public string AuthorEmail { get; set; }
        public string CommittedAt { get; set; }
        public string Subject { get; set; }
        public string ConvertedAt { get; set; }
        public string ConvertedBy { get; set; }

        public bool Dirty { get; set; }

        /// <summary>Whether these changes can honestly be credited to this commit's author.</summary>
        public bool Attributable { get; set; }

        /// <summary>
        /// Whether the previous snapshot's commit is this one's parent in the repository.
        ///
        /// False means nothing converted the commits in between, so these changes cover
        /// more than this commit made. Reported rather than smoothed over: the alternative
        /// is a report that credits one person with several people's work.
        /// </summary>
        public bool FollowsParent { get; set; }

        /// <summary>The commit these changes are measured from. Null for a branch's first.</summary>
        public string PreviousCommit { get; set; }

        /// <summary>
        /// Whether this snapshot's change detail has been removed to reclaim space.
        ///
        /// Its statistics and its stored summary are still here; the cell-by-cell log is
        /// not. Reported, because an empty changeset that does not say why reads as
        /// "nothing changed in this commit" - which is a different and wrong answer.
        /// </summary>
        public bool Pruned { get; set; }

        public HistoryChangeCounts Counts { get; set; }

        public IReadOnlyList<SchemaChangeView> Schema { get; set; }
        public IReadOnlyList<CellChangeView> Cells { get; set; }
        public IReadOnlyList<RowChangeView> Rows { get; set; }
    }

    public sealed class HistoryChangeCounts
    {
        public int Schema { get; set; }
        public int Rows { get; set; }
        public int Cells { get; set; }
    }

    public sealed class HistoryTotals
    {
        public int Snapshots { get; set; }
        public long Schema { get; set; }
        public long Rows { get; set; }
        public long Cells { get; set; }

        /// <summary>How many snapshots in the range cover more than their own commit.</summary>
        public int Gaps { get; set; }

        /// <summary>How many have had their change detail removed.</summary>
        public int Pruned { get; set; }
    }

    public sealed class SchemaChangeView
    {
        public string EntityKind { get; set; }
        public string Entity { get; set; }
        public string Member { get; set; }
        public string Kind { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        public SummaryLocation Location { get; set; }

        /// <summary>The name this column had before, when it was renamed rather than replaced.</summary>
        public string RenamedFrom { get; set; }
    }

    public sealed class RowChangeView
    {
        public string Table { get; set; }
        public string RowKey { get; set; }
        public string Kind { get; set; }
    }

    public sealed class CellChangeView
    {
        public string Table { get; set; }
        public string RowKey { get; set; }
        public string Field { get; set; }
        public string Kind { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        public SummaryLocation Location { get; set; }
    }

    // -------------------------------------------------------------- listings

    /// <summary>One snapshot, without its changes: what a timeline is drawn from.</summary>
    public sealed class SnapshotListing
    {
        public long Id { get; set; }
        public long Seq { get; set; }
        public string Commit { get; set; }
        public string ShortCommit { get; set; }
        public string Branch { get; set; }
        public string AuthorName { get; set; }
        public string AuthorEmail { get; set; }
        public string CommittedAt { get; set; }
        public string Subject { get; set; }
        public string ConvertedAt { get; set; }
        public bool Dirty { get; set; }
        public bool Attributable { get; set; }
        public bool Pruned { get; set; }

        public HistoryChangeCounts Counts { get; set; }
    }

    /// <summary>One point of a trend line.</summary>
    public sealed class TrendPoint
    {
        public string Commit { get; set; }
        public string ShortCommit { get; set; }
        public string CommittedAt { get; set; }
        public long Value { get; set; }
    }

    /// <summary>How much one person changed over a range.</summary>
    public sealed class AuthorSummary
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public int Snapshots { get; set; }
        public long Cells { get; set; }
        public long Rows { get; set; }
        public long Schema { get; set; }
        public string FirstAt { get; set; }
        public string LastAt { get; set; }
    }

    /// <summary>Every value one cell has held, newest first.</summary>
    public sealed class CellHistoryEntry
    {
        public string Commit { get; set; }
        public string ShortCommit { get; set; }
        public string AuthorName { get; set; }
        public string CommittedAt { get; set; }
        public string Table { get; set; }
        public string RowKey { get; set; }
        public string Field { get; set; }
        public string Kind { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
    }
}
