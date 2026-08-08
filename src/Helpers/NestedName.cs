namespace SheetMan.Helpers;

/// <summary>
/// The `Group.Member` notation that folds several columns into a record.
///
/// `Pos.X` and `Pos.Y` are one record; `Slot1.Id` and `Slot2.Id` are an array of them,
/// with the array length coming from the serial number on the group part exactly as it
/// does for a plain <see cref="SheetMan.Models.SerialField"/>. The rules and the reasons
/// are in spec/nested-fields.md.
///
/// Splitting happens before Pascal-casing, so each part is normalized on its own and a
/// separator can never be produced or consumed by the case conversion.
/// </summary>
public static class NestedName
{
    /// <summary>
    /// What separates a group from its member. A `.` because it is the one character a
    /// field name could not already contain - <see cref="Extensions.StringExtensions.IsValidIdentifier"/>
    /// rejects it - so the notation takes over names that were errors rather than
    /// changing the meaning of names that worked.
    /// </summary>
    public const char MemberSeparator = '.';

    /// <summary>
    /// Splits a column name into its group and member parts.
    /// </summary>
    /// <param name="rawName">
    /// The name as written in the sheet, after the wire tag and any `*` have been taken
    /// off, and before Pascal-casing.
    /// </param>
    /// <param name="group">The part before the separator, or the whole name when there is none.</param>
    /// <param name="member">The part after the separator, or null when the name is not nested.</param>
    /// <param name="problem">
    /// Why the name uses the separator in a way this does not support, or null when it is
    /// fine. Phrased as the middle of a sentence so callers can name the cell.
    /// </param>
    /// <returns>False only when <paramref name="problem"/> is set. A name with no
    /// separator is not a failure - it is the ordinary case, and reports itself by
    /// leaving <paramref name="member"/> null.</returns>
    public static bool TrySplit(string rawName, out string group, out string member, out string problem)
    {
        group = rawName;
        member = null;
        problem = null;

        if (string.IsNullOrEmpty(rawName))
            return true;

        int first = rawName.IndexOf(MemberSeparator);
        if (first < 0)
            return true;

        // Depth beyond one is refused rather than flattened. Of the nesting that occurs
        // in a real project's 615 tables, depth one covers 99.5% of the leaves; the rest
        // is a shape to add deliberately, not one to half-support by accident.
        int second = rawName.IndexOf(MemberSeparator, first + 1);
        if (second >= 0)
        {
            problem = $"uses `{MemberSeparator}` more than once. A record group may hold "
                    + "plain columns, but a member cannot itself be a group.";
            return false;
        }

        string groupPart = rawName.Substring(0, first).Trim();
        string memberPart = rawName.Substring(first + 1).Trim();

        // Both sides have to name something. `.Id` and `Slot1.` are more likely a typo
        // than an intent, and either one would otherwise produce a group or member with
        // an empty name that fails much later with a worse message.
        if (groupPart.Length == 0 || memberPart.Length == 0)
        {
            problem = $"has an empty part either side of `{MemberSeparator}`. "
                    + $"Write it as `Group{MemberSeparator}Member`, as in `Slot1{MemberSeparator}Id`.";
            return false;
        }

        group = groupPart;
        member = memberPart;
        return true;
    }

    /// <summary>
    /// Whether a name carries the separator at all, without deciding whether it does so
    /// legally. For the callers that only need to know a name is not a plain column.
    /// </summary>
    public static bool LooksNested(string rawName)
        => !string.IsNullOrEmpty(rawName) && rawName.IndexOf(MemberSeparator) >= 0;
}
