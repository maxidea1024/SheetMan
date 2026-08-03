using SheetMan.Models;
using SheetMan.Extensions;
using SheetMan.Helpers;
using SheetMan.Recipe;
using SheetMan.Targets;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Settings for the Rust target.
    ///
    /// Declared beside its generator and reached through the recipe's `Targets` list, as
    /// the Go one is.
    /// </summary>
    public sealed class RustRecipe : IOutputRecipe
    {
        /// <summary>Output directory. Created if it does not exist.</summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Crate name the generated Cargo.toml declares. Also how a consumer refers to the
        /// generated types.
        /// </summary>
        public string CrateName { get; set; } = "gamedata";

        /// <summary>
        /// Whether to write a Cargo.toml beside the generated source.
        ///
        /// On by default, so the output builds as it stands. Turn it off when vendoring the
        /// module into a crate that already has one.
        /// </summary>
        public bool WriteCargoToml { get; set; } = true;

        /// <summary>Rust edition the generated Cargo.toml declares.</summary>
        public string Edition { get; set; } = "2021";

        /// <summary>
        /// Extension of the table files the generated reader opens. Must match what the
        /// binary exporter was told to write.
        /// </summary>
        public string BinaryTableFileExtension { get; set; } = ".table";

        /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
        public string TargetSide { get; set; } = "cs";
    }

    /// <summary>
    /// Emits a Rust crate: one module holding every generated type, plus the binary reader.
    ///
    /// References are kept as indices rather than resolved into borrows. A record holding a
    /// reference to another record is a graph, and Rust will not let one own its
    /// neighbours; the alternatives are lifetimes threaded through every generated type or
    /// a reference-counted cell around every row. The index plus a lookup reads better and
    /// costs the caller one call, which is the same trade the database exporters make.
    ///
    /// The shape lives in templates/rust.sbn.
    /// </summary>
    [SheetManTarget("rust", TargetKind.CodeGeneration, Order = 60)]
    public class RustCodeGenerator : Target<RustRecipe>
    {
        private Model _model;
        private RustRecipe _recipe;

        protected override void Run(TargetContext context, RustRecipe recipe)
        {
            if (string.IsNullOrEmpty(recipe.Path))
                return;

            _recipe = recipe;
            _model = context.Model;

            Generate();
            WriteBinaryReaderRuntime();

            if (_recipe.WriteCargoToml)
                WriteCargoToml();
        }

        private void Generate()
        {
            // src/lib.rs, because the generated module declares `pub mod sheetman` and the
            // reader has to sit beside it for that to resolve.
            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(_recipe.Path, "src", "lib.rs"));

            Log.Information($"Generating codes for Rust into `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render("rust.sbn", BuildView()));
        }

        private void WriteBinaryReaderRuntime()
        {
            const string resourceName = "SheetMan.Runtime.Rust.lite_binary_reader.rs";

            using var stream = typeof(RustCodeGenerator).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new SheetManException($"Embedded resource `{resourceName}` is missing from the build.");

            using var reader = new StreamReader(stream);

            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(_recipe.Path, "src", "sheetman.rs"));

            StagingFiles.WriteAllTextToFile(filename, reader.ReadToEnd());
        }

        private void WriteCargoToml()
        {
            var text = new StringBuilder();
            text.Append("[package]\n");
            text.Append("name = \"").Append(_recipe.CrateName).Append("\"\n");
            text.Append("version = \"0.0.0\"\n");
            text.Append("edition = \"").Append(_recipe.Edition).Append("\"\n");
            text.Append('\n');
            text.Append("# No dependencies on purpose: the reader is core and std only, so the\n");
            text.Append("# generated crate builds without registry access.\n");
            text.Append("[dependencies]\n");

            StagingFiles.WriteAllTextToFile(
                System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, "Cargo.toml")),
                text.ToString());
        }

        // --------------------------------------------------------------- view

        private RustFileView BuildView() => new RustFileView
        {
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = new RustAccessorView
            {
                FileExtension = _recipe.BinaryTableFileExtension,
                Tables = _model.Tables.Select(table => new RustTableSlotView
                {
                    Name = RustName(table.Name),
                    TableName = table.Name.ToPascalCase() + "Table",

                    // Unescaped: this one names the file the exporter wrote.
                    DataFileName = table.Name,
                }).ToList(),
            },
        };

        private RustEnumView BuildEnum(Models.Enum enumm)
        {
            // Deriving Default needs exactly one variant marked, so the zero label gets it
            // when there is one and the first otherwise.
            int defaultIndex = enumm.Labels.FindIndex(label => label.Value == 0);
            if (defaultIndex < 0)
                defaultIndex = 0;

            return new RustEnumView
            {
                Name = enumm.Name.ToPascalCase(),
                Location = enumm.Location.ToString(),
                Comment = CommentLines(enumm.Comment),
                Labels = enumm.Labels.Select((label, index) => new RustEnumLabelView
                {
                    Name = label.Name.ToPascalCase(),
                    Value = label.Value.ToString(CultureInfo.InvariantCulture),
                    Comment = CommentLines(label.Comment),
                    IsDefault = index == defaultIndex,
                }).ToList(),
            };
        }

        private RustConstantSetView BuildConstantSet(ConstantSet constantSet) => new RustConstantSetView
        {
            ModuleName = constantSet.Name.ToSnakeCase(),
            Location = constantSet.Location.ToString(),
            Comment = CommentLines(constantSet.Comment),
            Constants = constantSet.Constants.Select(constant => new RustConstantView
            {
                // Rust constants are SCREAMING_SNAKE_CASE, and the compiler warns otherwise.
                Name = constant.Name.ToSnakeCase().ToUpperInvariant(),
                Type = ConstantTypeName(constant),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
            }).ToList(),
        };

        private RustTableView BuildTable(Table table) => new RustTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            IndexField = RustName(table.Fields[0].Name),
            Fields = table.SerialFields.Select(BuildField).ToList(),
        };

        private RustFieldView BuildField(SerialField sf)
        {
            string name = RustName(sf.Name);
            string elementType = ToRustTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull);

            return new RustFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                ElementCount = sf.Fields.Count,
                Declarations = Declarations(sf, name, elementType),
                ReadScalar = ReadExpression(sf),
                ReadElement = ReadExpression(sf),
            };
        }

        private IReadOnlyList<string> Declarations(SerialField sf, string name, string elementType)
        {
            if (sf.IsRef)
            {
                // Only the index. See the type remarks for why it is not resolved.
                return sf.IsArray
                    ? new[] { $"{name}_index: Vec<i32>," }
                    : new[] { $"{name}_index: i32," };
            }

            return sf.IsArray
                ? new[] { $"{name}: Vec<{elementType}>," }
                : new[] { $"{name}: {elementType}," };
        }

        private static string ReadKind(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return "var_array";

            if (sf.IsArray)
                return sf.IsRef ? "serial_ref" : "serial";

            return sf.IsRef ? "scalar_ref" : "scalar";
        }

        // ----------------------------------------------------------- rendering

        private string ReadExpression(SerialField sf)
        {
            switch (sf.ElementType)
            {
                case ValueType.String: return "reader.read_string()?";
                case ValueType.Bool: return "reader.read_bool()?";
                case ValueType.Int32: return "reader.read_i32()?";
                case ValueType.Int64: return "reader.read_i64()?";
                case ValueType.Float: return "reader.read_f32()?";
                case ValueType.Double: return "reader.read_f64()?";
                case ValueType.DateTime: return "reader.read_datetime_ticks()?";
                case ValueType.TimeSpan: return "reader.read_duration_ticks()?";
                case ValueType.Uuid: return "reader.read_uuid()?";

                // Enum values travel zig-zag encoded. A value the sheet never declared
                // falls back to the default rather than failing the whole read, matching
                // what the other generated readers do with an unknown label.
                case ValueType.Enum:
                    return $"{sf.FirstField.Enum.Name.ToPascalCase()}::from_value(reader.read_enum()?)" +
                           ".unwrap_or_default()";

                case ValueType.ForeignRecord: return "reader.read_i32()?";

                default:
                    throw new SheetManException($"The rust generator cannot read type `{sf.Type}`.");
            }
        }

        private string ToRustTypeName(ValueType type, Models.Enum enumm)
        {
            switch (ValueTypes.ElementOf(type))
            {
                case ValueType.Enum:
                    return enumm.Name.ToPascalCase();

                // A reference is carried as the target row's index.
                case ValueType.ForeignRecord:
                    return "i32";

                default:
                    return LanguageProfile.Rust.ScalarTypeName(type);
            }
        }

        /// <summary>
        /// The type of a constant, which is not always the type of a field.
        ///
        /// A `String` cannot be a constant - it allocates - so a string constant is a
        /// static string slice instead.
        /// </summary>
        private string ConstantTypeName(ConstantSet.Constant constant)
            => constant.Type == ValueType.String
                ? "&'static str"
                : ToRustTypeName(constant.Type, constant.Enum);

        private string RenderConstantValue(ConstantSet.Constant constant)
        {
            switch (constant.Type)
            {
                case ValueType.String:
                    return Quote((string)constant.Value);

                case ValueType.Bool:
                    return (bool)constant.Value ? "true" : "false";

                case ValueType.Int32:
                    return ((int)constant.Value).ToString(CultureInfo.InvariantCulture);

                case ValueType.Int64:
                    return ((long)constant.Value).ToString(CultureInfo.InvariantCulture);

                case ValueType.Float:
                    return Suffixed(((float)constant.Value).ToString("R", CultureInfo.InvariantCulture));

                case ValueType.Double:
                    return Suffixed(((double)constant.Value).ToString("R", CultureInfo.InvariantCulture));

                // Ticks, matching what the generated fields hold and for the same reason.
                case ValueType.DateTime:
                    return ((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture);

                case ValueType.TimeSpan:
                    return ((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture);

                case ValueType.Uuid:
                    return "sheetman::Uuid([" + string.Join(", ",
                        ((Guid)constant.Value).ToByteArray()
                            .Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + "])";

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                    return $"{constant.Enum.Name.ToPascalCase()}::{label.Name.ToPascalCase()}";
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the rust generator cannot render.");
            }
        }

        /// <summary>
        /// Gives a rendered float a decimal point when it has none.
        ///
        /// `3` is an integer literal in Rust and will not initialize an f32; `3.0` will.
        /// A value in exponent form already parses as a float.
        /// </summary>
        private static string Suffixed(string rendered)
            => rendered.Contains('.') || rendered.Contains('E') || rendered.Contains('e')
                ? rendered
                : rendered + ".0";

        private static string Quote(string value)
        {
            var literal = new StringBuilder("\"");

            foreach (var c in value ?? "")
            {
                if (c == '"')
                    literal.Append("\\\"");
                else if (c == '\\')
                    literal.Append(@"\\");
                else if (c == '\n')
                    literal.Append(@"\n");
                else if (c == '\r')
                    literal.Append(@"\r");
                else if (c == '\t')
                    literal.Append(@"\t");
                else if (c < 0x20)
                    literal.Append(@"\u{").Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append('}');
                else
                    literal.Append(c);
            }

            return literal.Append('"').ToString();
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// A struct member name.
        ///
        /// snake_case, and escaped when it lands on a keyword - which it can, unlike Go and
        /// C#, because Rust members are lowercase and so is every Rust keyword.
        /// </summary>
        private static string RustName(string name) => LanguageProfile.Rust.MemberName(name.ToSnakeCase());

        private static IReadOnlyList<string> CommentLines(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return Array.Empty<string>();

            return comment.Replace("\r\n", "\n").Split('\n');
        }
    }
}
