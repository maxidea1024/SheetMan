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

namespace SheetMan.CodeGeneration;

/// <summary>
/// Emits a header per generated type, plus an umbrella header a consumer includes.
///
/// Splitting a C++ target used to be the thing not worth doing, on the grounds that it would
/// push include-order management for the references between tables onto whoever read the
/// output. It does not: a record holding a whole-row reference has a pointer member, a pointer
/// needs only an incomplete type, and so every record is forward declared in one header that
/// all the table headers include. No table header includes another, which is what makes two
/// tables pointing at each other not a cycle - and a cycle between include-guarded headers
/// does not fail loudly, it resolves differently depending on which translation unit got
/// there first.
///
/// An enum is the opposite case: a field declared with one is a value, so its complete type
/// has to be there and its header is a real include.
///
/// The shapes live in templates/cpp-*.sbn, one per kind of file, over the shared head and
/// foot in cpp-file-head.sbn and cpp-file-foot.sbn. This file works out the values
/// that shape needs - read calls, defaults, escaped names, rendered literals - and
/// nothing else. Everything here used to be printer calls with the header's structure
/// spread through string literals across several hundred lines, which made the part a
/// reviewer cares about the part hardest to see.
///
/// Reading is done by lib/cpp/sheetman/scb_reader.h, which the emitted
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

        SweepStaleOutput(cppRecipe.Path, cppRecipe.Sweep);

        _cppRecipe = cppRecipe;

        // Already narrowed to the side this entry is built for. Both (the default)
        // leaves the model unchanged.
        _model = context.Model;

        GenerateModel();
        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// Writes the Scb reader beside the generated header.
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
            "SheetMan.Runtime.Cpp.scb_reader.h",
            Path.Combine(_cppRecipe.Path, "sheetman", "scb_reader.h"));

        // Asked for rather than assumed. It reaches the network, and it is the only
        // emitted file that needs a link flag.
        if (_cppRecipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Cpp.updater.h",
                Path.Combine(_cppRecipe.Path, "sheetman", "updater.h"));
        }
    }

    private void GenerateModel()
    {
        var view = BuildView();

        Log.Information(
            $"Generating codes for C++ into `{Path.GetFullPath(_cppRecipe.Path)}`");

        // Every record as an incomplete type, which is what a pointer member needs and all a
        // reference between two tables needs - so no table header includes another.
        Write(ForwardHeader, "cpp-forward.sbn", Part(
            Guard("FORWARD"),
            Array.Empty<string>(),
            part => part.Records = view.Tables.Select(table => table.RecordName).ToList()));

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            // An enum names its underlying type and nothing else.
            Write(EnumHeader(pair.rendered), "cpp-enum.sbn", Part(
                Guard("ENUM_" + pair.rendered.Name.ToSnakeCase()),
                new[] { "<cstdint>" },
                part => part.Enumm = pair.rendered));
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant set names the types of its own constants: an integer type, a string,
            // one of the reader's for a datetime, timespan or uuid, and an enum where one is
            // declared with it.
            Write(ConstantsHeader(pair.rendered), "cpp-constants.sbn", Part(
                Guard("CONST_" + pair.rendered.Name.ToSnakeCase()),
                StandardHeadersFor(pair.model.Constants.Select(constant => constant.Type))
                    .Concat(NeedsReader(pair.model) ? new[] { ReaderInclude } : Array.Empty<string>())
                    .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumHeaderFor)),
                part => part.Set = pair.rendered));
        }

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A table always holds a vector of rows and a map from index to position, always
            // takes a filename as a string, and always reads through the reader. On top of
            // that: the forward header for the records it points at, and the complete type of
            // every enum a field is declared with - an enum member is a value, not a pointer.
            Write(TableHeader(pair.rendered), "cpp-table.sbn", Part(
                Guard(pair.rendered.RawName.ToSnakeCase()),
                new[] { "<cstddef>", "<cstdint>", "<string>", "<unordered_map>", "<vector>", ReaderInclude }
                    .Append(ForwardHeader)
                    .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumHeaderFor)),
                part => part.Table = pair.rendered));
        }

        // The umbrella. A consumer's include is unchanged - same file name, same guard, same
        // types reachable from it - only now it reaches them by including the headers that
        // declare them.
        Write(_cppRecipe.AccessorName + ".h", "cpp-accessor.sbn", Part(
            IncludeGuard(_cppRecipe.AccessorName),
            new[] { "<cstddef>", "<string>" }
                .Concat(view.Enums.Select(EnumHeader))
                .Concat(view.ConstantSets.Select(ConstantsHeader))
                .Concat(view.Tables.Select(TableHeader)),
            part => part.Accessor = view.Accessor));
    }

    // --------------------------------------------------------- file layout

    /// <summary>
    /// Flat, one header per generated type.
    /// </summary>
    /// <remarks>
    /// The names carry the grouping rather than directories. C++ has namespaces, so
    /// subdirectories would be possible - but an include path is written into the generated
    /// text, so a directory is a string every file has to agree on rather than something the
    /// compiler works out. Same reasoning as C, and the two targets are better off answering
    /// it the same way.
    /// </remarks>
    private string ForwardHeader => _cppRecipe.AccessorName + "_forward.h";

    /// <summary>
    /// Where each kind of generated header goes, and what it is called.
    /// </summary>
    /// <remarks>
    /// The directory is the layout every target shares - `tables/`, `enums/`,
    /// `constants/` - and the `#include` lines come from these same helpers, so a file
    /// and the line that reaches for it cannot disagree.
    ///
    /// The accessor prefix stays in the name even inside a directory. It is what keeps
    /// two SheetMan outputs on one include path from colliding, and a directory does not
    /// take that over: `tables/template.h` from two of them is the same path twice.
    /// </remarks>
    private string EnumHeader(CppEnumView enumm) => $"enums/{_cppRecipe.AccessorName}_enum_{enumm.Name.ToSnakeCase()}.h";
    private string EnumHeaderFor(Models.Enum enumm) => $"enums/{_cppRecipe.AccessorName}_enum_{enumm.Name.ToSnakeCase()}.h";

    private string ConstantsHeader(CppConstantSetView set) => $"constants/{_cppRecipe.AccessorName}_const_{set.Name.ToSnakeCase()}.h";

    private string TableHeader(CppTableView table) => $"tables/{_cppRecipe.AccessorName}_{table.RawName.ToSnakeCase()}.h";

    private const string ReaderInclude = "\"sheetman/scb_reader.h\"";

    private string Guard(string suffix) => IncludeGuard($"{_cppRecipe.AccessorName}_{suffix}");

    private void Write(string filename, string templateName, CppPartView view)
    {
        string full = Path.GetFullPath(Path.Combine(_cppRecipe.Path, filename));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    /// <summary>
    /// The common shape of every part: the guard, the namespace, and the includes - standard
    /// library first, then this tool's own, with a blank line between.
    /// </summary>
    private CppPartView Part(string guard, IEnumerable<string> includes, Action<CppPartView> subject)
    {
        var parts = NamespaceParts().ToList();

        var part = new CppPartView
        {
            IncludeGuard = guard,
            Includes = IncludeLines(includes),
            NamespaceOpen = parts.Select(name => $"namespace {name} {{").ToList(),
            NamespaceClose = Enumerable.Reverse(parts).Select(name => $"}}  // namespace {name}").ToList(),
        };

        subject(part);

        return part;
    }

    /// <summary>
    /// `#include` lines, angle-bracketed ones first and quoted ones after, each group in the
    /// order given and separated by a blank line.
    /// </summary>
    private static IReadOnlyList<string> IncludeLines(IEnumerable<string> includes)
    {
        var all = includes.Distinct().ToList();

        var standard = all.Where(name => name.StartsWith("<", StringComparison.Ordinal)).ToList();
        var own = all.Where(name => !name.StartsWith("<", StringComparison.Ordinal)).ToList();

        var lines = standard.Select(name => $"#include {name}").ToList();

        if (standard.Count > 0 && own.Count > 0)
            lines.Add("");

        lines.AddRange(own.Select(name => name.StartsWith("\"", StringComparison.Ordinal)
            ? $"#include {name}"
            : $"#include \"{name}\""));

        return lines;
    }

    /// <summary>
    /// The standard headers a set of value types names between them.
    /// </summary>
    private static IEnumerable<string> StandardHeadersFor(IEnumerable<ValueType> types)
    {
        var seen = types.Select(ValueTypes.ElementOf).ToList();

        if (seen.Any(type => type == ValueType.Int32 || type == ValueType.Int64))
            yield return "<cstdint>";

        if (seen.Contains(ValueType.String))
            yield return "<string>";
    }

    /// <summary>
    /// Whether a constant set names one of the reader's own types: a datetime, a timespan or
    /// a uuid. Those are the only three a constant can be that C++ has no built-in for.
    /// </summary>
    private static bool NeedsReader(ConstantSet set)
        => set.Constants.Any(constant =>
            constant.Type == ValueType.DateTime
            || constant.Type == ValueType.TimeSpan
            || constant.Type == ValueType.Uuid);

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
        RawName = table.Name,
        RecordName = RecordName(table),
        TableName = TableName(table),
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<CppIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf =>
        {
            string keyType = ToCppTypeName(sf.FirstField);
            bool copyCosts = keyType == "std::string";

            return new CppIndexView
            {
                Member = CppName(sf.Name),
                Suffix = sf.Name.ToSnakeCase(),
                KeyType = keyType,
                KeyParam = copyCosts ? "const " + keyType + "&" : keyType,

                // std::string concatenates; everything else has to go through
                // std::to_string, and an enum through its underlying number first.
                KeyText = copyCosts
                    ? "key"
                    : (sf.ElementType == Models.ValueType.Enum
                        ? "std::to_string(static_cast<std::int64_t>(key))"
                        : "std::to_string(key)"),

                MapName = "by_" + sf.Name.ToSnakeCase() + "_",
                FieldName = sf.Name.ToPascalCase(),
            };
        }).ToList();

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `find_by_index`. The
    /// primary index is whatever the sheet put in the first column, and a sheet that
    /// calls it `Id` generates `find_by_id`.
    /// </remarks>
    private static string PrimaryLookup(Table refTable)
        => "find_by_" + refTable.SerialFields.First(sf => sf.IsIndexer).Name.ToSnakeCase();

    private CppFieldView BuildField(Table table, SerialField sf)
    {
        string name = CppName(sf.Name);

        return new CppFieldView
        {
            Comment = CommentLines(sf.FirstField.Comment),
            Declarations = Declarations(sf, name),
            Kind = ReadKind(sf),
            Tag = sf.FirstField.Tag.Value,
            ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
            Name = name,
            ElementCount = sf.Fields.Count,
            RefDefault = RefDefault(sf),
            ReadScalar = ReadElementExpression(sf, "record." + name),
            ReadElement = ReadElementExpression(sf, "record." + name + "[j]"),
            ReadVarElement = ReadElementExpression(sf, "record." + name + "[static_cast<std::size_t>(j)]"),
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
                    RefLookup = PrimaryLookup(sf.FirstField.ResolvedRefTable),
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

        if (sf.IsRef)
            return $"reader.read({target})";

        // The three promotable members read through the as-helpers, so a file written
        // before the column was widened still reads.
        return sf.ElementType switch
        {
            ValueType.Int32 => $"reader.read_i32_as(column.element, {target})",
            ValueType.Int64 => $"reader.read_i64_as(column.element, {target})",
            ValueType.Double => $"reader.read_f64_as(column.element, {target})",
            _ => $"reader.read({target})",
        };
    }

    /// <summary>
    /// The rendered check_column call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(SerialField sf, string tableName)
    {
        string kind = sf.IsVariableLengthArray
            ? "sheetman::kKindVarArray"
            : (sf.Fields.Count > 1 ? "sheetman::kKindFixedArray" : "sheetman::kKindScalar");

        int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

        string accepted;

        if (sf.IsRef)
            accepted = "sheetman::kElementI32";
        else
        {
            switch (sf.ElementType)
            {
                case ValueType.Int32:
                    accepted = "sheetman::kElementI32, sheetman::kElementVarint"; break;
                case ValueType.Int64:
                    accepted = "sheetman::kElementI64, sheetman::kElementI32, sheetman::kElementVarint"; break;
                case ValueType.Double:
                    accepted = "sheetman::kElementF64, sheetman::kElementF32, sheetman::kElementI32"; break;
                case ValueType.Float: accepted = "sheetman::kElementF32"; break;
                case ValueType.Bool: accepted = "sheetman::kElementBool"; break;
                case ValueType.String: accepted = "sheetman::kElementString"; break;
                case ValueType.Uuid: accepted = "sheetman::kElementUuid"; break;
                case ValueType.Enum: accepted = "sheetman::kElementVarint"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "sheetman::kElementI64"; break;

                default:
                    throw new SheetManException($"The cpp generator cannot check type `{sf.Type}`.");
            }
        }

        return $"sheetman::check_column(column, \"{tableName}.{sf.Name}\", {kind}, {count}, {{{accepted}}});";
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
        return ValueTypes.ElementOf(type) switch
        {
            ValueType.Bool => "false",
            ValueType.Float => "0.0f",
            ValueType.Double => "0.0",
            ValueType.String => "std::string()",
            _ => "0",
        };
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

            // A time_point and a duration, built from the tick counts the sheet holds.
            // from_net_ticks does the epoch shift, so a constant and a column read from
            // a file are the same value.
            case ValueType.DateTime:
                return "sheetman::from_net_ticks(" +
                       ((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture) + "LL)";

            case ValueType.TimeSpan:
                return "sheetman::TimeSpan(" +
                       ((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture) + "LL)";

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
