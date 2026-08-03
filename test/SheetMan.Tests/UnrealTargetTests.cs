using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SheetMan.Tests
{
    /// <summary>
    /// The Unreal target: USTRUCT rows, UENUM enums, a static accessor and a module the
    /// project can add as it stands.
    ///
    /// The wire format is not re-implemented here - the module ships the same C++ reader
    /// the plain C++ target does, and that one is already checked against the conformance
    /// corpus. What is new is the Unreal wrapping, and what can go wrong with it is what
    /// Unreal Header Tool rejects.
    /// </summary>
    public class UnrealTargetTests
    {
        private const string Scenario = "unreal";

        private static string ModuleDir(string scenario, string moduleName)
            => Path.Combine(RepoLayout.OutputDir(scenario), "Source", moduleName);

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
