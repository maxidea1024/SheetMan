using Newtonsoft.Json;

namespace SheetMan.Models.Raw;

/// <summary>
/// One cell as read from a sheet, before any meaning is attached to it.
///
/// Everything is text at this stage. The importers render each cell the way the
/// cooker will read it, and the cooker decides what type it should be from the
/// table's type row.
/// </summary>
public class RawCell
{
    /// <summary>
    /// Where the cell is.
    ///
    /// Carried on every cell so a diagnostic raised much later can still point at
    /// the sheet - and at a clickable URL, for Google Sheets sources.
    /// </summary>
    [JsonIgnore]
    public Location Location { get; set; }

    /// <summary>Cell contents as text, trimmed.</summary>
    public string Value { get; set; }

    /// <summary>
    /// The cell's note or comment, with the author prefix that Excel and Google
    /// Sheets prepend removed. Becomes the doc comment of whatever the cell defines.
    /// </summary>
    public string Note { get; set; }
}
