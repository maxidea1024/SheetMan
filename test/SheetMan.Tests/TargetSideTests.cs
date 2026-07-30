using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SheetMan.Tests
{
    /// <summary>
    /// Target-side filtering: building for one side leaves out whatever the sheet
    /// marked for the other.
    ///
    /// The markers were parsed and validated for years but never applied to any
    /// output - they only showed up as a column in the HTML documentation.
    ///
    /// Filtering happens by handing each exporter and generator a projected view of
    /// the model rather than by teaching each of them to filter, so these tests check
    /// the projection through the artifacts every consumer actually produces.
    /// </summary>
    public class TargetSideTests
    {
        private static string[] TableNames(string scenario)
            => Directory.GetFiles(Path.Combine(RepoLayout.OutputDir(scenario), "json-named"), "*.json")
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(name => !name.StartsWith("manifest"))
                        .OrderBy(name => name)
                        .ToArray();

        private static string[] FieldNames(string scenario, string table)
        {
            string json = File.ReadAllText(
                Path.Combine(RepoLayout.OutputDir(scenario), "json-named", table + ".json"));

            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement[0].EnumerateObject().Select(p => p.Name).ToArray();
        }

        [Fact]
        public void Client_build_drops_server_entities_and_columns()
        {
            var result = SheetManRunner.Convert("core-client");
            Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

            var tables = TableNames("core-client");
            Assert.Contains("ClientStrings", tables);
            Assert.DoesNotContain("ServerTuning", tables);

            var fields = FieldNames("core-client", "TestFieldTypes");
            Assert.Contains("boolField", fields);      // marked c
            Assert.DoesNotContain("intField", fields); // marked s
            Assert.Contains("index", fields);          // primary index always survives

            Assert.DoesNotContain("price", FieldNames("core-client", "Item"));
        }

        [Fact]
        public void Server_build_drops_client_entities_and_columns()
        {
            var result = SheetManRunner.Convert("core-server");
            Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

            var tables = TableNames("core-server");
            Assert.Contains("ServerTuning", tables);
            Assert.DoesNotContain("ClientStrings", tables);

            var fields = FieldNames("core-server", "TestFieldTypes");
            Assert.Contains("intField", fields);        // marked s
            Assert.DoesNotContain("boolField", fields); // marked c
            Assert.Contains("index", fields);

            Assert.Contains("price", FieldNames("core-server", "Item"));
        }

        /// <summary>
        /// Filtering has to reach the binary tables too, not just the readable
        /// artifacts - the generated readers are built against the same column set.
        /// </summary>
        [Fact]
        public void Binary_tables_reflect_the_filtered_column_set()
        {
            SheetManRunner.Convert("core");
            long both = new FileInfo(Path.Combine(RepoLayout.OutputDir("core"), "binary", "TestFieldTypes.table")).Length;

            SheetManRunner.Convert("core-client");
            long client = new FileInfo(Path.Combine(RepoLayout.OutputDir("core-client"), "binary", "TestFieldTypes.table")).Length;

            Assert.True(client < both,
                $"Client binary ({client} bytes) should be smaller than the unfiltered one ({both} bytes).");

            Assert.False(File.Exists(Path.Combine(RepoLayout.OutputDir("core-client"), "binary", "ServerTuning.table")),
                "A server-only table was written into the client build.");
        }

        /// <summary>
        /// An unrecognized side in a recipe is a configuration mistake, and the error
        /// has to name the section rather than a cell, because there is no cell.
        /// </summary>
        [Fact]
        public void Unrecognized_recipe_target_side_is_rejected()
        {
            var ex = Assert.Throws<SheetManException>(
                () => SheetMan.Recipe.RecipeTargetSide.Of("both", "Exports.Json"));

            Assert.Contains("Exports.Json", ex.Message);
            Assert.Contains("both", ex.Message);
        }
    }
}
