using SheetMan.Models.Raw;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Generic;
using SheetMan.Models;
using System;
using SheetMan.Recipe;
using System.Linq;
using System.Globalization;
using SheetMan.Sources;
using SheetMan.Cooking.Layouts;
using Serilog;

namespace SheetMan.Importers;

[SheetManSource("xlsx", "Sources.Xlsx", Order = 10)]
public class XlsxImporter : Source<RecipeModel.SourceRecipeGroup.XlsxRecipe>
{
    private RawModel _model;

    private string _currentFilename = "";
    private string _currentSheetName = "";

    private SheetImportSettings _settings;

    /// <summary>
    /// Every sheet name the workbooks held, so an unmatched `IncludeSheets` entry can be
    /// answered with what was actually there.
    /// </summary>
    private readonly List<string> _sheetNamesSeen = [];

    protected override void Import(SourceContext context, RecipeModel.SourceRecipeGroup.XlsxRecipe xlsx)
    {
        // An entry with either field left blank is treated as switched off, which is how
        // an entry is commented out in practice: its contents are removed but the object
        // stays in the list.
        if (string.IsNullOrEmpty(xlsx.FileExtensionPatterns) ||
            string.IsNullOrEmpty(xlsx.Path))
        {
            return;
        }

        _model = context.Model;
        _settings = SheetImportSettings.From(xlsx, context.Section);
        _sheetNamesSeen.Clear();

        var fileExtensionPatterns = xlsx.FileExtensionPatterns.Split(";");
        if (fileExtensionPatterns is null || fileExtensionPatterns.Length == 0)
        {
            fileExtensionPatterns = [".xlsx"];
        }
        else
        {
            for (int i = 0; i < fileExtensionPatterns.Length; i++)
                fileExtensionPatterns[i] = fileExtensionPatterns[i].Trim().ToLowerInvariant();
        }

        if (!Directory.Exists(xlsx.Path))
        {
            throw new SheetManException(
                $"Recipe `{context.Section}` reads workbooks from `{xlsx.Path}`, which does not exist.");
        }

        var files = Directory.GetFiles(xlsx.Path, "*.*", SearchOption.AllDirectories);
        foreach (var filename in files)
        {
            if (filename.Contains("/#") || filename.Contains("\\#"))
                continue;

            // Excel's lock file for a workbook somebody has open: `~$Book.xlsx`, same
            // extension and a few hundred bytes of nothing usable. Reading one throws,
            // so leaving a workbook open in Excel used to fail the whole run - and the
            // message named a file the author never created.
            if (Path.GetFileName(filename).StartsWith("~$"))
            {
                Log.Debug($"Skipping `{filename}`: an Excel lock file, not a workbook.");
                continue;
            }

            string fileExtensions = Path.GetExtension(filename).ToLowerInvariant();
            if (!fileExtensionPatterns.Contains(fileExtensions))
                continue;

            ImportXlsx(filename);
        }

        _settings.Filter.ReportUnmatchedIncludes(context.Section, _sheetNamesSeen);
    }

    private void ImportXlsx(string filename)
    {
        // FileShare.ReadWrite so a workbook somebody has open in Excel still reads.
        // Excel holds its own lock on the file; without this the run failed on whichever
        // workbook the designer happened to be looking at.
        using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var workbook = new XSSFWorkbook(fs);
        ImportWorkbook(workbook, filename);
    }

    /// <summary>
    /// One of the workbook's defined names, before it is matched to a sheet's grid.
    /// </summary>
    private sealed record WorkbookName(
        string Name, string SheetName, string Reference,
        int FirstRow, int FirstColumn, int LastRow, int LastColumn);

    /// <summary>
    /// The current workbook's defined names, or null when the layout does not use them.
    /// </summary>
    private List<WorkbookName> _currentWorkbookNames;

