using System.Collections.Generic;
using Newtonsoft.Json;

namespace SheetMan.Recipe;

/// <summary>
/// What every sheet-reading source takes, whatever it reads the sheets from.
/// </summary>
/// <remarks>
/// A workbook on disk and a Google Sheets document differ in how they are fetched and in
/// nothing else: both arrive as a grid of cells, and both need the same three answers -
/// which sheets to take, how to read what is in them, and what to do with data that a
/// stricter project would not have written. Those answers live here so a recipe says the
/// same thing whichever source it names.
/// </remarks>
public abstract class SheetSourceRecipe
{
    /// <summary>
    /// Which layout parser reads these sheets.
    /// </summary>
    /// <remarks>
    /// `sheetman` is the layout this tool defines: entities are declared with
    /// `~~table:Name~~` markers and can sit anywhere on a sheet.
    ///
    /// `rescue` reads sheets written to another convention - one table per sheet, named by its
    /// tab, with three header rows - as they are, without rewriting them first.
    /// </remarks>
    public string Layout { get; set; } = "sheetman";

    /// <summary>
    /// Separator for array cells in these sheets. Blank takes the recipe-wide setting.
    /// </summary>
    /// <remarks>
    /// The delimiter is a property of how a set of sheets was written, so it belongs beside
    /// the entry that reads them: two sets read in one run were authored under different
    /// conventions, and one of them using `|` should not force the other to. Set here, it
    /// wins over the recipe-wide `ArrayDelimiter` for this entry only.
    /// </remarks>
    public string ArrayDelimiter { get; set; } = "";

    /// <summary>
    /// Sheets to read. An empty list means every sheet.
    /// </summary>
    /// <remarks>
    /// Written either as an array of names or as one semicolon-separated string, whichever
    /// suits its length. `*` and `?` match the way they do in a file glob, so `Char*` takes
    /// every sheet whose name starts with `Char`.
    ///
    /// Naming the sheets is worth the typing when a workbook holds more than the data:
    /// reference tabs, working notes and half-built tables all look like input to a reader
    /// that takes whatever it finds. A named sheet that turns out not to exist is an error
    /// rather than a silent omission, which is the point of naming them.
    /// </remarks>
    [JsonConverter(typeof(StringListConverter))]
    public List<string> IncludeSheets { get; set; } = new List<string>();

    /// <summary>
    /// Sheets to skip, in the same form as <see cref="IncludeSheets"/>. Applied after it.
    /// </summary>
    [JsonConverter(typeof(StringListConverter))]
    public List<string> ExcludeSheets { get; set; } = new List<string>();

    /// <summary>
    /// What to do when two rows carry the same index value: `error`, `keep-first` or
    /// `keep-last`.
    /// </summary>
    /// <remarks>
    /// `error` is the default and the only one that keeps the guarantee an index is for.
    /// The other two are for sheets whose source cannot be corrected right away: refusing to
    /// convert anything until every duplicate is fixed blocks the rest of the data for a
    /// reason that is not the reader's to fix. Both log every row they drop, so the choice is
    /// visible in the run rather than only here.
    ///
    /// Only the `rescue` layout honours this. Sheets written in the `sheetman` layout are
    /// always checked, because there the duplicate can be fixed where it is.
    /// </remarks>
    public string OnDuplicateIndex { get; set; } = "error";
}
