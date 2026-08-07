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
/// in different layouts therefore work in one run - which is the case that matters, because
/// adopting a project means reading its workbooks alongside the ones already converted.
/// </remarks>
public sealed class SheetLayout
{
    /// <summary>The layout every sheet gets when a recipe entry does not name one.</summary>
    public static readonly SheetLayout Default = new SheetLayout("sheetman", DuplicateIndexPolicy.Error);

    public SheetLayout(string id, DuplicateIndexPolicy onDuplicateIndex)
    {
        Id = id;
        OnDuplicateIndex = onDuplicateIndex;
    }

    /// <summary>Id of the layout parser that reads this sheet.</summary>
    public string Id { get; }

    /// <summary>Honoured by layouts read from projects that predate this tool.</summary>
    public DuplicateIndexPolicy OnDuplicateIndex { get; }

    public override string ToString() => Id;
}
