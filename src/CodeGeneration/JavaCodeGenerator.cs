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
    /// Settings for the Java target.
    ///
    /// Declared beside its generator and reached through the recipe's `Targets` list.
    /// </summary>
    public sealed class JavaRecipe : IOutputRecipe
    {
        /// <summary>Source root. The package's directories are created underneath it.</summary>
        public string Path { get; set; } = "";

        /// <summary>Package the generated accessor declares.</summary>
        public string PackageName { get; set; } = "gamedata";

        /// <summary>
        /// Name of the accessor class, which every generated type nests inside and which
        /// names the file.
        /// </summary>
        public string AccessorName { get; set; } = "SheetManData";

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
    /// Emits one Java file holding every generated type, plus the binary reader.
    ///
    /// Nested types rather than a file each, because Java demands a public top-level type
    /// be alone in a file named after it: a model with forty entities would otherwise be
    /// forty files to place in somebody's source tree.
    ///
    /// The shape lives in templates/java.sbn.
    /// </summary>
    [SheetManTarget("java", TargetKind.CodeGeneration, Order = 80)]
    public class JavaCodeGenerator : CodeGenerator<JavaRecipe>
    {
        private Model _model;
        private JavaRecipe _recipe;

        protected override void Run(TargetContext context, JavaRecipe recipe)
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
            // Java expects a type's file to sit in a directory matching its package.
            string filename = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                new[] { _recipe.Path }
                    .Concat(_recipe.PackageName.Split('.'))
                    .Append(_recipe.AccessorName + ".java")
                    .ToArray()));

            Log.Information($"Generating codes for Java into `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render("java.sbn", BuildView()));
        }

        private void WriteBinaryReaderRuntime()
        {
            // Its own `sheetman` package, so the generated accessor's package is free to be
            // anything the consumer wants.
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Java.LiteBinaryReader.java",
                System.IO.Path.Combine(_recipe.Path, "sheetman", "LiteBinaryReader.java"));
        }

        // --------------------------------------------------------------- view

        private JavaFileView BuildView() => new JavaFileView
        {
            PackageName = _recipe.PackageName,
            AccessorName = _recipe.AccessorName,
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = BuildAccessor(),
        };

        private JavaEnumView BuildEnum(Models.Enum enumm)
        {
            var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

            return new JavaEnumView
            {
                Name = enumm.Name.ToPascalCase(),
                Location = enumm.Location.ToString(),
                Comment = CommentLines(enumm.Comment),
                DefaultLabel = JavaConstantName(fallback.Name),
                Labels = enumm.Labels.Select((label, index) => new JavaEnumLabelView
                {
                    Name = JavaConstantName(label.Name),
                    Value = label.Value.ToString(CultureInfo.InvariantCulture),
                    Comment = CommentLines(label.Comment),
                    Separator = index == enumm.Labels.Count - 1 ? ";" : ",",
                }).ToList(),
            };
        }

        private JavaConstantSetView BuildConstantSet(ConstantSet constantSet) => new JavaConstantSetView
        {
            Name = constantSet.Name.ToPascalCase(),
            Location = constantSet.Location.ToString(),
            Comment = CommentLines(constantSet.Comment),
            Constants = constantSet.Constants.Select(constant => new JavaConstantView
            {
                Name = JavaConstantName(constant.Name),
                Type = ToJavaTypeName(constant.Type, constant.Enum, null),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
            }).ToList(),
        };

        private JavaTableView BuildTable(Table table) => new JavaTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            IndexField = JavaName(table.Fields[0].Name),
            Fields = table.SerialFields.Select(BuildField).ToList(),
        };

        private JavaFieldView BuildField(SerialField sf)
        {
            string name = JavaName(sf.Name);
            string elementType = ResolvedElementType(sf);

            return new JavaFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                ElementCount = sf.Fields.Count,
                ElementType = elementType,
                Declarations = Declarations(sf, name, elementType),
                ReadScalar = ReadExpression(sf),
                ReadElement = ReadExpression(sf),
            };
        }

        private IReadOnlyList<string> Declarations(SerialField sf, string name, string elementType)
        {
            if (sf.IsRef)
            {
                return sf.IsArray
                    ? new[] { $"{elementType}[] {name};", $"int[] {name}Index;" }
                    : new[] { $"{elementType} {name};", $"int {name}Index;" };
            }

            return sf.IsArray
                ? new[] { $"{elementType}[] {name};" }
                : new[] { $"{elementType} {name};" };
        }

        private static string ReadKind(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return "var_array";

            if (sf.IsArray)
                return sf.IsRef ? "serial_ref" : "serial";

            return sf.IsRef ? "scalar_ref" : "scalar";
        }

        private JavaAccessorView BuildAccessor() => new JavaAccessorView
        {
            FileExtension = _recipe.BinaryTableFileExtension,

            Tables = _model.Tables.Select(table => new JavaTableSlotView
            {
                Name = JavaName(table.Name),
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
                .Select(x => new JavaCrossReferenceView
                {
                    Table = JavaName(x.Table.Name),
                    RecordName = x.Table.Name.ToPascalCase() + "Record",
                    Fields = x.Fields.Select(sf => new JavaReferenceFieldView
                    {
                        Name = JavaName(sf.Name),
                        RefTable = JavaName(sf.FirstField.ResolvedRefTable.Name),
                        RefRecordName = sf.FirstField.ResolvedRefTable.Name.ToPascalCase() + "Record",
                        Value = sf.ElementType == ValueType.ForeignRecord
                            ? "target"
                            : "target." + JavaName(sf.FirstField.ResolvedRefField.Name),
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
                    throw new SheetManException($"The java generator cannot read type `{sf.Type}`.");
            }
        }

        private string ResolvedElementType(SerialField sf)
        {
            if (sf.ElementType == ValueType.ForeignRecord)
                return sf.FirstField.ResolvedRefTable.Name.ToPascalCase() + "Record";

            return ToJavaTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull, null);
        }

        private string ToJavaTypeName(ValueType type, Models.Enum enumm, string refTableName)
        {
            switch (ValueTypes.ElementOf(type))
            {
                case ValueType.Enum:
                    return enumm.Name.ToPascalCase();

                case ValueType.ForeignRecord:
                    return refTableName.ToPascalCase() + "Record";

                default:
                    return LanguageProfile.Java.ScalarTypeName(type);
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

                // `L`, or the literal is an int and will not fit.
                case ValueType.Int64:
                    return ((long)constant.Value).ToString(CultureInfo.InvariantCulture) + "L";

                // `f`, or the literal is a double and will not narrow implicitly.
                case ValueType.Float:
                    return ((float)constant.Value).ToString("R", CultureInfo.InvariantCulture) + "f";

                case ValueType.Double:
                    return ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                // Ticks, matching what the generated fields hold and for the same reason.
                case ValueType.DateTime:
                    return ((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture) + "L";

                case ValueType.TimeSpan:
                    return ((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture) + "L";

                case ValueType.Uuid:
                    return "new LiteBinaryReader.Uuid(new byte[] { " + string.Join(", ",
                        ((Guid)constant.Value).ToByteArray()
                            .Select(b => "(byte) 0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + " })";

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                    return $"{constant.Enum.Name.ToPascalCase()}.{JavaConstantName(label.Name)}";
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the java generator cannot render.");
            }
        }

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
        /// A field name.
        ///
        /// camelCase, and never escaped: every Java keyword is lowercase and a single word,
        /// while a field name here comes from a sheet column and would have to be exactly
        /// one of them - which the profile's list covers.
        /// </summary>
        private static string JavaName(string name) => LanguageProfile.Java.MemberName(name.ToCamelCase());

        /// <summary>
        /// A constant or enum label name, SCREAMING_SNAKE_CASE as Java writes them.
        /// </summary>
        private static string JavaConstantName(string name) => name.ToSnakeCase().ToUpperInvariant();

    }
}
