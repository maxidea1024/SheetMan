using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        /// <summary>Whether a Go toolchain is on the path.</summary>
        public static bool GoIsAvailable(out string reason)
        {
            try
            {
                var probe = Execute("go", RepoLayout.Root, "version");
                reason = probe.Succeeded ? null : $"`go version` failed.{Environment.NewLine}{probe.Output}";
                return probe.Succeeded;
            }
            catch (Exception ex)
            {
                reason = $"`go` could not be started: {ex.Message}";
                return false;
            }
        }

        public static ToolResult RunGo(string scenario)
        {
            // The harness goes inside the generated module, as a package of its own, because
            // Go has no relative imports and the generated code is only importable from
            // within the module its go.mod declares.
            string moduleDir = Path.Combine(RepoLayout.OutputDir(scenario), "go");
            string harnessDir = Path.Combine(moduleDir, "harness");

            Directory.CreateDirectory(harnessDir);
            File.Copy(Path.Combine(HarnessDir("go"), "main.go"),
                      Path.Combine(harnessDir, "main.go"), overwrite: true);

            return Execute("go", moduleDir, "run", "./harness", BinaryDir(scenario));
        }

        /// <summary>Whether a Rust toolchain is on the path.</summary>
        public static bool RustIsAvailable(out string reason)
        {
            try
            {
                var probe = Execute("cargo", RepoLayout.Root, "--version");
                reason = probe.Succeeded ? null : $"`cargo --version` failed.{Environment.NewLine}{probe.Output}";
                return probe.Succeeded;
            }
            catch (Exception ex)
            {
                reason = $"`cargo` could not be started: {ex.Message}";
                return false;
            }
        }

        public static ToolResult RunRust(string scenario)
        {
            // As a binary inside the generated crate, for the same reason the Go harness is
            // a package inside the generated module: that is the only place the generated
            // types are importable from.
            string crateDir = Path.Combine(RepoLayout.OutputDir(scenario), "rust");
            string binDir = Path.Combine(crateDir, "src", "bin");

            Directory.CreateDirectory(binDir);
            File.Copy(Path.Combine(HarnessDir("rust"), "harness.rs"),
                      Path.Combine(binDir, "harness.rs"), overwrite: true);

            return Execute("cargo", crateDir,
                           "run", "--quiet", "--bin", "harness", "--", BinaryDir(scenario));
        }

        /// <summary>Whether a Python interpreter is on the path.</summary>
        public static bool PythonIsAvailable(out string reason)
        {
            try
            {
                var probe = Execute(PythonExecutable, RepoLayout.Root, "--version");
                reason = probe.Succeeded ? null : $"`python --version` failed.{Environment.NewLine}{probe.Output}";
                return probe.Succeeded;
            }
            catch (Exception ex)
            {
                reason = $"`{PythonExecutable}` could not be started: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// `python` on Windows, `python3` elsewhere - the name that exists on each.
        /// </summary>
        private static string PythonExecutable => OnWindows ? "python" : "python3";

        public static ToolResult RunPython(string scenario)
        {
            // Beside the generated package rather than inside it, so the package's own
            // directory holds only generated files and the import reads as a consumer's
            // would.
            string root = Path.Combine(RepoLayout.OutputDir(scenario), "python");
            string harness = Path.Combine(root, "harness.py");

            File.Copy(Path.Combine(HarnessDir("python"), "harness.py"), harness, overwrite: true);

            // Python writes its own standard output through an encoding of its choosing,
            // which on Windows is the console codepage and mangles anything non-ASCII.
            var environment = new Dictionary<string, string> { { "PYTHONIOENCODING", "utf-8" } };

            return Execute(PythonExecutable, root, environment, "harness.py", BinaryDir(scenario));
        }

        /// <summary>Whether a JDK is on the path.</summary>
        public static bool JavaIsAvailable(out string reason)
        {
            try
            {
                var probe = Execute("javac", RepoLayout.Root, "-version");
                reason = probe.Succeeded ? null : $"`javac -version` failed.{Environment.NewLine}{probe.Output}";
                return probe.Succeeded;
            }
            catch (Exception ex)
            {
                reason = $"`javac` could not be started: {ex.Message}";
                return false;
            }
        }

        public static ToolResult RunJava(string scenario)
        {
            // Beside the generated packages, because a Java source tree is rooted at the
            // package directories and the harness is in the default package.
            string root = Path.Combine(RepoLayout.OutputDir(scenario), "java");
            string classes = Path.Combine(root, "classes");

            File.Copy(Path.Combine(HarnessDir("java"), "Harness.java"),
                      Path.Combine(root, "Harness.java"), overwrite: true);

            Directory.CreateDirectory(classes);

            var sources = Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories).ToList();

            var arguments = new List<string> { "-encoding", "UTF-8", "-d", classes };
            arguments.AddRange(sources);

            var build = Execute("javac", root, arguments.ToArray());
            if (!build.Succeeded)
                return build;

            return Execute("java", root, "-cp", classes, "Harness", BinaryDir(scenario));
        }

        /// <summary>Whether a Kotlin compiler and a JVM to run it on are both here.</summary>
        public static bool KotlinIsAvailable(out string reason)
        {
            if (!JavaIsAvailable(out string why))
            {
                reason = $"The Kotlin compiler runs on a JVM. {why}";
                return false;
            }

            if (KotlinCompilerJar() == null)
            {
                reason = "`kotlin-compiler.jar` was not found on the path or in a known install.";
                return false;
            }

            reason = null;
            return true;
        }

        public static ToolResult RunKotlin(string scenario)
        {
            // Beside the generated package, in the default package, for the same reason the
            // Java harness is: a JVM source tree is rooted at the package directories.
            string root = Path.Combine(RepoLayout.OutputDir(scenario), "kotlin");
            string jar = Path.Combine(root, "harness.jar");

            File.Copy(Path.Combine(HarnessDir("kotlin"), "Harness.kt"),
                      Path.Combine(root, "Harness.kt"), overwrite: true);

            // Through the compiler jar rather than the `kotlinc` launcher, which on Windows
            // is a batch file and cannot be started as a process at all.
            var arguments = new List<string>
            {
                "-jar", KotlinCompilerJar(),
                "-nowarn",

                // A fat jar, so running it needs nothing but a JVM.
                "-include-runtime",
                "-d", jar,
            };

            arguments.AddRange(Directory.EnumerateFiles(root, "*.kt", SearchOption.AllDirectories));

            var build = Execute("java", root, arguments.ToArray());
            if (!build.Succeeded)
                return build;

            return Execute("java", root, "-jar", jar, BinaryDir(scenario));
        }

        /// <summary>Whether a Ruby interpreter is here.</summary>
        public static bool RubyIsAvailable(out string reason)
        {
            try
            {
                var probe = Execute(RubyExecutable, RepoLayout.Root, "--version");
                reason = probe.Succeeded ? null : $"`ruby --version` failed.{Environment.NewLine}{probe.Output}";
                return probe.Succeeded;
            }
            catch (Exception ex)
            {
                reason = $"`{RubyExecutable}` could not be started: {ex.Message}";
                return false;
            }
        }

        public static ToolResult RunRuby(string scenario)
        {
            // Beside the generated file, because `require_relative` resolves against the
            // requiring file and that is the import a consumer would write.
            string root = Path.Combine(RepoLayout.OutputDir(scenario), "ruby");

            File.Copy(Path.Combine(HarnessDir("ruby"), "harness.rb"),
                      Path.Combine(root, "harness.rb"), overwrite: true);

            return Execute(RubyExecutable, root, "harness.rb", BinaryDir(scenario));
        }

        /// <summary>Whether a Dart SDK is here.</summary>
        public static bool DartIsAvailable(out string reason)
        {
            try
            {
                var probe = Execute(DartExecutable, RepoLayout.Root, "--version");
                reason = probe.Succeeded ? null : $"`dart --version` failed.{Environment.NewLine}{probe.Output}";
                return probe.Succeeded;
            }
            catch (Exception ex)
            {
                reason = $"`{DartExecutable}` could not be started: {ex.Message}";
                return false;
            }
        }

        public static ToolResult RunDart(string scenario)
        {
            // Beside the generated library, whose import of the reader is relative.
            string root = Path.Combine(RepoLayout.OutputDir(scenario), "dart");

            File.Copy(Path.Combine(HarnessDir("dart"), "harness.dart"),
                      Path.Combine(root, "harness.dart"), overwrite: true);

            return Execute(DartExecutable, root, "run", "harness.dart", BinaryDir(scenario));
        }

        // ------------------------------------------------------- finding a tool

        /// <summary>
        /// Where a toolchain lives: the bare command when the path has it, and otherwise the
        /// first well-known install location that exists.
        ///
        /// The fallback is not convenience. An installer appends to the user's path, and a
        /// shell that was already open - which is the one running these tests - keeps the
        /// path it started with. A probe that asked only the path would then report the
        /// language missing and skip its check, which is the one answer a conformance suite
        /// must not give quietly.
        /// </summary>
        private static string Resolve(string command, params string[] candidates)
            => FindOnPath(command) ?? candidates.FirstOrDefault(File.Exists) ?? command;

        private static string FindOnPath(string command)
        {
            var extensions = OnWindows
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';')
                : new[] { "" };

            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                                      .Split(Path.PathSeparator))
            {
                if (directory.Length == 0)
                    continue;

                foreach (var extension in extensions)
                {
                    string candidate;

                    try
                    {
                        candidate = Path.Combine(directory, command + extension);
                    }
                    catch (ArgumentException)
                    {
                        // A malformed PATH entry, which is common enough on Windows.
                        break;
                    }

                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        private static string HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static string RubyExecutable => Resolve("ruby", RubyInstalls().ToArray());

        /// <summary>Where RubyInstaller puts an interpreter, newest first.</summary>
        private static IEnumerable<string> RubyInstalls()
        {
            if (!OnWindows)
                yield break;

            string[] roots;

            try
            {
                roots = Directory.GetDirectories(@"C:\", "Ruby*");
            }
            catch (IOException)
            {
                yield break;
            }

            Array.Sort(roots, StringComparer.OrdinalIgnoreCase);

            for (int i = roots.Length - 1; i >= 0; i--)
                yield return Path.Combine(roots[i], "bin", "ruby.exe");
        }

        private static string DartExecutable => Resolve("dart",
            Path.Combine(HomeDir, "tools", "dart-sdk", "bin", "dart.exe"),
            Path.Combine(HomeDir, "tools", "dart-sdk", "bin", "dart"),
            @"C:\tools\dart-sdk\bin\dart.exe");

        /// <summary>
        /// The Kotlin compiler jar, found beside whichever launcher is here.
        /// </summary>
        private static string KotlinCompilerJar()
        {
            foreach (string home in KotlinHomes())
            {
                if (home == null)
                    continue;

                string jar = Path.Combine(home, "lib", "kotlin-compiler.jar");

                if (File.Exists(jar))
                    return jar;
            }

            return null;
        }

        private static IEnumerable<string> KotlinHomes()
        {
            // The launcher sits in <home>/bin, so its grandparent is the install.
            string launcher = FindOnPath("kotlinc");

            if (launcher != null)
                yield return Path.GetDirectoryName(Path.GetDirectoryName(launcher));

            yield return Path.Combine(HomeDir, "tools", "kotlinc");
            yield return @"C:\tools\kotlinc";
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
            => Execute(fileName, workingDirectory, null, args);

        private static ToolResult Execute(
            string fileName,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            params string[] args)
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

            if (environment != null)
            {
                foreach (var pair in environment)
                    psi.Environment[pair.Key] = pair.Value;
            }

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
