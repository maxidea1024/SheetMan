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

        /// <summary>
        /// Whether to write the data updater beside the reader.
        ///
        /// It fetches the manifest and the changed data files over HTTP and keeps a local
        /// copy current, so a build can take new data without being redeployed. Off by
        /// default: a service that ships its data with its binary has no use for it.
        /// </summary>
        public bool WriteUpdater { get; set; } = false;

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
        /// Whether generated files this run did not write are removed from <see cref="Path"/>.
        /// </summary>
        /// <remarks>
        /// On, because the output is a file per table: delete a table from the sheets and its
        /// file stays behind naming types nothing declares any more. Only files carrying this
        /// tool's own header are removed, so a directory holding your own source is safe.
        ///
        /// Turn it off if you edit the generated files, which is a decision worth a line in a
        /// recipe.
        /// </remarks>
        public bool Sweep { get; set; } = true;

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
    public class GoCodeGenerator : CodeGenerator<GoRecipe>
    {
        private Model _model;
        private GoRecipe _recipe;

        protected override void Run(TargetContext context, GoRecipe recipe)
        {
            if (string.IsNullOrEmpty(recipe.Path))
                return;

            SweepStaleOutput(recipe.Path, recipe.Sweep);

            _recipe = recipe;

            // Already narrowed to the side this entry is built for.
            _model = context.Model;

            Generate();
            WriteBinaryReaderRuntime();

            if (_recipe.WriteGoMod)
                WriteGoMod();
        }

        /// <summary>
        /// Writes a file per table, per enum and per constant set, plus the accessor.
        /// </summary>
        /// <remarks>
        /// It used to be one file holding all of it, which made a deleted table a hunk of dead
        /// code inside a file that still compiled. The layout matches the other targets.
        ///
        /// Go's own difficulty is the imports: an unused one does not compile, so each file gets
        /// exactly what its own text reaches for. Its easiness is the other side of the same
        /// coin - one package, so nothing here imports another generated file and a table can
        /// name another table's record type freely.
        /// </remarks>
        private void Generate()
        {
            var view = BuildView();

            Log.Information($"Generating codes for Go into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

            // The accessor joins paths and nothing else; the errors it returns come from the
            // tables already wrapped.
            Write(_recipe.AccessorName + ".go", "go-accessor.sbn", new GoPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Imports(new[] { "path/filepath" }, reader: false),
                Accessor = view.Accessor,
            });

            foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
            {
                // A table reads through the reader and wraps its failures with fmt.Errorf.
                Write(pair.rendered.TableName.ToSnakeCase() + ".go", "go-table.sbn", new GoPartView
                {
                    PackageName = _recipe.PackageName,
                    Imports = Imports(new[] { "fmt" }, reader: true),
                    Table = pair.rendered,
                });
            }

            foreach (var enumm in view.Enums)
            {
                // An enum's String falls back to formatting the number.
                Write("enum_" + enumm.Name.ToSnakeCase() + ".go", "go-enum.sbn", new GoPartView
                {
                    PackageName = _recipe.PackageName,
                    Imports = Imports(new[] { "strconv" }, reader: false),
                    Enumm = enumm,
                });
            }

            foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
            {
                // A constant set names no standard library type, and reaches the reader only
                // for a uuid.
                Write("const_" + pair.rendered.Name.ToSnakeCase() + ".go", "go-constants.sbn",
                      new GoPartView
                      {
                          PackageName = _recipe.PackageName,
                          Imports = Imports(Array.Empty<string>(), reader: NamesUuid(pair.model)),
                          Set = pair.rendered,
                      });
            }
        }

        /// <summary>
        /// Flat rather than in `tables/`, `enums/` and `constants/` as the other targets do.
        ///
        /// A Go directory is a package, so a subdirectory would be a different one - and the
        /// generated types refer to each other without qualification. The names carry the
        /// grouping instead.
        /// </summary>
        private void Write(string filename, string templateName, object view)
        {
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, filename));

            StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
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
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Go.lite_binary_reader.go",
                System.IO.Path.Combine(_recipe.Path, "sheetman", "lite_binary_reader.go"));

            // Asked for rather than assumed. It reaches the network and it is of no use to a
            // service that ships its data with its binary.
            if (_recipe.WriteUpdater)
            {
                WriteBinaryReaderRuntime(
                    "SheetMan.Runtime.Go.updater.go",
                    System.IO.Path.Combine(_recipe.Path, "sheetman", "updater.go"));
            }
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

        /// <summary>
        /// The whole model, which <see cref="Generate"/> then splits into files.
        /// </summary>
        /// <remarks>
        /// No imports here any more: they are per file, because an unused one does not compile
        /// in Go, and a single list for the model would put an unused import in most of them.
        /// </remarks>
        private GoFileView BuildView() => new GoFileView
        {
            PackageName = _recipe.PackageName,
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
        /// <summary>
        /// The imports one generated file needs, and only those.
        /// </summary>
        /// <remarks>
        /// Per file, because an unused import does not compile in Go. Kotlin can hand every
        /// file the same list and suppress the warning; here each one gets exactly what its own
        /// text reaches for, worked out from what that file is rather than by scanning what was
        /// rendered.
        ///
        /// Nothing imports another generated file: they are all one package, so a table's file
        /// names another table's record type with no import at all.
        /// </remarks>
        /// <param name="standard">Standard library paths, without quotes.</param>
        /// <param name="reader">Whether the file names the emitted reader package.</param>
        private IReadOnlyList<string> Imports(IEnumerable<string> standard, bool reader)
        {
            var imports = standard.Select(path => $"\"{path}\"").ToList();

            if (!reader)
                return imports;

            // Blank line between the standard library and everything else, as gofmt would.
            if (imports.Count > 0)
                imports.Add("");

            imports.Add($"\"{_recipe.ModulePath}/sheetman\"");

            return imports;
        }

        /// <summary>Whether a constant set names the reader's UUID type.</summary>
        private static bool NamesUuid(ConstantSet set)
            => set.Constants.Any(constant => constant.Type == ValueType.Uuid);

        /// <summary>Whether a table's fields name the reader's UUID type.</summary>
        private static bool NamesUuid(Table table)
            => table.SerialFields.Any(sf => !sf.IsRef && sf.ElementType == ValueType.Uuid);

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
            Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),
        };

        private GoFieldView BuildField(Table table, SerialField sf)
        {
            string name = GoName(sf.Name);
            string elementType = ResolvedElementType(sf);

            return new GoFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                Tag = sf.FirstField.Tag.Value,
                ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
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

        /// <summary>
        /// The rendered CheckColumn call: kind, count, and the elements this member accepts -
        /// its own plus the lossless promotions, decided here at generation time.
        /// </summary>
        private static string ColumnCheck(SerialField sf, string tableName)
        {
            string kind = sf.IsVariableLengthArray
                ? "sheetman.KindVarArray"
                : (sf.Fields.Count > 1 ? "sheetman.KindFixedArray" : "sheetman.KindScalar");

            int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

            string accepted;

            if (sf.IsRef)
                accepted = "sheetman.ElementI32";
            else
            {
                switch (sf.ElementType)
                {
                    case ValueType.Int32:
                        accepted = "sheetman.ElementI32, sheetman.ElementVarint"; break;
                    case ValueType.Int64:
                        accepted = "sheetman.ElementI64, sheetman.ElementI32, sheetman.ElementVarint"; break;
                    case ValueType.Double:
                        accepted = "sheetman.ElementF64, sheetman.ElementF32, sheetman.ElementI32"; break;
                    case ValueType.Float: accepted = "sheetman.ElementF32"; break;
                    case ValueType.Bool: accepted = "sheetman.ElementBool"; break;
                    case ValueType.String: accepted = "sheetman.ElementString"; break;
                    case ValueType.Uuid: accepted = "sheetman.ElementUUID"; break;
                    case ValueType.Enum: accepted = "sheetman.ElementVarint"; break;

                    // Ticks are exact i64: reading an int as a datetime would be lossless
                    // and semantically wrong, so no promotion.
                    case ValueType.DateTime:
                    case ValueType.TimeSpan:
                        accepted = "sheetman.ElementI64"; break;

                    default:
                        throw new SheetManException($"The go generator cannot check type `{sf.Type}`.");
                }
            }

            return $"sheetman.CheckColumn(reader, column, \"{tableName}.{sf.Name.ToPascalCase()}\", {kind}, {count}, {accepted})";
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

                // Enum values travel zig-zag encoded rather than fixed width.
                case ValueType.Enum: return $"{sf.FirstField.Enum.Name.ToPascalCase()}(reader.ReadEnum())";

                case ValueType.ForeignRecord: return "reader.ReadInt32()";

                // Everything else is a plain call named in the profile, which is where the
                // nine of them live now rather than here and in nine other generators.
                default: return LanguageProfile.Go.ReadCall(sf.ElementType);
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

    }
}
