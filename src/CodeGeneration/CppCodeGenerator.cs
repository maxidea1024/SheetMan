using SheetMan.Models;
using SheetMan.Extensions;
using SheetMan.Helpers;
using SheetMan.Recipe;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using ValueType = SheetMan.Models.ValueType;
using SheetMan.Targets;

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
    /// Reading is done by lib/cpp/sheetman/lite_binary_reader.h, which the emitted
    /// header includes. That reader is the C++ half of the format the binary exporter
    /// writes, so the two have to change together.
    /// </summary>
    [SheetManTarget("cpp", TargetKind.CodeGeneration, Section = "CodeGenerations.Cpp", Order = 10)]
    public class CppCodeGenerator : Target<RecipeModel.CodeGenerationRecipeGroup.CppRecipe>
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
            const string resourceName = "SheetMan.Runtime.Cpp.lite_binary_reader.h";

            using var stream = typeof(CppCodeGenerator).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new SheetManException($"Embedded resource `{resourceName}` is missing from the build.");

            using var reader = new StreamReader(stream);

            string filename = Path.GetFullPath(
                Path.Combine(_cppRecipe.Path, "sheetman", "lite_binary_reader.h"));

            StagingFiles.WriteAllTextToFile(filename, reader.ReadToEnd());
        }

        private void GenerateModel()
        {
            string filename = Path.GetFullPath(
                Path.Combine(_cppRecipe.Path, _cppRecipe.AccessorName + ".h"));

            Log.Information($"Generating codes for C++ into `{filename}`");

            var cpp = new Printer();

            GenerateHeadLines(cpp);

            string guard = IncludeGuard(_cppRecipe.AccessorName);
            cpp.PrintLine($"#ifndef {guard}");
            cpp.PrintLine($"#define {guard}");
            cpp.PrintLine();
            cpp.PrintLine("#include <cstdint>");
            cpp.PrintLine("#include <string>");
            cpp.PrintLine("#include <unordered_map>");
            cpp.PrintLine("#include <vector>");
            cpp.PrintLine();
            cpp.PrintLine("#include \"sheetman/lite_binary_reader.h\"");

            BeginNamespace(cpp);

            GenerateEnums(cpp);
            GenerateConstantSets(cpp);
            GenerateTables(cpp);
            GenerateAccessor(cpp);

            EndNamespace(cpp);

            cpp.PrintLine();
            cpp.PrintLine($"#endif  // {guard}");

            StagingFiles.WriteAllTextToFile(filename, cpp.ToString());
        }

        // ------------------------------------------------------------- enums

        private void GenerateEnums(Printer cpp)
        {
            foreach (var enumm in _model.Enums)
            {
                cpp.PrintLine();
                cpp.PrintLine($"// Generated from {enumm.Location}");
                GenerateComment(cpp, enumm.Comment);

                // Fixed underlying type because values travel as int32, and scoped so
                // label names cannot collide across declarations.
                cpp.ScopeIn($"enum class {enumm.Name.ToPascalCase()} : std::int32_t\n{{");

                foreach (var label in enumm.Labels)
                {
                    GenerateComment(cpp, label.Comment);
                    cpp.PrintLine($"{label.Name.ToPascalCase()} = {label.Value.ToString(CultureInfo.InvariantCulture)},");
                }

                cpp.ScopeOut("};");
            }
        }

        // --------------------------------------------------------- constants

        private void GenerateConstantSets(Printer cpp)
        {
            foreach (var constantSet in _model.ConstantSets)
            {
                cpp.PrintLine();
                cpp.PrintLine($"// Generated from {constantSet.Location}");
                GenerateComment(cpp, constantSet.Comment);

                cpp.ScopeIn($"struct {constantSet.Name.ToPascalCase()}\n{{");

                foreach (var constant in constantSet.Constants)
                {
                    GenerateComment(cpp, constant.Comment);

                    string type = ToCppTypeName(constant.Type, constant.Enum);
                    string value = RenderConstantValue(constant);

                    // `static inline` so the header can be included from several
                    // translation units without a duplicate definition at link time.
                    // C++17 is the first standard allowing the definition in-class.
                    cpp.PrintLine($"static inline const {type} {constant.Name.ToPascalCase()} = {value};");
                }

                cpp.ScopeOut("};");
            }
        }

        // ------------------------------------------------------------ tables

        private void GenerateTables(Printer cpp)
        {
            // Records point at the records they reference and any two tables may point
            // at each other, so every record type is declared before any is defined.
            if (_model.Tables.Count > 0)
            {
                cpp.PrintLine();
                cpp.PrintLine("// Forward declarations, so records may reference each other in any order.");
                foreach (var table in _model.Tables)
                    cpp.PrintLine($"struct {RecordName(table)};");
            }

            foreach (var table in _model.Tables)
                GenerateTable(cpp, table);
        }

        private void GenerateTable(Printer cpp, Table table)
        {
            cpp.PrintLine();
            cpp.PrintLine($"// Generated from {table.Location}");
            GenerateComment(cpp, table.Comment);

            cpp.ScopeIn($"struct {RecordName(table)}\n{{");

            foreach (var sf in table.SerialFields)
            {
                GenerateComment(cpp, sf.FirstField.Comment);

                string name = CppName(sf.Name);

                if (sf.IsRef)
                {
                    // The target is resolved only once every table is loaded, so the
                    // field starts empty and the raw index is kept beside it.
                    string resolved = ResolvedRefTypeName(sf);

                    if (sf.IsArray)
                    {
                        cpp.PrintLine($"std::vector<{resolved}> {name};");
                        cpp.PrintLine($"std::vector<std::int32_t> {name}_index;");
                    }
                    else
                    {
                        cpp.PrintLine($"{resolved} {name} = {RefDefault(sf)};");
                        cpp.PrintLine($"std::int32_t {name}_index = 0;");
                    }

                    continue;
                }

                string type = ToCppTypeName(sf.FirstField);

                if (sf.IsArray)
                    cpp.PrintLine($"std::vector<{type}> {name};");
                else
                    cpp.PrintLine($"{type} {name}{DefaultInitializer(sf.FirstField)};");
            }

            GenerateRecordRead(cpp, table);

            cpp.ScopeOut("};");

            GenerateTableClass(cpp, table);
        }

        /// <summary>
        /// Emits the per-record read, in the exact field order the binary exporter
        /// writes them.
        /// </summary>
        private void GenerateRecordRead(Printer cpp, Table table)
        {
            cpp.PrintLine();
            cpp.PrintLine("/// Reads one record. Field order must match the exporter's.");
            cpp.ScopeIn("void read(sheetman::LiteBinaryReader& reader)\n{");

            if (table.SerialFields.Count == 0)
                cpp.PrintLine("(void)reader;");

            foreach (var sf in table.SerialFields)
            {
                string name = CppName(sf.Name);

                if (sf.IsVariableLengthArray)
                {
                    // Length varies per row, so it precedes the elements on the wire.
                    cpp.ScopeIn("{");
                    cpp.PrintLine("const std::int32_t count = reader.read_counter32();");
                    cpp.PrintLine($"{name}.resize(static_cast<std::size_t>(count));");
                    cpp.ScopeIn("for (std::int32_t i = 0; i < count; ++i)\n{");
                    cpp.PrintLine($"{ReadElementExpression(sf, name + "[static_cast<std::size_t>(i)]")};");
                    cpp.ScopeOut("}");
                    cpp.ScopeOut("}");
                    continue;
                }

                if (sf.IsArray)
                {
                    // A serial field has one element per column, a count this generator
                    // knows, so nothing precedes the elements on the wire.
                    int n = sf.Fields.Count;

                    if (sf.IsRef)
                    {
                        cpp.PrintLine($"{name}.assign({n}, {RefDefault(sf)});");
                        cpp.PrintLine($"{name}_index.resize({n});");
                        cpp.ScopeIn($"for (std::size_t i = 0; i < {n}; ++i)\n{{");
                        cpp.PrintLine($"reader.read({name}_index[i]);");
                        cpp.ScopeOut("}");
                    }
                    else
                    {
                        cpp.PrintLine($"{name}.resize({n});");
                        cpp.ScopeIn($"for (std::size_t i = 0; i < {n}; ++i)\n{{");
                        cpp.PrintLine($"{ReadElementExpression(sf, name + "[i]")};");
                        cpp.ScopeOut("}");
                    }

                    continue;
                }

                if (sf.IsRef)
                {
                    cpp.PrintLine($"reader.read({name}_index);");
                    continue;
                }

                cpp.PrintLine($"{ReadElementExpression(sf, name)};");
            }

            cpp.ScopeOut("}");
        }

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

        private void GenerateTableClass(Printer cpp, Table table)
        {
            string recordName = RecordName(table);
            string tableName = TableName(table);
            string indexField = CppName(table.Fields[0].Name);

            cpp.PrintLine();
            GenerateComment(cpp, table.Comment);
            cpp.ScopeIn($"class {tableName}\n{{");

            cpp.PrintLine("public:");
            cpp.PrintLine($"const std::vector<{recordName}>& records() const {{ return records_; }}");

            cpp.PrintLine();
            cpp.PrintLine("/// Record with the given primary index, or nullptr when there is none.");
            cpp.ScopeIn($"const {recordName}* find(std::int32_t index) const\n{{");
            cpp.PrintLine("const auto it = by_index_.find(index);");
            cpp.PrintLine("return it == by_index_.end() ? nullptr : &records_[it->second];");
            cpp.ScopeOut("}");

            cpp.PrintLine();
            cpp.PrintLine("/// Loads the table from a .table file written by SheetMan.");
            cpp.ScopeIn("void read(const std::string& filename)\n{");
            cpp.PrintLine("const std::vector<std::uint8_t> buffer = sheetman::read_all_bytes(filename);");
            cpp.PrintLine("sheetman::LiteBinaryReader reader(buffer);");
            cpp.PrintLine();
            cpp.PrintLine("const std::int32_t row_count = sheetman::read_table_header(reader);");
            cpp.PrintLine();
            cpp.PrintLine("records_.clear();");
            cpp.PrintLine("records_.resize(static_cast<std::size_t>(row_count));");
            cpp.ScopeIn("for (std::int32_t i = 0; i < row_count; ++i)\n{");
            cpp.PrintLine("records_[static_cast<std::size_t>(i)].read(reader);");
            cpp.ScopeOut("}");
            cpp.PrintLine();
            cpp.PrintLine("build_index();");
            cpp.ScopeOut("}");

            cpp.PrintLine();
            cpp.PrintLine("private:");
            cpp.PrintLine("friend class Tables;");
            cpp.PrintLine();
            cpp.ScopeIn("void build_index()\n{");
            cpp.PrintLine("by_index_.clear();");
            cpp.PrintLine("by_index_.reserve(records_.size());");
            cpp.ScopeIn("for (std::size_t i = 0; i < records_.size(); ++i)\n{");
            cpp.PrintLine($"by_index_.emplace(records_[i].{indexField}, i);");
            cpp.ScopeOut("}");
            cpp.ScopeOut("}");

            cpp.PrintLine();
            cpp.PrintLine($"std::vector<{recordName}> records_;");
            cpp.PrintLine("std::unordered_map<std::int32_t, std::size_t> by_index_;");

            cpp.ScopeOut("};");
        }

        // ---------------------------------------------------------- accessor

        private void GenerateAccessor(Printer cpp)
        {
            cpp.PrintLine();
            cpp.PrintLine("/// Every table, loaded together so cross-table references can be resolved.");
            cpp.ScopeIn("class Tables\n{");

            cpp.PrintLine("public:");

            foreach (var table in _model.Tables)
            {
                cpp.PrintLine($"const {TableName(table)}& {CppName(table.Name)}() const {{ return {CppName(table.Name)}_; }}");
            }

            cpp.PrintLine();
            cpp.PrintLine("/// Reads every table from `base_path`, then links the references between them.");
            cpp.ScopeIn($"void read_all(const std::string& base_path, const std::string& file_extension = \"{_cppRecipe.BinaryTableFileExtension}\")\n{{");

            if (_model.Tables.Count == 0)
            {
                cpp.PrintLine("(void)base_path;");
                cpp.PrintLine("(void)file_extension;");
            }
            else
            {
                foreach (var table in _model.Tables)
                    cpp.PrintLine($"{CppName(table.Name)}_.read(base_path + \"/{table.Name}\" + file_extension);");

                cpp.PrintLine();
                cpp.PrintLine("solve_cross_references();");
            }

            cpp.ScopeOut("}");

            cpp.PrintLine();
            cpp.PrintLine("private:");
            GenerateSolveCrossReferences(cpp);

            cpp.PrintLine();
            foreach (var table in _model.Tables)
                cpp.PrintLine($"{TableName(table)} {CppName(table.Name)}_;");

            cpp.ScopeOut("};");
        }

        /// <summary>
        /// Turns the stored indices into usable values, once every table is in memory.
        /// </summary>
        private void GenerateSolveCrossReferences(Printer cpp)
        {
            cpp.ScopeIn("void solve_cross_references()\n{");

            bool wroteAny = false;

            foreach (var table in _model.Tables)
            {
                var refFields = table.SerialFields.Where(sf => sf.IsRef).ToList();
                if (refFields.Count == 0)
                    continue;

                wroteAny = true;

                cpp.ScopeIn($"for (auto& record : {CppName(table.Name)}_.records_)\n{{");

                foreach (var sf in refFields)
                {
                    string name = CppName(sf.Name);
                    string refTable = CppName(sf.FirstField.ResolvedRefTable.Name);
                    string value = ReferenceValueExpression(sf, "target");

                    if (sf.IsArray)
                    {
                        cpp.PrintLine($"record.{name}.resize(record.{name}_index.size(), {RefDefault(sf)});");
                        cpp.ScopeIn($"for (std::size_t i = 0; i < record.{name}_index.size(); ++i)\n{{");
                        cpp.PrintLine($"const auto* target = {refTable}_.find(record.{name}_index[i]);");
                        cpp.PrintLine($"if (target != nullptr) record.{name}[i] = {value};");
                        cpp.ScopeOut("}");
                    }
                    else
                    {
                        cpp.ScopeIn("{");
                        cpp.PrintLine($"const auto* target = {refTable}_.find(record.{name}_index);");
                        cpp.PrintLine($"if (target != nullptr) record.{name} = {value};");
                        cpp.ScopeOut("}");
                    }
                }

                cpp.ScopeOut("}");
            }

            if (!wroteAny)
                cpp.PrintLine("// No table references another.");

            cpp.ScopeOut("}");
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

        private void GenerateComment(Printer cpp, string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return;

            foreach (var line in comment.Replace("\r\n", "\n").Split('\n'))
                cpp.PrintLine($"/// {line}");
        }

        private void BeginNamespace(Printer cpp)
        {
            if (string.IsNullOrEmpty(_cppRecipe.Namespace))
                return;

            cpp.PrintLine();
            foreach (var part in NamespaceParts())
                cpp.PrintLine($"namespace {part} {{");
        }

        private void EndNamespace(Printer cpp)
        {
            if (string.IsNullOrEmpty(_cppRecipe.Namespace))
                return;

            cpp.PrintLine();
            foreach (var part in NamespaceParts().Reverse())
                cpp.PrintLine($"}}  // namespace {part}");
        }

        private IEnumerable<string> NamespaceParts()
            => _cppRecipe.Namespace.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries);

        private void GenerateHeadLines(Printer cpp)
        {
            cpp.PrintLine("// ------------------------------------------------------------------------------");
            cpp.PrintLine("// <auto-generated>");
            cpp.PrintLine("//     THIS CODE WAS GENERATED BY SheetMan.");
            cpp.PrintLine("//");
            cpp.PrintLine("//     CHANGES TO THIS FILE MAY CAUSE INCORRECT BEHAVIOR AND WILL BE LOST IF");
            cpp.PrintLine("//     THE CODE IS REGENERATED.");
            cpp.PrintLine("// </auto-generated>");
            cpp.PrintLine("// ------------------------------------------------------------------------------");
            cpp.PrintLine();
        }
    }
}
