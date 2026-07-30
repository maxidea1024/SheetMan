using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// What every generated page carries: its title and the line recording who built it.
    /// </summary>
    internal abstract class HtmlPageView
    {
        public string Title { get; set; }

        /// <summary>Build time, already formatted. The golden comparison normalizes it away.</summary>
        public string CreatedAt { get; set; }

        public string User { get; set; }
    }

    internal sealed class HtmlIndexView : HtmlPageView
    {
        public IReadOnlyList<HtmlSummaryEntryView> Enums { get; set; }
        public IReadOnlyList<HtmlSummaryEntryView> Tables { get; set; }
        public IReadOnlyList<HtmlSummaryEntryView> ConstantSets { get; set; }
        public IReadOnlyList<HtmlSourceSheetView> SourceSheets { get; set; }
    }

    internal sealed class HtmlSummaryEntryView
    {
        public string Name { get; set; }

        /// <summary>Escaped comment, or empty. The template decides whether to show a dash.</summary>
        public string Comment { get; set; }
    }

    internal sealed class HtmlSourceSheetView
    {
        public string Url { get; set; }
        public string Filename { get; set; }
    }

    internal sealed class HtmlEnumPageView : HtmlPageView
    {
        public string Name { get; set; }

        /// <summary>A rendered anchor back to the source sheet, or empty when there is none.</summary>
        public string SourceLink { get; set; }

        public string Comment { get; set; }

        public IReadOnlyList<HtmlEnumLabelView> Labels { get; set; }
    }

    internal sealed class HtmlEnumLabelView
    {
        public int No { get; set; }
        public string Name { get; set; }
        public string SourceLink { get; set; }
        public string Value { get; set; }
        public string Comment { get; set; }
    }

    internal sealed class HtmlConstantSetsPageView : HtmlPageView
    {
        public IReadOnlyList<HtmlConstantSetView> Sets { get; set; }
    }

    internal sealed class HtmlConstantSetView
    {
        public string Name { get; set; }
        public string SourceLink { get; set; }
        public string Comment { get; set; }
        public IReadOnlyList<HtmlConstantView> Constants { get; set; }
    }

    internal sealed class HtmlConstantView
    {
        public int No { get; set; }

        /// <summary>The constant's own name, which the row's anchor id is built from.</summary>
        public string Name { get; set; }

        /// <summary>Rendered cell contents, because an enum constant shows links where a
        /// plain one shows text.</summary>
        public string NameCell { get; set; }

        public string TypeCell { get; set; }
        public string ValueCell { get; set; }
        public string Comment { get; set; }
    }

    internal sealed class HtmlTablesPageView : HtmlPageView
    {
        public IReadOnlyList<HtmlTableView> Tables { get; set; }
    }

    internal sealed class HtmlTableView
    {
        public string Name { get; set; }
        public string SourceLink { get; set; }
        public string Comment { get; set; }
        public int RecordCount { get; set; }

        /// <summary>Complete `&lt;th&gt;` elements for the column-name row, one per line.</summary>
        public IReadOnlyList<string> NameCells { get; set; }

        /// <summary>Complete `&lt;th&gt;` elements for the description row, one per line.</summary>
        public IReadOnlyList<string> CommentCells { get; set; }

        /// <summary>
        /// Complete `&lt;th&gt;` elements for the type row.
        ///
        /// These go on one line, unlike the rows above, because the printer used Print
        /// rather than PrintLine for them - and `&lt;/thead&gt;` lands at the end of that
        /// same line as a result. Reproduced rather than tidied, so the golden pages do
        /// not move.
        /// </summary>
        public IReadOnlyList<string> TypeCells { get; set; }

        /// <summary>Complete `&lt;th&gt;` elements for the target-side row, one per line.</summary>
        public IReadOnlyList<string> SideCells { get; set; }

        public IReadOnlyList<HtmlRowView> Rows { get; set; }
    }

    internal sealed class HtmlRowView
    {
        /// <summary>
        /// Complete `&lt;td&gt;` elements, rendered here because a cell's markup depends on
        /// the field's type. They go on one line, with `&lt;/tr&gt;` at the end of it.
        /// </summary>
        public IReadOnlyList<string> Cells { get; set; }
    }
}
