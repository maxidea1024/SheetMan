using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;
using SheetMan.Exporters;
using SheetMan.Models;
using SheetMan.Recipe;
using SheetMan.Targets;

namespace SheetMan.History
{
    /// <summary>
    /// The reading side of the command line: `--history` and `--stats`.
    ///
    /// Both go through <see cref="HistoryQuery"/> and serialise what it returns, exactly as
    /// the HTTP API does. The point of that is a promise worth keeping: a number this prints
    /// and the same number on the web page cannot disagree, because neither computes it.
    ///
    /// The connection comes from the recipe rather than from options of its own. It is
    /// already there, it already resolves `${NAME}` from the environment, and a second place
    /// to write an address is a second place for it to be wrong.
    /// </summary>
    public static class HistoryCommand
    {
        private static readonly JsonSerializerSettings Format = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new StringEnumConverter() },
        };

        /// <summary>Reports what changed between two commits.</summary>
        public static int RunHistory(Options options, RecipeModel recipe)
        {
            // Before the connection, so a misspelled --format is reported immediately
            // rather than after a query has run.
            bool text = IsText(options);

            var (connectionString, projectKey) = Connection(options, recipe);

            using var query = HistoryQuery.Open(connectionString);

            string branch = options.Branch ?? query.DefaultBranch(projectKey);

            if (branch == null)
            {
                Log.Error($"The history holds nothing for project `{projectKey}`. " +
                          $"Run a conversion with the history target enabled first.");
                return 1;
            }

            var document = query.Diff(
                projectKey, branch,
                options.From, options.To,
                options.Table, options.Field, options.Author,
                options.Limit <= 0 ? HistoryQuery.DefaultLimit : options.Limit);

            Write(options, text ? HistoryText.Render(document) : Serialize(document));

            return 0;
        }

        /// <summary>Reports the statistics of one commit.</summary>
        public static int RunStats(Options options, RecipeModel recipe)
        {
            bool text = IsText(options);

            var (connectionString, projectKey) = Connection(options, recipe);

            using var query = HistoryQuery.Open(connectionString);

            string branch = options.Branch ?? query.DefaultBranch(projectKey);

            if (branch == null)
            {
                Log.Error($"The history holds nothing for project `{projectKey}`. " +
                          $"Run a conversion with the history target enabled first.");
                return 1;
            }

            var summary = query.Stats(projectKey, branch, options.At);

            if (summary == null)
            {
                Log.Error(options.At == null
                    ? $"Branch `{branch}` of `{projectKey}` has no snapshots."
                    : $"The history has no snapshot for `{options.At}` on branch `{branch}`.");

                return 1;
            }

            Write(options, text ? HistoryText.Render(summary, branch) : Serialize(summary));

            return 0;
        }

        /// <summary>The document, exactly as the API serves it.</summary>
        public static string Serialize(object document)
            => JsonConvert.SerializeObject(document, Format).Replace("\r\n", "\n") + "\n";

        private static bool IsText(Options options)
        {
            string format = (options.Format ?? "json").Trim().ToLowerInvariant();

            switch (format)
            {
                case "json": return false;
                case "text": return true;

                default:
                    throw new SheetManException(
                        $"`--format {options.Format}` is not a format. Use `json` or `text`.");
            }
        }

        private static void Write(Options options, string content)
        {
            if (string.IsNullOrEmpty(options.Out))
            {
                // Through the console rather than the log, because this is the answer rather
                // than a note about producing it - and a caller may be piping it.
                //
                // Onto the raw stream in UTF-8 rather than through Console.Out, whose
                // encoding on Windows is the console codepage. A report is full of author
                // names and cell values, and that codepage turns every non-ASCII one into
                // question marks - in a file somebody redirected it into, where nothing will
                // ever say what happened.
                using var stdout = Console.OpenStandardOutput();
                using var writer = new StreamWriter(stdout, new UTF8Encoding(false));

                writer.Write(content);
                writer.Flush();

                return;
            }

            string path = Path.GetFullPath(options.Out);

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Written directly rather than staged: a report is not a build artifact, and a
            // failure part-way through leaves nothing worth rolling back.
            File.WriteAllText(path, content, new UTF8Encoding(false));

            Log.Information($"Wrote the report to `{path}`");
        }

        /// <summary>
        /// Where the history is, and which project to read - from the recipe's history
        /// entry.
        /// </summary>
        private static (string ConnectionString, string ProjectKey) Connection(
            Options options, RecipeModel recipe)
        {
            var planned = TargetRegistry.Plan(recipe, TargetSide.Both)
                                        .Where(p => p.Entry is HistoryRecipe)
                                        .ToList();

            if (planned.Count == 0)
            {
                throw new SheetManException(
                    "This recipe has no `history` target, so there is nothing to read. Add one, or " +
                    "point --recipe at the recipe the conversions use.");
            }

            if (planned.Count > 1 && options.Project == null)
            {
                var keys = planned.Select(p => ((HistoryRecipe)p.Entry).ProjectKey).Distinct().ToList();

                throw new SheetManException(
                    $"This recipe has {planned.Count} history targets ({string.Join(", ", keys)}). " +
                    $"Name the one to read with --project.");
            }

            var chosen = options.Project == null
                ? planned[0]
                : planned.FirstOrDefault(p => string.Equals(
                      ((HistoryRecipe)p.Entry).ProjectKey, options.Project, StringComparison.OrdinalIgnoreCase));

            if (chosen.Entry == null)
            {
                throw new SheetManException(
                    $"This recipe has no history target for project `{options.Project}`.");
            }

            var entry = (HistoryRecipe)chosen.Entry;

            return (ConnectionString.Resolve(entry.ConnectionString, chosen.Section), entry.ProjectKey);
        }
    }
}
