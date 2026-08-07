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
    /// Name of the accessor class, and so of its file.
    ///
    /// Every generated type used to nest inside it. They are one file each now, because
    /// Java demands a public type be alone in a file named after it.
    /// </summary>
    public string AccessorName { get; set; } = "SheetManData";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".table";

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
/// Emits a Java package: a file per generated type, plus the binary reader.
///
/// A file each rather than nested types, which is what Java asks for - a public top-level
/// type has to be alone in a file named after it. Two files per table, then, one for the
/// record and one for the table: the alternative was nesting the record inside the table and
/// calling it `VectorsTable.Record`, and a worse name is not worth one fewer file.
///
/// All in one package and flat, so nothing imports another generated type. Same as Go.
///
/// The shape lives in templates/java-*.sbn, one per kind of file, over the shared header in
/// java-file-head.sbn.
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
        var view = BuildView();

        Log.Information($"Generating codes for Java into `{System.IO.Path.GetFullPath(PackageDir)}`");

        // The accessor holds a field per table and links the references between them. No
        // reader: it never touches a byte itself.
        Write(_recipe.AccessorName, "java-accessor.sbn", new JavaPartView
        {
            PackageName = _recipe.PackageName,
            AccessorName = _recipe.AccessorName,
            Imports = Imports(new[] { "java.nio.file.Paths" }, reader: false),
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A record reads itself, so it names the reader. Its enum-typed fields name
            // enums, and its references name other records - all in this package, so
            // neither is an import.
            Write(pair.rendered.RecordName, "java-record.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Imports(Array.Empty<string>(), reader: true),
                Table = pair.rendered,
            });

            // A table holds the rows and the index, and opens the file.
            Write(pair.rendered.TableName, "java-table.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Imports(
                    new[]
                    {
                        "java.nio.file.Path", "java.util.ArrayList", "java.util.HashMap",
                        "java.util.List", "java.util.Map",
                    },
                    reader: true),
                Table = pair.rendered,
            });
        }

        foreach (var enumm in view.Enums)
        {
            // An enum is a leaf: it names nothing but the integers it is built from.
            Write(enumm.Name, "java-enum.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Array.Empty<string>(),
                Enumm = enumm,
            });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant set names an enum when one of its constants is typed with one -
            // same package, so no import - and the reader when one is a uuid, whose type is
            // LiteBinaryReader.Uuid.
            Write(pair.rendered.Name, "java-constants.sbn", new JavaPartView
            {
                PackageName = _recipe.PackageName,
                Imports = Imports(Array.Empty<string>(), reader: NamesUuid(pair.model)),
                Set = pair.rendered,
            });
        }
    }

    /// <summary>
    /// Where the files go: Java expects a type's file to sit in a directory matching its
    /// package.
    /// </summary>
    private string PackageDir
        => System.IO.Path.Combine(
            new[] { _recipe.Path }.Concat(_recipe.PackageName.Split('.')).ToArray());

    /// <summary>
    /// Flat inside the package rather than in `tables`, `enums` and `constants`
    /// subpackages.
    /// </summary>
    /// <remarks>
    /// A Java directory is a package, so a subdirectory would be a different one and every
    /// generated type would have to import the others. One package instead: nothing imports
    /// anything of this tool's making, and the names carry the grouping - which is the same
    /// answer Go, Python and Rust arrived at.
    /// </remarks>
    private void Write(string typeName, string templateName, JavaPartView view)
    {
        string full = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(PackageDir, typeName + ".java"));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    /// <summary>
    /// Import lines, with a blank entry where Java convention wants a gap between the
    /// java.* group and the rest.
    /// </summary>
    private static IReadOnlyList<string> Imports(IReadOnlyList<string> standard, bool reader)
    {
        var lines = new List<string>();

        foreach (var name in standard)
            lines.Add($"import {name};");

        if (reader)
        {
            if (lines.Count > 0)
                lines.Add("");

            lines.Add("import sheetman.LiteBinaryReader;");
        }

        return lines;
    }

    /// <summary>
    /// Whether a constant set has a uuid in it, which is the only way its file reaches the
    /// reader - the constant's own type is LiteBinaryReader.Uuid.
    /// </summary>
    private static bool NamesUuid(ConstantSet set)
        => set.Constants.Any(constant => constant.Type == ValueType.Uuid);

    private void WriteBinaryReaderRuntime()
    {
        // Its own `sheetman` package, so the generated accessor's package is free to be
        // anything the consumer wants.
        WriteBinaryReaderRuntime(
            "SheetMan.Runtime.Java.LiteBinaryReader.java",
            System.IO.Path.Combine(_recipe.Path, "sheetman", "LiteBinaryReader.java"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Java.SheetManUpdater.java",
                System.IO.Path.Combine(_recipe.Path, "sheetman", "SheetManUpdater.java"));
        }
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
        Indexes = Indexes(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<JavaIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf => new JavaIndexView
        {
            Member = JavaName(sf.Name),
            Suffix = sf.Name.ToPascalCase(),
            KeyType = Boxed(ResolvedElementType(sf)),
            KeyParam = ResolvedElementType(sf),
            MapName = "by" + sf.Name.ToPascalCase(),
            FieldName = sf.Name.ToPascalCase(),
        }).ToList();

    /// <summary>
    /// The reference type standing in for a primitive, because a Map cannot be keyed by
    /// one.
    /// </summary>
    private static string Boxed(string type)
    {
        return type switch
        {
            "boolean" => "Boolean",
            "int" => "Integer",
            "long" => "Long",
            "float" => "Float",
            "double" => "Double",
            _ => type,
        };
    }

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

    private JavaFieldView BuildField(Table table, SerialField sf)
    {
        string name = JavaName(sf.Name);
        string elementType = ResolvedElementType(sf);

        return new JavaFieldView
        {
            Comment = CommentLines(sf.FirstField.Comment),
            Name = name,
            Kind = ReadKind(sf),
            Tag = sf.FirstField.Tag.Value,
            ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
            ElementCount = sf.Fields.Count,
            ElementType = elementType,
            Declarations = Declarations(sf, name, elementType),
            ReadScalar = ReadExpression(sf),
            ReadElement = ReadExpression(sf),
        };
    }

    /// <summary>
    /// The member declarations, every one of them initialized.
    /// </summary>
    /// <remarks>
    /// Java's own default for a reference is null, and a column the file does not carry
    /// is exactly the case that leaves a member at its default: delete a column, and
    /// code generated before the deletion reads a file that has nothing for it. An
    /// empty string and an empty array are values a consumer can use; null is a crash
    /// one field later.
    ///
    /// A reference is the exception and stays null, because the absence of a referenced
    /// row is what null means here and there is nothing to put in its place.
    /// </remarks>
    private IReadOnlyList<string> Declarations(SerialField sf, string name, string elementType)
    {
        if (sf.IsRef)
        {
            return sf.IsArray
                ? new[] { $"{elementType}[] {name} = new {elementType}[0];", $"int[] {name}Index = new int[0];" }
                : new[] { $"{elementType} {name};", $"int {name}Index;" };
        }

        return sf.IsArray
            ? new[] { $"{elementType}[] {name} = new {elementType}[0];" }
            : new[] { $"{elementType} {name}{Initializer(sf)};" };
    }

    /// <summary>
    /// An empty value of the member's own type, for the declaration to start at.
    /// </summary>
    private string Initializer(SerialField sf)
    {
        return sf.ElementType switch
        {
            // The reference types. Everything else is a primitive whose zero is already
            // an empty value, and saying so again would only be noise.
            ValueType.String => " = \"\"",
            ValueType.Uuid => " = LiteBinaryReader.Uuid.empty()",
            ValueType.Enum => $" = {sf.FirstField.Enum.Name.ToPascalCase()}.of(0)",
            _ => "",
        };
    }

    /// <summary>
    /// The rendered checkColumn call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(SerialField sf, string tableName)
    {
        string kind = sf.IsVariableLengthArray
            ? "LiteBinaryReader.KIND_VAR_ARRAY"
            : (sf.Fields.Count > 1 ? "LiteBinaryReader.KIND_FIXED_ARRAY" : "LiteBinaryReader.KIND_SCALAR");

        int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

        string accepted;

        if (sf.IsRef)
            accepted = "LiteBinaryReader.ELEMENT_I32";
        else
        {
            switch (sf.ElementType)
            {
                case ValueType.Int32:
                    accepted = "LiteBinaryReader.ELEMENT_I32, LiteBinaryReader.ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "LiteBinaryReader.ELEMENT_I64, LiteBinaryReader.ELEMENT_I32, LiteBinaryReader.ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "LiteBinaryReader.ELEMENT_F64, LiteBinaryReader.ELEMENT_F32, LiteBinaryReader.ELEMENT_I32"; break;
                case ValueType.Float: accepted = "LiteBinaryReader.ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "LiteBinaryReader.ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "LiteBinaryReader.ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "LiteBinaryReader.ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "LiteBinaryReader.ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "LiteBinaryReader.ELEMENT_I64"; break;

                default:
                    throw new SheetManException($"The java generator cannot check type `{sf.Type}`.");
            }
        }

        return $"LiteBinaryReader.checkColumn(column, \"{tableName}.{sf.Name}\", {kind}, {count}, {accepted});";
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
                    RefLookup = PrimaryLookup(sf.FirstField.ResolvedRefTable),
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

            // Enum values travel zig-zag encoded rather than fixed width.
            case ValueType.Enum:
                return $"{sf.FirstField.Enum.Name.ToPascalCase()}.of(reader.readEnum())";

            case ValueType.ForeignRecord: return "reader.readInt32()";

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in nine other generators.
            default: return LanguageProfile.Java.ReadCall(sf.ElementType);
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
