using System;
using System.Collections.Generic;
using SheetMan.Models;
using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// How one output language spells the things SheetMan generates.
    ///
    /// The three generators each carried the same switch over <see cref="ValueType"/> -
    /// twenty-seven arms of pure table data, in three places, in three shapes. Adding a
    /// language meant writing a fourth. Here it is a table, which is also what a template
    /// can read once the generators move to templates.
    ///
    /// Only what is genuinely declarative lives here. Enum and foreign-record types are
    /// not in the table because both name something from the model and each language
    /// qualifies them its own way, so each generator keeps those two arms. What a
    /// generator does with a type - the file layout, the reader calls, the comment syntax -
    /// stays in the generator.
    /// </summary>
    public sealed class LanguageProfile
    {
        private readonly HashSet<string> _reservedMemberNames;

        public LanguageProfile(
            string id,
            IReadOnlyDictionary<ValueType, string> scalarTypes,
            string arrayFormat,
            string memberNameEscape,
            params string[] reservedMemberNames)
        {
            Id = id;
            ScalarTypes = scalarTypes;
            ArrayFormat = arrayFormat;
            MemberNameEscape = memberNameEscape;

            // Ordinal: every language here is case-sensitive about its keywords.
            _reservedMemberNames = new HashSet<string>(reservedMemberNames, StringComparer.Ordinal);
        }

        /// <summary>Matches the target id, so an error message names what the recipe asked for.</summary>
        public string Id { get; }

        /// <summary>
        /// The name of each scalar type in this language.
        ///
        /// Enum and ForeignRecord are deliberately absent; see the type remarks.
        /// </summary>
        public IReadOnlyDictionary<ValueType, string> ScalarTypes { get; }

        /// <summary>
        /// How an array of an already-rendered element type is written, with `{0}` standing
        /// for the element - `{0}[]`, `std::vector&lt;{0}&gt;`.
        /// </summary>
        public string ArrayFormat { get; }

        /// <summary>
        /// The name of a scalar type, or an error naming the language and the type.
        ///
        /// Takes an array type as readily as a scalar one and answers for its element,
        /// because every caller renders an array by naming the element and wrapping it.
        /// </summary>
        public string ScalarTypeName(ValueType type)
        {
            var element = ValueTypes.ElementOf(type);

            if (ScalarTypes.TryGetValue(element, out string name))
                return name;

            throw new SheetManException($"The {Id} generator cannot render type `{type}`.");
        }

        /// <summary>Wraps an already-rendered element type as an array.</summary>
        public string ArrayOf(string elementTypeName) => string.Format(ArrayFormat, elementTypeName);

        /// <summary>
        /// How a reserved name is made usable, with `{0}` standing for the name.
        /// </summary>
        public string MemberNameEscape { get; }

        /// <summary>
        /// Names this language will not accept for a member, after the generator's casing
        /// has been applied.
        ///
        /// After the casing, which is why the three lists differ so much in size: C# renders
        /// members PascalCase and every C# keyword is lowercase, so none of them can survive
        /// into one. TypeScript renders them camelCase but accepts a reserved word as a
        /// member name, so only the handful that are genuinely special appear. C++ renders
        /// them snake_case and accepts nothing, so it has the full keyword list.
        ///
        /// The repository used to hold two lists that nothing read - a C# one whose note
        /// claimed escaping made the problem moot, and a TypeScript one - and no list at all
        /// for C++, the one language that needed it. The reserved-words fixture is what
        /// decides these contents now: the suite compiles its output in all three languages.
        /// </summary>
        public IReadOnlyCollection<string> ReservedMemberNames => _reservedMemberNames;

        /// <summary>
        /// A member name this language will accept, given the cased name.
        ///
        /// Leaves anything usable exactly as it was, so only the colliding names change.
        /// </summary>
        public string MemberName(string casedName)
        {
            return _reservedMemberNames.Contains(casedName)
                ? string.Format(MemberNameEscape, casedName)
                : casedName;
        }

        // ------------------------------------------------------------ profiles

        /// <summary>
        /// C++17. Fixed-width integer names from &lt;cstdint&gt; rather than `int` and
        /// `long long`, whose widths are not fixed by the language; the date, duration and
        /// uuid types come from the emitted reader header.
        /// </summary>
        public static readonly LanguageProfile Cpp = new LanguageProfile(
            "cpp",
            new Dictionary<ValueType, string>
            {
                { ValueType.String, "std::string" },
                { ValueType.Bool, "bool" },
                { ValueType.Int32, "std::int32_t" },
                { ValueType.Int64, "std::int64_t" },
                { ValueType.Float, "float" },
                { ValueType.Double, "double" },
                { ValueType.DateTime, "sheetman::DateTime" },
                { ValueType.TimeSpan, "sheetman::TimeSpan" },
                { ValueType.Uuid, "sheetman::Uuid" },
            },
            "std::vector<{0}>",

            // A prefix, not the idiomatic trailing underscore, because the accessor already
            // uses a trailing underscore for its private members: a table called Template
            // would give the method `template_` and the field `template__`, and any
            // identifier containing a double underscore is reserved to the implementation.
            // Escaping the method but not the field instead makes the two collide.
            "sm_{0}",

            // https://en.cppreference.com/w/cpp/keyword - the whole list, because a C++
            // member name is snake_case and every keyword is lowercase, so all of them
            // survive the casing.
            "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel", "atomic_commit",
            "atomic_noexcept", "auto", "bitand", "bitor", "bool", "break", "case", "catch",
            "char", "char8_t", "char16_t", "char32_t", "class", "co_await", "co_return",
            "co_yield", "compl", "concept", "const", "const_cast", "consteval", "constexpr",
            "constinit", "continue", "decltype", "default", "delete", "do", "double",
            "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float",
            "for", "friend", "goto", "if", "inline", "int", "long", "mutable", "namespace",
            "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq",
            "private", "protected", "public", "reflexpr", "register", "reinterpret_cast",
            "requires", "return", "short", "signed", "sizeof", "static", "static_assert",
            "static_cast", "struct", "switch", "synchronized", "template", "this",
            "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union",
            "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor",
            "xor_eq");

        /// <summary>
        /// C#. The three framework types are fully qualified so a generated file needs no
        /// `using System` and cannot collide with a namespace the consumer already has.
        /// </summary>
        public static readonly LanguageProfile CSharp = new LanguageProfile(
            "csharp",
            new Dictionary<ValueType, string>
            {
                { ValueType.String, "string" },
                { ValueType.Bool, "bool" },
                { ValueType.Int32, "int" },
                { ValueType.Int64, "long" },
                { ValueType.Float, "float" },
                { ValueType.Double, "double" },
                { ValueType.DateTime, "System.DateTime" },
                { ValueType.TimeSpan, "System.TimeSpan" },
                { ValueType.Uuid, "System.Guid" },
            },
            "{0}[]",

            // Never used: the list below is empty, so nothing is ever escaped. Kept as the
            // place the answer would go if that changed.
            "@{0}"

            // No reserved names. C# renders members PascalCase and every C# keyword is
            // lowercase, so a field called `class` becomes `Class` and cannot collide. The
            // reserved-words fixture compiles a table whose fields are named after keywords
            // in all three languages, which is what turns that from an argument into a fact.
            );

        /// <summary>
        /// Go.
        ///
        /// The least eventful of the profiles: int64 is int64, float32 is float32, and a
        /// uint32 shifts the way varint decoding wants. Nothing has to be worked around.
        ///
        /// datetime and timespan are int64 ticks rather than time.Time and time.Duration,
        /// and that is not a matter of taste. Both of those count nanoseconds in an int64,
        /// which spans about 1678 to 2262 for an instant and about 292 years for a duration.
        /// The corpus holds 0001-01-01 and TimeSpan.MaxValue, and both overflow. Ticks are
        /// exact for everything a sheet can hold; the reader offers Time and Duration for a
        /// caller who knows their range.
        /// </summary>
        public static readonly LanguageProfile Go = new LanguageProfile(
            "go",
            new Dictionary<ValueType, string>
            {
                { ValueType.String, "string" },
                { ValueType.Bool, "bool" },
                { ValueType.Int32, "int32" },
                { ValueType.Int64, "int64" },
                { ValueType.Float, "float32" },
                { ValueType.Double, "float64" },
                { ValueType.DateTime, "int64" },
                { ValueType.TimeSpan, "int64" },
                { ValueType.Uuid, "sheetman.UUID" },
            },
            "[]{0}",

            // Never used, as with C#: Go exports a name by capitalizing it and every Go
            // keyword is lowercase, so none can survive into an exported member.
            "{0}_"
            );

        /// <summary>
        /// Rust.
        ///
        /// As uneventful as Go on the numbers: i64 is i64, f32 is f32, and the shifts
        /// behave. datetime and timespan are i64 ticks for a different reason than Go's -
        /// std has no date type at all, and the values a sheet can hold reach 0001-01-01
        /// and 9999-12-31, which most crates' types cannot express either.
        ///
        /// Unlike Go and C#, Rust does need escaping: members are snake_case and every Rust
        /// keyword is lowercase, so a field called `Type` becomes `type` and stops the
        /// compiler.
        /// </summary>
        public static readonly LanguageProfile Rust = new LanguageProfile(
            "rust",
            new Dictionary<ValueType, string>
            {
                { ValueType.String, "String" },
                { ValueType.Bool, "bool" },
                { ValueType.Int32, "i32" },
                { ValueType.Int64, "i64" },
                { ValueType.Float, "f32" },
                { ValueType.Double, "f64" },
                { ValueType.DateTime, "i64" },
                { ValueType.TimeSpan, "i64" },
                { ValueType.Uuid, "sheetman::Uuid" },
            },
            "Vec<{0}>",

            // A trailing underscore rather than a raw identifier. `r#type` is the idiomatic
            // escape but does not work for all of them - `crate`, `self`, `super` and `Self`
            // cannot be raw - and one rule that always holds beats two that nearly do.
            "{0}_",

            // https://doc.rust-lang.org/reference/keywords.html - strict, reserved and the
            // weak ones, because a member name is snake_case and every keyword is lowercase.
            "as", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern",
            "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move",
            "mut", "pub", "ref", "return", "self", "static", "struct", "super", "trait",
            "true", "type", "unsafe", "use", "where", "while", "async", "await", "abstract",
            "become", "box", "do", "final", "macro", "override", "priv", "try", "typeof",
            "unsized", "virtual", "yield", "gen", "union");

        /// <summary>
        /// Python.
        ///
        /// The scalar names are only used for documentation - Python is not annotated here -
        /// but the entries record what a value becomes, and two are worth stating.
        ///
        /// float is `float`, which is a double: Python has no single-precision type, so a
        /// float32 read widens. The value is exactly the one stored, held in a wider type,
        /// and printing it shows digits the original 32 bits never carried - which is why
        /// the conformance comparison narrows before comparing.
        ///
        /// datetime and timespan are ticks. `datetime` cannot hold a tick, only a
        /// microsecond, and `timedelta` tops out near 2,700,000 days where TimeSpan reaches
        /// about 29,000 years.
        /// </summary>
        public static readonly LanguageProfile Python = new LanguageProfile(
            "python",
            new Dictionary<ValueType, string>
            {
                { ValueType.String, "str" },
                { ValueType.Bool, "bool" },
                { ValueType.Int32, "int" },
                { ValueType.Int64, "int" },
                { ValueType.Float, "float" },
                { ValueType.Double, "float" },
                { ValueType.DateTime, "int" },
                { ValueType.TimeSpan, "int" },
                { ValueType.Uuid, "sheetman.Uuid" },
            },
            "list[{0}]",

            // A trailing underscore, which is what PEP 8 prescribes for exactly this.
            "{0}_",

            // https://docs.python.org/3/reference/lexical_analysis.html#keywords, plus the
            // soft keywords. Members are snake_case and nearly every keyword is lowercase.
            "False", "None", "True", "and", "as", "assert", "async", "await", "break",
            "class", "continue", "def", "del", "elif", "else", "except", "finally", "for",
            "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
            "pass", "raise", "return", "try", "while", "with", "yield", "match", "case",
            "type");

        /// <summary>
        /// Java.
        ///
        /// The first language with no unsigned types, which is where the format's varint
        /// decoding goes wrong if nobody is watching: a byte with its high bit set is
        /// negative and must be masked before it is shifted, and undoing the zig-zag fold
        /// needs the unsigned shift rather than the arithmetic one. Both live in the
        /// reader; nothing about the type table shows it.
        ///
        /// datetime and timespan are ticks, as everywhere but C# and C++. Instant and
        /// Duration could hold these values, but the conversion is lossy coming back and a
        /// caller passing the value through should not pay for it.
        /// </summary>
        public static readonly LanguageProfile Java = new LanguageProfile(
            "java",
            new Dictionary<ValueType, string>
            {
                { ValueType.String, "String" },
                { ValueType.Bool, "boolean" },
                { ValueType.Int32, "int" },
                { ValueType.Int64, "long" },
                { ValueType.Float, "float" },
                { ValueType.Double, "double" },
                { ValueType.DateTime, "long" },
                { ValueType.TimeSpan, "long" },
                { ValueType.Uuid, "LiteBinaryReader.Uuid" },
            },
            "{0}[]",

            // A trailing underscore. Java has no escape for an identifier that lands on a
            // keyword, so the name has to change.
            "{0}_",

            // https://docs.oracle.com/javase/specs/jls/se21/html/jls-3.html#jls-3.9 - the
            // keywords and the three literals, all reserved as identifiers.
            "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char",
            "class", "const", "continue", "default", "do", "double", "else", "enum",
            "extends", "final", "finally", "float", "for", "goto", "if", "implements",
            "import", "instanceof", "int", "interface", "long", "native", "new", "package",
            "private", "protected", "public", "return", "short", "static", "strictfp",
            "super", "switch", "synchronized", "this", "throw", "throws", "transient",
            "try", "void", "volatile", "while", "true", "false", "null");

        /// <summary>
        /// TypeScript.
        ///
        /// Two entries here are not the obvious ones, and both are about values arriving
        /// wrong rather than failing to arrive:
        ///
        /// int64 is `bigint`, not `number`. A double carries 53 bits of mantissa, so a
        /// 64-bit value past 2^53 comes back quietly changed - the same class of corruption
        /// the binary writer itself once had, and just as invisible.
        ///
        /// datetime, timespan and uuid are `string`. TypeScript reads the JSON export and
        /// JSON has none of the three, so each arrives as text. Declaring `Date` would
        /// oblige the generated reader to parse on load - work a consumer may not want, on
        /// a value it may only pass through - and there is nothing to parse a duration or a
        /// uuid into at all. The text is exactly what was exported.
        /// </summary>
        public static readonly LanguageProfile Typescript = new LanguageProfile(
            "typescript",
            new Dictionary<ValueType, string>
            {
                { ValueType.String, "string" },
                { ValueType.Bool, "boolean" },
                { ValueType.Int32, "number" },
                { ValueType.Int64, "bigint" },
                { ValueType.Float, "number" },
                { ValueType.Double, "number" },
                { ValueType.DateTime, "string" },
                { ValueType.TimeSpan, "string" },
                { ValueType.Uuid, "string" },
            },
            "{0}[]",

            // A trailing underscore is safe here: TypeScript's private members carry a
            // leading one, so the two conventions cannot combine into anything illegal.
            "{0}_",

            // Not the reserved words. TypeScript accepts `class`, `function`, `delete` and
            // the rest as member names, and escaping them would rename the generated API
            // for no reason. These three are the ones a class genuinely cannot declare:
            // `constructor` because an accessor may not be called that - which is exactly
            // what the compiler said about the reserved-words fixture, TS1341 - and the
            // other two because they are how an object's own machinery is reached.
            "constructor", "prototype", "__proto__");
    }
}
