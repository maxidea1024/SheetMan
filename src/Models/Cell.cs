using SheetMan.Models.Raw;

namespace SheetMan.Models;

/// <summary>Cell</summary>
public class Cell
{
    /// <summary>Raw cell</summary>
    public required RawCell RawCell { get; set; }

    /// <summary>Imported value</summary>
    public required object Value { get; set; }
}
