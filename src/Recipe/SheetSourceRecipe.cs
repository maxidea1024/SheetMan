using System.Collections.Generic;
using Newtonsoft.Json;

namespace SheetMan.Recipe
{
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
        /// `rescue` reads the shape a particular existing project already had - one table per
        /// sheet, named by the sheet tab, with three header rows. It exists so a project can be
        /// converted without first rewriting every workbook, and it is not the layout to start
        /// a new project in.
        /// </remarks>
        public string Layout { get; set; } = "sheetman";

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
        /// The other two exist for a layout being adopted rather than authored - a workbook
        /// that has been in use for years may have duplicates in it, and refusing to convert
        /// any of it until every one is fixed helps nobody. Both log every row they drop, so
        /// the choice is visible in the run rather than only in the recipe.
        ///
        /// Only the `rescue` layout honours this. Sheets in the `sheetman` layout are always
        /// checked, because a project authoring in it has no legacy to carry.
        /// </remarks>
        public string OnDuplicateIndex { get; set; } = "error";
    }
}
