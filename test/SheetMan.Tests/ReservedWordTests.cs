using System;
using System.IO;
using Xunit;

namespace SheetMan.Tests
{
    /// <summary>
    /// Identifiers taken from a sheet that collide with a keyword in an output language.
    ///
    /// Whether this matters depends on how a generator cases an identifier, and the three
    /// disagree. C# renders members PascalCase, which lifts every all-lowercase keyword out
    /// of the way. TypeScript renders them camelCase. C++ renders them snake_case, so a
    /// field called `Int` becomes `int` and a field called `Class` becomes `class`.
    ///
    /// Both keyword lists in the repository - CsCodeGenerator.Keywords.cs and
    /// TsCodeGenerator.Keywords.cs - were declared and never read by anything, and the C++
    /// generator had no list at all. The C# one carried a note claiming escaping made the
    /// problem moot. For C# that happens to be true; for C++ it was not, and the generator
    /// emitted `std::string class;` while the conversion reported success.
    ///
    /// These tests exist so the compilers answer the question rather than a comment.
    /// </summary>
    public class ReservedWordTests
    {
        private const string Scenario = "reserved-words";

        /// <summary>
        /// C++ members are snake_case, which is where this actually bites.
        /// </summary>
        [Fact]
        public void Generated_cpp_compiles_with_keyword_named_fields()
        {
            var conversion = SheetManRunner.Convert(Scenario);
            Assert.True(conversion.Succeeded,
                $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

            Assert.True(CppToolchain.IsAvailable(out string why),
                $"A C++17 compiler is required to check the generated C++. {why}");

            var result = CppToolchain.Compile(Scenario, "ReservedAccessor");

            Assert.True(result.Succeeded,
                $"Generated C++ does not compile.{Environment.NewLine}{result.Output}");
        }

        /// <summary>
        /// C# members are PascalCase, so a lowercase keyword cannot survive into one. The
        /// test records that rather than assuming it.
        /// </summary>
        [Fact]
        public void Generated_csharp_compiles_with_keyword_named_fields()
        {
            var conversion = SheetManRunner.Convert(Scenario);
            Assert.True(conversion.Succeeded,
                $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

            var result = CsToolchain.Compile(Scenario, "ReservedAccessor");

            Assert.True(result.Succeeded,
                $"Generated C# does not compile.{Environment.NewLine}{result.Output}");
        }

        /// <summary>
        /// TypeScript members are camelCase. Most reserved words are legal as member names,
        /// but `constructor` is not something a class can define as an accessor.
        /// </summary>
        [Fact]
        public void Generated_typescript_type_checks_with_keyword_named_fields()
        {
            Assert.True(TypescriptToolchain.IsAvailable(out string why),
                $"Node toolchain required to type-check generated TypeScript. {why}");

            var conversion = SheetManRunner.Convert(Scenario);
            Assert.True(conversion.Succeeded,
                $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

            string generatedDir = Path.Combine(RepoLayout.OutputDir(Scenario), "typescript");

            var check = TypescriptToolchain.TypeCheck(generatedDir);

            Assert.True(check.Succeeded,
                $"Generated TypeScript does not compile.{Environment.NewLine}{check.Output}");
        }
    }
}
