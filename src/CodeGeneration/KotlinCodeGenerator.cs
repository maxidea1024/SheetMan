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
/// Settings for the Kotlin target.
///
/// Declared beside its generator and reached through the recipe's `Targets` list.
/// </summary>
public sealed class KotlinRecipe : IOutputRecipe
{
    /// <summary>Source root. The package's directories are created underneath it.</summary>
    public string Path { get; set; } = "";

    /// <summary>Package the generated file declares.</summary>
    public string PackageName { get; set; } = "gamedata";

    /// <summary>Name of the accessor object, which also names the file.</summary>
    public string AccessorName { get; set; } = "SheetManData";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".scb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    /// </summary>
    /// <remarks>
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a program can take new data without being redeployed. Off by
    /// default: one that ships its data alongside its code has no use for it, and this
    /// is the only generated file that reaches the network.
    /// </remarks>
    public bool WriteUpdater { get; set; } = false;

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
/// Emits one Kotlin file holding every generated type, plus the binary reader.
///
/// A Kotlin reader rather than the Java one, even though Kotlin would call it happily:
/// kotlinc reads Java sources for resolution but does not compile them, so a pure
/// Kotlin project would need javac in its build purely to get a reader.
///
/// The shape lives in templates/kotlin.sbn.
/// </summary>
[SheetManTarget("kotlin", TargetKind.CodeGeneration, Order = 85)]
public class KotlinCodeGenerator : CodeGenerator<KotlinRecipe>
{
    private Model _model;
    private KotlinRecipe _recipe;

    protected override void Run(TargetContext context, KotlinRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;

        Generate();
        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// Writes a file per table, per enum and per constant set, plus the accessor.
    /// </summary>
    /// <remarks>
    /// It used to be one file holding all of it, which made a deleted table a hunk of dead
    /// code inside a file that still compiled. The layout is the one the TypeScript and C#
    /// targets have, because a consumer working in more than one should not have to learn
    /// a shape per language.
    ///
    /// Kotlin has no rule tying a file's name to what is in it, so these names are for
    /// people rather than the compiler - which is why the table files keep the `Table`
    /// suffix their class has.
    /// </remarks>
    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Kotlin into `{System.IO.Path.GetFullPath(PackageDir)}`");

        Write(_recipe.AccessorName + ".kt", "kotlin-accessor.sbn", view);

        foreach (var table in view.Tables)
            Write(System.IO.Path.Combine("tables", table.TableName + ".kt"),
                  "kotlin-table.sbn", Part(table: table));

        foreach (var enumm in view.Enums)
            Write(System.IO.Path.Combine("enums", enumm.Name + ".kt"),
                  "kotlin-enum.sbn", Part(enumm: enumm));

        foreach (var set in view.ConstantSets)
            Write(System.IO.Path.Combine("constants", set.Name + ".kt"),
                  "kotlin-constants.sbn", Part(set: set));
    }

    /// <summary>
    /// The package's own directory, which the generated files live under.
    /// </summary>
    private string PackageDir => System.IO.Path.Combine(
        new[] { _recipe.Path }.Concat(_recipe.PackageName.Split('.')).ToArray());

    private KotlinPartView Part(
        KotlinTableView table = null, KotlinEnumView enumm = null, KotlinConstantSetView set = null)
        => new KotlinPartView
        {
            PackageName = _recipe.PackageName,
            Table = table,
            Enumm = enumm,
            Set = set,
        };

    private void Write(string relative, string templateName, object view)
    {
        string filename = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(PackageDir, relative));

        StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render(templateName, view));
    }

    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "SheetMan.Runtime.Kotlin.LiteBinaryReader.kt",
            System.IO.Path.Combine(_recipe.Path, "sheetman", "LiteBinaryReader.kt"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Kotlin.SheetManUpdater.kt",
                System.IO.Path.Combine(_recipe.Path, "sheetman", "SheetManUpdater.kt"));
        }
    }

    // --------------------------------------------------------------- view

    private KotlinFileView BuildView() => new KotlinFileView
    {
        PackageName = _recipe.PackageName,
        AccessorName = _recipe.AccessorName,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private KotlinEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new KotlinEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            DefaultLabel = ConstantName(fallback.Name),
            Labels = enumm.Labels.Select((label, index) => new KotlinEnumLabelView
            {
                Name = ConstantName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),

                // A Kotlin enum body needs a semicolon after the last constant when
                // anything follows it, and a companion object always does.
                Separator = index == enumm.Labels.Count - 1 ? ";" : ",",
            }).ToList(),
        };
    }

    private KotlinConstantSetView BuildConstantSet(ConstantSet constantSet) => new KotlinConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new KotlinConstantView
        {
            Name = ConstantName(constant.Name),
            Type = ToKotlinTypeName(constant.Type, constant.Enum, null),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private KotlinTableView BuildTable(Table table) => new KotlinTableView
    {
        RawName = table.Name,
        RecordName = table.Name.ToPascalCase() + "Record",
        TableName = table.Name.ToPascalCase() + "Table",
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<KotlinIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf => new KotlinIndexView
        {
            Member = KotlinName(sf.Name),
            Suffix = sf.Name.ToPascalCase(),
            KeyType = ResolvedElementType(sf),
            MapName = "by" + sf.Name.ToPascalCase(),
            FieldName = sf.Name.ToPascalCase(),
        }).ToList();

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `findByIndex`. The primary
    /// index is whatever the sheet put in the first column, and a sheet that calls it `Id`
    /// generates `findById`.
    /// </remarks>
    private static string PrimaryLookup(Table refTable)
        => "findBy" + refTable.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase();

    private KotlinFieldView BuildField(Table table, SerialField sf)
    {
        string name = KotlinName(sf.Name);

        return new KotlinFieldView
        {
            Comment = CommentLines(sf.FirstField.Comment),
            Name = name,
            Kind = ReadKind(sf),
            Tag = sf.FirstField.Tag.Value,
            ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
            ElementCount = sf.Fields.Count,
            Declarations = Declarations(sf, name),
            ReadScalar = ReadExpression(sf),
            ReadElement = ReadExpression(sf),
        };
    }

    /// <summary>
    /// The property declarations, each initialized.
    ///
    /// Initialized rather than `lateinit`, because Kotlin's null safety would otherwise
    /// turn a read of an unread record into a runtime failure where every other
    /// generated reader hands back a default.
    /// </summary>
    private IReadOnlyList<string> Declarations(SerialField sf, string name)
    {
        string elementType = ResolvedElementType(sf);

        if (sf.IsRef)
        {
            return sf.IsArray
                ? new[]
                {
                    $"var {name}: MutableList<{elementType}> = ArrayList()",
                    $"var {name}Index: MutableList<Int> = ArrayList()",
                }
                : new[]
                {
                    $"var {name}: {elementType}? = null",
                    $"var {name}Index: Int = 0",
                };
        }

        if (sf.IsArray)
            return new[] { $"var {name}: MutableList<{elementType}> = ArrayList()" };

        return new[] { $"var {name}: {elementType} = {DefaultValue(sf)}" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "false";
            case ValueType.Int64: return "0L";
            case ValueType.Float: return "0.0f";
            case ValueType.Double: return "0.0";
            case ValueType.DateTime:
            case ValueType.TimeSpan: return "0L";
            case ValueType.Uuid: return "Uuid()";
            case ValueType.Enum:
                return $"{sf.FirstField.Enum.Name.ToPascalCase()}.of(0)";
            default: return "0";
        }
    }

    /// <summary>
    /// The rendered checkColumn call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(SerialField sf, string tableName)
    {
        string kind = sf.IsVariableLengthArray
            ? "KIND_VAR_ARRAY"
            : (sf.Fields.Count > 1 ? "KIND_FIXED_ARRAY" : "KIND_SCALAR");

        int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

        string accepted;

        if (sf.IsRef)
            accepted = "ELEMENT_I32";
        else
        {
            switch (sf.ElementType)
            {
                case ValueType.Int32: accepted = "ELEMENT_I32, ELEMENT_VARINT"; break;
                case ValueType.Int64: accepted = "ELEMENT_I64, ELEMENT_I32, ELEMENT_VARINT"; break;
                case ValueType.Double: accepted = "ELEMENT_F64, ELEMENT_F32, ELEMENT_I32"; break;
                case ValueType.Float: accepted = "ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "ELEMENT_I64"; break;

                default:
                    throw new SheetManException($"The kotlin generator cannot check type `{sf.Type}`.");
            }
        }

        return $"checkColumn(column, \"{tableName}.{sf.Name}\", {kind}, {count}, {accepted})";
    }

    private static string ReadKind(SerialField sf)
    {
        if (sf.IsVariableLengthArray)
            return "var_array";

        if (sf.IsArray)
            return sf.IsRef ? "serial_ref" : "serial";

        return sf.IsRef ? "scalar_ref" : "scalar";
    }

    private KotlinAccessorView BuildAccessor() => new KotlinAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new KotlinTableSlotView
        {
            Name = KotlinName(table.Name),
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
            .Select(x => new KotlinCrossReferenceView
            {
                Table = KotlinName(x.Table.Name),
                Fields = x.Fields.Select(sf => new KotlinReferenceFieldView
                {
                    Name = KotlinName(sf.Name),
                    RefTable = KotlinName(sf.FirstField.ResolvedRefTable.Name),
                    RefLookup = PrimaryLookup(sf.FirstField.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + KotlinName(sf.FirstField.ResolvedRefField.Name),
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

            // Enum values travel zig-zag encoded rather than fixed width.
            case ValueType.Enum:
                return $"{sf.FirstField.Enum.Name.ToPascalCase()}.of(reader.readEnum())";

            case ValueType.ForeignRecord: return "reader.readInt32()";

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in nine other generators.
            default: return LanguageProfile.Kotlin.ReadCall(sf.ElementType);
        }
    }

    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return sf.FirstField.ResolvedRefTable.Name.ToPascalCase() + "Record";

        return ToKotlinTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull, null);
    }

    private string ToKotlinTypeName(ValueType type, Models.Enum enumm, string refTableName)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return enumm.Name.ToPascalCase();

            case ValueType.ForeignRecord:
                return refTableName.ToPascalCase() + "Record";

            default:
                return LanguageProfile.Kotlin.ScalarTypeName(type);
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
                return ((long)constant.Value).ToString(CultureInfo.InvariantCulture) + "L";

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
                return "Uuid(byteArrayOf(" + string.Join(", ",
                    ((Guid)constant.Value).ToByteArray()
                        .Select(b => b.ToString(CultureInfo.InvariantCulture) + ".toByte()")) + "))";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}.{ConstantName(label.Name)}";
            }

            default:
                throw new SheetManException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the kotlin generator cannot render.");
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
            else if (c == '$')
                // A dollar starts a template expression in a Kotlin string.
                literal.Append(@"\$");
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
    /// A property name.
    ///
    /// camelCase, and escaped with backticks when it lands on a keyword - which Kotlin
    /// accepts for exactly this, unlike Java, where the name has to change instead.
    /// </summary>
    private static string KotlinName(string name) => LanguageProfile.Kotlin.MemberName(name.ToCamelCase());

    /// <summary>An enum constant, SCREAMING_SNAKE_CASE as Kotlin writes them.</summary>
    private static string ConstantName(string name) => name.ToSnakeCase().ToUpperInvariant();

}
