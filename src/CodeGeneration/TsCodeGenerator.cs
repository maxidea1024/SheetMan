using SheetMan.Recipe;
using SheetMan.Models;
using SheetMan.Targets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;
using SheetMan.Extensions;
using SheetMan.Helpers;

// `using System` brings System.ValueType into scope, which collides with the
// model's own ValueType that this file refers to unqualified throughout.
using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Emits a TypeScript module per entity, plus a barrel index and the binary reader.
    ///
    /// A module per entity rather than one file, unlike the C# and C++ generators:
    /// TypeScript has a module system, so the imports between generated files are the
    /// language's job rather than the reader's.
    ///
    /// The shapes live in templates/ts-*.sbn. This file works out the values they need -
    /// type names, read calls, the JSON conversions - and nothing else.
    /// </summary>
    [SheetManTarget("typescript", TargetKind.CodeGeneration, Section = "CodeGenerations.Typescript", Order = 30)]
    public class TsCodeGenerator : CodeGenerator<RecipeModel.CodeGenerationRecipeGroup.TypescriptRecipe>
    {
        private Model _model;
        private RecipeModel.CodeGenerationRecipeGroup.TypescriptRecipe _typescriptRecipe;

        protected override void Run(TargetContext context, RecipeModel.CodeGenerationRecipeGroup.TypescriptRecipe typescriptRecipe)
        {
            // A blank path means the entry is inert, as it is in the skeleton recipe.
            // Without this the index and the reader land in the working directory - the
            // same defect the C# target had, and for the same reason: an empty first
            // component makes Path.Combine hand back a relative path rather than nothing.
            if (string.IsNullOrEmpty(typescriptRecipe.Path))
                return;

            SweepStaleOutput(typescriptRecipe.Path, typescriptRecipe.Sweep);

            _typescriptRecipe = typescriptRecipe;

            // Already narrowed to the side this entry is built for. Both (the default)
            // leaves the model unchanged.
            _model = context.Model;

            GenerateModel();
        }

        private void GenerateModel()
        {
            GenerateIndexTs();

            if (_model.Enums.Count > 0)
            {
                foreach (var enumm in _model.Enums)
                    Write($"enums/{enumm.Name}.ts", "ts-enum.sbn", BuildEnum(enumm));
            }

            if (_model.Tables.Count > 0)
            {
                foreach (var table in _model.Tables)
                    Write($"tables/{table.Name}.ts", "ts-table.sbn", BuildTable(table));

                Write("Tables.ts", "ts-tables-set.sbn", new TsTableSetView
                {
                    Tables = _model.Tables.Select(table => new TsTableSlotView
                    {
                        Member = TsName(table.Name),
                        Name = table.Name,
                    }).ToList(),
                });

                Write("Updater.ts", "ts-updater.sbn", new TsUpdaterView());
            }

            if (_model.ConstantSets.Count > 0)
            {
                foreach (var constantSet in _model.ConstantSets)
                    Write($"constants/{constantSet.Name}.ts", "ts-constants.sbn", BuildConstantSet(constantSet));
            }

            WriteBinaryReaderRuntime();
        }

        private void GenerateIndexTs()
        {
            string ns = _typescriptRecipe.Namespace;

            Write("index.ts", "ts-index.sbn", new TsIndexView
            {
                NamespaceOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns}\n{{",
                NamespaceClose = string.IsNullOrEmpty(ns) ? "" : "}",
                EnumNames = _model.Enums.Select(x => x.Name).ToList(),
                TableNames = _model.Tables.Select(x => x.Name).ToList(),
                ConstantSetNames = _model.ConstantSets.Select(x => x.Name).ToList(),
            });
        }

        /// <summary>
        /// Writes the LiteBinary reader into the output, beside the generated modules.
        ///
        /// Emitted rather than left for the consumer to copy: the generated tables import
        /// it by a relative path, and TypeScript has no include-path setting that would
        /// let a project point somewhere else. Shipping it makes the output directory
        /// self-contained.
        ///
        /// The source is an embedded resource taken from lib/ts, so there is one copy to
        /// maintain and it cannot drift from what is shipped.
        /// </summary>
        private void WriteBinaryReaderRuntime()
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Ts.lite_binary_reader.ts",
                GetTsFilename("sheetman/lite_binary_reader.ts"));
        }

        // --------------------------------------------------------------- view

        private TsEnumView BuildEnum(Models.Enum enumm) => new TsEnumView
        {
            Name = enumm.Name,
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            Labels = enumm.Labels.Select((label, index) => new TsEnumLabelView
            {
                Name = label.Name,

                // A string enum reads better in a debugger and survives a JSON round trip
                // as itself; a numeric one matches what the binary carries.
                Value = _typescriptRecipe.UseStringEnum
                    ? $"'{label.Name}'"
                    : label.Value.ToString(CultureInfo.InvariantCulture),

                Comment = CommentLines(label.Comment),
                IsLast = index == enumm.Labels.Count - 1,
            }).ToList(),
        };

        private TsConstantSetView BuildConstantSet(ConstantSet constantSet) => new TsConstantSetView
        {
            Name = constantSet.Name.ToPascalCase(),
            Location = constantSet.Location.ToString(),
            Comment = CommentLines(constantSet.Comment),

            Imports = constantSet.Constants
                                 .Where(c => c.Type == ValueType.Enum)
                                 .Select(c => $"import {{ {c.Enum.Name} }} from '../enums/{c.Enum.Name}'")
                                 .Distinct()
                                 .ToList(),

            Constants = constantSet.Constants.Select(constant => new TsConstantView
            {
                Name = TsName(constant.Name),
                Type = ToTypescriptTypename(constant.Type, constant.Enum, null),
                Value = RenderConstantValue(constant),
                Comment = CommentLines(constant.Comment),
            }).ToList(),
        };

        private TsTableView BuildTable(Models.Table table)
        {
            var fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList();

            return new TsTableView
            {
                Name = table.Name,
                Location = table.Location.ToString(),
                Comment = CommentLines(table.Comment),
                Imports = BuildImports(table),
                Fields = fields,

                IndexedFields = table.SerialFields
                                     .Select((sf, i) => new { sf, view = fields[i] })
                                     .Where(x => x.sf.IsIndexer)
                                     .Select(x => x.view)
                                     .ToList(),

                ReferenceFields = table.SerialFields
                                       .Select((sf, i) => new { sf, view = fields[i] })
                                       .Where(x => x.sf.IsRef)
                                       .Select(x => x.view)
                                       .ToList(),
            };
        }

        /// <summary>
        /// The imports this module needs for the types it names.
        ///
        /// The record branch used to be missing, so a module referring to another table's
        /// record named a type it never pulled in and did not compile - the enum branch had
        /// always been the only one.
        /// </summary>
        private IReadOnlyList<string> BuildImports(Models.Table table)
        {
            var imports = new List<string>();

            foreach (var sf in table.SerialFields)
            {
                if (sf.ElementType == ValueType.Enum)
                {
                    Add($"import {{ {sf.FirstField.Enum.Name} }} from '../enums/{sf.FirstField.Enum.Name}'");
                }
                else if (sf.ElementType == ValueType.ForeignRecord)
                {
                    // Resolved rather than declared table name: the declared one is the raw
                    // detail-type text, while resolution has already followed the reference
                    // chain to the table actually being pointed at.
                    var refTable = sf.FirstField.ResolvedRefTable;

                    if (refTable != null && refTable.Name != table.Name)
                        Add($"import {{ {refTable.Name.ToPascalCase()}Record }} from './{refTable.Name}'");
                }
            }

            return imports;

            void Add(string statement)
            {
                if (!imports.Contains(statement))
                    imports.Add(statement);
            }
        }

        private TsFieldView BuildField(Table table, SerialField sf)
        {
            string prop = TsName(sf.Name);
            string field = "_" + prop;
            string fieldType = ToTypescriptTypename(sf.FirstField);

            return new TsFieldView
            {
                Comment = CommentLines(sf.FirstField.Comment),
                PropName = prop,
                FieldName = field,
                PascalName = sf.Name.ToPascalCase(),
                DefaultValue = DefaultValue(sf),
                FieldType = fieldType,
                JsonWireType = JsonWireTypeOf(sf),
                ElementCount = sf.Fields.Count,
                RefTable = sf.FirstField.RefTableName.ToPascalCase(),
                Kind = DeclarationKind(sf),
                IsArray = sf.IsArray,

                ReferenceSetterType = sf.ElementType == ValueType.ForeignRecord
                    ? sf.FirstField.RefTableName.ToPascalCase() + "Record"
                    : fieldType,

                ReferenceIsRecord = sf.ElementType == ValueType.ForeignRecord,

                FromNamedRow = NamedRowAssignment(sf, field, prop),
                FromCompactRow = CompactRowStatements(sf, field, prop),
                BinaryRead = BinaryReadExpression(sf),
                Tag = sf.FirstField.Tag.Value,
                ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
            };
        }

        /// <summary>
        /// An empty value of the member's own type, for the declaration to start at.
        /// </summary>
        /// <remarks>
        /// A column the file does not carry leaves its member at whatever the declaration
        /// gave it, and that is not a hypothetical: delete a column and every build made
        /// before the deletion reads files that have nothing for it. An empty string is a
        /// value a consumer can use; `undefined` is a crash one field later.
        /// </remarks>
        private string DefaultValue(SerialField sf)
        {
            // A reference stays undefined: the absence of a referenced row is what that
            // means here, and there is nothing to put in its place.
            if (sf.IsRef)
                return "undefined";

            switch (sf.ElementType)
            {
                case ValueType.String: return "''";
                case ValueType.Bool: return "false";

                // A bigint literal, because a number does not assign to one.
                case ValueType.Int64: return "0n";

                // Both travel as ticks and are exposed as a decimal string, and a uuid as
                // its canonical text form.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                case ValueType.Uuid: return "''";

                // A numeric enum, so its zero is a value whether or not a label names it.
                case ValueType.Enum: return $"0 as {sf.FirstField.Enum.Name}";

                default: return "0";
            }
        }

        private static string DeclarationKind(SerialField sf)
        {
            if (sf.IsArray)
            {
                if (sf.IsRef)
                    return "array_ref";

                return sf.IsVariableLengthArray ? "var_array" : "array";
            }

            return sf.IsRef ? "scalar_ref" : "scalar";
        }

        /// <summary>
        /// The assignment reading one field out of a named JSON row.
        /// </summary>
        private string NamedRowAssignment(SerialField sf, string field, string prop)
        {
            if (!NeedsJsonConversion(sf))
            {
                // Array or scalar alike: a value the JSON carries as-is is assigned
                // straight through.
                return $"this.{field} = dataRow.{prop}";
            }

            if (sf.IsArray)
                return $"this.{field} = dataRow.{prop}.map(v => {FromJsonExpression(sf, "v")})";

            return $"this.{field} = {FromJsonExpression(sf, $"dataRow.{prop}")}";
        }

        /// <summary>
        /// The statements reading one field out of a compact JSON row.
        ///
        /// The compact row is flat: a serial field contributes one entry per column,
        /// matching how the binary exporter writes them. Reading a single entry for the
        /// whole group took only its first column and left every later field reading
        /// someone else's value.
        /// </summary>
        private IReadOnlyList<string> CompactRowStatements(SerialField sf, string field, string prop)
        {
            string convert = NeedsJsonConversion(sf)
                ? $".map(v => {FromJsonExpression(sf, "v")})"
                : "";

            if (sf.IsVariableLengthArray)
            {
                // One entry that already is an array, so it is taken whole. A serial field
                // is flattened across N entries and sliced below.
                return new[] { $"this.{field} = dataRow[offset++]{convert}" };
            }

            if (sf.IsArray)
            {
                return new[]
                {
                    $"this.{field} = dataRow.slice(offset, offset + {sf.Fields.Count}){convert}",
                    $"offset += {sf.Fields.Count}",
                };
            }

            if (NeedsJsonConversion(sf))
                return new[] { $"this.{field} = {FromJsonExpression(sf, "dataRow[offset++]")}" };

            return new[] { $"this.{field} = dataRow[offset++]" };
        }

        // ----------------------------------------------------------- rendering

        /// <summary>
        /// The call reading one value of a column's element type.
        /// </summary>
        /// <summary>
        /// The rendered checkColumn call: kind, count, and the elements this member accepts -
        /// its own plus the lossless promotions, decided here at generation time.
        /// </summary>
        private static string ColumnCheck(SerialField sf, string tableName)
        {
            string kind = sf.IsVariableLengthArray
                ? "sheetman.KIND_VAR_ARRAY"
                : (sf.Fields.Count > 1 ? "sheetman.KIND_FIXED_ARRAY" : "sheetman.KIND_SCALAR");

            int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

            string accepted;

            if (sf.IsRef)
                accepted = "sheetman.ELEMENT_I32";
            else
            {
                switch (sf.ElementType)
                {
                    case ValueType.Int32:
                        accepted = "sheetman.ELEMENT_I32, sheetman.ELEMENT_VARINT"; break;
                    case ValueType.Int64:
                        accepted = "sheetman.ELEMENT_I64, sheetman.ELEMENT_I32, sheetman.ELEMENT_VARINT"; break;
                    case ValueType.Double:
                        accepted = "sheetman.ELEMENT_F64, sheetman.ELEMENT_F32, sheetman.ELEMENT_I32"; break;
                    case ValueType.Float: accepted = "sheetman.ELEMENT_F32"; break;
                    case ValueType.Bool: accepted = "sheetman.ELEMENT_BOOL"; break;
                    case ValueType.String: accepted = "sheetman.ELEMENT_STRING"; break;
                    case ValueType.Uuid: accepted = "sheetman.ELEMENT_UUID"; break;
                    case ValueType.Enum: accepted = "sheetman.ELEMENT_VARINT"; break;

                    // Ticks are exact i64: reading an int as a datetime would be lossless
                    // and semantically wrong, so no promotion.
                    case ValueType.DateTime:
                    case ValueType.TimeSpan:
                        accepted = "sheetman.ELEMENT_I64"; break;

                    default:
                        throw new SheetManException($"The typescript generator cannot check type `{sf.Type}`.");
                }
            }

            return $"sheetman.checkColumn(column, '{tableName}.{sf.Name}', {kind}, {count}, [{accepted}])";
        }

        private string BinaryReadExpression(SerialField sf)
        {
            switch (sf.ElementType)
            {

                // Enum values travel zig-zag encoded rather than fixed width.
                case ValueType.Enum: return $"reader.readEnum() as {ToTypescriptTypename(sf.FirstField)}";

                case ValueType.ForeignRecord: return "reader.readInt32()";

                // Everything else is a plain call named in the profile, which is where the
                // nine of them live now rather than here and in eight other generators.
                default: return LanguageProfile.Typescript.ReadCall(sf.ElementType);
            }
        }

        /// <summary>
        /// The type a value has in the JSON export, which is not always the type the
        /// generated member exposes.
        /// </summary>
        private string JsonWireTypeOf(SerialField sf)
        {
            // A 64-bit integer is exported as a string, because JSON's single numeric
            // type is a double and would round it.
            if (sf.ElementType == ValueType.Int64)
                return "string";

            return ToTypescriptTypename(sf.FirstField);
        }

        /// <summary>
        /// Wraps a value read from JSON so it becomes the member's type.
        ///
        /// Two types need it. A 64-bit integer arrives as a string and is reconstructed
        /// exactly. A float arrives as the shortest decimal that round-trips it, which in
        /// JavaScript widens to a double a hair away from the stored 32-bit value - so it
        /// is rounded back to float precision, and both read paths then agree.
        /// </summary>
        private string FromJsonExpression(SerialField sf, string source)
        {
            switch (sf.ElementType)
            {
                case ValueType.Int64: return $"BigInt({source})";
                case ValueType.Float: return $"Math.fround({source})";
                default: return source;
            }
        }

        /// <summary>
        /// Whether values of this column need converting on the way in from JSON.
        /// </summary>
        private bool NeedsJsonConversion(SerialField sf)
            => sf.ElementType == ValueType.Int64 || sf.ElementType == ValueType.Float;

        /// <summary>
        /// Renders a cooked constant value as a TypeScript literal.
        ///
        /// Types that TypeScript has no native equivalent for - datetime, timespan and
        /// uuid - are surfaced as strings, matching ToTypescriptTypename.
        /// </summary>
        private string RenderConstantValue(ConstantSet.Constant constant)
        {
            switch (constant.Type)
            {
                case ValueType.String:
                    return $"'{EscapeTypescriptString((string)constant.Value)}'";

                case ValueType.Bool:
                    return (bool)constant.Value ? "true" : "false";

                case ValueType.Int32:
                    return ((int)constant.Value).ToString(CultureInfo.InvariantCulture);

                case ValueType.Int64:
                    // `n` suffix: a bigint-typed member cannot be initialized from a
                    // number literal, and TypeScript rejects it outright.
                    return ((long)constant.Value).ToString(CultureInfo.InvariantCulture) + "n";

                case ValueType.Float:
                    return ((float)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                case ValueType.Double:
                    return ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                case ValueType.DateTime:
                    return $"'{((DateTime)constant.Value).ToString("o", CultureInfo.InvariantCulture)}'";

                case ValueType.TimeSpan:
                    return $"'{((TimeSpan)constant.Value).ToString(null, CultureInfo.InvariantCulture)}'";

                case ValueType.Uuid:
                    return $"'{(Guid)constant.Value}'";

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                    return $"{constant.Enum.Name}.{label.Name}";
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the TypeScript generator cannot render.");
            }
        }

        private string EscapeTypescriptString(string input)
        {
            var literal = new StringBuilder(input.Length + 2);

            foreach (var c in input)
            {
                switch (c)
                {
                    case '\'': literal.Append("\\\'"); break;
                    case '\\': literal.Append(@"\\"); break;
                    case '\0': literal.Append(@"\0"); break;
                    case '\b': literal.Append(@"\b"); break;
                    case '\f': literal.Append(@"\f"); break;
                    case '\n': literal.Append(@"\n"); break;
                    case '\r': literal.Append(@"\r"); break;
                    case '\t': literal.Append(@"\t"); break;
                    case '\v': literal.Append(@"\v"); break;
                    default:
                        if (c >= 0x20 && c <= 0x7e)
                            literal.Append(c);
                        else
                            literal.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        break;
                }
            }

            return literal.ToString();
        }

        // ------------------------------------------------------------- types

        /// <summary>
        /// A member name.
        ///
        /// camelCase, then escaped if TypeScript will not take it. Most reserved words are
        /// legal as member names, so only the few that genuinely are not get renamed -
        /// `constructor` above all, which a class may not declare as an accessor.
        /// </summary>
        private static string TsName(string name) => LanguageProfile.Typescript.MemberName(name.ToCamelCase());

        private string ToTypescriptTypename(Field field, bool asArray = false)
        {
            // ElementType, not Type: an array field is rendered by naming its element
            // and letting the caller add the brackets, exactly as a serial field is.
            return ToTypescriptTypename(field.ElementType, field.EnumOrNull, field.RefTableName, asArray);
        }

        private string ToTypescriptTypename(Models.ValueType type, Models.Enum enumm, string refTableName, bool asArray = false)
        {
            string result;
            switch (type)
            {
                // The two that name something from the model rather than the language.
                // Why int64 is bigint, and why the three text-shaped types are string, is
                // recorded on the profile itself.
                case ValueType.Enum:
                    result = enumm.Name.ToPascalCase();
                    break;

                case ValueType.ForeignRecord:
                    result = $"{refTableName.ToPascalCase()}Record";
                    break;

                default:
                    result = LanguageProfile.Typescript.ScalarTypeName(type);
                    break;
            }

            return asArray ? LanguageProfile.Typescript.ArrayOf(result) : result;
        }

        // ----------------------------------------------------------- helpers

        /// <summary>
        /// A comment as the doc-comment lines the templates emit verbatim.
        ///
        /// Rendered here rather than in the template because the wrapping is not a simple
        /// per-line prefix: a comment of one line becomes `/** text * /` on that line, and a
        /// longer one is run together, which is what the printer did.
        /// </summary>
        // `new`, and not the base one: TypeScript wraps the whole comment in `/** */`
        // and runs its lines together, which is a different answer rather than the same
        // one spelled differently.
        private static new IReadOnlyList<string> CommentLines(string comment)
        {
            if (string.IsNullOrEmpty(comment))
                return Array.Empty<string>();

            var text = new StringBuilder("/** ");

            if (comment.Count(c => c == '\n') <= 1)
            {
                text.Append(comment.Replace("\n", ""));
            }
            else
            {
                var lines = comment.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    bool last = i == lines.Length - 1;

                    if (lines[i].Length == 0 && !last)
                        text.Append('\n');
                    else if (lines[i].Length > 0 || !last)
                        text.Append(lines[i]);
                }
            }

            text.Append(" */");

            return text.ToString().Split('\n');
        }

        private string GetTsFilename(string name) => Path.Combine(_typescriptRecipe.Path, name);

        private void Write(string filename, string templateName, object view)
        {
            StagingFiles.WriteAllTextToFile(
                GetTsFilename(filename), TemplateEngine.Render(templateName, view));
        }
    }
}
