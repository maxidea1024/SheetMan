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
using SheetMan.Helpers;
using SheetMan.Extensions;

namespace SheetMan.CodeGeneration;

/// <summary>
/// Emits a single self-contained C# file per recipe entry, plus the binary reader.
///
/// The file's shape lives in templates/csharp.sbn. This file works out the values that
/// shape needs - type names, read calls, rendered literals - and nothing else.
///
/// The pieces that are the same in every output - the exception type, the collection
/// and ToString helpers, the reader delegates - are template partials. They used to be
/// verbatim string constants in a Snippets file, indented at run time by the printer's
/// scope, which is why they carried their own escaping for `$` and `"`.
/// </summary>
[SheetManTarget("csharp", TargetKind.CodeGeneration, Section = "CodeGenerations.CSharp", Order = 20)]
public class CsCodeGenerator : CodeGenerator<RecipeModel.CodeGenerationRecipeGroup.CSharpRecipe>
{
    private Model _model;
    private RecipeModel.CodeGenerationRecipeGroup.CSharpRecipe _csharpReceipe;

    protected override void Run(TargetContext context, RecipeModel.CodeGenerationRecipeGroup.CSharpRecipe csharpRecipe)
    {
        // A blank path means the entry is inert - which is what every list in the
        // skeleton recipe holds. Without this, `Path.Combine("", "GameData.cs")` is a
        // relative path, so the accessor and the reader land in the working directory:
        // two files in the repository root that got committed before anyone noticed,
        // because the run succeeded.
        if (string.IsNullOrEmpty(csharpRecipe.Path))
            return;

        SweepStaleOutput(csharpRecipe.Path, csharpRecipe.Sweep);

        _csharpReceipe = csharpRecipe;

        // Already narrowed to the side this entry is built for. Both (the default)
        // leaves the model unchanged.
        _model = context.Model;

        GenerateModel();
        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// Writes SheetMan's binary reader beside the generated accessor.
    ///
    /// Emitted rather than installed. The generated code used to reference a runtime
    /// that a Unity project had to carry as a plugin - 3,600 lines of read and write
    /// machinery, of which the generated code called four members - so a consumer had
    /// setup to do before the output would compile. The C++ and TypeScript outputs
    /// already ship their own reader; this brings C# in line.
    ///
    /// The source is an embedded resource taken from lib/cs, so there is one copy to
    /// maintain and it cannot drift from what is shipped.
    /// </summary>
    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "SheetMan.Runtime.Cs.ScbReader.cs",
            Path.Combine(_csharpReceipe.Path, "sheetman", "SheetManBinaryReader.cs"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // project that ships its data inside the build.
        if (_csharpReceipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Cs.SheetManUpdater.cs",
                Path.Combine(_csharpReceipe.Path, "sheetman", "SheetManUpdater.cs"));
        }
    }

    /// <summary>
    /// Writes a file per table, per enum and per constant set, plus the accessor and the
    /// helpers.
    /// </summary>
    /// <remarks>
    /// It used to be one file holding all of it, which made a deleted table a hunk of dead
    /// code inside a file that still compiled - and a diff of a generated file show the
    /// helpers as changed every time a table was added, because they moved down the page.
    ///
    /// The layout is the one the TypeScript target has always had, because a consumer
    /// working in two languages should not have to learn two shapes.
    /// </remarks>
    private void GenerateModel()
    {
        var view = BuildView();

        Log.Information($"Generating codes for CSharp into `{Path.GetFullPath(_csharpReceipe.Path)}`");

        Write(_csharpReceipe.AccessorName + ".cs", "csharp-accessor.sbn", view);
        Write(Path.Combine("sheetman", "SheetManHelpers.cs"), "csharp-helpers.sbn", Part());

        foreach (var table in view.Tables)
            Write(Path.Combine("tables", table.Name + "Table.cs"), "csharp-table.sbn", Part(table: table));

        foreach (var enumm in view.Enums)
            Write(Path.Combine("enums", enumm.Name + ".cs"), "csharp-enum.sbn", Part(enumm: enumm));

        foreach (var set in view.ConstantSets)
            Write(Path.Combine("constants", set.Name + ".cs"), "csharp-constants.sbn", Part(set: set));
    }

