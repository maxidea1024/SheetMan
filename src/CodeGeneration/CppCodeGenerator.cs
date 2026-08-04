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
    /// Emits a single self-contained C++17 header per recipe entry.
    ///
    /// One header rather than a file per entity, unlike the TypeScript generator:
    /// C++ has no module system to lean on, and splitting would push include-order
    /// management for the references between tables onto the reader. The C# generator
    /// makes the same choice for the same reason.
    ///
    /// The shape of the header lives in templates/cpp.sbn. This file works out the values
    /// that shape needs - read calls, defaults, escaped names, rendered literals - and
    /// nothing else. Everything here used to be printer calls with the header's structure
    /// spread through string literals across several hundred lines, which made the part a
    /// reviewer cares about the part hardest to see.
    ///
    /// Reading is done by lib/cpp/sheetman/lite_binary_reader.h, which the emitted
    /// header includes. That reader is the C++ half of the format the binary exporter
    /// writes, so the two have to change together.
    /// </summary>
    [SheetManTarget("cpp", TargetKind.CodeGeneration, Section = "CodeGenerations.Cpp", Order = 10)]
    public class CppCodeGenerator : CodeGenerator<RecipeModel.CodeGenerationRecipeGroup.CppRecipe>
    {
        private Model _model;
        private RecipeModel.CodeGenerationRecipeGroup.CppRecipe _cppRecipe;

        protected override void Run(TargetContext context, RecipeModel.CodeGenerationRecipeGroup.CppRecipe cppRecipe)
        {
            if (string.IsNullOrEmpty(cppRecipe.Path))
                return;

            _cppRecipe = cppRecipe;

            // Already narrowed to the side this entry is built for. Both (the default)
            // leaves the model unchanged.
            _model = context.Model;

            GenerateModel();
            WriteBinaryReaderRuntime();
        }

        /// <summary>
        /// Writes the LiteBinary reader beside the generated header.
        ///
        /// Emitted rather than left in lib/cpp for the consumer to put on an include
        /// path. The generated header includes it by a relative path, so the output
        /// directory is self-contained and there is no way to pair generated code with a
        /// reader of a different vintage.
        ///
        /// The source is an embedded resource taken from lib/cpp, so there is one copy to
        /// maintain and it cannot drift from what is shipped.
        /// </summary>
        private void WriteBinaryReaderRuntime()
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Cpp.lite_binary_reader.h",
                Path.Combine(_cppRecipe.Path, "sheetman", "lite_binary_reader.h"));
        }

        private void GenerateModel()
        {
            string filename = Path.GetFullPath(
                Path.Combine(_cppRecipe.Path, _cppRecipe.AccessorName + ".h"));

            Log.Information($"Generating codes for C++ into `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render("cpp.sbn", BuildView()));
        }

        // --------------------------------------------------------------- view

        private CppFileView BuildView()
        {
            var parts = NamespaceParts().ToList();

            return new CppFileView
            {
                IncludeGuard = IncludeGuard(_cppRecipe.AccessorName),

                NamespaceOpen = parts.Select(part => $"namespace {part} {{").ToList(),

                // Innermost first, and each closer names its namespace, because a header
                // that ends in a run of bare braces is unreadable.
                NamespaceClose = Enumerable.Reverse(parts).Select(part => $"}}  // namespace {part}").ToList(),

                Enums = _model.Enums.Select(BuildEnum).ToList(),
                ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
                Tables = _model.Tables.Select(BuildTable).ToList(),
                Accessor = BuildAccessor(),
            };
        }

        private CppEnumView BuildEnum(Models.Enum enumm) => new CppEnumView
        {
            // Fixed underlying type because values travel as int32, and scoped so label
            // names cannot collide across declarations - both decided in the template.
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            Labels = enumm.Labels.Select(label => new CppEnumLabelView
            {
                Name = label.Name.ToPascalCase(),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };

        private CppConstantSetView BuildConstantSet(ConstantSet constantSet) => new CppConstantSetView
        {
            Name = constantSet.Name.ToPascalCase(),
            Location = constantSet.Location.ToString(),
            Comment = CommentLines(constantSet.Comment),
            Constants = constantSet.Constants.Select(constant => new CppConstantView
            {
                Name = constant.Name.ToPascalCase(),
                Type = ToCppTypeName(constant.Type, constant.Enum),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
            }).ToList(),
        };

        private CppTableView BuildTable(Table table) => new CppTableView
        {
            RecordName = RecordName(table),
            TableName = TableName(table),
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            IndexField = CppName(table.Fields[0].Name),
            Fields = table.SerialFields.Select(BuildField).ToList(),
        };

        private CppFieldView BuildField(SerialField sf)
        {
            string name = CppName(sf.Name);

            return new CppFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                Declarations = Declarations(sf, name),
                Kind = ReadKind(sf),
                Name = name,
                ElementCount = sf.Fields.Count,
                RefDefault = RefDefault(sf),
                ReadScalar = ReadElementExpression(sf, name),
                ReadElement = ReadElementExpression(sf, name + "[i]"),
                ReadVarElement = ReadElementExpression(sf, name + "[static_cast<std::size_t>(i)]"),
            };
        }

        /// <summary>
        /// The member declarations for a field.
        ///
        /// A reference gets two: the resolved value, and the raw index it was read as. The
        /// target is not known until every table is loaded, so the first starts empty.
        /// </summary>
        private IReadOnlyList<string> Declarations(SerialField sf, string name)
        {
            if (sf.IsRef)
            {
                string resolved = ResolvedRefTypeName(sf);

                return sf.IsArray
                    ? new[]
                    {
                        $"std::vector<{resolved}> {name};",
                        $"std::vector<std::int32_t> {name}_index;",
                    }
                    : new[]
                    {
                        $"{resolved} {name} = {RefDefault(sf)};",
                        $"std::int32_t {name}_index = 0;",
                    };
            }

            string type = ToCppTypeName(sf.FirstField);

            return sf.IsArray
                ? new[] { $"std::vector<{type}> {name};" }
                : new[] { $"{type} {name}{DefaultInitializer(sf.FirstField)};" };
        }

        /// <summary>
        /// Which of the five read shapes a field takes.
        ///
        /// A variable-length array is tested first because it is also an array: its length
        /// varies per row and so precedes the elements on the wire, where a serial field's
        /// length is its column count and the generated code already knows it.
        /// </summary>
        private static string ReadKind(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return "var_array";

            if (sf.IsArray)
                return sf.IsRef ? "serial_ref" : "serial";

            return sf.IsRef ? "scalar_ref" : "scalar";
        }

        private CppAccessorView BuildAccessor() => new CppAccessorView
        {
            FileExtension = _cppRecipe.BinaryTableFileExtension,

            Tables = _model.Tables.Select(table => new CppTableSlotView
            {
                Name = CppName(table.Name),
                TableName = TableName(table),

                // Unescaped: this one names the file the exporter wrote, not an identifier.
                DataFileName = table.Name,
            }).ToList(),

            CrossReferences = _model.Tables
                .Select(table => new
                {
                    Table = table,
                    Fields = table.SerialFields.Where(sf => sf.IsRef).ToList(),
                })
                .Where(x => x.Fields.Count > 0)
                .Select(x => new CppCrossReferenceView
                {
                    Table = CppName(x.Table.Name),
                    Fields = x.Fields.Select(sf => new CppReferenceFieldView
                    {
                        Name = CppName(sf.Name),
                        RefTable = CppName(sf.FirstField.ResolvedRefTable.Name),
                        Value = ReferenceValueExpression(sf, "target"),
                        RefDefault = RefDefault(sf),
                        IsArray = sf.IsArray,
                    }).ToList(),
                })
                .ToList(),
        };

        // ----------------------------------------------------------- rendering

        /// <summary>
        /// The call reading one value of a field's element type into
        /// <paramref name="target"/>.
        /// </summary>
        private string ReadElementExpression(SerialField sf, string target)
        {
            // Enum values are zig-zag encoded rather than fixed width, so they need
            // the dedicated overload.
            if (sf.ElementType == ValueType.Enum)
                return $"reader.read_enum({target})";

            return $"reader.read({target})";
        }

        /// <summary>
        /// What a resolved reference yields: the record itself, or one of its fields
        /// when the reference names a field.
        /// </summary>
        private string ReferenceValueExpression(SerialField sf, string targetVariable)
        {
            if (sf.ElementType == ValueType.ForeignRecord)
                return targetVariable;

            return $"{targetVariable}->{CppName(sf.FirstField.ResolvedRefField.Name)}";
        }

        /// <summary>
        /// The type a resolved reference is stored as: a pointer to the referenced
        /// record, or a copy of the referenced field's value.
        /// </summary>
        private string ResolvedRefTypeName(SerialField sf)
        {
            if (sf.ElementType == ValueType.ForeignRecord)
                return "const " + RecordName(sf.FirstField.ResolvedRefTable) + "*";

            return ToCppTypeName(sf.FirstField);
        }

        private string RefDefault(SerialField sf)
            => sf.ElementType == ValueType.ForeignRecord ? "nullptr" : DefaultValueLiteral(sf.ElementType);

        // ------------------------------------------------------------- types

        /// <summary>
        /// A member or member-function name.
        ///
        /// snake_case, which is what makes the escape necessary here and nowhere else: every
        /// C++ keyword is lowercase, so `Int` becomes `int` and `Class` becomes `class`. The
        /// generator used to emit those verbatim - `std::string class;` - and report success,
        /// because nothing compiled the result.
        /// </summary>
        private static string CppName(string name) => LanguageProfile.Cpp.MemberName(name.ToSnakeCase());

        private string ToCppTypeName(Field field) => ToCppTypeName(field.ElementType, field.EnumOrNull);

        private string ToCppTypeName(ValueType type, Models.Enum enumm)
        {
            switch (ValueTypes.ElementOf(type))
            {
                // The two that name something from the model rather than the language.
                case ValueType.Enum:
                    return enumm.Name.ToPascalCase();

                // A reference is carried as the target row's primary index; the generated
                // read turns it into a pointer once every table is loaded.
                case ValueType.ForeignRecord:
                    return "std::int32_t";

                default:
                    return LanguageProfile.Cpp.ScalarTypeName(type);
            }
        }

        /// <summary>
        /// Initializer for a scalar member, so a default-constructed record holds
        /// defined values rather than whatever was on the stack.
        /// </summary>
        private string DefaultInitializer(Field field)
        {
            switch (field.ElementType)
            {
                // These default-construct themselves.
                case ValueType.String:
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                case ValueType.Uuid:
                    return "";

                case ValueType.Enum:
                    return $" = static_cast<{ToCppTypeName(field)}>(0)";

                default:
                    return $" = {DefaultValueLiteral(field.ElementType)}";
            }
        }

        private string DefaultValueLiteral(ValueType type)
        {
            switch (ValueTypes.ElementOf(type))
            {
                case ValueType.Bool: return "false";
                case ValueType.Float: return "0.0f";
                case ValueType.Double: return "0.0";
                case ValueType.String: return "std::string()";
                default: return "0";
            }
        }

        private string RenderConstantValue(ConstantSet.Constant constant)
        {
            switch (constant.Type)
            {
                case ValueType.String:
                    return $"\"{EscapeCppString((string)constant.Value)}\"";

                case ValueType.Bool:
                    return (bool)constant.Value ? "true" : "false";

                case ValueType.Int32:
                    return ((int)constant.Value).ToString(CultureInfo.InvariantCulture);

                case ValueType.Int64:
                    return ((long)constant.Value).ToString(CultureInfo.InvariantCulture) + "LL";

                case ValueType.Float:
                    return ((float)constant.Value).ToString("R", CultureInfo.InvariantCulture) + "f";

                case ValueType.Double:
                    return ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                case ValueType.DateTime:
                    return $"sheetman::DateTime{{ {((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture)}LL }}";

                case ValueType.TimeSpan:
                    return $"sheetman::TimeSpan{{ {((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture)}LL }}";

                case ValueType.Uuid:
                    return RenderUuidLiteral((Guid)constant.Value);

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel((int)constant.Value, constant.Location);
                    return $"{constant.Enum.Name.ToPascalCase()}::{label.Name.ToPascalCase()}";
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the C++ generator cannot render.");
            }
        }

        /// <summary>
        /// A Uuid as its raw bytes, in the order the reader expects.
        /// </summary>
        private string RenderUuidLiteral(Guid value)
        {
            var parts = value.ToByteArray().Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture));
            return $"sheetman::Uuid{{ {{ {string.Join(", ", parts)} }} }}";
        }

        private string EscapeCppString(string input)
        {
            var literal = new StringBuilder(input.Length + 2);

            foreach (var c in input)
            {
                switch (c)
                {
                    case '"': literal.Append("\\\""); break;
                    case '\\': literal.Append(@"\\"); break;
                    case '\n': literal.Append(@"\n"); break;
                    case '\r': literal.Append(@"\r"); break;
                    case '\t': literal.Append(@"\t"); break;
                    default:
                        if (c < 0x20)
                            literal.Append(@"\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                        else
                            literal.Append(c);  // Non-ASCII passes through; the file is UTF-8.
                        break;
                }
            }

            return literal.ToString();
        }

        // ----------------------------------------------------------- helpers

        private string RecordName(Table table) => table.Name.ToPascalCase() + "Record";

        private string TableName(Table table) => table.Name.ToPascalCase() + "Table";

        private static string IncludeGuard(string accessorName)
        {
            var guard = new StringBuilder("SHEETMAN_GENERATED_");

            foreach (var c in accessorName)
                guard.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');

            guard.Append("_H");
            return guard.ToString();
        }

        private IEnumerable<string> NamespaceParts()
            => _cppRecipe.Namespace.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
