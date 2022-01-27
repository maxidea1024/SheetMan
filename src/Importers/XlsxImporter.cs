using SheetMan.Models.Raw;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Generic;
using SheetMan.Models;
using System;
using SheetMan.Recipe;
using System.Linq;

namespace SheetMan.Importers
{
    public class XlsxImporter
    {
        private Options _options;
        private RecipeModel _recipe;
        private RawModel _model;

        //TODO
        //엔트리는 선언되었지만 recipe.json 파일에서 주석 처리되어 있을 경우 경로가 null일수 있으므로,
        //이러한 경우에 대해서 대응해야함.

        public void Import(Options options, RecipeModel recipe, RawModel model)
        {
            _options = options;
            _recipe = recipe;
            _model = model;

            foreach (var xlsx in _recipe.Sources.Xlsx)
            {
                if (string.IsNullOrEmpty(xlsx.FileExtensionPatterns) ||
                    string.IsNullOrEmpty(xlsx.Path))
                {
                    continue;
                }

                var fileExtensionPatterns = xlsx.FileExtensionPatterns.Split(";");
                if (fileExtensionPatterns == null || fileExtensionPatterns.Length == 0)
                {
                    fileExtensionPatterns = new string[] { ".xlsx" };
                }
                else
                {
                    for (int i = 0; i < fileExtensionPatterns.Length; i++)
                        fileExtensionPatterns[i] = fileExtensionPatterns[i].Trim().ToLower();
                }

                var files = Directory.GetFiles(xlsx.Path, "*.*", SearchOption.AllDirectories);
                foreach (var filename in files)
                {
                    if (filename.Contains("/#") || filename.Contains("\\#"))
                        continue;

                    string fileExtensions = Path.GetExtension(filename).ToLower();
                    if (!fileExtensionPatterns.Contains(fileExtensions))
                        continue;

                    ImportXlsx(filename);
                }
            }
        }

        private void ImportXlsx(string filename)
        {
            //todo 만약 여기서 오류가 발생하면 복사를 한 후에 시도하면 될수도?
            // 사본을 만들어서 읽어들여야 공유 이슈를 해결할 수 있음.
            // 엑셀에서 테스트를 해보자.

            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var workbook = new XSSFWorkbook(fs);
                ImportWorkbook(workbook, filename);
            }
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

                ImportSheet(sheet, filename, sheetName);
            }
        }

        private void ImportSheet(ISheet sheet, string filename, string sheetName)
        {
            RawSheet rawSheet = new RawSheet
            {
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

                List<RawCell> rawRow = new List<RawCell>();
                for (int colIndex = 0/*row.FirstCellNum*/; colIndex <= row.LastCellNum; colIndex++)
                {
                    var cell = row.GetCell(colIndex);

                    string value = SafeCellValue(cell);
                    string note = SafeCellComment(cell);

                    RawCell rawCell = new RawCell
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
                case CellType.Unknown:
                    return "$unknown$";

                case CellType.Numeric:
                    return cell.NumericCellValue.ToString().Trim();

                case CellType.String:
                    return cell.StringCellValue.Trim();

                case CellType.Formula:
                    if (cell.CachedFormulaResultType == CellType.Numeric)
                    {
                        return cell.NumericCellValue.ToString().Trim();
                    }
                    else if (cell.CachedFormulaResultType == CellType.String)
                    {
                        return cell.StringCellValue.ToString().Trim();
                    }
                    else if (cell.CachedFormulaResultType == CellType.Boolean)
                    {
                        return cell.BooleanCellValue.ToString().Trim();
                    }
                    else
                    {
                        return cell.StringCellValue.Trim();
                    }

                case CellType.Blank:
                    return "";

                case CellType.Boolean:
                    return cell.BooleanCellValue.ToString().Trim();

                case CellType.Error:
                    //TODO 예외를 던지는 형태로 처리하는게 좋을듯..
                    return "$error$";
            }

            return "";
        }
    }
}