    /// <summary>A view for one of the single-subject templates.</summary>
    private CsPartView Part(
        CsTableView table = null, CsEnumView enumm = null, CsConstantSetView set = null)
        => new CsPartView
        {
            Namespace = _csharpReceipe.Namespace,
            Table = table,
            Enumm = enumm,
            Set = set,
        };

    private void Write(string relative, string templateName, object view)
    {
        string filename = Path.GetFullPath(Path.Combine(_csharpReceipe.Path, relative));

        StagingFiles.WriteAllTextToFile(filename, Outdent(TemplateEngine.Render(templateName, view)));
    }

    /// <summary>
    /// Takes one level of indentation back off when there is no namespace to sit inside.
    ///
    /// The template's indentation is literal and written for the nested case, which is
    /// the normal one. The printer this replaced got its indentation from a scope stack,
    /// so it handled both without anything like this.
    /// </summary>
    private string Outdent(string rendered)
    {
        if (!string.IsNullOrEmpty(_csharpReceipe.Namespace))
            return rendered;

        var result = new StringBuilder(rendered.Length);

        foreach (var line in rendered.Split('\n'))
        {
            result.Append(line.StartsWith("    ", StringComparison.Ordinal) ? line.Substring(4) : line);
            result.Append('\n');
        }

        // Split on the final newline yields one empty segment, which the loop above has
        // already given a newline of its own, so the trailing blank line survives.
        return result.ToString(0, result.Length - 1);
    }

    // --------------------------------------------------------------- view

    private CsFileView BuildView()
    {
        var tables = _model.Tables.Select(BuildTable).ToList();

        return new CsFileView
        {
            Namespace = _csharpReceipe.Namespace ?? "",
            FileExtension = _csharpReceipe.BinaryTableFileExtension,
            Tables = tables,
            TablesWithReferences = tables.Where(t => t.ReferenceFields.Count > 0).ToList(),
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        };
    }

    private CsTableView BuildTable(Table table)
    {
        var fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList();

        return new CsTableView
        {
            Name = table.Name.ToPascalCase(),
            RawName = table.Name,
            Comment = CommentLines(table.Comment),
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

            // One scratch int for the whole method rather than one per field: the reader
            // hands back an int and an enum field needs a cast through something.
            NeedsEnumTemp = table.SerialFields.Any(sf => sf.ElementType == Models.ValueType.Enum),

            // Pascal-casing a folded group's name gives the property it is exposed under
            // - `TextEn_array` becomes `TextEnArray` - so these literals name the very
            // members BuildObjectValueMap reads.
            FieldNameLiterals = string.Join(", ", table.SerialFields.Select(sf => $"\"{sf.Name.ToPascalCase()}\"")),
            FieldValueExpressions = string.Join(", ", table.SerialFields.Select(sf => "r." + sf.Name.ToPascalCase())),
        };
    }

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// The name of that index is not fixed - only its type is - so this reads it off the
    /// table being pointed at. A non-reference field has no table to read, and the value
    /// is never rendered for one.
    /// </remarks>
    private static string PrimaryLookup(Table refTable)
        => refTable is null
            ? ""
            : "GetBy" + refTable.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase() + "OrThrow";