    /// <summary>
    /// Names Excel maintains for itself, which are not tables however a layout reads them.
    /// </summary>
    /// <remarks>
    /// `_xlnm.*` is the built-in family - print areas, the autofilter's range - and `_xlfn.*`
    /// marks a function the file was written with. A leading `!_` is how a sheet-scoped name
    /// arrives spelled in some tools. The same three the project whose layout this serves
    /// filters, so the two agree about what a table is.
    /// </remarks>
    private static readonly string[] ReservedNameMarkers = ["_xlnm", "_xlfn", "!_"];

    private void ImportWorkbook(XSSFWorkbook workbook, string filename)
    {
        _currentWorkbookNames = CollectWorkbookNames(workbook, filename);

        int sheetCount = workbook.NumberOfSheets;
        for (int sheetIndex = 0; sheetIndex < sheetCount; sheetIndex++)
        {
            var sheet = workbook.GetSheetAt(sheetIndex);
            var sheetName = sheet.SheetName.Trim();
            if (sheetName.StartsWith("#") || sheetName.StartsWith("//"))
                continue;

            _sheetNamesSeen.Add(sheetName);

            if (!_settings.Filter.Includes(sheetName))
            {
                Log.Information($"Skipping sheet `{sheetName}` of `{filename}`: the recipe does not ask for it.");
                continue;
            }

            ImportSheet(sheet, filename, sheetName);
        }
    }

    private void ImportSheet(ISheet sheet, string filename, string sheetName)
    {
        // Remembered so cell-level diagnostics can name where they came from; the
        // NPOI cell itself knows its row and column but not its workbook or sheet.
        _currentFilename = filename;
        _currentSheetName = sheetName;

        RawSheet rawSheet = new RawSheet
        {
            Layout = _settings.Layout,
            Location = new Location
            {
                Filename = filename,
                Sheet = sheetName,
                Column = 0,
                Row = sheet.FirstRowNum
            }
        };

        for (int rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is null)
                continue;

            List<RawCell> rawRow = [];
            for (int colIndex = 0/*row.FirstCellNum*/; colIndex <= row.LastCellNum; colIndex++)
            {
                var cell = row.GetCell(colIndex);

                string value = SafeCellValue(cell);
                string note = SafeCellComment(cell);

                RawCell rawCell = new()
                {
                    Location = new Location
                    {
                        Filename = filename,
                        Sheet = sheetName,
                        Column = colIndex,
                        Row = rowIndex
                    },
                    Value = value,
                    Note = note
                };
                rawRow.Add(rawCell);
            }

            rawSheet.Rows.Add(rawRow);
        }

        if (!rawSheet.Optimize())
            return;

        AttachNamedRanges(rawSheet, sheetName);

