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

        /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
        public string TargetSide { get; set; } = "cs";
    }

    /// <summary>
    /// Emits a C header and source, plus the binary reader.
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
    /// The shapes live in templates/c-header.sbn and templates/c-source.sbn.
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

            _recipe = recipe;
            _model = context.Model;

            var view = BuildView();

            Write(view.HeaderName, "c-header.sbn", view);
            Write(FileBase + ".c", "c-source.sbn", view);

            WriteBinaryReaderRuntime();
        }

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

        private void Write(string filename, string templateName, CFileView view)
        {
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, filename));

            Log.Information($"Generating codes for C into `{full}`");

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

            // Flattened: C has nothing to nest a set in, so the set's name becomes part of
            // each constant's name rather than a scope around them.
            Constants = _model.ConstantSets
                              .SelectMany(set => set.Constants.Select(c => BuildConstant(set, c)))
                              .ToList(),

            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = BuildAccessor(),
        };

        private CEnumView BuildEnum(Models.Enum enumm) => new CEnumView
        {
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
        /// A reference contributes the index that came off the wire as well as the pointer
        /// it resolves to, and a variable length array contributes a count beside its
        /// pointer - C has nowhere else to keep either.
        /// </summary>
        private IReadOnlyList<string> Declarations(SerialField sf, string name)
        {
            string elementType = ResolvedElementType(sf);

            if (sf.IsRef)
            {
                // A pointer to const: the row belongs to the table it came from, and a
                // caller writing through this one would be editing that table's copy.
                return sf.IsArray
                    ? new[]
                    {
                        $"const {elementType}* {name}[{sf.Fields.Count}];",
                        $"int32_t {name}_index[{sf.Fields.Count}];",
                    }
                    : new[]
                    {
                        $"const {elementType}* {name};",
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