    private CsFieldView BuildField(Table table, SerialField sf)
    {
        string fieldType = ToCSharpTypeName(sf.FirstField);
        string fieldName = "_" + sf.Name.ToCamelCase();
        string refTable = sf.FirstField.RefTableName.ToPascalCase();

        return new CsFieldView
        {
            Comment = CommentLines(sf.FirstField.Comment),
            PropName = sf.Name.ToPascalCase(),
            FieldName = fieldName,
            FieldType = fieldType,
            Initializer = Initializer(sf),
            ElementCount = sf.Fields.Count,
            RefTable = refTable,
            RefLookup = PrimaryLookup(sf.FirstField.ResolvedRefTable),
            RefField = sf.FirstField.RefFieldName.ToPascalCase(),
            Kind = DeclarationKind(sf),
            ReadKind = ReadKind(sf),
            Tag = sf.FirstField.Tag.Value,
            ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
            ElementRead = ElementReadLines(sf, fieldName, fieldType, refTable),

            // A reference to a whole row is assigned the target record; one that names a
            // field is assigned that field's value, which is the field's own type.
            ReferenceSetterType = sf.ElementType == Models.ValueType.ForeignRecord
                ? refTable + "Table.Record"
                : fieldType,

            ReferencesField = !string.IsNullOrEmpty(sf.FirstField.RefFieldName),
        };
    }

    /// <summary>
    /// Which member-declaration shape a field takes.
    ///
    /// A variable-length array declares no `_N` constant: its length differs per row, so
    /// there is no element count to expose, and the array is allocated by the read path
    /// once it knows how long this row's is.
    /// </summary>
    /// <summary>
    /// What a member is initialized to, as the text that follows its declaration -
    /// nothing at all where C#'s own default is already an empty value.
    /// </summary>
    /// <remarks>
    /// A column the file does not carry leaves its member at its default, and that is
    /// not a hypothetical: delete a column and every build made before the deletion
    /// reads files with nothing for it. Every type here zero-initializes to something
    /// usable except a string, which starts null - and a null string is a crash one
    /// field later rather than an empty one.
    ///
    /// A reference is left alone: the absence of a referenced row is what null means
    /// here, and there is nothing to put in its place.
    /// </remarks>
    private static string Initializer(SerialField sf)
        => !sf.IsRef && sf.ElementType == Models.ValueType.String ? " = \"\"" : "";

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

    private static string ReadKind(SerialField sf)
    {
        if (!sf.IsArray)
            return "scalar";

        return sf.IsVariableLengthArray ? "var_array" : "serial";
    }

    /// <summary>
    /// The lines that read one element, whether the template places them in a loop or
    /// straight into the method body.
    /// </summary>
    /// <summary>
    /// The rendered CheckColumn call for one field: its kind, its count, and every wire
    /// element this member reads - its own plus the lossless promotions.
    /// </summary>
    /// <remarks>
    /// The accepted list is decided here, at generation time, so the runtime carries no
    /// table of what-converts-to-what: an int member says it takes i32 and varint, a
    /// double member says f64, f32 and i32, and everything else is exact. Anything not
    /// listed is refused by name before a byte of the block is read.
    /// </remarks>
    private static string ColumnCheck(SerialField sf, string tableName)
    {
        string kind = sf.IsVariableLengthArray
            ? "ScbTable.KindVarArray"
            : (sf.Fields.Count > 1 ? "ScbTable.KindFixedArray" : "ScbTable.KindScalar");

        int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

        string accepted;

        if (sf.IsRef)
            accepted = "ScbTable.ElementI32";
        else
        {
            switch (sf.ElementType)
            {
                case Models.ValueType.Int32:
                    accepted = "ScbTable.ElementI32, ScbTable.ElementVarint";
                    break;
                case Models.ValueType.Int64:
                    accepted = "ScbTable.ElementI64, ScbTable.ElementI32, ScbTable.ElementVarint";
                    break;
                case Models.ValueType.Double:
                    accepted = "ScbTable.ElementF64, ScbTable.ElementF32, ScbTable.ElementI32";
                    break;
                case Models.ValueType.Float: accepted = "ScbTable.ElementF32"; break;
                case Models.ValueType.Bool: accepted = "ScbTable.ElementBool"; break;
                case Models.ValueType.String: accepted = "ScbTable.ElementString"; break;
                case Models.ValueType.Uuid: accepted = "ScbTable.ElementUuid"; break;
                case Models.ValueType.Enum: accepted = "ScbTable.ElementVarint"; break;

                // Ticks are exact i64: reading an int as a datetime would be numerically
                // lossless and semantically wrong, so no promotion.
                case Models.ValueType.DateTime:
                case Models.ValueType.TimeSpan:
                    accepted = "ScbTable.ElementI64";
                    break;

                default:
                    throw new SheetManException($"The csharp generator cannot check type `{sf.Type}`.");
            }
        }

        return $"ScbTable.CheckColumn(column, \"{tableName}.{sf.Name}\", {kind}, {count}, {accepted});";
    }

