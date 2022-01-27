using System;
using System.IO;
using CommandLine;
using SheetMan.Models.Raw;
using SheetMan.Importers;
using SheetMan.Cooking;
using SheetMan.Exporters;
using SheetMan.CodeGeneration;
using SheetMan.Recipe;
using Serilog;
using System.Diagnostics;
using SheetMan.Helpers;
using SheetMan.Extensions;

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
            var result = parser.ParseArguments<Options>(args)
                .WithParsed(r => { options = r; });


            SetupLogging(options.Verbose, options.Silent);


            if (!string.IsNullOrEmpty(options.NewRecipeFilename))
            {
                var newEmptyRecipe = new RecipeModel();
                newEmptyRecipe.WriteToFile(options.NewRecipeFilename);
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

            if (options != null)
            {
                if (!options.Silent)
                {
                    Console.WriteLine(@"  _________.__                   __     _____                 ");
                    Console.WriteLine(@" /   _____/|  |__   ____   _____/  |_  /     \ _____    ____  ");
                    Console.WriteLine(@" \_____  \ |  |  \_/ __ \_/ __ \   __\/  \ /  \\__  \  /    \ ");
                    Console.WriteLine(@" /        \|   Y  \  ___/\  ___/|  | /    Y    \/ __ \|   |  \");
                    Console.WriteLine(@"/_______  /|___|  /\___  >\___  >__| \____|__  (____  /___|  /");
                    Console.WriteLine(@"        \/      \/     \/     \/             \/     \/     \/ ");
                    Console.WriteLine(@"");
                }

                Log.Information($"Start working with recipe `{Path.GetFullPath(options.RecipeFilename)}`");

                var stopWatch = new Stopwatch();

                stopWatch.Start();
                int rc = Process(options, recipe);
                stopWatch.Stop();

                if (rc == 0)
                {
                    if (!options.Silent)
                    {
                        Console.WriteLine(@" ______   _______  __    _  _______  __  ");
                        Console.WriteLine(@"|      | |       ||  |  | ||       ||  | ");
                        Console.WriteLine(@"|  _    ||   _   ||   |_| ||    ___||  | ");
                        Console.WriteLine(@"| | |   ||  | |  ||       ||   |___ |  | ");
                        Console.WriteLine(@"| |_|   ||  |_|  ||  _    ||    ___||__| ");
                        Console.WriteLine(@"|       ||       || | |   ||   |___  __  ");
                        Console.WriteLine(@"|______| |_______||_|  |__||_______||__| ");
                        Console.WriteLine();

                        Log.Information($"All work is done successfuly. Total time spent is {stopWatch.ElapsedMilliseconds} ms.");
                        //Log.Information($"  Take a look at the `{summaryFilename}` for details on the results.");
                    }
                }

                return rc;
            }
            else
            {
                return 1;
            }
        }

        private static int Process(Options options, RecipeModel recipeModel)
        {
            try
            {
                // Imports

                RawModel rawModel = new RawModel();

                //todo factory 형태로 등록을 하면 좀 간단해질듯..

                if (recipeModel.Sources.Xlsx.Count > 0)
                {
                    var importer = new XlsxImporter();
                    importer.Import(options, recipeModel, rawModel);
                }

                if (recipeModel.Sources.GoogleSheets.Count > 0)
                {
                    var importer = new GoogleSheetsImporter();
                    importer.Import(options, recipeModel, rawModel);
                }


                // Cooking

                var cooker = new ModelCooker();
                var model = cooker.Cook(options, recipeModel, rawModel);


                // Exporting

                if (recipeModel.Exports.Binary.Count > 0)
                {
                    var exporter = new BinaryExporter();
                    exporter.Export(options, recipeModel, model);
                }

                if (recipeModel.Exports.Json.Count > 0)
                {
                    var exporter = new JsonExporter();
                    exporter.Export(options, recipeModel, model);
                }


                // Code generation

                if (recipeModel.CodeGenerations.Cpp.Count > 0)
                {
                    var codeGenerator = new CppCodeGenerator();
                    codeGenerator.Generate(options, recipeModel, model);
                }

                if (recipeModel.CodeGenerations.CSharp.Count > 0)
                {
                    var codeGenerator = new CsCodeGenerator();
                    codeGenerator.Generate(options, recipeModel, model);
                }

                if (recipeModel.CodeGenerations.Typescript.Count > 0)
                {
                    var codeGenerator = new TsCodeGenerator();
                    codeGenerator.Generate(options, recipeModel, model);
                }

                if (recipeModel.CodeGenerations.Html.Count > 0)
                {
                    var codeGenerator = new HtmlCodeGenerator();
                    codeGenerator.Generate(options, recipeModel, model);
                }

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
            Log.Fatal(@" ______ _____  _____   ____  _____  _ _ _");
            Log.Fatal(@"|  ____|  __ \|  __ \ / __ \|  __ \| | | |");
            Log.Fatal(@"| |__  | |__) | |__) | |  | | |__) | | | |");
            Log.Fatal(@"|  __| |  _  /|  _  /| |  | |  _  /| | | |");
            Log.Fatal(@"| |____| | \ \| | \ \| |__| | | \ \|_|_|_|");
            Log.Fatal(@"|______|_|  \_\_|  \_\\____/|_|  \_(_|_|_)");
            Log.Fatal(@"");

            Log.Fatal(ex.Message);

            if (ex is SheetManException sheetManEx)
            {
                if (sheetManEx.Location != null)
                    Log.Fatal($"   at {sheetManEx.Location}");

                if (sheetManEx.Details != null && sheetManEx.Details.Count > 0)
                {
                    for (int detailIndex = 0; detailIndex < sheetManEx.Details.Count; detailIndex++)
                    {
                        var detail = sheetManEx.Details[detailIndex];

                        Log.Fatal("");
                        Log.Fatal("Details:");
                        Log.Fatal($"  [{detailIndex + 1,3}] {detail.Message} at {detail.Location}");
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