        _model.Sheets.Add(rawSheet);
    }

    /// <summary>
    /// The workbook's defined names, parsed into sheet and rectangle.
    /// </summary>
    /// <remarks>
    /// Workbook scope only. A sheet-scoped name is a local helper - a filter range, a chart
    /// source - and taking one as a table would convert something the workbook never
    /// exported. That is also what the project this serves does, through its own reader.
    ///
    /// Skipped entirely unless the layout asks for names, because parsing a reference means
    /// resolving it and a workbook can hold hundreds.
    /// </remarks>
    private List<WorkbookName> CollectWorkbookNames(XSSFWorkbook workbook, string filename)
    {
        if (!LayoutRegistry.UsesNamedRanges(_settings.Layout.Id))
            return null;

        var result = new List<WorkbookName>();

        foreach (var name in workbook.GetAllNames())
        {
            // -1 is workbook scope in NPOI; anything else is one sheet's own.
            if (name.SheetIndex != -1)
                continue;

            if (ReservedNameMarkers.Any(marker => name.NameName.Contains(marker, StringComparison.Ordinal)))
                continue;

            string reference = name.RefersToFormula;
            if (string.IsNullOrEmpty(reference) || reference.Contains("#REF!", StringComparison.Ordinal))
            {
                // A name whose target was deleted. Worth saying so rather than dropping in
                // silence: in real workbooks these are leftovers, and one of them being a
                // table nobody exports any more is a thing to know.
                Log.Warning(
                    $"Defined name `{name.NameName}` of `{filename}` refers to `{reference}`, "
                    + "which is not a range. Skipped.");
                continue;
            }

            var area = TryParseArea(reference);
            if (area is null)
            {
                Log.Warning(
                    $"Defined name `{name.NameName}` of `{filename}` refers to `{reference}`, "
                    + "which this importer cannot read as a single rectangle. Skipped.");
                continue;
            }

            result.Add(area with { Name = name.NameName });
        }

        return result;
    }

    /// <summary>
    /// Reads a reference like `'Ocean Zone'!$A$1:$IP$100` into a sheet name and a rectangle.
    /// </summary>
    /// <remarks>
    /// NPOI can build an AreaReference from this, and does it correctly for the quoting and
    /// the `$`. It throws on the shapes that are not one rectangle - a union, a whole
    /// column, a reference into another workbook - and those are the ones to skip rather
    /// than guess at, so the throw is the answer.
    /// </remarks>
    private static WorkbookName TryParseArea(string reference)
    {
        try
        {
            var area = new NPOI.SS.Util.AreaReference(reference, NPOI.SS.SpreadsheetVersion.EXCEL2007);
            var first = area.FirstCell;
            var last = area.LastCell;

            if (string.IsNullOrEmpty(first.SheetName))
                return null;

            return new WorkbookName(
                Name: "",
                SheetName: first.SheetName,
                Reference: reference,
                FirstRow: Math.Min(first.Row, last.Row),
                FirstColumn: Math.Min(first.Col, last.Col),
                LastRow: Math.Max(first.Row, last.Row),
                LastColumn: Math.Max(first.Col, last.Col));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The workbook's defined names that point into this sheet, translated into the grid's
    /// coordinates.
    /// </summary>
    /// <remarks>
    /// Collected here because this is the last place the workbook is open: the cooker sees
    /// a cell grid and has nothing left to ask about names. Only for the layouts that use
    /// them - every other sheet gets an empty list and pays nothing.
    ///
    /// Translation rather than absolute coordinates, because <see cref="RawSheet.Optimize"/>
    /// has just trimmed the blank margins and everything downstream indexes the trimmed
    /// grid. The top-left cell knows where it came from, which is what the offset is.
    /// </remarks>
    private void AttachNamedRanges(RawSheet rawSheet, string sheetName)
    {
        if (_currentWorkbookNames is null || _currentWorkbookNames.Count == 0)
            return;

        // Where the trimmed grid sits in the sheet, so a name's cells can be found in it.
        var topLeft = rawSheet.Rows[0][0].Location;

        foreach (var named in _currentWorkbookNames)
        {
            if (!string.Equals(named.SheetName, sheetName, StringComparison.Ordinal))
                continue;

            int row = named.FirstRow - topLeft.Row;
            int column = named.FirstColumn - topLeft.Column;

            // A name may cover rows or columns the grid no longer has - trailing blanks
            // are exactly what Optimize removes, and a range drawn generously over them is
            // ordinary. Clamped rather than refused, so the table is the cells that exist.
            int height = Math.Min(named.LastRow - named.FirstRow + 1, rawSheet.Rows.Count - row);
            int width = Math.Min(named.LastColumn - named.FirstColumn + 1, rawSheet.ColumnCount - column);

            if (row < 0 || column < 0 || height <= 0 || width <= 0)
            {
                Log.Warning(
                    $"Defined name `{named.Name}` of `{_currentFilename}` covers "
                    + $"{named.Reference}, which is outside the cells sheet `{sheetName}` has. Skipped.");
                continue;
            }

            rawSheet.NamedRanges.Add(new RawNamedRange
            {
                Name = named.Name,
                Row = row,
                Column = column,
                Height = height,
                Width = width,
            });
        }
    }

    private string SafeCellComment(ICell cell)
    {
        if (cell is null || cell.CellComment is null)
            return "";

        string comment = "";
        try
        {
            cell.CellComment.Author = "";
            comment = cell.CellComment.String.String;
            int colon = comment.IndexOf(":");
            if (colon >= 0)
            {
                string author = comment.Substring(0, colon);
                string prefix = author + ":" + "\n";
                if (comment.StartsWith(prefix))
                    comment = comment.Substring(colon + 2);
            }
        }
        catch (Exception)
        {
        }

        return comment.Trim();
    }

    private string SafeCellValue(ICell cell)
    {
        if (cell is null)
            return "";

        switch (cell.CellType)
        {
            // NPOI 2.8 removed CellType.Unknown, so no cell can report it any
            // more. The arm used to yield the sentinel "$unknown$", which would
            // have failed to parse as any type anyway.

            case CellType.Numeric:
                return NumericCellText(cell);

            case CellType.String:
                return cell.StringCellValue.Trim();

            case CellType.Formula:
                // The cached result, which is what the file carries; SheetMan does
                // not evaluate formulas itself.
                switch (cell.CachedFormulaResultType)
                {
                    case CellType.Numeric:
                        return NumericCellText(cell);

                    case CellType.String:
                        return cell.StringCellValue.Trim();

                    case CellType.Boolean:
                        return cell.BooleanCellValue.ToString().Trim();

                    case CellType.Error:
                        // Reported here as well as below. A formula that evaluated
                        // to an error has CellType.Formula, not CellType.Error, so
                        // it would otherwise fall through to StringCellValue and be
                        // stored as whatever text that returned.
                        throw new SheetManException(CellLocation(cell),
                            $"Formula evaluated to `{FormulaErrorText(cell)}`. " +
                            $"Fix the formula, or replace it with a literal value.");

                    default:
                        return cell.StringCellValue.Trim();
                }

            case CellType.Blank:
                return "";

            case CellType.Boolean:
                return cell.BooleanCellValue.ToString().Trim();

            case CellType.Error:
                // A formula that evaluated to #REF!, #DIV/0! and so on.
                //
                // Reported rather than passed through. It used to yield the literal
                // text "$error$", which a typed column would at least fail to parse
                // but a string column would happily store - so a broken formula
                // reached the game as the text "$error$".
                throw new SheetManException(CellLocation(cell),
                    $"Cell contains the formula error `{FormulaErrorText(cell)}`. " +
                    $"Fix the formula, or replace it with a literal value.");
        }

        return "";
    }

    /// <summary>
    /// Describes an error cell the way Excel shows it, so the message names what the
    /// author sees in the sheet.
    /// </summary>
    private static string FormulaErrorText(ICell cell)
    {
        try
        {
            return FormulaError.ForInt(cell.ErrorCellValue).String;
        }
        catch (Exception)
        {
            // Unknown error codes are possible; the code itself is still a clue.
            return $"error code {cell.ErrorCellValue}";
        }
    }

    /// <summary>
    /// Location of a cell, for diagnostics raised while importing it.
    /// </summary>
    private Location CellLocation(ICell cell)
    {
        return new Location
        {
            Filename = _currentFilename,
            Sheet = _currentSheetName,
            Column = cell.ColumnIndex,
            Row = cell.RowIndex,
        };
    }

    /// <summary>
    /// Renders a numeric cell as the text the cooker will parse.
    ///
    /// Excel has no date type: a date is a number carrying a date format, so a
    /// cell showing 2022-01-24 10:30:00 reads back as 44585.4375. Feeding that
    /// straight through meant `datetime` columns could never be authored in Excel
    /// as actual dates - only as text.
    ///
    /// Plain numbers are formatted round-trip and invariant. The default
    /// ToString() both follows the machine's locale, so a comma decimal separator
    /// would reach an int/float parse that expects a dot, and drops to scientific
    /// notation for large magnitudes, which no integer parse accepts.
    /// </summary>
    private string NumericCellText(ICell cell)
    {
        if (DateUtil.IsCellDateFormatted(cell))
        {
            // Nullable in NPOI 2.8; fall through to the raw serial number if the
            // cell is formatted as a date but holds no usable value.
            DateTime? date = cell.DateCellValue;
            if (date.HasValue)
            {
                // Round-trippable and unambiguous to DateTime.Parse on any locale.
                return date.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        return cell.NumericCellValue.ToString("R", CultureInfo.InvariantCulture).Trim();
    }
}
