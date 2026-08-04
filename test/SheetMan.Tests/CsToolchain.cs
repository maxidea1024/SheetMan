using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SheetMan.Tests
{
    /// <summary>
    /// Compiles a scenario's generated C# on its own.
    ///
    /// The round-trip gate in CsGeneratorTests builds cs-check, which links a Program that
    /// reads the `core` fixture's tables by name and so only works for that scenario. This
    /// builds cs-compile-check instead: no source of its own, just the generated files, for
    /// the scenarios where the question is only whether the output is valid C#.
    /// </summary>
    internal static class CsToolchain
    {
        /// <summary>
        /// Compiles a scenario's generated C# the way a plain .NET consumer would.
        /// </summary>
        public static ToolResult Compile(string scenario, string accessorName)
            => Compile(scenario, accessorName, unitySymbols: null);

        /// <summary>
        /// Compiles it with a set of Unity's own symbols defined, so the branches Unity
        /// takes are checked rather than assumed.
        /// </summary>
        /// <param name="unitySymbols">
        /// Semicolon separated, as Unity would have them - `UNITY_5_3_OR_NEWER` on its own
        /// for the old API level, plus `UNITY_2021_2_OR_NEWER` for the current one. Null
        /// compiles the plain path.
        /// </param>
        public static ToolResult Compile(string scenario, string accessorName, string unitySymbols)
        {
            string label = unitySymbols == null
                ? "-compile"
                : "-compile-" + unitySymbols.Replace(';', '-').ToLowerInvariant();

            string workDir = Path.Combine(RepoLayout.OutputDir("_cscheck"), scenario + label);
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);

            Directory.CreateDirectory(workDir);

            string generatedDir = Path.Combine(RepoLayout.OutputDir(scenario), "csharp");

            if (!File.Exists(Path.Combine(generatedDir, accessorName + ".cs")))
            {
                return new ToolResult
                {
                    Succeeded = false,
                    Output = $"No generated accessor at {Path.Combine(generatedDir, accessorName + ".cs")}.",
                };
            }

            var arguments = new System.Collections.Generic.List<string>
            {
                "build",
                Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "cs-compile-check", "cs-compile-check.csproj"),
                "--nologo",
                $"-p:GeneratedDir={generatedDir}",
            };

            if (unitySymbols != null)
            {
                // %3B, because MSBuild splits a property value on a literal semicolon and
                // would take the second symbol for another target to build.
                arguments.Add($"-p:UnitySymbols={unitySymbols.Replace(";", "%3B")}");
            }

            arguments.Add("-o");
            arguments.Add(workDir);

            return Execute("dotnet", RepoLayout.Root, arguments.ToArray());
        }

        private static ToolResult Execute(string fileName, string workingDirectory, params string[] args)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            var stdout = new StringBuilder();
            var combined = new StringBuilder();

            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stdout.AppendLine(e.Data);
                combined.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) combined.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(milliseconds: 300_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"`{fileName}` did not finish within 5 minutes.");
            }

            process.WaitForExit();

            return new ToolResult
            {
                Succeeded = process.ExitCode == 0,
                StdOut = stdout.ToString(),
                Output = combined.ToString(),
            };
        }
    }
}
