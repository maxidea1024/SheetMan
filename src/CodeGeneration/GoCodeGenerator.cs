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
    /// Settings for the Go target.
    ///
    /// Declared here rather than in <see cref="RecipeModel"/>, and reached through the
    /// recipe's `Targets` list rather than a section of its own. That is what the dynamic
    /// target list is for: a language added after it costs its own files and touches
    /// nothing existing.
    /// </summary>
    public sealed class GoRecipe : IOutputRecipe
    {
        /// <summary>Output directory. Created if it does not exist.</summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Go package the generated file declares.
        /// </summary>
        public string PackageName { get; set; } = "gamedata";

        /// <summary>
        /// Module path the generated go.mod declares, and the prefix the generated file
        /// imports its reader by.
        ///
        /// Go has no relative imports outside GOPATH mode, so the output needs a module of
        /// its own to be buildable at all. Point this at wherever the directory ends up if
        /// the generated code is vendored into a larger module.
        /// </summary>
        public string ModulePath { get; set; } = "gamedata";

        /// <summary>
        /// Whether to write a go.mod beside the generated file.
        ///
        /// On by default, so the output builds as it stands. Turn it off when vendoring the
        /// directory into a module that already has one.
        /// </summary>
        public bool WriteGoMod { get; set; } = true;

        /// <summary>Go version the generated go.mod requires.</summary>
        public string GoVersion { get; set; } = "1.21";

        /// <summary>Base name of the generated file, without its extension.</summary>
        public string AccessorName { get; set; } = "sheetman_data";

        /// <summary>
        /// Extension of the table files the generated reader opens. Must match what the
        /// binary exporter was told to write.
        /// </summary>
        public string BinaryTableFileExtension { get; set; } = ".table";

        /// <summary>
        /// Which side this output is built for: "c", "s", or "cs"/blank for both.
        /// </summary>
        public string TargetSide { get; set; } = "cs";
    }

    /// <summary>
    /// Emits a single self-contained Go file per recipe entry, plus the binary reader.
    ///
    /// One file rather than one per entity, as for C# and C++: Go resolves identifiers
    /// across a package regardless of which file they are in, so splitting would buy
    /// nothing and cost the reader a search.
    ///
    /// The shape lives in templates/go.sbn.
    /// </summary>
    [SheetManTarget("go", TargetKind.CodeGeneration, Order = 50)]
    public class GoCodeGenerator : Target<GoRecipe>
    {
        private Model _model;
        private GoRecipe _recipe;

        protected override void Run(TargetContext context, GoRecipe recipe)
        {
            if (string.IsNullOrEmpty(recipe.Path))
                return;

            _recipe = recipe;

            // Already narrowed to the side this entry is built for.
            _model = context.Model;

            Generate();
            WriteBinaryReaderRuntime();

            if (_recipe.WriteGoMod)
                WriteGoMod();
        }

        private void Generate()
        {
            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(_recipe.Path, _recipe.AccessorName + ".go"));

            Log.Information($"Generating codes for Go into `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render("go.sbn", BuildView()));
        }

        /// <summary>
        /// Writes the LiteBinary reader into a `sheetman` package beside the generated file.
        ///
        /// Emitted rather than fetched, as for the other languages: the output directory is
        /// then self-contained and there is no way to pair generated code with a reader of a
        /// different vintage.
        /// </summary>
        private void WriteBinaryReaderRuntime()
        {
            const string resourceName = "SheetMan.Runtime.Go.lite_binary_reader.go";

            using var stream = typeof(GoCodeGenerator).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new SheetManException($"Embedded resource `{resourceName}` is missing from the build.");

            using var reader = new StreamReader(stream);

            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(_recipe.Path, "sheetman", "lite_binary_reader.go"));

            StagingFiles.WriteAllTextToFile(filename, reader.ReadToEnd());
        }

        /// <summary>
        /// Writes the go.mod that makes the output a module, which is what lets the
        /// generated file import its reader at all.
        /// </summary>
        private void WriteGoMod()
        {
            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(_recipe.Path, "go.mod"));

            var text = new StringBuilder();
            text.Append("module ").Append(_recipe.ModulePath).Append('\n');
            text.Append('\n');
            text.Append("go ").Append(_recipe.GoVersion).Append('\n');

            StagingFiles.WriteAllTextToFile(filename, text.ToString());
        }

        // --------------------------------------------------------------- view

        private GoFileView BuildView() => new GoFileView
        {
            PackageName = _recipe.PackageName,
            Imports = BuildImports(),
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = BuildAccessor(),
        };

        /// <summary>
        /// Exactly the imports the generated file uses.
        ///
        /// Go rejects an unused import outright, so this cannot be a fixed list - a model
        /// with no enums does not mention strconv, and one with no tables does not mention
        /// filepath or fmt.
        /// </summary>
        private IReadOnlyList<string> BuildImports()
        {
            var imports = new List<string>();

            if (_model.Tables.Count > 0)
                imports.Add("\"fmt\"");

            if (_model.Tables.Count > 0)
                imports.Add("\"path/filepath\"");

            if (_model.Enums.Count > 0)
                imports.Add("\"strconv\"");

            // Blank line between the standard library and everything else, as gofmt would.
            if (imports.Count > 0)
                imports.Add("");

            imports.Add($"\"{_recipe.ModulePath}/sheetman\"");

            return imports;
        }

        private GoEnumView BuildEnum(Models.Enum enumm) => new GoEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            Labels = enumm.Labels.Select(label => new GoEnumLabelView
            {
                Name = label.Name.ToPascalCase(),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };

        private GoConstantSetView BuildConstantSet(ConstantSet constantSet) => new GoConstantSetView
        {
            Name = constantSet.Name.ToPascalCase(),
            Location = constantSet.Location.ToString(),
            Comment = CommentLines(constantSet.Comment),
            Constants = constantSet.Constants.Select(constant => new GoConstantView
            {
                Name = constant.Name.ToPascalCase(),
                Type = ToGoTypeName(constant.Type, constant.Enum, null),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
            }).ToList(),
        };

        private GoTableView BuildTable(Table table) => new GoTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            IndexField = GoName(table.Fields[0].Name),
            Fields = table.SerialFields.Select(BuildField).ToList(),
        };

        private GoFieldView BuildField(SerialField sf)
        {
            string name = GoName(sf.Name);
            string elementType = ResolvedElementType(sf);

            return new GoFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                ElementCount = sf.Fields.Count,
                ArrayType = "[]" + elementType,
                Declarations = Declarations(sf, name, elementType),
                ReadScalar = ReadExpression(sf),
                ReadElement = ReadExpression(sf),
            };
        }

        /// <summary>
        /// The member declarations for a field. A reference gets two: the resolved value and
        /// the raw index it was read as, because the target is not known until every table
        /// is loaded.
        /// </summary>
        private IReadOnlyList<string> Declarations(SerialField sf, string name, string elementType)
        {
            if (sf.IsRef)
            {
                return sf.IsArray
                    ? new[] { $"{name} []{elementType}", $"{name}Index []int32" }
                    : new[] { $"{name} {elementType}", $"{name}Index int32" };
            }

            return sf.IsArray
                ? new[] { $"{name} []{elementType}" }
                : new[] { $"{name} {elementType}" };
        }

        private static string ReadKind(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return "var_array";

            if (sf.IsArray)
                return sf.IsRef ? "serial_ref" : "serial";

            return sf.IsRef ? "scalar_ref" : "scalar";
        }

        private GoAccessorView BuildAccessor() => new GoAccessorView
        {
            FileExtension = _recipe.BinaryTableFileExtension,

            Tables = _model.Tables.Select(table => new GoTableSlotView
            {
                Name = GoName(table.Name),
                TableName = table.Name.ToPascalCase() + "Table",

                // Unescaped: this one names the file the exporter wrote.
                DataFileName = table.Name,
            }).ToList(),

            CrossReferences = _model.Tables
                .Select(table => new
                {
                    Table = table,
                    Fields = table.SerialFields.Where(sf => sf.IsRef).ToList(),
                })
                .Where(x => x.Fields.Count > 0)
                .Select(x => new GoCrossReferenceView
                {
                    Table = GoName(x.Table.Name),
                    Fields = x.Fields.Select(sf => new GoReferenceFieldView
                    {
                        Name = GoName(sf.Name),
                        RefTable = GoName(sf.FirstField.ResolvedRefTable.Name),
                        Value = ReferenceValueExpression(sf),
                        IsArray = sf.IsArray,
                    }).ToList(),
                })
                .ToList(),
        };

        // ----------------------------------------------------------- rendering

        /// <summary>
        /// The call reading one value of a field's element type.
        /// </summary>
        private string ReadExpression(SerialField sf)
        {
            switch (sf.ElementType)
            {
                case ValueType.String: return "reader.ReadString()";
                case ValueType.Bool: return "reader.ReadBool()";
                case ValueType.Int32: return "reader.ReadInt32()";
                case ValueType.Int64: return "reader.ReadInt64()";
                case ValueType.Float: return "reader.ReadFloat32()";
                case ValueType.Double: return "reader.ReadFloat64()";
                case ValueType.DateTime: return "reader.ReadDateTimeTicks()";
                case ValueType.TimeSpan: return "reader.ReadDurationTicks()";
                case ValueType.Uuid: return "reader.ReadUUID()";

                // Enum values travel zig-zag encoded rather than fixed width.
                case ValueType.Enum: return $"{sf.FirstField.Enum.Name.ToPascalCase()}(reader.ReadEnum())";

                case ValueType.ForeignRecord: return "reader.ReadInt32()";

                default:
                    throw new SheetManException($"The go generator cannot read type `{sf.Type}`.");
            }
        }

        /// <summary>
        /// What a resolved reference yields: a pointer to the record, or one of its fields.
        /// </summary>
        private string ReferenceValueExpression(SerialField sf)
            => sf.ElementType == ValueType.ForeignRecord
                ? "target"
                : "target." + GoName(sf.FirstField.ResolvedRefField.Name);

        /// <summary>
        /// The type a field holds: a pointer to the referenced record, a copy of the
        /// referenced field's value, or the element type itself.
        /// </summary>
        private string ResolvedElementType(SerialField sf)
        {
            if (sf.ElementType == ValueType.ForeignRecord)
                return "*" + sf.FirstField.ResolvedRefTable.Name.ToPascalCase() + "Record";

            return ToGoTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull, null);
        }

        private string ToGoTypeName(ValueType type, Models.Enum enumm, string refTableName)
        {
            switch (ValueTypes.ElementOf(type))
            {
                case ValueType.Enum:
                    return enumm.Name.ToPascalCase();

                case ValueType.ForeignRecord:
                    return "*" + refTableName.ToPascalCase() + "Record";

                default:
                    return LanguageProfile.Go.ScalarTypeName(type);
            }
        }

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
                    return ((float)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                case ValueType.Double:
                    return ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                // Ticks, matching what the generated fields hold and for the same reason.
                case ValueType.DateTime:
                    return ((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture);

                case ValueType.TimeSpan:
                    return ((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture);

                case ValueType.Uuid:
                    return "sheetman.UUID{" + string.Join(", ",
                        ((Guid)constant.Value).ToByteArray()
                            .Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + "}";

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                    return constant.Enum.Name.ToPascalCase() + label.Name.ToPascalCase();
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the go generator cannot render.");
            }
        }

        /// <summary>
        /// A Go interpreted string literal.
        /// </summary>
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
                    literal.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    literal.Append(c);
            }

            return literal.Append('"').ToString();
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// An exported member name.
        ///
        /// Pascal case, which is how Go exports, and which is also why nothing is ever
        /// escaped: every Go keyword is lowercase.
        /// </summary>
        private static string GoName(string name) => LanguageProfile.Go.MemberName(name.ToPascalCase());

        private static IReadOnlyList<string> CommentLines(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return Array.Empty<string>();

            return comment.Replace("\r\n", "\n").Split('\n');
        }
    }
}
