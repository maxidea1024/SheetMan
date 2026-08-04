using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SheetMan.Tests
{
    /// <summary>
    /// Compiles and runs the generated C.
    ///
    /// Separate from <see cref="CppToolchain"/> even though it finds the same compiler,
    /// because what it asks of it is different: C rather than C++, more than one
    /// translation unit, and the strict warning set that catches what C lets through
    /// quietly. Discovery is shared - a machine with a C++ compiler has a C one.
    ///
    /// Warnings are errors here. C will happily compile an implicit declaration or a
    /// pointer conversion that is wrong, and generated code is exactly where nobody is
    /// reading closely enough to notice.
    /// </summary>
    internal static class CToolchain
    {
        private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        public static bool IsAvailable(out string reason) => CppToolchain.IsAvailable(out reason);

        /// <summary>
        /// Builds a harness against a scenario's generated C and leaves the executable in
        /// <paramref name="workDir"/>.
        /// </summary>
        public static ToolResult CompileHarness(
            string workDir, string includeDir, string source, string accessorHeader,
            IReadOnlyList<string> sources, string exeName)
        {
            Directory.CreateDirectory(workDir);

            var all = new List<string> { source };
            all.AddRange(sources);

            string exe = Path.Combine(workDir, OnWindows ? exeName + ".exe" : exeName);

            return Build(workDir, includeDir, all, accessorHeader, exe);
        }

        /// <summary>
        /// Compiles a scenario's generated C without running anything.
        ///
        /// For the reserved-word fixture, where the question is only whether the names the
        /// generator chose are legal C. The translation unit is written here rather than
        /// committed: it is three lines, and the header differs per scenario.
        /// </summary>
        public static ToolResult CompileOnly(
            string workDir, string includeDir, IReadOnlyList<string> sources, string accessorHeader)
        {
            Directory.CreateDirectory(workDir);

            string main = Path.Combine(workDir, "compile-only.c");

            File.WriteAllText(main, string.Join(Environment.NewLine, new[]
            {
                "/* Written by the test suite. Includes the generated header and nothing",
                "   else, so a failure here is the generated code and not the harness. */",
                "#include SHEETMAN_ACCESSOR_HEADER",
                "int main(void) { return 0; }",
                "",
            }));

            var all = new List<string> { main };
            all.AddRange(sources);

            string exe = Path.Combine(workDir, OnWindows ? "compile-only.exe" : "compile-only");

            return Build(workDir, includeDir, all, accessorHeader, exe);
        }

        /// <summary>
        /// Compiles the generated header as C++.
        ///
        /// The header wraps its declarations in `extern "C"`, which says it may be included
        /// from C++ - and nothing checked that. A member named `class` or `delete` is
        /// perfectly good C and stops a C++ compiler dead, so the C build stayed green
        /// while the header was unusable from the language it advertised.
        /// </summary>
        public static ToolResult CompileAsCpp(string workDir, string includeDir, string accessorName)
        {
            Directory.CreateDirectory(workDir);

            string source = Path.Combine(workDir, "include-from-cpp.cpp");

            File.WriteAllText(source, string.Join(Environment.NewLine, new[]
            {
                "// Written by the test suite. Includes the generated C header from C++ and",
                "// nothing else, which is the whole of what `extern \"C\"` promises.",
                "#include SHEETMAN_ACCESSOR_HEADER",
                "int main() { return 0; }",
                "",
            }));

            return CppToolchain.CompileHarness(
                workDir, includeDir, source, accessorName, "include-from-cpp");
        }

        private static ToolResult Build(
            string workDir, string includeDir, IReadOnlyList<string> sources,
            string accessorHeader, string exe)
        {
            string runtimeDir = Path.Combine(RepoLayout.Root, "lib", "c");

            return OnWindows
                ? BuildWithMsvc(workDir, includeDir, runtimeDir, sources, accessorHeader, exe)
                : BuildWithGcc(workDir, includeDir, runtimeDir, sources, accessorHeader, exe);
        }

        private static ToolResult BuildWithMsvc(
            string workDir, string includeDir, string runtimeDir,
            IReadOnlyList<string> sources, string accessorHeader, string exe)
        {
            string vcvars = FindVcVars();

            // A batch file rather than a direct cl.exe launch: cl needs the include and
            // library paths vcvars64.bat exports, and those cannot be inherited from a
            // process that never ran it.
            //
            // /TC forces C even for a file cl would otherwise take for C++, and /utf-8 says
            // the sources are UTF-8 - which they are, and the corpus depends on it.
            string script = Path.Combine(workDir, "build.bat");

            var quoted = string.Join(" ", sources.Select(path => $"\"{path}\""));

            File.WriteAllText(script, string.Join(Environment.NewLine, new[]
            {
                "@echo off",
                $"call \"{vcvars}\" >nul",
                $"cd /d \"{workDir}\"",
                $"cl /nologo /TC /W4 /WX /utf-8 /DSHEETMAN_ACCESSOR_HEADER=\\\"{accessorHeader}\\\" " +
                $"/I \"{includeDir}\" /I \"{runtimeDir}\" {quoted} " +
                $"/Fe\"{exe}\"",
                "exit /b %ERRORLEVEL%",
            }));

            return Execute("cmd.exe", workDir, "/c", script);
        }

        private static ToolResult BuildWithGcc(
            string workDir, string includeDir, string runtimeDir,
            IReadOnlyList<string> sources, string accessorHeader, string exe)
        {
            var arguments = new List<string>
            {
                "-std=c99", "-Wall", "-Wextra", "-Werror", "-pedantic",
                $"-DSHEETMAN_ACCESSOR_HEADER=\"{accessorHeader}\"",
                "-I", includeDir, "-I", runtimeDir,
            };

            arguments.AddRange(sources);
            arguments.Add("-o");
            arguments.Add(exe);

            return Execute("gcc", workDir, arguments.ToArray());
        }

        /// <summary>
        /// Locates vcvars64.bat, by the same search the C++ toolchain uses.
        ///
        /// Borrowed rather than copied: two searches that could disagree about which
        /// compiler is in use would be two answers to one question.
        /// </summary>
        private static string FindVcVars() => CppToolchain.FindVcVars();

        private static ToolResult Execute(string fileName, string workingDirectory, params string[] args)
            => CppToolchain.Execute(fileName, workingDirectory, args);
    }
}
