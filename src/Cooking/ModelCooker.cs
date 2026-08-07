using System.Collections.Generic;
using System.Linq;
using Serilog;
using SheetMan.Cooking.Layouts;
using SheetMan.Models;
using SheetMan.Models.Raw;
using SheetMan.Recipe;
using SheetMan.Targets;

namespace SheetMan.Cooking;

/// <summary>
/// Turns the cells the sources read into the model everything downstream consumes.
/// </summary>
/// <remarks>
/// The interpreting itself belongs to a <see cref="ILayoutParser"/>, chosen per sheet from
/// the recipe entry that imported it. What is left here is the part that is true whatever
/// the sheets looked like: run every layout's declarations before any layout's tables,
/// resolve references across the lot, and check the result once.
/// </remarks>
public partial class ModelCooker
{
    public Model Cook(Options options, RecipeModel recipeModel, RawModel rawModel)
    {
        var result = new Model();

        var context = new CookingContext(result, recipeModel);

        ParseRawModel(context, rawModel);

        // Resolution and validation share one collector, so a workbook comes back
        // with everything wrong with it rather than one problem per run.
        var diagnostics = new Diagnostics();

        result.SolveTableCrossReferencings(diagnostics);

        // Runs after resolution: validation follows references to check that what
        // they point at exists.
        //
        // The requested side is passed in so a narrowed run is checked against what it
        // will actually build. Without it, `--target-side client` could fail on a
        // problem that only exists in the server cut it is not producing.
        ValidateModel(result, recipeModel, CommandLineTargetSide.Of(options), diagnostics);

        diagnostics.ThrowIfAny("The workbook did not pass validation.");

        return result;
    }

    /// <summary>
    /// Hands each layout the sheets that named it, declarations first.
    /// </summary>
    /// <remarks>
    /// Two passes over the layouts rather than one pass each: a table column typed with an
    /// enum resolves by name, and in a project part-way through being converted the enum
    /// and the table that uses it will be in workbooks read under different layouts. Doing
    /// one layout completely before starting the next would make that work or not work
    /// depending on which order the recipe happened to list its sources in.
    /// </remarks>
    private void ParseRawModel(CookingContext context, RawModel rawModel)
    {
        Log.Information("Parsing raw-model...");

        var byLayout = GroupByLayout(rawModel);

        var parsers = byLayout
            .Select(group => (Parser: LayoutRegistry.Get(group.Key).CreateParser(), group.Value))
            .ToList();

        foreach (var (parser, sheets) in parsers)
            parser.ParseDeclarations(context, sheets);

        foreach (var (parser, sheets) in parsers)
            parser.ParseTables(context, sheets);
    }

    /// <summary>
    /// Sorts the sheets by the layout their source stamped on them, keeping the order the
    /// importers produced them in.
    /// </summary>
    private static Dictionary<string, List<RawSheet>> GroupByLayout(RawModel rawModel)
    {
        // The value type is the concrete list rather than IReadOnlyList. Holding the
        // interface and casting back to add read the same until a collection expression
        // was applied to the `new List<RawSheet>()`: an empty `[]` for an IReadOnlyList
        // target is an array, and the cast then threw on the first sheet of every run.
        // A parser takes IReadOnlyList, which List satisfies, so nothing needed the
        // wider type here.
        var result = new Dictionary<string, List<RawSheet>>();

        foreach (var sheet in rawModel.Sheets)
        {
            string id = (sheet.Layout ?? SheetLayout.Default).Id;

            if (!result.TryGetValue(id, out var sheets))
            {
                sheets = [];
                result.Add(id, sheets);
            }

            sheets.Add(sheet);
        }

        return result;
    }
}
