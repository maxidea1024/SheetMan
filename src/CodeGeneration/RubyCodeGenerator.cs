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
    /// Settings for the Ruby target.
    ///
    /// Declared beside its generator and reached through the recipe's `Targets` list.
    /// </summary>
    public sealed class RubyRecipe : IOutputRecipe
    {
        /// <summary>Output directory. Created if it does not exist.</summary>
        public string Path { get; set; } = "";

        /// <summary>Module every generated type is nested in.</summary>
        public string ModuleName { get; set; } = "GameData";

        /// <summary>Base name of the generated file, without its extension.</summary>
        public string AccessorName { get; set; } = "sheetman_data";

        /// <summary>
        /// Extension of the table files the generated reader opens. Must match what the
        /// binary exporter was told to write.
        /// </summary>
        public string BinaryTableFileExtension { get; set; } = ".table";

        /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
        public string TargetSide { get; set; } = "cs";
    }

    /// <summary>
    /// Emits one Ruby file holding every generated type, plus the binary reader beside it.
    ///
    /// Enums are modules of integer constants rather than a class per label: the value is
    /// what travels on the wire, comparisons against it are what consuming code does, and
    /// a module of constants is what Ruby reaches for.
    ///
    /// The shape lives in templates/ruby.sbn.
    /// </summary>
    [SheetManTarget("ruby", TargetKind.CodeGeneration, Order = 75)]
    public class RubyCodeGenerator : Target<RubyRecipe>
    {
        private Model _model;
        private RubyRecipe _recipe;

        protected override void Run(TargetContext context, RubyRecipe recipe)
        {
            if (string.IsNullOrEmpty(recipe.Path))
                return;

            _recipe = recipe;
            _model = context.Model;

            Generate();
            WriteBinaryReaderRuntime();
        }

        private void Generate()
        {
            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(_recipe.Path, _recipe.AccessorName + ".rb"));

            Log.Information($"Generating codes for Ruby into `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render("ruby.sbn", BuildView()));
        }

        private void WriteBinaryReaderRuntime()
        {
            const string resourceName = "SheetMan.Runtime.Ruby.lite_binary_reader.rb";

            using var stream = typeof(RubyCodeGenerator).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new SheetManException($"Embedded resource `{resourceName}` is missing from the build.");

            using var reader = new StreamReader(stream);

            StagingFiles.WriteAllTextToFile(
                System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(_recipe.Path, "sheetman", "lite_binary_reader.rb")),
                reader.ReadToEnd());
        }

        // --------------------------------------------------------------- view

        private RubyFileView BuildView() => new RubyFileView
        {
            ModuleName = _recipe.ModuleName.ToPascalCase(),
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = BuildAccessor(),
        };

        private RubyEnumView BuildEnum(Models.Enum enumm) => new RubyEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            Labels = enumm.Labels.Select(label => new RubyEnumLabelView
            {
                Name = ConstantName(label.Name),
                Symbol = RubyName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };

        private RubyConstantSetView BuildConstantSet(ConstantSet constantSet) => new RubyConstantSetView
        {
            Name = constantSet.Name.ToPascalCase(),
            Location = constantSet.Location.ToString(),
            Comment = CommentLines(constantSet.Comment),
            Constants = constantSet.Constants.Select(constant => new RubyConstantView
            {
                Name = ConstantName(constant.Name),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
            }).ToList(),
        };

        private RubyTableView BuildTable(Table table)
        {
            // A reference contributes its index as well as its value, and both are read
            // from outside the record when references are linked.
            var accessors = new List<string>();

            foreach (var sf in table.SerialFields)
            {
                accessors.Add(RubyName(sf.Name));

                if (sf.IsRef)
                    accessors.Add(RubyName(sf.Name) + "_index");
            }

            return new RubyTableView
            {
                RawName = table.Name,
                RecordName = table.Name.ToPascalCase() + "Record",
                TableName = table.Name.ToPascalCase() + "Table",
                Location = table.Location.ToString(),
                Comment = CommentLines(table.Comment),
                IndexField = RubyName(table.Fields[0].Name),
                AccessorNames = Symbols(accessors),
                Fields = table.SerialFields.Select(BuildField).ToList(),
            };
        }

        private RubyFieldView BuildField(SerialField sf)
        {
            string name = RubyName(sf.Name);

            return new RubyFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                ElementCount = sf.Fields.Count,
                Initializers = Initializers(sf, name),
                ReadScalar = ReadExpression(sf),
                ReadElement = ReadExpression(sf),
            };
        }

        private IReadOnlyList<string> Initializers(SerialField sf, string name)
        {
            if (sf.IsRef)
            {
                return sf.IsArray
                    ? new[] { $"@{name} = []", $"@{name}_index = []" }
                    : new[] { $"@{name} = nil", $"@{name}_index = 0" };
            }

            if (sf.IsArray)
                return new[] { $"@{name} = []" };

            return new[] { $"@{name} = {DefaultValue(sf)}" };
        }

        private string DefaultValue(SerialField sf)
        {
            switch (sf.ElementType)
            {
                case ValueType.String: return "''";
                case ValueType.Bool: return "false";
                case ValueType.Float:
                case ValueType.Double: return "0.0";
                case ValueType.Uuid: return "Sheetman::Uuid.new";
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

        private RubyAccessorView BuildAccessor() => new RubyAccessorView
        {
            FileExtension = _recipe.BinaryTableFileExtension,
            ReaderNames = Symbols(_model.Tables.Select(table => RubyName(table.Name)).ToList()),

            Tables = _model.Tables.Select(table => new RubyTableSlotView
            {
                Name = RubyName(table.Name),
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
                .Select(x => new RubyCrossReferenceView
                {
                    Table = RubyName(x.Table.Name),
                    Fields = x.Fields.Select(sf => new RubyReferenceFieldView
                    {
                        Name = RubyName(sf.Name),
                        RefTable = RubyName(sf.FirstField.ResolvedRefTable.Name),
                        Value = sf.ElementType == ValueType.ForeignRecord
                            ? "target"
                            : "target." + RubyName(sf.FirstField.ResolvedRefField.Name),
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
                case ValueType.String: return "reader.read_string";
                case ValueType.Bool: return "reader.read_bool";
                case ValueType.Int32: return "reader.read_int32";
                case ValueType.Int64: return "reader.read_int64";
                case ValueType.Float: return "reader.read_float";
                case ValueType.Double: return "reader.read_double";
                case ValueType.DateTime: return "reader.read_datetime_ticks";
                case ValueType.TimeSpan: return "reader.read_duration_ticks";
                case ValueType.Uuid: return "reader.read_uuid";

                // Enum values travel zig-zag encoded rather than fixed width, and arrive as
                // the integer the sheet declared.
                case ValueType.Enum: return "reader.read_enum";

                case ValueType.ForeignRecord: return "reader.read_int32";

                default:
                    throw new SheetManException($"The ruby generator cannot read type `{sf.Type}`.");
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
                    return "Sheetman::Uuid.new([" + string.Join(", ",
                        ((Guid)constant.Value).ToByteArray()
                            .Select(b => b.ToString(CultureInfo.InvariantCulture))) + "].pack('C*'))";

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                    return $"{constant.Enum.Name.ToPascalCase()}::{ConstantName(label.Name)}";
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the ruby generator cannot render.");
            }
        }

        /// <summary>
        /// A single-quoted Ruby literal, which interpolates nothing and so needs only two
        /// characters escaped.
        /// </summary>
        private static string Quote(string value)
        {
            var literal = new StringBuilder("'");

            foreach (var c in value ?? "")
            {
                if (c == '\'' || c == '\\')
                    literal.Append('\\');

                literal.Append(c);
            }

            return literal.Append('\'').ToString();
        }

        private static string Symbols(IReadOnlyList<string> names)
            => string.Join(", ", names.Select(name => ":" + name));

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// An attribute name.
        ///
        /// snake_case, and escaped when it lands on a keyword - which it can, because Ruby
        /// members are lowercase and so is nearly every Ruby keyword.
        /// </summary>
        private static string RubyName(string name) => LanguageProfile.Ruby.MemberName(name.ToSnakeCase());

        /// <summary>A constant, SCREAMING_SNAKE_CASE as Ruby writes them.</summary>
        private static string ConstantName(string name) => name.ToSnakeCase().ToUpperInvariant();

        private static IReadOnlyList<string> CommentLines(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return Array.Empty<string>();

            return comment.Replace("\r\n", "\n").Split('\n');
        }
    }
}
