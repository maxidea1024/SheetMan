using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SheetMan.Tests
{
    /// <summary>
    /// Builds and runs the per-language conformance harnesses.
    ///
    /// One method per language, each about as long as the harness it drives. Adding a
    /// language means adding one of each, which is the whole point of the corpus: the
    /// comparison in ConformanceTests is language-agnostic and does not grow.
    /// </summary>
    internal static class ConformanceHarness
    {
        private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        private static string HarnessDir(string language)
            => Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "conformance", language);

        private static string BinaryDir(string scenario)
            => Path.Combine(RepoLayout.OutputDir(scenario), "binary");

        public static ToolResult RunCsharp(string scenario)
        {
            string workDir = WorkDir(scenario, "csharp");

            var build = Execute("dotnet", RepoLayout.Root,
                "build",
                Path.Combine(HarnessDir("csharp"), "conformance-csharp.csproj"),
                "--nologo",
                $"-p:GeneratedDir={Path.Combine(RepoLayout.OutputDir(scenario), "csharp")}",
                "-o", workDir);

            if (!build.Succeeded)
                return build;

            return Execute(Path.Combine(workDir, OnWindows ? "conformance-csharp.exe" : "conformance-csharp"),
                           workDir, BinaryDir(scenario));
        }

        public static ToolResult RunCpp(string scenario)
        {
            string workDir = WorkDir(scenario, "cpp");

            var build = CppToolchain.CompileHarness(
                workDir,
                includeDir: Path.Combine(RepoLayout.OutputDir(scenario), "cpp"),
                source: Path.Combine(HarnessDir("cpp"), "main.cpp"),
                accessorName: "ConformanceAccessor",
                exeName: "conformance-cpp");

            if (!build.Succeeded)
                return build;

            return Execute(Path.Combine(workDir, OnWindows ? "conformance-cpp.exe" : "conformance-cpp"),
                           workDir, BinaryDir(scenario));
        }

        public static ToolResult RunTypescript(string scenario)
        {
            // The harness is copied in beside the generated modules rather than importing
            // across directories, because its import paths are the ones a consumer would
            // write and those are relative to the generated output.
            string generatedDir = Path.Combine(RepoLayout.OutputDir(scenario), "typescript");
            string entry = Path.Combine(generatedDir, "conformance-main.ts");

            File.Copy(Path.Combine(HarnessDir("ts"), "main.ts"), entry, overwrite: true);

            return TypescriptToolchain.RunScript(entry, BinaryDir(scenario));
        }

        private static string WorkDir(string scenario, string language)
        {
            string dir = Path.Combine(RepoLayout.OutputDir("_conformance"), scenario, language);

            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            Directory.CreateDirectory(dir);
            return dir;
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
                StandardOutputEncoding = new UTF8Encoding(false),
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
