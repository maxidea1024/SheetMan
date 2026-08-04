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
    /// Settings for the PHP target.
    ///
    /// Declared beside its generator and reached through the recipe's `Targets` list.
    /// </summary>
    public sealed class PhpRecipe : IOutputRecipe
    {
        /// <summary>Directory the generated file and the reader are written into.</summary>
        public string Path { get; set; } = "";

        /// <summary>Namespace the generated file declares.</summary>
        public string Namespace { get; set; } = "GameData";

        /// <summary>Name of the accessor class, which also names the file.</summary>
        public string AccessorName { get; set; } = "SheetManData";

        /// <summary>
        /// Extension of the table files the generated reader opens. Must match what the
        /// binary exporter was told to write.
        /// </summary>
        public string BinaryTableFileExtension { get; set; } = ".table";

        /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
        public string TargetSide { get; set; } = "cs";
    }

    /// <summary>
    /// Emits one PHP file holding every generated type, plus the binary reader.
    ///
    /// PHP 8.1 or later, for two things worth having: backed enums, so an enum carries its
    /// declared value rather than needing a lookup table beside it, and typed properties,
    /// so a record says what it holds.
    ///
    /// int64 is `int` and that is safe here, unlike in TypeScript and Dart: PHP's integer
    /// is a full 64 bits on any 64 bit build, so 2^53+1 survives. What is not safe is
    /// reading it with `unpack('P')`, which the reader explains.
    ///
    /// The shape lives in templates/php.sbn.
    /// </summary>
    [SheetManTarget("php", TargetKind.CodeGeneration, Order = 87)]
    public class PhpCodeGenerator : Target<PhpRecipe>
    {
        private Model _model;
        private PhpRecipe _recipe;

        protected override void Run(TargetContext context, PhpRecipe recipe)
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
                System.IO.Path.Combine(_recipe.Path, _recipe.AccessorName + ".php"));

            Log.Information($"Generating codes for PHP into `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render("php.sbn", BuildView()));
        }

        private void WriteBinaryReaderRuntime()
        {
            const string resourceName = "SheetMan.Runtime.Php.LiteBinaryReader.php";

            using var stream = typeof(PhpCodeGenerator).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new SheetManException($"Embedded resource `{resourceName}` is missing from the build.");

            using var reader = new StreamReader(stream);

            StagingFiles.WriteAllTextToFile(
                System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(_recipe.Path, "sheetman", "LiteBinaryReader.php")),
                reader.ReadToEnd());
        }

        // --------------------------------------------------------------- view

        private PhpFileView BuildView() => new PhpFileView
        {
            Namespace = _recipe.Namespace,
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = BuildAccessor(),
        };

        private PhpEnumView BuildEnum(Models.Enum enumm)
        {
            var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

            return new PhpEnumView
            {
                Name = EnumName(enumm),
                Location = enumm.Location.ToString(),
                Comment = CommentLines(enumm.Comment),
                DefaultCase = CaseName(fallback.Name),
                Cases = enumm.Labels.Select(label => new PhpEnumCaseView
                {
                    Name = CaseName(label.Name),
                    Value = label.Value.ToString(CultureInfo.InvariantCulture),
                    Comment = CommentLines(label.Comment),
                }).ToList(),
            };
        }

        private PhpConstantSetView BuildConstantSet(ConstantSet constantSet) => new PhpConstantSetView
        {
            Name = constantSet.Name.ToPascalCase(),
            Location = constantSet.Location.ToString(),
            Comment = CommentLines(constantSet.Comment),
            Constants = constantSet.Constants.Select(constant => new PhpConstantView
            {
                Name = ConstantName(constant.Name),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
            }).ToList(),
        };

        private PhpTableView BuildTable(Table table) => new PhpTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            IndexField = PhpName(table.Fields[0].Name),
            Fields = table.SerialFields.Select(BuildField).ToList(),
        };

        private PhpFieldView BuildField(SerialField sf)
        {
            string name = PhpName(sf.Name);

            return new PhpFieldView
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
        /// The property declarations, each typed and initialized.
        ///
        /// Initialized rather than left uninitialized, because reading a typed property
        /// that was never assigned is an Error in PHP - where every other generated reader
        /// hands back a default.
        /// </summary>
        private IReadOnlyList<string> Declarations(SerialField sf, string name)
        {
            string elementType = ResolvedElementType(sf);

            if (sf.IsRef)
            {
                // A reference contributes two properties: the index off the wire, and the
                // record it resolves to once every table is loaded. The resolved one is
                // nullable because a reference into a row that is not there stays null
                // rather than inventing a record.
                return sf.IsArray
                    ? new[]
                    {
                        $"/** @var list<?{elementType}> */",
                        $"public array ${name} = [];",
                        "",
                        "/** @var list<int> */",
                        $"public array ${name}Index = [];",
                    }
                    : new[]
                    {
                        $"public ?{elementType} ${name} = null;",
                        "",
                        $"public int ${name}Index = 0;",
                    };
            }

            if (sf.IsArray)
            {
                return new[]
                {
                    $"/** @var list<{elementType}> */",
                    $"public array ${name} = [];",
                };
            }

            // A uuid is the one scalar that cannot be defaulted in place: a property
            // initializer has to be a constant expression and `new Uuid(...)` is not. So
            // the property is nullable and starts null, which is also honest - it holds
            // nothing until the record is read.
            if (sf.ElementType == ValueType.Uuid)
                return new[] { $"public ?{elementType} ${name} = null;" };

            return new[] { $"public {elementType} ${name} = {DefaultValue(sf)};" };
        }

        private string DefaultValue(SerialField sf)
        {
            switch (sf.ElementType)
            {
                case ValueType.String: return "''";
                case ValueType.Bool: return "false";
                case ValueType.Float:
                case ValueType.Double: return "0.0";

                case ValueType.Enum:
                    return $"{EnumName(sf.FirstField.Enum)}::{DefaultCaseOf(sf.FirstField.Enum)}";

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

        private PhpAccessorView BuildAccessor() => new PhpAccessorView
        {
            Name = _recipe.AccessorName.ToPascalCase(),
            FileExtension = _recipe.BinaryTableFileExtension,

            Tables = _model.Tables.Select(table => new PhpTableSlotView
            {
                Name = PhpName(table.Name),
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
                .Select(x => new PhpCrossReferenceView
                {
                    Table = PhpName(x.Table.Name),
                    Fields = x.Fields.Select(sf => new PhpReferenceFieldView
                    {
                        Name = PhpName(sf.Name),
                        RefTable = PhpName(sf.FirstField.ResolvedRefTable.Name),
                        Value = sf.ElementType == ValueType.ForeignRecord
                            ? "$target"
                            : "$target->" + PhpName(sf.FirstField.ResolvedRefField.Name),
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
                case ValueType.String: return "$reader->readString()";
                case ValueType.Bool: return "$reader->readBool()";
                case ValueType.Int32: return "$reader->readInt32()";
                case ValueType.Int64: return "$reader->readInt64()";
                case ValueType.Float: return "$reader->readFloat()";
                case ValueType.Double: return "$reader->readDouble()";
                case ValueType.DateTime: return "$reader->readDateTimeTicks()";
                case ValueType.TimeSpan: return "$reader->readTimespanTicks()";
                case ValueType.Uuid: return "$reader->readUuid()";

                // Enum values travel zig-zag encoded. `tryFrom` rather than `from`, so a
                // value the sheet never declared lands on the fallback instead of throwing
                // - which is what the other generated readers do.
                case ValueType.Enum:
                {
                    string name = EnumName(sf.FirstField.Enum);

                    return $"{name}::tryFrom($reader->readEnum()) ?? {name}::{DefaultCaseOf(sf.FirstField.Enum)}";
                }

                case ValueType.ForeignRecord: return "$reader->readInt32()";

                default:
                    throw new SheetManException($"The php generator cannot read type `{sf.Type}`.");
            }
        }

        private string ResolvedElementType(SerialField sf)
        {
            if (sf.ElementType == ValueType.ForeignRecord)
                return sf.FirstField.ResolvedRefTable.Name.ToPascalCase() + "Record";

            if (sf.ElementType == ValueType.Enum)
                return EnumName(sf.FirstField.Enum);

            return LanguageProfile.Php.ScalarTypeName(sf.FirstField.ElementType);
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

                // The text, not a Uuid: a class constant has to be a constant expression
                // and `new` is not one. The caller builds a Uuid from it if it wants one.
                case ValueType.Uuid:
                    return Quote(((Guid)constant.Value).ToString());

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                    return $"{EnumName(constant.Enum)}::{CaseName(label.Name)}";
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the php generator cannot render.");
            }
        }

        /// <summary>
        /// A single-quoted PHP string.
        ///
        /// Single quotes because they interpolate nothing: a value holding `$name` or a
        /// backslash escape would otherwise be evaluated rather than stored. Only the quote
        /// and the backslash need escaping inside them.
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

        // ------------------------------------------------------------- helpers

        private static string EnumName(Models.Enum enumm) => enumm.Name.ToPascalCase();

        /// <summary>An enum case, PascalCase as PHP's own enums are written.</summary>
        private static string CaseName(string name) => name.ToPascalCase();

        private static string DefaultCaseOf(Models.Enum enumm)
        {
            var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

            return CaseName(fallback.Name);
        }

        /// <summary>A class constant, SCREAMING_SNAKE_CASE as PHP writes them.</summary>
        private static string ConstantName(string name) => name.ToSnakeCase().ToUpperInvariant();

        /// <summary>A property name, camelCase.</summary>
        private static string PhpName(string name) => LanguageProfile.Php.MemberName(name.ToCamelCase());

        private static IReadOnlyList<string> CommentLines(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return Array.Empty<string>();

            return comment.Replace("\r\n", "\n").Split('\n');
        }
    }
}
