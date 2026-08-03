using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;
using SheetMan.Helpers;
using SheetMan.Targets;

namespace SheetMan.History
{
    /// <summary>
    /// Settings for the summary target.
    /// </summary>
    public sealed class SummaryRecipe : IOutputRecipe
    {
        /// <summary>Output directory. Created if it does not exist.</summary>
        public string Path { get; set; } = "";

        /// <summary>Name of the document, without a directory.</summary>
        public string FileName { get; set; } = "summary.json";

        /// <summary>
        /// Which side this entry is built for: `c`, `s`, or `cs`/blank for both.
        ///
        /// This decides whether the entry runs at all, as it does for every target. It does
        /// not narrow what the document says: a summary always describes everything the
        /// sheets declared, or a client build would report the server's tables as gone.
        /// </summary>
        public string TargetSide { get; set; } = "cs";
    }

    /// <summary>
    /// Writes what a conversion produced, as the document every other view renders from.
    ///
    /// Nothing here formats anything. The report a build leaves behind, the rows a snapshot
    /// puts in the history, the JSON the API serves and the page a browser draws are all
    /// this file's shape - because two renderings of one question drift and nothing
    /// notices, and the answer that is wrong looks exactly like the one that is right.
    /// </summary>
    [SheetManTarget("summary", TargetKind.Description, Order = 10)]
    public class SummaryTarget : Target<SummaryRecipe>
    {
        /// <summary>
        /// camelCase names, string enums, indented, and no `\r`.
        ///
        /// The document is read by a browser as much as by this tool, and it is compared
        /// byte for byte by the regression suite - so the line ending is decided here
        /// rather than by whichever machine ran the build.
        /// </summary>
        private static readonly JsonSerializerSettings Format = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new StringEnumConverter() },
        };

        protected override void Run(TargetContext context, SummaryRecipe recipe)
        {
            // An entry left in the recipe with a blank path is switched off, as everywhere.
            if (string.IsNullOrEmpty(recipe.Path))
                return;

            // The unnarrowed model, always. `context.Model` is the cut this entry's target
            // side asked for, and describing that as if it were the whole thing is how a
            // client build comes to report every server-only table as deleted.
            var document = SummaryBuilder.Build(context.FullModel, context.Commit, context);

            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(recipe.Path, recipe.FileName));

            Log.Information($"Writing the summary to `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, Render(document));
        }

        /// <summary>The document as it is written and served.</summary>
        public static string Render(SummaryDocument document)
            => JsonConvert.SerializeObject(document, Format).Replace("\r\n", "\n") + "\n";
    }
}