    /// <summary>
    /// The lines that read one element into a record, inside the columnar fill loop.
    ///
    /// `record` is the row being filled and `column` the descriptor in scope; an array
    /// element is at `[j]`, the template's inner loop variable.
    /// </summary>
    private static IReadOnlyList<string> ElementReadLines(
        SerialField sf, string fieldName, string fieldType, string refTable)
    {
        string target = sf.IsArray ? $"record.{fieldName}[j]" : $"record.{fieldName}";
        string flag = sf.IsArray ? $"record.{fieldName}_F[j]" : $"record.{fieldName}_F";
        string index = sf.IsArray
            ? $"record.{fieldName}_{refTable}_index[j]"
            : $"record.{fieldName}_{refTable}_index";

        if (sf.ElementType == Models.ValueType.Enum)
        {
            // Enum values are zig-zag encoded, and the reader hands back an int.
            return new[]
            {
                "reader.ReadOptimalInt32(out tempEnumInt);",
                $"{target} = ({fieldType})tempEnumInt;",
            };
        }

        if (sf.IsRef)
        {
            // Only the stored index is on the wire; the value is filled in once every
            // table is loaded, and the flag records whether that happened.
            return new[]
            {
                $"reader.Read(out {index});",
                $"{target} = default({fieldType}); // will be assigned.",
                $"{flag} = false;",
            };
        }

        // The three promotable members read through the As-helpers, so a file written
        // before the column was widened still reads. Everything else is exact.
        switch (sf.ElementType)
        {
            case Models.ValueType.Int32:
                return new[] { $"{target} = reader.ReadI32As(column.Element);" };
            case Models.ValueType.Int64:
                return new[] { $"{target} = reader.ReadI64As(column.Element);" };
            case Models.ValueType.Double:
                return new[] { $"{target} = reader.ReadF64As(column.Element);" };
            default:
                return new[] { $"reader.Read(out {target});" };
        }
    }

    private CsEnumView BuildEnum(Models.Enum enumm) => new CsEnumView
    {
        Name = enumm.Name.ToPascalCase(),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select((label, index) => new CsEnumLabelView
        {
            Name = label.Name.ToPascalCase(),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            Comment = CommentLines(label.Comment),
            IsLast = index == enumm.Labels.Count - 1,
        }).ToList(),
    };

