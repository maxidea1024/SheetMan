using SheetMan.Recipe;
using SheetMan.Models;
using System.IO;
using Serilog;
using SheetMan.Helpers;
using System.Collections.Generic;
using System.Linq;
using SheetMan.Extensions;
using System;
using System.Globalization;
using SheetMan.Targets;

namespace SheetMan.Exporters;

[SheetManTarget("json", TargetKind.Export, Section = "Exports.Json", Order = 20)]
public class JsonExporter : Target<RecipeModel.ExportRecipeGroup.JsonRecipe>
{
    private Manifest _manifest;


    protected override void Run(TargetContext context, RecipeModel.ExportRecipeGroup.JsonRecipe recipe)
    {
        // An entry left in the recipe with a blank path is treated as switched off.
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        string manifestFilename = Path.Combine(recipe.Path, "manifest-json.json");

        _manifest = Manifest.Load(manifestFilename);

        // context.Model is already narrowed to this entry's target side.
        foreach (var table in context.Model.Tables)
            ExportTable(recipe, table);

        _manifest.BuildAndWriteToFile(manifestFilename);
    }

    /// <summary>
    /// Adjusts a value for JSON, where the format cannot carry it faithfully.
    ///
    /// Only 64-bit integers need this. JSON has one numeric type and every reader
    /// treats it as a double, so a value past 2^53 is silently rounded on the way in -
    /// JavaScript turns 9007199254740993 into ...992 without complaint. Written as a
    /// string, it survives, and a reader that wants the number reconstructs it
    /// exactly. This is the same choice Protocol Buffers makes for int64 in its JSON
    /// mapping, and for the same reason.
    /// </summary>
    private static object ForJson(object value)
    {
        switch (value)
        {
            case long i64:
                return i64.ToString(CultureInfo.InvariantCulture);

            case Array array:
            {
                var items = new object[array.Length];
                for (int i = 0; i < array.Length; i++)
                    items[i] = ForJson(array.GetValue(i));

                return items;
            }

            default:
                return value;
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

            // Projected through the table's fields rather than over the raw row.
            // A row always carries every column the sheet declared, while the
            // table's field list is what this output is meant to contain - they
            // differ as soon as a field is filtered out by target side.
            var columns = table.Fields.ToArray();

            foreach (var row in table.Data)
            {
                var rawData = new object[columns.Length];
                for (int i = 0; i < columns.Length; i++)
                    rawData[i] = ForJson(row[columns[i].Index].Value);

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

                foreach (var sf in table.SerialFields)
                {
                    string name = sf.Name.ToCamelCase();

                    // Indexed through each field's own column, not a running
                    // counter over the groups.
                    //
                    // A serial field collapses N columns into one named entry, so
                    // there are fewer groups than columns. Walking a counter
                    // therefore took the first column of each group and then
                    // drifted: every value after the first array landed under the
                    // wrong name, and the remaining columns were dropped entirely.
                    if (sf.IsVariableLengthArray)
                    {
                        // The cell already parsed into an array; gathering it
                        // across the group's fields would nest it one deep.
                        dataRow.Add(name, ForJson(row[sf.FirstField.Index].Value));
                    }
                    else if (sf.IsArray)
                    {
                        dataRow.Add(name, sf.Fields.Select(f => ForJson(row[f.Index].Value)).ToArray());
                    }
                    else
                    {
                        dataRow.Add(name, ForJson(row[sf.FirstField.Index].Value));
                    }
                }

                writableRows.Add(dataRow);
            }

            sourceRows = writableRows;
        }

        string stagingFilename = StagingFiles.WriteToJsonFile(filename, sourceRows, recipe.Indented);
        _manifest.Add(table.Name + ".json", stagingFilename);
    }
}
