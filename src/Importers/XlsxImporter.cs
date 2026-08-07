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
using Serilog;

namespace SheetMan.Importers
{
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
            if (fileExtensionPatterns == null || fileExtensionPatterns.Length == 0)
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
            //todo 만약 여기서 오류가 발생하면 복사를 한 후에 시도하면 될수도?
            // 사본을 만들어서 읽어들여야 공유 이슈를 해결할 수 있음.
            // 엑셀에서 테스트를 해보자.

            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var workbook = new XSSFWorkbook(fs);
            ImportWorkbook(workbook, filename);
        }

        private void ImportWorkbook(XSSFWorkbook workbook, string filename)
        {
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
                if (row == null)
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

            if (rawSheet.Optimize())
                _model.Sheets.Add(rawSheet);
        }

        private string SafeCellComment(ICell cell)
        {
            if (cell == null || cell.CellComment == null)
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
            if (cell == null)
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
}
