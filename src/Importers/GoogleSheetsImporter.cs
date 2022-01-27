using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Services;
using Google.Apis.Util.Store;

using System;
using System.IO;
using System.Threading;
using SheetMan.Models;
using SheetMan.Models.Raw;
using System.Collections.Generic;
using SheetMan.Extensions;
using SheetMan.Recipe;
using Serilog;
using System.Diagnostics;

namespace SheetMan.Importers
{
    public class GoogleSheetsImporter
    {
        //TODO
        //엔트리는 선언되었지만 recipe.json 파일에서 주석 처리되어 있을 경우 경로가 null일수 있으므로,
        //이러한 경우에 대해서 대응해야함.

        static string ApplicationName = "SheetMan";
        static string[] Scopes = { SheetsService.Scope.SpreadsheetsReadonly };

        private Options _options;
        private RecipeModel _recipe;
        private RawModel _model;

        public void Import(Options options, RecipeModel recipe, RawModel model)
        {
            _options = options;
            _recipe = recipe;
            _model = model;

            foreach (var googleSheets in _recipe.Sources.GoogleSheets)
            {
                var sheetsService = AcquireSheetsService(googleSheets);
                ImportSheets(sheetsService, googleSheets);
            }
        }

        private SheetsService AcquireSheetsService(RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe)
        {
            UserCredential credential;

            using (var stream = new FileStream(recipe.ClientSecretFilename, FileMode.Open, FileAccess.Read))
            {
                string credentialsPath = Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
                credentialsPath = Path.Combine(credentialsPath, ".credentials/sheets.googleapis.com-sheetman");

                var clientSecrets = GoogleClientSecrets.FromStream(stream);

                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    clientSecrets.Secrets,
                    Scopes,
                    // If the user name is different, authentication is required again, so the user is fixed.
                    //Environment.UserName,
                    "SheetManUser",
                    CancellationToken.None,
                    new FileDataStore(credentialsPath, true)).Result;
            }

            // Create Google Sheets API service.
            var sheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            return sheetsService;
        }

        private void ImportSheets(SheetsService sheetsService, RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe)
        {
            var sheetsId = recipe.SheetsId;
            var request = sheetsService.Spreadsheets.Get(sheetsId);
            request.IncludeGridData = true;

            Log.Information($"Importing google-spreadsheets `{sheetsId}`");

            // 시간을 측정하자.
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var response = request.Execute();
            stopWatch.Stop();
            Log.Information($"   => It took {stopWatch.ElapsedMilliseconds} milliseconds to fetch the Google Sheets data.");

            var sheetsTitle = response.Properties.Title;
            if (sheetsTitle.StartsWith("#") || sheetsTitle.StartsWith("//"))
            {
                Log.Warning($"Sheet `{sheetsTitle}` is marked as excluded and is ignored.");
                return;
            }

            foreach (var sheet in response.Sheets)
            {
                string sheetTitle = sheet.Properties.Title.Trim();

                if (sheetTitle.StartsWith("#") || sheetTitle.StartsWith("//"))
                {
                    Log.Warning($"Sheet `{sheetsTitle}.{sheetTitle}` is marked as excluded and is ignored.");
                    continue;
                }

                if (sheet.Data == null)
                    continue;

                foreach (var d in sheet.Data)
                {
                    if (d == null || d.RowData == null)
                        continue;

                    int startColumn = d.StartColumn ?? 0;
                    int startRow = d.StartRow ?? 0;

                    RawSheet rawSheet = new RawSheet
                    {
                        Location = new Location
                        {
                            //Filename = spreadsheetsId,
                            //Sheet = sheetTitle,
                            Column = startColumn,
                            Row = startRow
                        }
                    };

                    rawSheet.Location.Filename = $"googlesheets.{sheetsTitle}";///{sheetTitle}",
                    rawSheet.Location.Sheet = sheetTitle;
                    rawSheet.Location.SheetUrl = MakeGoogleSheetsUrl(sheetsId, sheet.Properties.SheetId ?? 0);

                    Log.Information($"Importing google-spreadsheets sheet `{rawSheet.Location.SheetUrl}`");

                    int rowIndex = startRow;

                    foreach (var r in d.RowData)
                    {
                        if (r.Values == null)
                        {
                            rowIndex++;
                            continue;
                        }

                        List<RawCell> rawRow = new List<RawCell>();

                        int colIndex = startColumn;
                        foreach (var v in r.Values)
                        {
                            string value = v.FormattedValue.SafeTrim();
                            string note = v.Note.SafeTrim();

                            RawCell rawCell = new RawCell
                            {
                                Location = new Location
                                {
                                    Sheet = sheetTitle,
                                    Column = colIndex,
                                    Row = rowIndex
                                },
                                Value = value,
                                Note = note
                            };

                            rawCell.Location.Filename = $"googlesheets.{sheetsTitle}";///{sheetTitle}",
                            rawCell.Location.Sheet = sheetTitle;
                            rawCell.Location.SheetUrl = MakeGoogleSheetsUrl(sheetsId, sheet.Properties.SheetId ?? 0, rawCell.Location.CellRange);

                            rawRow.Add(rawCell);

                            colIndex++;
                        }

                        rawSheet.Rows.Add(rawRow);

                        rowIndex++;
                    }

                    if (rawSheet.Optimize())
                        _model.Sheets.Add(rawSheet);
                }
            }
        }

        //https://webapps.stackexchange.com/questions/44473/link-to-a-cell-in-a-google-sheets-via-url
        private string MakeGoogleSheetsUrl(string sheetsId, int sheetId, string cellRange = null)
        {
            if (!string.IsNullOrEmpty(cellRange))
                return $"https://docs.google.com/spreadsheets/d/{sheetsId}/edit#gid={sheetId}&range={cellRange}";
            else
                return $"https://docs.google.com/spreadsheets/d/{sheetsId}/edit#gid={sheetId}";
        }
    }
}
