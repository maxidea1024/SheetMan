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

            // --no-launch-profile: src/Properties/launchSettings.json carries a
            // hardcoded recipe path and working directory from another machine, and
            // `dotnet run` would apply them over the arguments below.
            //
            // --debug: makes SheetMan print the call stack when it throws. Successful
            // runs are unaffected, and it lets the defect tests assert on stack frames
            // instead of framework exception text, which the runtime localizes.
            var args = new List<string>
            {
                "run", "--project", RepoLayout.CliProject, "--no-launch-profile", "--",
                "--recipe", RepoLayout.Recipe(scenario), "--debug",
            };

            args.AddRange(extraArgs ?? Array.Empty<string>());

            return Run(environment, args.ToArray());
        }

        private static RunResult Run(IReadOnlyDictionary<string, string> environment, params string[] args)
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = RepoLayout.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
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
