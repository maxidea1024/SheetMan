using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SheetMan.Helpers;

/// <summary>
/// A column name from one live project's workbooks, where the name is a path into the row.
/// </summary>
/// <remarks>
/// `patrolBuilding[0]` is element 0 of an array; `character[0]["Id"]` is the `Id` of element
/// 0 of an array of records; `pos["x"]` is the `x` of a record. The original exporter builds
/// its JSON straight out of these.
///
/// Translated into the same three things SheetMan's own `Group.Member` notation produces - a
/// group, a member, and an element ordinal - so one model sits behind two notations. That is
/// the whole point of doing it here: two models would be two answers to "what is this
/// column", and the second one would be wrong somewhere nobody looked.
///
/// The shapes that occur, measured across 615 exported tables, are in
/// doc/uwo-레이아웃-분석-20260808.md - depth one covers 99.5% of them.
/// </remarks>
public static class UwoColumnPath
{
    /// <summary>What a column name turned out to be.</summary>
    public readonly struct Path
    {
        public Path(string group, string member, int ordinal, bool isNested)
        {
            Group = group;
            Member = member;
            Ordinal = ordinal;
            IsNested = isNested;
        }

        /// <summary>The name before any brackets, or the whole name when there are none.</summary>
        public string Group { get; }

        /// <summary>The record member, or empty when the column is not one.</summary>
        public string Member { get; }

        /// <summary>Which element of the group, as the sheet numbered it.</summary>
        public int Ordinal { get; }

        /// <summary>Whether this column is part of a record rather than a plain column.</summary>
        public bool IsNested { get; }
    }

    /// <summary>`[0]` or `["Id"]`, in order.</summary>
    private static readonly Regex Steps = new Regex(@"\[\s*(?:(\d+)|""([^""]*)"")\s*\]", RegexOptions.Compiled);

    /// <summary>
    /// Splits a column name into a group, a member and an element ordinal.
    /// </summary>
    /// <param name="problem">
    /// Why the name is a shape this does not support, or null when it is fine. Phrased as
    /// the middle of a sentence so the caller can name the column.
    /// </param>
    /// <returns>False only when <paramref name="problem"/> is set. A plain column is not a
    /// failure - it is the ordinary case.</returns>
    public static bool TrySplit(string rawName, out Path path, out string problem)
    {
        path = default;
        problem = null;

        if (string.IsNullOrEmpty(rawName))
        {
            problem = "is empty.";
            return false;
        }

        int firstBracket = rawName.IndexOf('[');
        if (firstBracket < 0)
        {
            path = new Path(rawName, "", 0, isNested: false);
            return true;
        }

        string group = rawName.Substring(0, firstBracket).Trim();
        if (group.Length == 0)
        {
            problem = "opens with a bracket, so it names no group.";
            return false;
        }

        var matches = Steps.Matches(rawName, firstBracket);

        // Every bracket has to be one of the two forms. A leftover means the name is
        // something else - a formula, a typo - and guessing at it would put a column of
        // values under a name nobody wrote.
        int consumed = matches.Sum(m => m.Length);
        if (matches.Count == 0 || consumed != rawName.Length - firstBracket)
        {
            problem = $"is not a path this layout reads. Expected `name[0]` or `name[0][\"Member\"]`.";
            return false;
        }

        switch (matches.Count)
        {
            // `patrolBuilding[0]` - an array of plain values. One member, unnamed: the group
            // is the array and there is nothing inside an element to name.
            case 1 when matches[0].Groups[1].Success:
                path = new Path(group, "", Ordinal(matches[0]), isNested: false);
                return ArrayOfScalarsNotSupported(out problem);

            // `pos["x"]` - a record with one element.
            case 1:
                path = new Path(group, matches[0].Groups[2].Value, 0, isNested: true);
                return true;

            // `character[0]["Id"]` - an array of records.
            case 2 when matches[0].Groups[1].Success && matches[1].Groups[2].Success:
                path = new Path(group, matches[1].Groups[2].Value, Ordinal(matches[0]), isNested: true);
                return true;

            default:
                problem = "nests deeper than one level, which is not supported yet.";
                return false;
        }
    }

    /// <summary>
    /// `name[0]` is an array of plain values, which needs the serial-number folding this
    /// layout deliberately turns off.
    /// </summary>
    /// <remarks>
    /// Refused rather than half-handled. Folding by number is off for this layout because a
    /// number in a table name is part of the name, and turning it back on for column names
    /// only would mean two rules about digits in one parser. The way to support these is a
    /// group with one unnamed member, which the record model does not have yet.
    /// </remarks>
    private static bool ArrayOfScalarsNotSupported(out string problem)
    {
        problem = "is an array of plain values (`name[0]`), which is not supported in this layout yet.";
        return false;
    }

    private static int Ordinal(Match match)
        => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
}
