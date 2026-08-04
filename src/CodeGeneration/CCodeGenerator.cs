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
    /// Settings for the C target.
    ///
    /// Declared beside its generator and reached through the recipe's `Targets` list.
    /// </summary>
    public sealed class CRecipe : IOutputRecipe
    {
        /// <summary>Directory the header, the source and the reader are written into.</summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Name of the accessor, which also names the two files and prefixes every
        /// generated identifier.
        ///
        /// C has no namespaces, so this prefix is the whole of the collision avoidance -
        /// which is the one place this target departs from the style it otherwise follows.
        /// Doom and Quake put a subsystem prefix on functions (`P_SpawnMobj`) and none on
        /// types (`mobj_t`), because a game is one program. Generated code is dropped into
        /// somebody else's, so the types carry it too.
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
    /// Emits a C header per generated type, a source beside the ones that need code, an umbrella
    /// header a consumer includes, and the binary reader.
    ///
    /// Two questions C asks that none of the other targets do.
    ///
    /// Who owns the strings. Each table owns one arena; its records hold pointers into it
    /// and the whole thing is released in one call. The alternative - a malloc per string
    /// and a free the caller has to find - is how a generated API turns into a leak.
    ///
    /// What happens on a bad file. C has nothing to throw, so the reader returns false and
    /// remembers why, and a failed load frees what it had and leaves the table empty. A
    /// caller that ignores the return value still sees no rows rather than half of them.
    ///
    /// A third question, which arrived with the split. What includes what. A reference between two
    /// tables is a cycle as often as not, and a pointer member needs only an incomplete type - so
    /// every record is forward declared in one header that every table header includes, and no
    /// table header includes another. An enum is different: a field declared with one is a value,
    /// so its complete type has to be there.
    ///
    /// The shapes live in templates/c-*.sbn, one per kind of file, over the shared heads in
    /// c-header-head.sbn and c-source-head.sbn. What a file needs comes from
    /// <see cref="TypeDependencies"/>.
    /// </summary>
    [SheetManTarget("c", TargetKind.CodeGeneration, Order = 86)]
    public class CCodeGenerator : CodeGenerator<CRecipe>
    {
        private Model _model;
        private CRecipe _recipe;

        protected override void Run(TargetContext context, CRecipe recipe)
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
            var view = BuildView();

            Log.Information(
                $"Generating codes for C into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

            // Every record as an incomplete type. This is C's answer to a reference between two
            // tables: a pointer member needs no more than this, so no table header includes
            // another and a cycle between them is not a cycle here.
            Write(ForwardHeader, "c-forward.sbn", new CPartView
            {
                Guard = Guard("FORWARD"),
                Includes = Array.Empty<string>(),
                Forwards = Array.Empty<string>(),
                Records = view.Tables.Select(table => table.RecordName).ToList(),
            });

            foreach (var enumm in view.Enums)
            {
                // An enum needs nothing: its labels are integers, and neither a typedef nor an
                // enum has linkage, so no extern "C" either.
                Write(EnumHeader(enumm), "c-enum.sbn", new CPartView
                {
                    Guard = Guard("ENUM_" + enumm.RawName.ToSnakeCase().ToUpperInvariant()),
                    Includes = Array.Empty<string>(),
                    Forwards = Array.Empty<string>(),
                    Enumm = enumm,
                });
            }

            foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
            {
                bool anyExtern = pair.rendered.Constants.Any(constant => constant.IsExtern);

                // A uuid constant's type is the reader's, and an enum-typed one names an enum by
                // its complete type. extern "C" because an `extern const` has linkage.
                Write(ConstantsHeader(pair.rendered), "c-constants-header.sbn", new CPartView
                {
                    Guard = Guard("CONST_" + pair.rendered.Name.ToSnakeCase().ToUpperInvariant()),
                    Includes = Includes(
                        reader: NamesUuid(pair.model),
                        headers: TypeDependencies.EnumsNamedBy(pair.model).Select(EnumHeaderFor)),
                    Forwards = Array.Empty<string>(),
                    ExternC = anyExtern,
                    Set = pair.rendered,
                });

                // And nothing at all when there is none to define: a translation unit holding one
                // include is still one a build system has to be told about.
                if (anyExtern)
                {
                    Write(ConstantsSource(pair.rendered), "c-constants-source.sbn", new CPartView
                    {
                        Includes = Includes(reader: false, headers: new[] { ConstantsHeader(pair.rendered) }),
                        Set = pair.rendered,
                    });
                }
            }

            foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
            {
                // The reader for the arena and the index, the forward header for the records it
                // points at, and the complete type of every enum a field is declared with - an
                // enum member is a value, not a pointer, so an incomplete type will not do.
                Write(TableHeader(pair.rendered), "c-table-header.sbn", new CPartView
                {
                    Guard = Guard(pair.rendered.RawName.ToSnakeCase().ToUpperInvariant()),
                    Includes = Includes(
                        reader: true,
                        headers: new[] { ForwardHeader }
                            .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumHeaderFor))),
                    Forwards = Array.Empty<string>(),
                    ExternC = true,
                    Table = pair.rendered,
                });

                // Its own header first, which is what makes the header prove it compiles alone.
                Write(TableSource(pair.rendered), "c-table-source.sbn", new CPartView
                {
                    Includes = Includes(reader: false, headers: new[] { TableHeader(pair.rendered) })
                        .Append("").Append("#include <string.h>").ToList(),
                    Table = pair.rendered,
                });
            }

            // The umbrella. A consumer's `#include "X.h"` is unchanged: it still reaches every
            // generated type, only now by including the headers that declare them.
            Write(FileBase + ".h", "c-accessor-header.sbn", new CPartView
            {
                Guard = Guard(null),
                Includes = Includes(
                    reader: false,
                    headers: view.Enums.Select(EnumHeader)
                        .Concat(view.ConstantSets.Select(ConstantsHeader))
                        .Concat(view.Tables.Select(TableHeader))),
                Forwards = Array.Empty<string>(),
                ExternC = true,
                Accessor = view.Accessor,
            });

            // snprintf explicitly: the reader's header only reaches for stdio.h inside its
            // implementation branch, which used to be this file and is now its own.
            Write(FileBase + ".c", "c-accessor-source.sbn", new CPartView
            {
                Includes = Includes(reader: false, headers: new[] { FileBase + ".h" })
                    .Append("").Append("#include <stdio.h>").Append("#include <string.h>").ToList(),
                Accessor = view.Accessor,
            });

            Write(FileBase + "_Reader.c", "c-reader-source.sbn", new CPartView());
        }

        // --------------------------------------------------------- file layout

        /// <summary>
        /// Flat, one header per generated type and a source beside the ones that need code.
        /// </summary>
        /// <remarks>
        /// The names carry the grouping rather than directories, as they do for Go, Python, Rust
        /// and Java - and here there is a further reason: an include path is written into the
        /// generated text, so a directory is a string every file has to agree on rather than
        /// something the compiler works out.
        /// </remarks>
        private string ForwardHeader => FileBase + "_Forward.h";

        private string EnumHeader(CEnumView enumm) => $"{FileBase}_Enum{enumm.RawName}.h";
        private string EnumHeaderFor(Models.Enum enumm) => $"{FileBase}_Enum{enumm.Name.ToPascalCase()}.h";

        private string ConstantsHeader(CConstantSetView set) => $"{FileBase}_Const{set.Name}.h";
        private string ConstantsSource(CConstantSetView set) => $"{FileBase}_Const{set.Name}.c";

        private string TableHeader(CTableView table) => $"{FileBase}_{table.RawName.ToPascalCase()}.h";
        private string TableSource(CTableView table) => $"{FileBase}_{table.RawName.ToPascalCase()}.c";

        /// <summary>
        /// An include guard. <paramref name="suffix"/> null gives the umbrella's own, which is
        /// what it has always been, so a consumer testing for it still can.
        /// </summary>
        private string Guard(string suffix)
            => suffix == null ? UpperPrefix + "_H" : $"{UpperPrefix}_{suffix}_H";

        /// <summary>
        /// Include lines, reader first and then this tool's own, with a blank line between the
        /// groups.
        /// </summary>
        /// <remarks>
        /// The reader comes first because everything else depends on it and nothing in it depends
        /// on anything here - which is the whole of the ordering, the graph being a DAG once the
        /// table-to-table edges are forward declarations instead of includes.
        /// </remarks>
        private static IReadOnlyList<string> Includes(bool reader, IEnumerable<string> headers)
        {
            var lines = new List<string>();

            if (reader)
                lines.Add("#include \"sheetman/sheetman_lite_binary_reader.h\"");

            var own = headers.Distinct().ToList();

            if (own.Count > 0 && lines.Count > 0)
                lines.Add("");

            foreach (var header in own)
                lines.Add($"#include \"{header}\"");

            return lines;
        }

        /// <summary>
        /// Whether a constant set has a uuid in it, which is the only way its header reaches the
        /// reader - a uuid's type is the reader's own struct.
        /// </summary>
        private static bool NamesUuid(ConstantSet set)
            => set.Constants.Any(constant => constant.Type == ValueType.Uuid);

        /// <summary>
        /// What every generated name starts with, PascalCase.
        ///
        /// The naming here follows the one C has actually settled on for readable systems
        /// code - Doom's and Quake's. A type is PascalCase with a `_t` suffix, a function is
        /// the subsystem prefix and then PascalCase, a struct member is snake_case, and a
        /// constant is SCREAMING_SNAKE. The prefix stands in for the namespace C does not
        /// have, so `SheetManData_ItemRecord_t` and `SheetManData_ItemLoad` rather than the
        /// bare `mobj_t` and `P_SpawnMobj` a single program can get away with.
        /// </summary>
        private string Prefix => _recipe.AccessorName.ToPascalCase();

        /// <summary>The files are named as the recipe named the accessor, unchanged.</summary>
        private string FileBase => _recipe.AccessorName;

        /// <summary>The include guard and the constant names.</summary>
        private string UpperPrefix => _recipe.AccessorName.ToSnakeCase().ToUpperInvariant();

        private void Write(string filename, string templateName, CPartView view)
        {
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, filename));

            StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
        }

        private void WriteBinaryReaderRuntime()
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.C.sheetman_lite_binary_reader.h",
                System.IO.Path.Combine(_recipe.Path, "sheetman", "sheetman_lite_binary_reader.h"));
        }

        // --------------------------------------------------------------- view

        private CFileView BuildView() => new CFileView
        {
            Prefix = Prefix,
            UpperPrefix = UpperPrefix,
            HeaderName = FileBase + ".h",
            Enums = _model.Enums.Select(BuildEnum).ToList(),

            // The names are flat - C has nothing to nest a set in, so the set's name becomes part
            // of each constant's name rather than a scope around them - but they are still
            // grouped by set, because that is the unit a file corresponds to.
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),

            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = BuildAccessor(),
        };

        private CEnumView BuildEnum(Models.Enum enumm) => new CEnumView
        {
            RawName = enumm.Name.ToPascalCase(),
            Name = EnumName(enumm),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            Labels = enumm.Labels.Select(label => new CEnumLabelView
            {
                Name = ConstantName(enumm.Name, label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };

        private CConstantSetView BuildConstantSet(ConstantSet set) => new CConstantSetView
        {
            Name = set.Name.ToPascalCase(),
            Location = set.Location.ToString(),
            Comment = CommentLines(set.Comment),
            Constants = set.Constants.Select(constant => BuildConstant(set, constant)).ToList(),
        };

        private CConstantView BuildConstant(ConstantSet set, ConstantSet.Constant constant)
        {
            // A uuid is a struct, and a struct defined in a header would be a separate
            // object in every translation unit including it. Those go in the .c.
            bool isStruct = constant.Type == ValueType.Uuid;

            return new CConstantView
            {
                Name = ConstantName(set.Name, constant.Name),
                Type = ScalarTypeName(constant.Type, constant.Enum),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
                IsExtern = isStruct,
            };
        }

        private CTableView BuildTable(Table table) => new CTableView
        {
            RawName = table.Name,
            RecordName = RecordName(table),
            TableName = TableTypeName(table),
            FunctionPrefix = FunctionPrefix(table),
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            IndexField = CName(table.Fields[0].Name),

            // One byte per encoded field at the very least, and a serial field encodes
            // once per element. Nothing encodes to nothing.
            MinRowBytes = table.SerialFields.Sum(sf => sf.IsVariableLengthArray ? 1 : sf.Fields.Count),

            Fields = table.SerialFields.Select(BuildField).ToList(),
        };

        private CFieldView BuildField(SerialField sf)
        {
            string name = CName(sf.Name);
            bool isEnum = !sf.IsRef && sf.ElementType == ValueType.Enum;

            return new CFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                ElementCount = sf.Fields.Count,
                ElementType = ResolvedElementType(sf),
                Declarations = Declarations(sf, name),
                NeedsScratch = isEnum,
                EnumType = isEnum ? EnumName(sf.FirstField.Enum) : null,
                ReadScalar = ReadCall(sf, $"&record->{name}"),
                ReadElement = ReadCall(sf, $"&record->{name}[element]"),
            };
        }

        /// <summary>
        /// The member declarations.
        ///
        /// A reference contributes the index that came off the wire as well as what it
        /// resolves to, and a variable length array contributes a count beside its pointer -
        /// C has nowhere else to keep either.
        /// </summary>
        private IReadOnlyList<string> Declarations(SerialField sf, string name)
        {
            string elementType = ResolvedElementType(sf);

            if (sf.IsRef)
            {
                // What a reference resolves to depends on which kind it is, and getting this
                // wrong compiled for a while: every scenario that reached the C target had no
                // reference in it, so nothing crossed a table until the conformance corpus
                // grew one.
                //
                // A whole-row reference resolves to the other table's row, so it is a pointer
                // to const - the row belongs to the table it came from, and writing through
                // this one would edit that table's copy.
                //
                // A field reference resolves to one of that row's values, so it is that
                // value's own type. Declaring a pointer there gave `const int32_t* tier` and
                // an assignment of an int32_t to it.
                string resolved = sf.ElementType == ValueType.ForeignRecord
                    ? $"const {elementType}*"
                    : elementType;

                return sf.IsArray
                    ? new[]
                    {
                        $"{resolved} {name}[{sf.Fields.Count}];",
                        $"int32_t {name}_index[{sf.Fields.Count}];",
                    }
                    : new[]
                    {
                        $"{resolved} {name};",
                        $"int32_t {name}_index;",
                    };
            }

            if (sf.IsVariableLengthArray)
            {
                return new[]
                {
                    $"{elementType}* {name};",
                    $"int32_t {name}_count;",
                };
            }

            if (sf.IsArray)
                return new[] { $"{elementType} {name}[{sf.Fields.Count}];" };

            return new[] { $"{elementType} {name};" };
        }

        private static string ReadKind(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return "var_array";

            if (sf.IsArray)
                return sf.IsRef ? "serial_ref" : "serial";

            return sf.IsRef ? "scalar_ref" : "scalar";
        }

        private CAccessorView BuildAccessor() => new CAccessorView
        {
            Name = Prefix,
            TypeName = Prefix + "_t",
            FileExtension = _recipe.BinaryTableFileExtension,

            Tables = _model.Tables.Select(table => new CTableSlotView
            {
                Name = CName(table.Name),
                TableName = TableTypeName(table),
                FunctionPrefix = FunctionPrefix(table),

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
                .Select(x => new CCrossReferenceView
                {
                    Table = CName(x.Table.Name),
                    FunctionPrefix = FunctionPrefix(x.Table),
                    RecordName = RecordName(x.Table),
                    Fields = x.Fields.Select(BuildReferenceField).ToList(),
                })
                .ToList(),
        };

        private CReferenceFieldView BuildReferenceField(SerialField sf)
        {
            string name = CName(sf.Name);
            var refTable = sf.FirstField.ResolvedRefTable;
            string refRecord = RecordName(refTable);

            return new CReferenceFieldView
            {
                Name = name,
                RefTable = CName(refTable.Name),
                RefFunctionPrefix = FunctionPrefix(refTable),
                RefRecordName = refRecord,

                // Only a whole-record reference resolves to a pointer. A field reference
                // stores the target's value, and the member is declared as that value's
                // type - so there is nothing to point at.
                Value = sf.ElementType == ValueType.ForeignRecord
                    ? "target"
                    : "target->" + CName(sf.FirstField.ResolvedRefField.Name),

                IsArray = sf.IsArray,

                CountExpression = sf.IsVariableLengthArray
                    ? $"record->{name}_count"
                    : sf.Fields.Count.ToString(CultureInfo.InvariantCulture),
            };
        }

        // ----------------------------------------------------------- rendering

        /// <summary>A complete reader call filling the given address.</summary>
        private static string ReadCall(SerialField sf, string address)
        {
            if (sf.IsRef)
                return $"sm_read_int32(reader, {address})";

            switch (sf.ElementType)
            {
                case ValueType.String: return $"sm_read_string(reader, {address})";
                case ValueType.Bool: return $"sm_read_bool(reader, {address})";
                case ValueType.Int32: return $"sm_read_int32(reader, {address})";
                case ValueType.Int64: return $"sm_read_int64(reader, {address})";
                case ValueType.Float: return $"sm_read_float(reader, {address})";
                case ValueType.Double: return $"sm_read_double(reader, {address})";
                case ValueType.DateTime: return $"sm_read_datetime(reader, {address})";
                case ValueType.TimeSpan: return $"sm_read_timespan(reader, {address})";
                case ValueType.Uuid: return $"sm_read_uuid(reader, {address})";

                // Handled with a scratch int32 and a cast; nothing calls this for one.
                case ValueType.Enum: return $"sm_read_enum(reader, {address})";

                default:
                    throw new SheetManException($"The c generator cannot read type `{sf.Type}`.");
            }
        }

        private string ResolvedElementType(SerialField sf)
        {
            if (sf.ElementType == ValueType.ForeignRecord)
                return RecordName(sf.FirstField.ResolvedRefTable);

            return ScalarTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull);
        }

        private string ScalarTypeName(ValueType type, Models.Enum enumm)
        {
            if (ValueTypes.ElementOf(type) == ValueType.Enum)
                return EnumName(enumm);

            return LanguageProfile.C.ScalarTypeName(type);
        }

        private string RenderConstantValue(ConstantSet.Constant constant)
        {
            switch (constant.Type)
            {
                case ValueType.String:
                    return Quote((string)constant.Value);

                // C has no bool literal without <stdbool.h>, which the reader includes -
                // and the header includes the reader, so these are safe.
                case ValueType.Bool:
                    return (bool)constant.Value ? "true" : "false";

                case ValueType.Int32:
                    return ((int)constant.Value).ToString(CultureInfo.InvariantCulture);

                // The suffix matters: without it the literal is an int and the value is
                // truncated before it ever reaches the constant.
                case ValueType.Int64:
                    return ((long)constant.Value).ToString(CultureInfo.InvariantCulture) + "LL";

                case ValueType.Float:
                    return ((float)constant.Value).ToString("R", CultureInfo.InvariantCulture) + "f";

                case ValueType.Double:
                    return ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                case ValueType.DateTime:
                    return ((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture) + "LL";

                case ValueType.TimeSpan:
                    return ((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture) + "LL";

                case ValueType.Uuid:
                    return "{ { " + string.Join(", ",
                        ((Guid)constant.Value).ToByteArray()
                            .Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + " } }";

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);

                    return ConstantName(constant.Enum.Name, label.Name);
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the c generator cannot render.");
            }
        }

        /// <summary>
        /// A C string literal.
        ///
        /// Non-ASCII goes through as UTF-8 bytes rather than as an escape: the generated
        /// files are UTF-8 and so is the format, and \u in a narrow literal is
        /// implementation-defined. A question mark is escaped because three of them in a
        /// row start a trigraph in C99.
        /// </summary>
        private static string Quote(string value)
        {
            var literal = new StringBuilder("\"");

            foreach (var c in value ?? "")
            {
                switch (c)
                {
                    case '"': literal.Append("\\\""); break;
                    case '\\': literal.Append(@"\\"); break;
                    case '\n': literal.Append(@"\n"); break;
                    case '\r': literal.Append(@"\r"); break;
                    case '\t': literal.Append(@"\t"); break;
                    case '?': literal.Append(@"\?"); break;

                    default:
                        if (c < 0x20)
                            literal.Append(@"\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                        else
                            literal.Append(c);

                        break;
                }
            }

            return literal.Append('"').ToString();
        }

        // ------------------------------------------------------------- helpers

        // The naming, in one place. Doom and Quake's conventions: a type is PascalCase with
        // a `_t` suffix, a function is a subsystem prefix and then PascalCase, a member is
        // snake_case and a constant is SCREAMING_SNAKE. The prefix is what stands in for
        // the namespace C does not have.

        private string EnumName(Models.Enum enumm) => $"{Prefix}_{enumm.Name.ToPascalCase()}_t";

        private string RecordName(Table table) => $"{Prefix}_{table.Name.ToPascalCase()}Record_t";

        private string TableTypeName(Table table) => $"{Prefix}_{table.Name.ToPascalCase()}Table_t";

        /// <summary>
        /// What a table's functions are called, minus the verb.
        ///
        /// `SheetManData_Item`, so the template appends `Load`, `Free` or `Find` and gets
        /// `SheetManData_ItemLoad` - one underscore, at the subsystem boundary, as in
        /// `P_SpawnMobj`.
        /// </summary>
        private string FunctionPrefix(Table table) => $"{Prefix}_{table.Name.ToPascalCase()}";

        private string ConstantName(params string[] parts)
            => (UpperPrefix + "_" + string.Join("_", parts.Select(p => p.ToSnakeCase())))
               .ToUpperInvariant();

        /// <summary>A member name, snake_case as Doom writes them.</summary>
        private static string CName(string name) => LanguageProfile.C.MemberName(name.ToSnakeCase());

    }
}
