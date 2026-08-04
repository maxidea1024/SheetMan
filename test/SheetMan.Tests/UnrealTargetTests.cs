using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SheetMan.Tests
{
    /// <summary>
    /// The Unreal target: USTRUCT rows, UENUM enums, a static accessor and a module the
    /// project can add as it stands.
    ///
    /// The wire format is checked by the conformance corpus through the plain C++ reader,
    /// which reads the same bytes. What is checked here is everything that makes this an
    /// Unreal module rather than C++ in a folder: that Unreal Header Tool accepts it, and
    /// that it is written in the engine's own types and error handling.
    ///
    /// The last two matter because the target shipped for a while with the plain C++
    /// reader inside it. That built, and UHT accepted it, and the corpus passed - and it
    /// was still wrong: std::string and a SheetMan uuid struct where FString and FGuid
    /// belonged, costing an allocation per string cell and a text parse per uuid, and a
    /// reader that reported a malformed file by throwing inside a module that Unreal
    /// builds with exceptions disabled. Nothing in the suite noticed, so these do.
    /// </summary>
    public class UnrealTargetTests
    {
        private const string Scenario = "unreal";

        private static string ModuleDir(string scenario, string moduleName)
            => Path.Combine(RepoLayout.OutputDir(scenario), "Source", moduleName);

        /// <summary>
        /// Every generated line of the module that is not a comment.
        ///
        /// Comments are dropped because the ones explaining why the standard library is
        /// not used here would otherwise fail the tests that check it is not used.
        /// </summary>
        private static IReadOnlyList<(string File, int Line, string Text)> CodeLines()
        {
            var lines = new List<(string, int, string)>();

            string module = ModuleDir(Scenario, "SheetManCore");

            foreach (var path in Directory.EnumerateFiles(module, "*.*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(path) != ".h" && Path.GetExtension(path) != ".cpp")
                    continue;

                var text = File.ReadAllLines(path);

                for (int i = 0; i < text.Length; i++)
                {
                    string trimmed = text[i].TrimStart();

                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal)
                        || trimmed.StartsWith("/*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    lines.Add((Path.GetFileName(path), i + 1, text[i]));
                }
            }

            Assert.NotEmpty(lines);

            return lines;
        }

        private static void NothingContains(string needle, string why)
        {
            var offenders = CodeLines()
                .Where(line => line.Text.Contains(needle, StringComparison.Ordinal))
                .Select(line => $"  {line.File}:{line.Line}  {line.Text.Trim()}")
                .ToList();

            Assert.True(offenders.Count == 0,
                $"{why}{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
        }

        [Fact]
        public void Generates_a_module_that_needs_no_wiring_up()
        {
            var result = SheetManRunner.Convert(Scenario);
            Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

            string module = ModuleDir(Scenario, "SheetManCore");

            // A module is these four things. Anything missing and a project has work to do
            // before the output compiles, which is the thing this target is for.
            Assert.True(File.Exists(Path.Combine(module, "SheetManCore.Build.cs")));
            Assert.True(File.Exists(Path.Combine(module, "Public", "FSheetManCore.h")));
            Assert.True(File.Exists(Path.Combine(module, "Private", "FSheetManCore.cpp")));
            Assert.True(File.Exists(Path.Combine(module, "Public", "SheetManLiteBinaryReader.h")));
        }

        /// <summary>
        /// The module is written in the engine's types, not the standard library's.
        ///
        /// Unreal has an equivalent for every type a table holds, and going through the
        /// standard library's meant building an FString from a std::string and an FGuid by
        /// parsing text a uuid struct had just printed. Both are gone; this is what keeps
        /// them gone, because nothing else in the suite can tell the difference.
        /// </summary>
        [Fact]
        public void The_module_is_written_in_engine_types()
        {
            SheetManRunner.Convert(Scenario);

            NothingContains("std::", "The module uses a standard library type where the engine has one:");

            // A standard library header is how one gets in. Engine headers are quoted.
            NothingContains("#include <", "The module includes a standard library header:");
        }

        /// <summary>
        /// Nothing in the module throws.
        ///
        /// Unreal builds a module with exceptions disabled unless its Build.cs asks
        /// otherwise, so a throw is not a failure a caller can handle - it is the process
        /// ending. The reader reports a malformed file by returning false instead, which
        /// is what `bool Read(const FString&)` has always claimed to do.
        /// </summary>
        [Fact]
        public void Nothing_in_the_module_throws()
        {
            SheetManRunner.Convert(Scenario);

            NothingContains("throw", "The module throws, and Unreal builds it with exceptions disabled:");

            // And the Build.cs must not quietly turn exceptions on to make the above safe.
            // That would work, and it would also mean every module depending on this one
            // pays for it. The assignment rather than the word: the file says in a comment
            // why it does not set this, and saying so is the opposite of an offence.
            string build = File.ReadAllText(
                Path.Combine(ModuleDir(Scenario, "SheetManCore"), "SheetManCore.Build.cs"));

            Assert.DoesNotMatch(@"bEnableExceptions\s*=\s*true", build);
        }

        /// <summary>
        /// A malformed table is refused rather than half-loaded.
        ///
        /// Checked on the generated text rather than by running it, because running it
        /// needs an engine. What is pinned is that the load looks at the reader's failure
        /// after the row loop and returns false - the loop itself cannot, since the reader
        /// keeps going quietly by design so that twenty fields need no twenty checks.
        /// </summary>
        [Fact]
        public void A_malformed_table_is_refused()
        {
            SheetManRunner.Convert(Scenario);

            string source = File.ReadAllText(Path.Combine(
                ModuleDir(Scenario, "SheetManCore"), "Private", "FSheetManCore.cpp"));

            Assert.Contains("if (Reader.HasFailed())", source);

            // The row loop stops on failure too. Without it a corrupt row count spins,
            // appending a default record per turn until the allocator gives up.
            Assert.Contains("&& !Reader.HasFailed())", source);
        }

        /// <summary>
        /// The generated include must be the last one.
        ///
        /// Unreal Header Tool requires it, and when it is not, the error it reports names
        /// some other line entirely - so this is worth pinning rather than rediscovering.
        /// </summary>
        [Fact]
        public void The_generated_include_comes_last()
        {
            SheetManRunner.Convert(Scenario);

            var includes = File.ReadAllLines(
                    Path.Combine(ModuleDir(Scenario, "SheetManCore"), "Public", "FSheetManCore.h"))
                .Where(line => line.TrimStart().StartsWith("#include", StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(includes);
            Assert.Contains(".generated.h", includes[includes.Count - 1]);
        }

        /// <summary>
        /// A BlueprintType enum is uint8, so a label outside 0 to 255 cannot be represented.
        ///
        /// The generator says so rather than emitting a header that fails deep inside the
        /// header tool, after a build has already started.
        /// </summary>
        [Fact]
        public void Enum_values_outside_a_byte_are_rejected()
        {
            var result = SheetManRunner.Convert("unreal-enum-range");

            Assert.False(result.Succeeded, "An enum value a uint8 cannot hold was accepted.");
            Assert.Contains("1048576", result.StdOut);
            Assert.Contains("uint8", result.StdOut);
        }

        /// <summary>
        /// Unreal Header Tool accepts the generated module.
        ///
        /// This is the only check that reaches the Unreal-specific part - the reflection
        /// macros, the include order, the property types UHT will and will not take - and
        /// it needs an engine, which CI does not have. Point SHEETMAN_UE_ROOT at an engine
        /// and it runs; leave it unset and it does not.
        ///
        /// Verified by hand against 4.27.2 when the target was written. UE4 is the stricter
        /// of the two the target supports: its header tool rejects a double property, which
        /// is why a double member here carries no UPROPERTY.
        /// </summary>
        [Fact]
        public void Unreal_header_tool_accepts_the_generated_module()
        {
            string engineRoot = Environment.GetEnvironmentVariable("SHEETMAN_UE_ROOT");

            if (string.IsNullOrEmpty(engineRoot))
                return;

            SheetManRunner.Convert(Scenario);

            var result = UnrealToolchain.RunHeaderTool(
                engineRoot,
                ModuleDir(Scenario, "SheetManCore"),
                moduleName: "SheetManCore",
                headerName: "FSheetManCore.h");

            Assert.True(result.Succeeded,
                $"Unreal Header Tool rejected the generated module.{Environment.NewLine}{result.Output}");
        }
    }
}
