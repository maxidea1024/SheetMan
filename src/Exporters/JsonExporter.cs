using SheetMan.Recipe;
using SheetMan.Models;
using System.IO;
using Serilog;
using SheetMan.Helpers;
using System.Collections.Generic;
using System.Linq;
using SheetMan.Extensions;

namespace SheetMan.Exporters
{
    public class JsonExporter
    {
        private Options _options;
        private RecipeModel _recipeModel;
        private Model _model;
        private Manifest _manifest;

        public void Export(Options options, RecipeModel recipeModel, Model model)
        {
            _options = options;
            _recipeModel = recipeModel;
            _model = model;

            foreach (var recipe in _recipeModel.Exports.Json)
            {
                if (string.IsNullOrEmpty(recipe.Path))
                    continue;

                string manifestFilename = Path.Combine(recipe.Path, "manifest-json.json");

                _manifest = Manifest.Load(manifestFilename);

                foreach (var table in _model.Tables)
                    ExportTable(recipe, table);

                _manifest.BuildAndWriteToFile(manifestFilename);
            }
        }

        private void ExportTable(RecipeModel.ExportRecipeGroup.JsonRecipe recipe, Table table)
        {
            var filename = Path.Combine(recipe.Path, table.Name + ".json");
            filename = Path.GetFullPath(filename);

            Log.Information($"Exporting json file `{filename}`");

            object sourceRows = null;

            if (recipe.UseCompactRowFormat)
            {
                var writableRows = new List<object[]>();
                foreach (var row in table.Data)
                {
                    var rawData = row.Select(x => x.Value).ToArray();
                    writableRows.Add(rawData);
                }

                sourceRows = writableRows;
            }
            else
            {
                var writableRows = new List<Dictionary<string, object>>();
                foreach (var row in table.Data)
                {
                    var dataRow = new Dictionary<string, object>();

                    int i = 0;
                    foreach (var sf in table.SerialFields)
                    {
                        string name = sf.Name.ToCamelCase();
                        object value = row[i++].Value;

                        dataRow.Add(name, value);
                    }

                    writableRows.Add(dataRow);
                }

                sourceRows = writableRows;
            }

            string stagingFilename = StagingFiles.WriteToJsonFile(filename, sourceRows, recipe.Indented);
            _manifest.Add(table.Name + ".json", stagingFilename);
        }
    }
}