    private CsConstantSetView BuildConstantSet(ConstantSet constantSet) => new CsConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new CsConstantView
        {
            Name = constant.Name.ToPascalCase(),
            Type = ToCSharpTypeName(constant.Type, constant.Enum, null),
            Value = RenderConstantValue(constant.Type, constant.Enum, constant.Value, constant.Location),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    // ------------------------------------------------------------- types

    private string ToCSharpTypeName(Field field, bool asArray = false)
    {
        // ElementType, not Type: an array field is rendered by naming its element
        // and letting the caller add the brackets, exactly as a serial field is.
        return ToCSharpTypeName(field.ElementType, field.EnumOrNull, field.RefTableName, asArray);
    }

    private string ToCSharpTypeName(Models.ValueType type, Models.Enum enumm, string refTableName, bool asArray = false)
    {
        string result;
        switch (type)
        {
            // The two that name something from the model rather than the language.
            case Models.ValueType.Enum:
                result = QualifiedNamespacePrefix + enumm.Name.ToPascalCase();
                break;

            case Models.ValueType.ForeignRecord:
                result = $"{refTableName.ToPascalCase()}Table.Record";
                break;

            default:
                result = LanguageProfile.CSharp.ScalarTypeName(type);
                break;
        }

        return asArray ? LanguageProfile.CSharp.ArrayOf(result) : result;
    }

    private string RenderConstantValue(
        Models.ValueType valueType, Models.Enum enumm, object value, Location location)
    {
        switch (valueType)
        {
            case Models.ValueType.String:
                return $"\"{EscapeString((string)value)}\"";

            case Models.ValueType.Bool:
                return (bool)value ? "true" : "false";

            case Models.ValueType.Int32:
                return ((int)value).ToString(CultureInfo.InvariantCulture);

            case Models.ValueType.Int64:
                return ((long)value).ToString(CultureInfo.InvariantCulture);

            // Round-trip format, and invariant. The current culture would write a
            // comma for the decimal separator wherever the build machine uses one,
            // and `1,5f` is not a C# literal.
            case Models.ValueType.Float:
                return ((float)value).ToString("R", CultureInfo.InvariantCulture) + "f";

            case Models.ValueType.Double:
                return ((double)value).ToString("R", CultureInfo.InvariantCulture);

            // These three used to be written as their default ToString, which is not a
            // literal in any of the three cases - a constant of one of these types
            // produced a file that did not compile. Ticks and a uuid string are exact
            // and need no parsing at a culture's mercy.
            case Models.ValueType.TimeSpan:
                return $"new System.TimeSpan({((TimeSpan)value).Ticks.ToString(CultureInfo.InvariantCulture)}L)";

            case Models.ValueType.DateTime:
                return $"new System.DateTime({((DateTime)value).Ticks.ToString(CultureInfo.InvariantCulture)}L)";

            case Models.ValueType.Uuid:
                return $"new System.Guid(\"{(Guid)value}\")";

            case Models.ValueType.Enum:
            {
                var label = enumm.GetLabel(value, location);
                return $"{QualifiedNamespacePrefix}{enumm.Name.ToPascalCase()}.{label.Name.ToPascalCase()}";
            }

            default:
                throw new SheetManException(location, $"unsupported constant type `{valueType}`");
        }
    }

    private string EscapeString(string input)
    {
        var literal = new StringBuilder(input.Length + 2);

        foreach (var c in input)
        {
            switch (c)
            {
                case '\'': literal.Append("\\\'"); break;
                case '\\': literal.Append(@"\\"); break;
                case '\0': literal.Append(@"\0"); break;
                case '\a': literal.Append(@"\a"); break;
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

    // ----------------------------------------------------------- helpers

    /// <summary>
    /// Fully qualified so a generated reference to an enum cannot be captured by a type
    /// of the same name in the consumer's own namespace.
    /// </summary>
    private string QualifiedNamespacePrefix
        => string.IsNullOrEmpty(_csharpReceipe.Namespace)
            ? ""
            : "global::" + _csharpReceipe.Namespace + ".";

    /// <summary>
    /// A comment split into the lines the template will wrap in a doc comment. Empty
    /// when there is no comment, so the template needs no test of its own.
    /// </summary>
    // `new`, and not the base one: this tests IsNullOrEmpty rather than
    // IsNullOrWhiteSpace, so a comment of nothing but spaces reaches the template as
    // one blank line instead of none - and the golden pages record that.
    private static new IReadOnlyList<string> CommentLines(string comment)
    {
        if (string.IsNullOrEmpty(comment))
            return Array.Empty<string>();

        return comment.Replace("\r\n", "\n").Split('\n');
    }
}
