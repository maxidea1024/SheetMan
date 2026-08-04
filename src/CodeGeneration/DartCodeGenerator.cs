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
    /// Settings for the Dart target.
    ///
    /// Declared beside its generator and reached through the recipe's `Targets` list.
    /// </summary>
    public sealed class DartRecipe : IOutputRecipe
    {
        /// <summary>Output directory. Created if it does not exist.</summary>
        public string Path { get; set; } = "";

        /// <summary>Base name of the generated library, without its extension.</summary>
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

        /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
        public string TargetSide { get; set; } = "cs";
    }

    /// <summary>
    /// Emits one Dart library holding every generated type, plus the binary reader.
    ///
    /// int64 and both tick counts are BigInt rather than int. Dart's int is 64 bits on the
    /// VM and a double on the web, where it carries 53 - and a value past that does not
    /// fail there, it comes back changed. The TypeScript target reached the same
    /// conclusion for the same reason, which is the argument for the corpus: the trap is a
    /// property of the format meeting the language, and it is invisible without a value
    /// that exercises it.
    ///
    /// The shape lives in templates/dart.sbn.
    /// </summary>
    [SheetManTarget("dart", TargetKind.CodeGeneration, Order = 95)]
    public class DartCodeGenerator : CodeGenerator<DartRecipe>
    {
        private Model _model;
        private DartRecipe _recipe;

        protected override void Run(TargetContext context, DartRecipe recipe)
        {
            if (string.IsNullOrEmpty(recipe.Path))
                return;

            SweepStaleOutput(recipe.Path, recipe.Sweep);

            _recipe = recipe;
            _model = context.Model;

            Generate();
            WriteBinaryReaderRuntime();
        }

        private void Generate()
        {
            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(_recipe.Path, _recipe.AccessorName + ".dart"));

            Log.Information($"Generating codes for Dart into `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render("dart.sbn", BuildView()));
        }

        private void WriteBinaryReaderRuntime()
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Dart.lite_binary_reader.dart",
                System.IO.Path.Combine(_recipe.Path, "sheetman", "lite_binary_reader.dart"));
        }

        // --------------------------------------------------------------- view

        private DartFileView BuildView() => new DartFileView
        {
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = BuildAccessor(),
        };

        private DartEnumView BuildEnum(Models.Enum enumm)
        {
            var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

            return new DartEnumView
            {
                Name = enumm.Name.ToPascalCase(),
                Location = enumm.Location.ToString(),
                Comment = CommentLines(enumm.Comment),
                DefaultLabel = DartName(fallback.Name),
                Labels = enumm.Labels.Select((label, index) => new DartEnumLabelView
                {
                    Name = DartName(label.Name),
                    Value = label.Value.ToString(CultureInfo.InvariantCulture),
                    Comment = CommentLines(label.Comment),

                    // A Dart enum body needs a semicolon after the last constant when
                    // anything follows it, and the constructor always does.
                    Separator = index == enumm.Labels.Count - 1 ? ";" : ",",
                }).ToList(),
            };
        }

        private DartConstantSetView BuildConstantSet(ConstantSet constantSet) => new DartConstantSetView
        {
            Name = constantSet.Name.ToPascalCase(),
            Location = constantSet.Location.ToString(),
            Comment = CommentLines(constantSet.Comment),
            Constants = constantSet.Constants.Select(constant => new DartConstantView
            {
                Name = DartName(constant.Name),
                Type = ToDartTypeName(constant.Type, constant.Enum, null),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
            }).ToList(),
        };

        private DartTableView BuildTable(Table table) => new DartTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            IndexField = DartName(table.Fields[0].Name),
            Fields = table.SerialFields.Select(BuildField).ToList(),
        };

        private DartFieldView BuildField(SerialField sf)
        {
            string name = DartName(sf.Name);

            return new DartFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                ElementCount = sf.Fields.Count,
                Declarations = Declarations(sf, name),
                ReadScalar = ReadExpression(sf),
                ReadElement = ReadExpression(sf),
            };
        }

        /// <summary>
        /// The field declarations, each initialized.
        ///
        /// Initialized rather than `late`, because Dart's null safety would otherwise turn
        /// a read of an unread record into a runtime failure where every other generated
        /// reader hands back a default.
        /// </summary>
        private IReadOnlyList<string> Declarations(SerialField sf, string name)
        {
            string elementType = ResolvedElementType(sf);

            if (sf.IsRef)
            {
                return sf.IsArray
                    ? new[]
                    {
                        $"List<{elementType}?> {name} = [];",
                        $"List<int> {name}Index = [];",
                    }
                    : new[]
                    {
                        $"{elementType}? {name};",
                        $"int {name}Index = 0;",
                    };
            }

            if (sf.IsArray)
                return new[] { $"List<{elementType}> {name} = [];" };

            return new[] { $"{elementType} {name} = {DefaultValue(sf)};" };
        }

        private string DefaultValue(SerialField sf)
        {
            switch (sf.ElementType)
            {
                case ValueType.String: return "''";
                case ValueType.Bool: return "false";
                case ValueType.Float:
                case ValueType.Double: return "0.0";
                case ValueType.Int64:
                case ValueType.DateTime:
                case ValueType.TimeSpan: return "BigInt.zero";
                case ValueType.Uuid: return "Uuid.empty()";
                case ValueType.Enum:
                    return $"{sf.FirstField.Enum.Name.ToPascalCase()}.of(0)";
                default: return "0";
            }
        }

        private static string ReadKind(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return "var_array";

            if (sf.IsArray)
                return sf.IsRef ? "serial_ref" : "serial";

            return sf.IsRef ? "scalar_ref" : "scalar";
        }

        private DartAccessorView BuildAccessor() => new DartAccessorView
        {
            FileExtension = _recipe.BinaryTableFileExtension,

            Tables = _model.Tables.Select(table => new DartTableSlotView
            {
                Name = DartName(table.Name),
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
                .Select(x => new DartCrossReferenceView
                {
                    Table = DartName(x.Table.Name),
                    Fields = x.Fields.Select(sf => new DartReferenceFieldView
                    {
                        Name = DartName(sf.Name),
                        RefTable = DartName(sf.FirstField.ResolvedRefTable.Name),
                        Value = sf.ElementType == ValueType.ForeignRecord
                            ? "target"
                            : "target." + DartName(sf.FirstField.ResolvedRefField.Name),
                        IsArray = sf.IsArray,
                    }).ToList(),
                })
                .ToList(),
        };

        // ----------------------------------------------------------- rendering

        private string ReadExpression(SerialField sf)
        {
            switch (sf.ElementType)
            {
                case ValueType.String: return "reader.readString()";
                case ValueType.Bool: return "reader.readBool()";
                case ValueType.Int32: return "reader.readInt32()";
                case ValueType.Int64: return "reader.readInt64()";
                case ValueType.Float: return "reader.readFloat()";
                case ValueType.Double: return "reader.readDouble()";
                case ValueType.DateTime: return "reader.readDateTimeTicks()";
                case ValueType.TimeSpan: return "reader.readDurationTicks()";
                case ValueType.Uuid: return "reader.readUuid()";

                // Enum values travel zig-zag encoded rather than fixed width.
                case ValueType.Enum:
                    return $"{sf.FirstField.Enum.Name.ToPascalCase()}.of(reader.readEnum())";

                case ValueType.ForeignRecord: return "reader.readInt32()";

                default:
                    throw new SheetManException($"The dart generator cannot read type `{sf.Type}`.");
            }
        }

        private string ResolvedElementType(SerialField sf)
        {
            if (sf.ElementType == ValueType.ForeignRecord)
                return sf.FirstField.ResolvedRefTable.Name.ToPascalCase() + "Record";

            return ToDartTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull, null);
        }

        private string ToDartTypeName(ValueType type, Models.Enum enumm, string refTableName)
        {
            switch (ValueTypes.ElementOf(type))
            {
                case ValueType.Enum:
                    return enumm.Name.ToPascalCase();

                case ValueType.ForeignRecord:
                    return refTableName.ToPascalCase() + "Record";

                default:
                    return LanguageProfile.Dart.ScalarTypeName(type);
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

                // Parsed rather than written as a literal: an int literal past 2^53 is not
                // exact on the web, which is the whole reason this is a BigInt.
                case ValueType.Int64:
                    return $"BigInt.parse('{((long)constant.Value).ToString(CultureInfo.InvariantCulture)}')";

                case ValueType.Float:
                case ValueType.Double:
                    return Decimal(constant.Type == ValueType.Float
                        ? ((float)constant.Value).ToString("R", CultureInfo.InvariantCulture)
                        : ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture));

                case ValueType.DateTime:
                    return $"BigInt.parse('{((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture)}')";

                case ValueType.TimeSpan:
                    return $"BigInt.parse('{((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture)}')";

                case ValueType.Uuid:
                    return "Uuid(Uint8List.fromList([" + string.Join(", ",
                        ((Guid)constant.Value).ToByteArray()
                            .Select(b => b.ToString(CultureInfo.InvariantCulture))) + "]))";

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                    return $"{constant.Enum.Name.ToPascalCase()}.{DartName(label.Name)}";
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the dart generator cannot render.");
            }
        }

        /// <summary>
        /// Gives a rendered float a decimal point when it has none: `3` is an int literal
        /// in Dart and will not initialize a double.
        /// </summary>
        private static string Decimal(string rendered)
            => rendered.Contains('.') || rendered.Contains('E') || rendered.Contains('e')
                ? rendered
                : rendered + ".0";

        private static string Quote(string value)
        {
            var literal = new StringBuilder("'");

            foreach (var c in value ?? "")
            {
                if (c == '\'')
                    literal.Append(@"\'");
                else if (c == '\\')
                    literal.Append(@"\\");
                else if (c == '$')
                    // A dollar starts an interpolation in a Dart string.
                    literal.Append(@"\$");
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

            return literal.Append('\'').ToString();
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// A field name.
        ///
        /// camelCase, and escaped with a trailing underscore when it lands on a reserved
        /// word. Not a leading one: that would make the member private to its library.
        /// </summary>
        private static string DartName(string name) => LanguageProfile.Dart.MemberName(name.ToCamelCase());

    }
}
