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
    /// These tests exist so the compilers answer the question rather than a comment - and
    /// every language SheetMan generates is here, not the three whose answer somebody had
    /// already worked out. Extending it to the other seven found one immediately: a field
    /// named `Int` became `int` in Dart, which shadows the type inside its own class, so
    /// `int int = 0;` did not compile and neither did any declaration after it. Dart's
    /// keyword list did not catch it because `int` is not a keyword - it is an ordinary
    /// identifier that happens to name a type, which is exactly why it collides.
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

        // ------------------------------------------------- the other seven languages

        /// <summary>
        /// Converts once and hands back nothing: each language's test calls this and then
        /// compiles its own output.
        /// </summary>
        private static void Convert()
        {
            var conversion = SheetManRunner.Convert(Scenario);

            Assert.True(conversion.Succeeded,
                $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");
        }

        /// <summary>
        /// Go members are PascalCase and every Go keyword is lower case, so nothing should
        /// collide - and an exported member has to start with a capital anyway. Recorded
        /// rather than assumed.
        /// </summary>
        [Fact]
        public void Generated_go_compiles_with_keyword_named_fields()
        {
            Assert.True(ConformanceHarness.GoIsAvailable(out string why), why);

            Convert();

            var result = ConformanceHarness.CompileGo(Scenario);

            Assert.True(result.Succeeded, $"Generated Go does not compile.{Environment.NewLine}{result.Output}");
        }

        /// <summary>Rust members are snake_case, which is where its keywords live.</summary>
        [Fact]
        public void Generated_rust_compiles_with_keyword_named_fields()
        {
            Assert.True(ConformanceHarness.RustIsAvailable(out string why), why);

            Convert();

            var result = ConformanceHarness.CompileRust(Scenario);

            Assert.True(result.Succeeded, $"Generated Rust does not compile.{Environment.NewLine}{result.Output}");
        }

        [Fact]
        public void Generated_python_compiles_with_keyword_named_fields()
        {
            Assert.True(ConformanceHarness.PythonIsAvailable(out string why), why);

            Convert();

            var result = ConformanceHarness.CompilePython(Scenario);

            Assert.True(result.Succeeded, $"Generated Python does not compile.{Environment.NewLine}{result.Output}");
        }

        [Fact]
        public void Generated_java_compiles_with_keyword_named_fields()
        {
            Assert.True(ConformanceHarness.JavaIsAvailable(out string why), why);

            Convert();

            var result = ConformanceHarness.CompileJava(Scenario);

            Assert.True(result.Succeeded, $"Generated Java does not compile.{Environment.NewLine}{result.Output}");
        }

        /// <summary>
        /// Kotlin escapes with backticks rather than by changing the name, so the generated
        /// member really is called `class`. Whether that compiles is the question.
        /// </summary>
        [Fact]
        public void Generated_kotlin_compiles_with_keyword_named_fields()
        {
            Assert.True(ConformanceHarness.KotlinIsAvailable(out string why), why);

            Convert();

            var result = ConformanceHarness.CompileKotlin(Scenario);

            Assert.True(result.Succeeded, $"Generated Kotlin does not compile.{Environment.NewLine}{result.Output}");
        }

        /// <summary>
        /// Ruby members are snake_case and nearly every Ruby keyword is lower case, so this
        /// is the language with the most ways to collide.
        /// </summary>
        [Fact]
        public void Generated_ruby_parses_with_keyword_named_fields()
        {
            Assert.True(ConformanceHarness.RubyIsAvailable(out string why), why);

            Convert();

            var result = ConformanceHarness.CompileRuby(Scenario);

            Assert.True(result.Succeeded, $"Generated Ruby does not parse.{Environment.NewLine}{result.Output}");
        }

        /// <summary>
        /// The one that found a defect. A field named `Int` became `int`, which is not a
        /// Dart keyword but is the name of a type - and a field of that name shadows the
        /// type inside its own class, so the declaration after it does not compile.
        /// </summary>
        [Fact]
        public void Generated_dart_compiles_with_keyword_named_fields()
        {
            Assert.True(ConformanceHarness.DartIsAvailable(out string why), why);

            Convert();

            var result = ConformanceHarness.CompileDart(Scenario);

            Assert.True(result.Succeeded, $"Generated Dart does not compile.{Environment.NewLine}{result.Output}");
        }
    }
}
