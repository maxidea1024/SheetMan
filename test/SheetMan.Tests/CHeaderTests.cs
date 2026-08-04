using System;
using Xunit;

namespace SheetMan.Tests
{
    /// <summary>
    /// Whether each generated C header compiles as the only thing a translation unit includes.
    ///
    /// The question the split created, and the one compiling the sources does not ask. Building
    /// the .c files says the headers work in the order those files include them; it says nothing
    /// about a header a consumer reaches for directly. A table header that needs an enum's
    /// complete type and does not include it compiles perfectly well inside a source file that
    /// included the umbrella first, and fails the moment anyone includes it alone.
    ///
    /// C is the one target where this can go wrong at all. Every other language resolves a name
    /// by module or package; C resolves it by whatever text came before, so the includes are the
    /// dependency graph rather than a description of it.
    /// </summary>
    public class CHeaderTests
    {
        /// <summary>
        /// Both corpora that generate C, because they fail differently.
        ///
        /// `conformance` has an enum a table is typed with and two tables referencing each other's
        /// rows - the edges. `reserved-words` has names taken from the keyword list - the
        /// escaping. Neither covers the other: dropping the enum include is invisible to
        /// reserved-words, which declares no enum.
        /// </summary>
        public static TheoryData<string> Scenarios => new TheoryData<string> { "conformance", "reserved-words" };

        [Theory]
        [MemberData(nameof(Scenarios))]
        public void Every_generated_header_compiles_on_its_own(string scenario)
        {
            Assert.True(ConformanceHarness.CIsAvailable(out string why), why);

            var conversion = SheetManRunner.Convert(scenario);

            Assert.True(conversion.Succeeded,
                $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

            var result = ConformanceHarness.CompileEachCHeaderAlone(scenario);

            Assert.True(result.Succeeded, result.Output);
        }

        /// <summary>
        /// And a table's own header does not include another table's.
        /// </summary>
        /// <remarks>
        /// Two tables referencing each other's rows is legal in the sheets and does happen, so
        /// includes between table headers would be a cycle - and a cycle between include-guarded
        /// headers does not fail loudly. It resolves: whichever is included first sees an
        /// incomplete version of the other and compiles, or does not, depending on which
        /// translation unit reached it first. The generated code would work until somebody
        /// included the headers in the other order.
        ///
        /// A pointer member needs only an incomplete type, so every record is forward declared in
        /// one header that all of them include. This says that is what happened, rather than that
        /// today's corpus happens not to have found the cycle.
        /// </remarks>
        [Theory]
        [MemberData(nameof(Scenarios))]
        public void No_table_header_includes_another(string scenario)
        {
            var conversion = SheetManRunner.Convert(scenario);

            Assert.True(conversion.Succeeded,
                $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

            string root = System.IO.Path.Combine(RepoLayout.OutputDir(scenario), "c");

            // A table header is the one declaring a record struct; the umbrella and the forward
            // header are not, and the umbrella includes everything on purpose.
            var tableHeaders = System.IO.Directory.GetFiles(root, "*.h");
            int checkedCount = 0;

            foreach (var header in tableHeaders)
            {
                string text = System.IO.File.ReadAllText(header);

                if (!text.Contains("Record_t {"))
                    continue;

                checkedCount++;

                foreach (var other in tableHeaders)
                {
                    string otherName = System.IO.Path.GetFileName(other);

                    if (other == header || !System.IO.File.ReadAllText(other).Contains("Record_t {"))
                        continue;

                    Assert.DoesNotContain($"#include \"{otherName}\"", text);
                }
            }

            Assert.True(checkedCount > 0, $"`{scenario}` generated no C table header to check.");
        }
    }
}
