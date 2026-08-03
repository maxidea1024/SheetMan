using System;
using System.IO;
using CommandLine;
using SheetMan.Models.Raw;
using SheetMan.Importers;
using SheetMan.Cooking;
using SheetMan.History;
using SheetMan.Exporters;
using SheetMan.CodeGeneration;
using SheetMan.Recipe;
using Serilog;
using System.Diagnostics;
using SheetMan.Helpers;
using SheetMan.Extensions;
using System.Collections.Generic;
using SheetMan.Targets;
using SheetMan.Sources;

namespace SheetMan
{
    class Program
    {
        static int Main(string[] args)
        {
            //string summaryFilename = Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), ".sheetman/sheetman.summary.json"); ;

            if (args.Length == 1 && args[0].StartsWith("@"))
            {
                var argFile = args[0][1..];
                if (File.Exists(argFile))
                {
                    args = File.ReadAllLines(argFile);
                }
                else
                {
                    Console.WriteLine($"File not found: {argFile}");
                    return 1;
                }
            }

            var parser = new Parser(recipe => recipe.HelpWriter = Console.Out);
            if (args.Length == 0)
            {
                parser.ParseArguments<Options>(new[] { "--help" });
                return 1;
            }

            Options options = null;
            parser.ParseArguments<Options>(args)
                .WithParsed(r => { options = r; });

            // WithParsed only fires on success, so a rejected argument leaves `options`
            // null. Every path below dereferences it, so bail out here instead.
            // CommandLineParser has already written the error and the help text.
            if (options == null)
                return 1;

            SetupLogging(options.Verbose, options.Silent);

            // Serilog's file sink buffers, so the last writes are lost unless the
            // logger is closed. Every exit below runs through this.
            try
            {
                return Run(parser, options);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static int Run(Parser parser, Options options)
        {
            if (!string.IsNullOrEmpty(options.NewRecipeFilename))
            {
                RecipeSkeleton.WriteToFile(options.NewRecipeFilename);

                Console.WriteLine($"Wrote a starting recipe to {Path.GetFullPath(options.NewRecipeFilename)}");
                return 0;
            }

            RecipeModel recipe = null;
            if (!string.IsNullOrEmpty(options.RecipeFilename))
            {
                try
                {
                    recipe = RecipeModel.LoadFromFile(options.RecipeFilename);
                }
                catch (Exception ex)
                {
                    //TODO detail한 오류 메시지를 출력할 수 있어야...
                    Console.WriteLine(ex.Message);
                    return 1;
                }
            }
            else
            {
                parser.ParseArguments<Options>(new[] { "--help" });
                return 1;
            }

            // Reading the history is not a conversion: no sources are imported, nothing is
            // written to the output tree, and the answer goes to standard output. The recipe
            // is still needed, because that is where the history's address is.
            if (options.History || options.Stats || options.Serve || options.Prune)
            {
                try
                {
                    if (options.Serve)
                        return HistoryServer.Run(options, recipe);

                    if (options.Prune)
                        return HistoryCommand.RunPrune(options, recipe);

                    return options.History
                        ? HistoryCommand.RunHistory(options, recipe)
                        : HistoryCommand.RunStats(options, recipe);
                }
                catch (Exception ex)
                {
                    LogException(options, ex);
                    return 1;
                }
            }

            {
                Log.Information($"Start working with recipe `{Path.GetFullPath(options.RecipeFilename)}`");

                var stopWatch = new Stopwatch();

                stopWatch.Start();
                int rc = Process(options, recipe);
                stopWatch.Stop();

                if (rc == 0)
                {
                    if (!options.Silent)
                    {
                        Log.Information($"All work is done successfuly. Total time spent is {stopWatch.ElapsedMilliseconds} ms.");
                        //Log.Information($"  Take a look at the `{summaryFilename}` for details on the results.");
                    }
                }

                return rc;
            }
        }

        private static int Process(Options options, RecipeModel recipeModel)
        {
            try
            {
                // Read before any work starts, and discarded: the consumers below take it
                // from the options themselves. Parsing it here is what turns a misspelled
                // --target-side into an immediate error rather than one reported after
                // every workbook has been read.
                CommandLineTargetSide.Of(options);

                // Same reason: a misspelled --commit-date should be reported now rather
                // than after every workbook has been read. Working out which commit this
                // is spawns git, so that part waits until a target asks for it.
                CommitInfo.ValidateOptions(options);


                // Imports

                // Every source the recipe lists, into one raw model: a project may spread
                // its tables across workbooks and Google Sheets documents and they cook
                // together. Which sources exist is discovered by attribute, so adding one
                // touches only the file that defines it.
                RawModel rawModel = new RawModel();

                SourceRegistry.ImportAll(options, recipeModel, rawModel);


                // Cooking

                var cooker = new ModelCooker();
                var model = cooker.Cook(options, recipeModel, rawModel);


                // Output

                // Every export and code-generation target the recipe asks for, in a fixed
                // order. Which targets exist is discovered by attribute, so adding one
                // touches only the file that defines it - this used to be a run of ten
                // near-identical `if (recipe.X.Y.Count > 0)` blocks, and the validation
                // pass had to name the same sections a second time.
                //
                // The database targets differ from the file ones in when their output
                // becomes visible. File targets stage their work and commit it below,
                // while each database target loads into shadow storage and swaps it in as
                // it goes. Atomicity is per store either way: files and four databases
                // cannot share one transaction without a distributed coordinator.

                TargetRegistry.RunAll(options, recipeModel, model);

                Log.Information("Now that we have completed all the work, we are copying the generated staging files to the destination folder.");

                try
                {
                    StagingFiles.CommitFiles((filename, stagedFilename) =>
                    {
                        Log.Debug($"Commit staged file `{filename}`");
                    });
                }
                catch (Exception ex)
                {
                    // Delete all files created in the staging area.
                    StagingFiles.Rollback();

                    LogException(options, ex,
                        "While moving the artifact file to the actual target path, We got the below error. " +
                        "This would have caused problems with the final result. " +
                        "Please return to the previous state with version control such as git or svn."
                    );

                    return 1;
                }
            }
            catch (Exception ex)
            {
                LogException(options, ex);

                return 1;
            }

            return 0;
        }

        private static void LogException(Options options, Exception ex, string subject = "")
        {
            Log.Fatal(ex.Message);

            if (ex is SheetManException sheetManEx)
            {
                if (sheetManEx.Location != null)
                    Log.Fatal($"   at {sheetManEx.Location}");

                if (sheetManEx.Details != null && sheetManEx.Details.Count > 0)
                {
                    // Header printed once, ahead of the list. It used to be inside the
                    // loop, so it was repeated before every single entry.
                    Log.Fatal("");
                    Log.Fatal("Details:");

                    for (int detailIndex = 0; detailIndex < sheetManEx.Details.Count; detailIndex++)
                    {
                        var detail = sheetManEx.Details[detailIndex];

                        Log.Fatal($"  [{detailIndex + 1,3}] {detail.Message}");
                        if (detail.Location != null)
                            Log.Fatal($"        at {detail.Location}");
                    }
                }
            }

            if (options.Debugging && ex.StackTrace != null)
            {
                Log.Fatal("");
                Log.Fatal("Callstack:");
                Log.Fatal(ex.StackTrace);
            }
        }

        private static void SetupLogging(bool verbose, bool silent)
        {
            Serilog.Events.LogEventLevel loggingLevel = Serilog.Events.LogEventLevel.Information;

            if (silent)
                loggingLevel = Serilog.Events.LogEventLevel.Error;
            else if (verbose)
                loggingLevel = Serilog.Events.LogEventLevel.Debug;

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}",
                                restrictedToMinimumLevel: loggingLevel)
                .WriteTo.File("logs/sheetman.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
    }
}
