namespace SheetMan.Models.Raw;

/// <summary>
/// What to do about two rows carrying the same index value.
/// </summary>
public enum DuplicateIndexPolicy
{
    /// <summary>Report it, which is what an index is for.</summary>
    Error,

    /// <summary>Keep the row that appeared first and drop the rest, logging each.</summary>
    KeepFirst,

    /// <summary>Keep the row that appeared last and drop the rest, logging each.</summary>
    KeepLast,
}

/// <summary>
/// How a sheet is to be read, carried from the recipe entry that imported it.
/// </summary>
/// <remarks>
/// A tag rather than a lookup: sources append to one shared raw model, so by the time the
/// cooker sees a sheet there is nothing left to say which entry brought it in. Two entries
/// in different layouts therefore work in one run, which is the case that matters: two sets
/// of sheets written to different conventions are read side by side into one model.
/// </remarks>
public sealed class SheetLayout
{
    /// <summary>The layout every sheet gets when a recipe entry does not name one.</summary>
    public static readonly SheetLayout Default = new SheetLayout("sheetman", DuplicateIndexPolicy.Error);

    public SheetLayout(
        string id, DuplicateIndexPolicy onDuplicateIndex, char? arrayDelimiter = null)
    {
        Id = id;
        OnDuplicateIndex = onDuplicateIndex;
        ArrayDelimiter = arrayDelimiter;
    }

    /// <summary>Id of the layout parser that reads this sheet.</summary>
    public string Id { get; }

    /// <summary>Honoured by the `rescue` layout only.</summary>
    public DuplicateIndexPolicy OnDuplicateIndex { get; }

    /// <summary>
    /// Separator for array cells in these sheets, or null to use the recipe-wide one.
    /// </summary>
    /// <remarks>
    /// Per entry because the delimiter is a property of how a sheet was written, not of the
    /// run: two sets of sheets read together were authored by different people under
    /// different conventions, and one of them writing `1|2|3` should not force the other to.
    /// </remarks>
    public char? ArrayDelimiter { get; }

    public override string ToString() => Id;
}
