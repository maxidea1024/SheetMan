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
using SheetMan.Sources;

namespace SheetMan.Importers
{
    [SheetManSource("googlesheets", "Sources.GoogleSheets", Order = 20)]
    public class GoogleSheetsImporter : Source<RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe>
    {
        static string ApplicationName = "SheetMan";
        static string[] Scopes = { SheetsService.Scope.SpreadsheetsReadonly };

        private RawModel _model;

        private SheetImportSettings _settings;

        protected override void Import(
            SourceContext context, RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe googleSheets)
        {
            // An entry with either field left blank is switched off, matching how the Excel
            // source treats a blank path - which is how an entry is commented out in
            // practice: its contents are removed but the object stays in the list.
            //
            // This check was missing, and its absence was not harmless: a blank
            // ClientSecretFilename went straight into a FileStream, so a recipe with an
            // emptied-out Google Sheets entry failed with an argument exception about a path
            // rather than being skipped.
            if (string.IsNullOrWhiteSpace(googleSheets.ClientSecretFilename) ||
                string.IsNullOrWhiteSpace(googleSheets.SheetsId))
            {
                Log.Debug($"Skipping Google Sheets source `{context.Section}`: not configured.");
                return;
            }

            if (!File.Exists(googleSheets.ClientSecretFilename))
            {
                throw new SheetManException(
                    $"Recipe `{context.Section}` names client secret file " +
                    $"`{googleSheets.ClientSecretFilename}`, which does not exist.");
            }

            _model = context.Model;
            _settings = SheetImportSettings.From(googleSheets, context.Section);

            var sheetsService = AcquireSheetsService(googleSheets);
            ImportSheets(sheetsService, googleSheets, context.Section);
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

        private void ImportSheets(
            SheetsService sheetsService, RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe, string section)
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

            var sheetNamesSeen = new List<string>();

            foreach (var sheet in response.Sheets)
            {
                string sheetTitle = sheet.Properties.Title.Trim();

                if (sheetTitle.StartsWith("#") || sheetTitle.StartsWith("//"))
                {
                    Log.Warning($"Sheet `{sheetsTitle}.{sheetTitle}` is marked as excluded and is ignored.");
                    continue;
                }

                sheetNamesSeen.Add(sheetTitle);

                if (!_settings.Filter.Includes(sheetTitle))
                {
                    Log.Information($"Skipping sheet `{sheetsTitle}.{sheetTitle}`: the recipe does not ask for it.");
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
                        Layout = _settings.Layout,
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

            _settings.Filter.ReportUnmatchedIncludes(section, sheetNamesSeen);
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
