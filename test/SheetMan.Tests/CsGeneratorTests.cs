using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SheetMan.Tests
{
    /// <summary>
    /// The generated C# and the binary reader emitted with it.
    ///
    /// C# went without this check for a long time, and its absence is why nothing noticed
    /// that the writer truncated every 64-bit value: the reader and writer were two
    /// halves of one shared runtime, so a value that survived a round trip inside C#
    /// looked correct whatever it did on the wire. The writer now lives in the exporter
    /// and the reader is emitted separately, which makes them independent enough to be
    /// worth comparing.
    ///
    /// It also checks the thing that motivated the split: that the generated output
    /// compiles on its own, with nothing installed.
    /// </summary>
    public class CsGeneratorTests
    {
        private const string Scenario = "core";
        private const string Accessor = "CoreAccessor";

        /// <summary>
        /// The generated code compiles for a plain .NET consumer with nothing defined.
        ///
        /// It did not use to. The read path switched on `NO_UNITY`, a symbol nobody defines
        /// by default, so the default branch was the Unity one - and it carried a
        /// `using Cysharp.Threading.Tasks;` that nothing in the generated code referenced.
        /// A .NET project without UniTask installed therefore failed to compile on a line
        /// that bought it nothing.
        /// </summary>
        [Fact]
        public void Generated_code_compiles_with_nothing_defined()
        {
            var conversion = SheetManRunner.Convert(Scenario);
            Assert.True(conversion.Succeeded,
                $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

            var result = CsToolchain.Compile(Scenario, Accessor);

            Assert.True(result.Succeeded,
                $"Generated C# does not compile for a plain consumer.{Environment.NewLine}{result.Output}");
        }

        /// <summary>
        /// And it compiles for the two API levels Unity offers.
        ///
        /// `UNITY_2021_2_OR_NEWER` selects `File.ReadAllBytesAsync`, which is what .NET
        /// Standard 2.1 gave Unity; without it the code falls back to a worker thread. Both
        /// branches existed for a long time and neither was ever compiled by anything, so
        /// either could have been broken for as long as it had been there.
        ///
        /// The WebGL branch is not here. It names UnityEngine.Networking, so checking it
        /// needs an engine - the same limitation the Unreal target's header-tool gate has.
        /// </summary>
        [Theory]
        [InlineData("UNITY_5_3_OR_NEWER")]
        [InlineData("UNITY_5_3_OR_NEWER;UNITY_2021_2_OR_NEWER")]
        public void Generated_code_compiles_for_unity(string symbols)
        {
            var conversion = SheetManRunner.Convert(Scenario);
            Assert.True(conversion.Succeeded,
                $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

            var result = CsToolchain.Compile(Scenario, Accessor, symbols);

            Assert.True(result.Succeeded,
                $"Generated C# does not compile with `{symbols}` defined." +
                $"{Environment.NewLine}{result.Output}");
        }

        private static JsonElement RunGeneratedReader()
        {
            var conversion = SheetManRunner.Convert(Scenario);
            Assert.True(conversion.Succeeded,
                $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

            string workDir = Path.Combine(RepoLayout.OutputDir("_cscheck"), Scenario);
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);

            Directory.CreateDirectory(workDir);

            string generatedDir = Path.Combine(RepoLayout.OutputDir(Scenario), "csharp");

            var build = Execute("dotnet", RepoLayout.Root,
                "build",
                Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "cs-check", "cs-check.csproj"),
                "--nologo",
                $"-p:GeneratedDir={generatedDir}",
                "-o", workDir);

            Assert.True(build.Succeeded,
                $"Generated C# failed to compile on its own.{Environment.NewLine}{build.Output}");

            var run = Execute(Path.Combine(workDir, OnWindows ? "cs-check.exe" : "cs-check"),
                              workDir,
                              Path.Combine(RepoLayout.OutputDir(Scenario), "binary"));

            Assert.True(run.Succeeded,
                $"Generated C# failed to read the exported binary.{Environment.NewLine}{run.Output}");

            return JsonDocument.Parse(run.StdOut).RootElement.Clone();
        }

        private static JsonElement ExporterRows(string table)
        {
            string json = File.ReadAllText(
                Path.Combine(RepoLayout.OutputDir(Scenario), "json-named", table + ".json"));

            return JsonDocument.Parse(json).RootElement.Clone();
        }

        /// <summary>
        /// The output has to build with nothing added to the project.
        ///
        /// This is what the reader being emitted rather than installed buys: before, a
        /// consuming project had to carry a 3,600-line runtime as a plugin, of which the
        /// generated code called four members.
        /// </summary>
        [Fact]
        public void Generated_csharp_compiles_without_anything_installed()
        {
            // Compiling is the assertion; the reader would fail to resolve otherwise.
            RunGeneratedReader();

            // And the emitted reader is genuinely there, beside the accessor.
            Assert.True(File.Exists(Path.Combine(
                RepoLayout.OutputDir(Scenario), "csharp", "SheetManBinaryReader.cs")));

            // Nothing points at the runtime that used to be required.
            string accessor = File.ReadAllText(Path.Combine(
                RepoLayout.OutputDir(Scenario), "csharp", "CoreAccessor.cs"));

            Assert.DoesNotContain("SheetMan.Runtime", accessor);
        }

        [Fact]
        public void Generated_csharp_reads_back_every_primitive_type()
        {
            var actual = RunGeneratedReader().GetProperty("TestFieldTypes");
            var expected = ExporterRows("TestFieldTypes");

            Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());

            for (int i = 0; i < expected.GetArrayLength(); i++)
            {
                Assert.Equal(expected[i].GetProperty("index").GetInt32(),
                             actual[i].GetProperty("index").GetInt32());
                Assert.Equal(expected[i].GetProperty("stringField").GetString(),
                             actual[i].GetProperty("stringField").GetString());
                Assert.Equal(expected[i].GetProperty("boolField").GetBoolean(),
                             actual[i].GetProperty("boolField").GetBoolean());
                Assert.Equal(expected[i].GetProperty("intField").GetInt32(),
                             actual[i].GetProperty("intField").GetInt32());
                Assert.Equal(expected[i].GetProperty("uuidField").GetString(),
                             actual[i].GetProperty("uuidField").GetString());

                // Exported as a string so JSON cannot round it; compared as text.
                Assert.Equal(expected[i].GetProperty("bigIntField").GetString(),
                             actual[i].GetProperty("bigIntField").GetString());
            }
        }

        /// <summary>
        /// A17 - the writer used to cast a 64-bit value through uint, truncating it.
        ///
        /// Now that the writer is the exporter's own and the reader is emitted, the two
        /// are separate implementations and this comparison means something.
        /// </summary>
        [Fact]
        public void A17_sixty_four_bit_values_survive_the_round_trip()
        {
            var records = RunGeneratedReader().GetProperty("TestFieldTypes");

            Assert.Equal("9007199254740993", records[0].GetProperty("bigIntField").GetString());
            Assert.Equal("-9007199254740993", records[1].GetProperty("bigIntField").GetString());
        }

        [Fact]
        public void Generated_csharp_reads_both_array_kinds()
        {
            var records = RunGeneratedReader().GetProperty("ArrayTypes");

            string[] Texts(JsonElement row, string name)
                => row.GetProperty(name).EnumerateArray().Select(e => e.GetRawText().Trim('"')).ToArray();

            // Delimited: a different length in every row, including an empty one.
            Assert.Equal(new[] { "red", "green", "blue" }, Texts(records[0], "tags"));
            Assert.Empty(Texts(records[2], "tags"));

            // Serial: fixed width, unaffected by the delimited columns beside it.
            Assert.Equal(new[] { "5", "6" }, Texts(records[2], "slotArray"));
        }

        [Fact]
        public void Generated_csharp_resolves_cross_table_references()
        {
            var records = RunGeneratedReader().GetProperty("Item");

            Assert.Equal("Weapon", records[0].GetProperty("categoryName").GetString());
            Assert.Equal("Armor", records[1].GetProperty("categoryName").GetString());
            Assert.Equal("Potion", records[2].GetProperty("categoryName").GetString());
        }

        private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        private sealed class ToolRun
        {
            public bool Succeeded;
            public string StdOut;
            public string Output;
        }

        private static ToolRun Execute(string fileName, string workingDirectory, params string[] args)
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

            return new ToolRun
            {
                Succeeded = process.ExitCode == 0,
                StdOut = stdout.ToString(),
                Output = combined.ToString(),
            };
        }
    }
}
