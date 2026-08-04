using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace SheetMan.Tests
{
    internal sealed class RunResult
    {
        public int ExitCode;
        public string StdOut;
        public string StdErr;

        public bool Succeeded => ExitCode == 0;

        public string Describe()
            => $"exit code {ExitCode}{Environment.NewLine}--- stdout ---{Environment.NewLine}{StdOut}{Environment.NewLine}--- stderr ---{Environment.NewLine}{StdErr}";
    }

    /// <summary>
    /// Drives the SheetMan CLI as a subprocess.
    ///
    /// Running out of process rather than calling into the code directly is
    /// deliberate: SheetMan keeps conversion state in statics (Model.Current,
    /// RecipeModel.Current, StagingFiles), so two in-process conversions in the same
    /// test run would contaminate each other. A subprocess also exercises the real
    /// entry point, including argument parsing and exit codes.
    /// </summary>
    internal static class SheetManRunner
    {
        private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>
        /// The CLI as an executable, built once for the whole test run.
        /// </summary>
        /// <remarks>
        /// Every conversion used to go through `dotnet run --project`, which evaluates the
        /// project and checks the build before it starts the program - two to four seconds
        /// each, on a suite that converts more than thirty times. That was most of a minute of
        /// MSBuild doing nothing.
        ///
        /// Built once into a directory of its own instead, and then invoked directly. The
        /// subprocess is still a subprocess, which is the point of running it this way at all:
        /// SheetMan keeps conversion state in statics, so two in-process conversions in one
        /// test run would contaminate each other.
        ///
        /// Lazy rather than a fixture, because xunit runs collections in parallel and a
        /// fixture would have to be depended on by every class that converts - including the
        /// ones whose only interest in the CLI is that it exists.
        /// </remarks>
        private static readonly Lazy<string> Executable = new Lazy<string>(Build);

        private static string Build()
        {
            string outputDir = RepoLayout.OutputDir("_cli");

            var built = Run(null, "dotnet",
                "build", RepoLayout.CliProject, "--nologo", "-v", "quiet", "-o", outputDir);

            if (!built.Succeeded)
                throw new InvalidOperationException($"Could not build the CLI.{Environment.NewLine}{built.Describe()}");

            string path = Path.Combine(outputDir, OnWindows ? "SheetMan.exe" : "SheetMan");

            if (!File.Exists(path))
                throw new InvalidOperationException($"The CLI build produced no executable at {path}.");

            return path;
        }

        /// <param name="extraArgs">
        /// Further command line arguments, for the options whose whole purpose is to change
        /// what a run produces from an unchanged recipe.
        /// </param>
        public static RunResult Convert(string scenario,
                                        IReadOnlyDictionary<string, string> environment = null,
                                        params string[] extraArgs)
        {
            // Each scenario owns its output tree, and it is rebuilt from scratch so a
            // file that stops being generated shows up as a deletion rather than
            // lingering from a previous run.
            string outputDir = RepoLayout.OutputDir(scenario);
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);

            // --debug: makes SheetMan print the call stack when it throws. Successful runs are
            // unaffected, and it lets the defect tests assert on stack frames instead of
            // framework exception text, which the runtime localizes.
            //
            // No --no-launch-profile any more: launchSettings.json is `dotnet run`'s business
            // and the executable does not read it.
            var args = new List<string> { "--recipe", RepoLayout.Recipe(scenario), "--debug" };

            args.AddRange(extraArgs ?? Array.Empty<string>());

            return Run(environment, Executable.Value, args.ToArray());
        }

        /// <summary>
        /// Invokes the CLI with arbitrary arguments and no scenario.
        ///
        /// For the options that do not convert anything - `--new-recipe`, `--help` - where
        /// there is no output tree to clear and no recipe to point at.
        /// </summary>
        public static RunResult Invoke(params string[] arguments) => Invoke(null, arguments);

        /// <summary>
        /// The same, with an environment.
        ///
        /// The reading side of the history takes its connection from a recipe whose
        /// `${...}` placeholders come from the environment, exactly as a conversion's does.
        /// </summary>
        public static RunResult Invoke(IReadOnlyDictionary<string, string> environment, params string[] arguments)
            => Run(environment, Executable.Value, arguments);

        private static RunResult Run(
            IReadOnlyDictionary<string, string> environment, string fileName, params string[] args)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = RepoLayout.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,

                // UTF-8 explicitly. Without it the subprocess's output is decoded as the
                // system codepage, which on Windows turns every non-ASCII author name and
                // cell value in a report into question marks.
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            // Database connection strings in the recipes carry `${...}` placeholders
            // that the converter resolves from its own environment, so secrets stay out
            // of committed files. The values have to reach the subprocess.
            if (environment != null)
            {
                foreach (var pair in environment)
                    psi.Environment[pair.Key] = pair.Value;
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(milliseconds: 300_000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException("SheetMan did not finish within 5 minutes.");
                }

                // Flushes any output still buffered after the process exits.
                process.WaitForExit();

                return new RunResult
                {
                    ExitCode = process.ExitCode,
                    StdOut = stdout.ToString(),
                    StdErr = stderr.ToString(),
                };
            }
        }
    }
}
