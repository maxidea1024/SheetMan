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
/// Settings for the Python target.
///
/// Declared beside its generator and reached through the recipe's `Targets` list.
/// </summary>
public sealed class PythonRecipe : IOutputRecipe
{
    /// <summary>Directory the package is written into. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Name of the generated package, which is also the directory it goes in and how a
    /// consumer imports it.
    /// </summary>
    public string PackageName { get; set; } = "gamedata";

    /// <summary>Module the generated types live in, inside the package.</summary>
    public string ModuleName { get; set; } = "tables";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".scb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    ///
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a program can take new data without being redeployed. Off by
    /// default: one that ships its data alongside its code has no use for it.
    /// </summary>
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
/// Emits a Python package: a module per table, per enum and per constant set, the accessor,
/// the binary reader, and an `__init__` that re-exports every generated name.
///
/// Records use `__slots__`. A localization table is tens of thousands of rows, and a
/// per-instance dictionary on each is the difference between tens of megabytes and a
/// few.
///
/// The shape lives in templates/python-*.sbn, one per kind of file, over the shared header
/// in python-file-head.sbn. Which siblings a file imports comes from
/// <see cref="TypeDependencies"/>.
/// </summary>
[SheetManTarget("python", TargetKind.CodeGeneration, Order = 70)]
public class PythonCodeGenerator : CodeGenerator<PythonRecipe>
{
    private Model _model;
    private PythonRecipe _recipe;

    protected override void Run(TargetContext context, PythonRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;

        Generate();
        WriteBinaryReaderRuntime();
        WriteInit();
    }

    private string PackageDir
        => System.IO.Path.Combine(_recipe.Path, _recipe.PackageName);

    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Python into `{System.IO.Path.GetFullPath(PackageDir)}`");

        // The accessor constructs every table and links the references between them, so it
        // names each table class and no record type.
        Write(_recipe.ModuleName + ".py", "python-accessor.sbn", new PythonPartView
        {
            Imports = _model.Tables.Select(table => TableImport(table)).ToList(),
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A table names the enums its fields are typed with. Not the tables it
            // references: resolution happens in the accessor, and importing them here
            // would turn two tables pointing at each other into an import cycle.
            Write(TableModule(pair.model) + ".py", "python-table.sbn", new PythonPartView
            {
                Imports = TypeDependencies.EnumsNamedBy(pair.model).Select(EnumImport).ToList(),
                Table = pair.rendered,
            });
        }

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            // An enum is a leaf: enum.IntEnum comes from the standard library.
            Write(EnumModule(pair.model) + ".py", "python-enum.sbn", new PythonPartView
            {
                Imports = Array.Empty<string>(),
                Enumm = pair.rendered,
            });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant of an enum type renders as one of that enum's labels.
            Write(ConstantsModule(pair.model) + ".py", "python-constants.sbn", new PythonPartView
            {
                Imports = TypeDependencies.EnumsNamedBy(pair.model).Select(EnumImport).ToList(),
                Set = pair.rendered,
            });
        }
    }

    /// <summary>
    /// Flat inside the package rather than in `tables/`, `enums/` and `constants/` as most
    /// targets do.
    /// </summary>
    /// <remarks>
    /// A Python subdirectory is a subpackage, so each would need an `__init__` of its own
    /// and every import would gain a level. Worse, `ModuleName` defaults to `tables`, and a
    /// `tables/` package sitting beside `tables.py` is resolved in favour of the package -
    /// the accessor would quietly stop being importable. The names carry the grouping
    /// instead, as they do for Go.
    /// </remarks>
    private void Write(string filename, string templateName, PythonPartView view)
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(PackageDir, filename));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    // ------------------------------------------------------- module layout

    /// <remarks>
    /// A table's own name first, as Go spells it: `template_table.py`, not
    /// `table_template.py`. An enum and a constant set take the prefix instead, because
    /// neither has a noun of its own to carry - `flag` alone does not say what it is, and
    /// `flag_enum` reads like a field.
    /// </remarks>
    private static string TableModule(Table table) => table.Name.ToSnakeCase() + "_table";
    private static string EnumModule(Models.Enum enumm) => "enum_" + enumm.Name.ToSnakeCase();
    private static string ConstantsModule(ConstantSet set) => "const_" + set.Name.ToSnakeCase();

    private static string TableImport(Table table)
        => $"from .{TableModule(table)} import {table.Name.ToPascalCase()}Table";

    private static string EnumImport(Models.Enum enumm)
        => $"from .{EnumModule(enumm)} import {enumm.Name.ToPascalCase()}";

    private void WriteBinaryReaderRuntime()
    {
        string runtime = System.IO.Path.Combine(PackageDir, "sheetman");

        WriteBinaryReaderRuntime(
            "SheetMan.Runtime.Python.scb_reader.py",
            System.IO.Path.Combine(runtime, "scb_reader.py"));

        // The subpackage's `__init__`, so `from . import sheetman` keeps naming the
        // reader's own symbols - which is what every generated module reaches for.
        // Two lines rather than making the reader itself the `__init__`: the file is
        // called scb_reader in all thirteen languages, and it should be here
        // too.
        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(runtime, "__init__.py")),
            string.Join("\n", new[]
            {
                "# ------------------------------------------------------------------------------",
                $"# {GeneratedFileMarker.TextWithWarning}",
                "# ------------------------------------------------------------------------------",
                "",
                "from .scb_reader import *  # noqa: F401,F403",
                "",
            }));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Python.updater.py",
                System.IO.Path.Combine(runtime, "updater.py"));
        }
    }

    /// <summary>
    /// Writes the package's `__init__`, which re-exports every generated name so a consumer
    /// imports the package rather than a file inside it.
    /// </summary>
    /// <remarks>
    /// One `from .module import Name` per type, not `import *`. A star import would re-export
    /// whatever else a module happens to hold - `enum`, `os`, `sheetman` - and give a
    /// consumer no way to see what the package offers. It also means `__all__` is exact,
    /// which is what `from gamedata import *` reads.
    ///
    /// The order follows the dependency graph, so an interpreter that reads this file top to
    /// bottom loads enums before the tables that name them. Python does not require that -
    /// each module's own imports would pull what it needs - but a file whose order says
    /// something true is worth more than one whose order says nothing.
    /// </remarks>
    private void WriteInit()
    {
        var exported = new List<string>();
        var text = new StringBuilder();

        text.Append("# ------------------------------------------------------------------------------\n");
        text.Append($"# {GeneratedFileMarker.TextWithWarning}\n");
        text.Append("#\n");
        text.Append("# Changes to this file may cause incorrect behavior and will be lost if the code is\n");
        text.Append("# regenerated.\n");
        text.Append("# ------------------------------------------------------------------------------\n");
        text.Append('\n');

        foreach (var enumm in _model.Enums)
            Export(text, exported, EnumModule(enumm), enumm.Name.ToPascalCase());

        foreach (var set in _model.ConstantSets)
            Export(text, exported, ConstantsModule(set), set.Name.ToPascalCase());

        foreach (var table in _model.Tables)
        {
            Export(text, exported, TableModule(table),
                   table.Name.ToPascalCase() + "Record", table.Name.ToPascalCase() + "Table");
        }

        Export(text, exported, _recipe.ModuleName, "Tables");

        text.Append('\n');
        text.Append("__all__ = [\n");

        foreach (string name in exported)
            text.Append("    \"").Append(name).Append("\",\n");

        text.Append("]\n");

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(PackageDir, "__init__.py")),
            text.ToString());
    }

    private static void Export(StringBuilder text, List<string> exported, string module, params string[] names)
    {
        text.Append("from .").Append(module).Append(" import ").Append(string.Join(", ", names)).Append('\n');

        exported.AddRange(names);
    }

    // --------------------------------------------------------------- view

    private PythonFileView BuildView() => new PythonFileView
    {
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private PythonEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new PythonEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = DocText(enumm.Location.ToString()),
            Comment = CommentLines(enumm.Comment),
            DefaultValue = fallback.Value.ToString(CultureInfo.InvariantCulture),
            Labels = enumm.Labels.Select(label => new PythonEnumLabelView
            {
                Name = PythonName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };
    }

    private PythonConstantSetView BuildConstantSet(ConstantSet constantSet) => new PythonConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = DocText(constantSet.Location.ToString()),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new PythonConstantView
        {
            // Python constants are SCREAMING_SNAKE_CASE by convention.
            Name = constant.Name.ToSnakeCase().ToUpperInvariant(),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private PythonTableView BuildTable(Table table)
    {
        var fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList();

        // A reference contributes its index as well as its value, and both need a slot.
        var slots = new List<string>();
        foreach (var sf in table.SerialFields)
        {
            slots.Add(PythonName(sf.Name));

            if (sf.IsRef)
                slots.Add(PythonName(sf.Name) + "_index");
        }

        return new PythonTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = DocText(table.Location.ToString()),
            Comment = CommentLines(table.Comment),
            Indexes = Indexes(table),
            TableSlotNames = Tuple(
                new[] { "records" }.Concat(Indexes(table).Select(index => index.MapName)).ToList()),
            SlotNames = Tuple(slots),
            ReprFormat = string.Join(", ", table.SerialFields.Select(sf => PythonName(sf.Name) + "=%r")),
            ReprValues = Tuple(table.SerialFields.Select(sf => "self." + PythonName(sf.Name)).ToList(),
                               quote: false),
            Fields = fields,
        };
    }

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<PythonIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf => new PythonIndexView
        {
            Member = PythonName(sf.Name),
            Suffix = sf.Name.ToSnakeCase(),
            MapName = "by_" + sf.Name.ToSnakeCase(),
            FieldName = sf.Name.ToPascalCase(),
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

    private PythonFieldView BuildField(Table table, SerialField sf)
    {
        string name = PythonName(sf.Name);

        return new PythonFieldView
        {
            Comment = CommentLines(sf.FirstField.Comment),
            Name = name,
            Kind = ReadKind(sf),
            Tag = sf.FirstField.Tag.Value,
            ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(sf, table.Name.ToPascalCase()),
            ElementCount = sf.Fields.Count,
            Initializers = Initializers(sf, name),
            ReadScalar = UsesCursor(sf) ? CursorReadExpression(sf) : ReadExpression(sf),
            ReadElement = ReadExpression(sf),
        };
    }

    /// <summary>
    /// Whether a field's column reads through the cursor: every scalar column whose
    /// element the encodings apply to, or promote from. Arrays are always raw and keep
    /// reading the reader directly, as do the scalar elements that stay raw by spec.
    /// </summary>
    private static bool UsesCursor(SerialField sf)
    {
        if (sf.IsArray)
            return false;

        if (sf.IsRef)
            return true;

        switch (sf.ElementType)
        {
            // Int64 and Double are here for their promotions: the file may carry an
            // i32 column - encoded - where the member has since widened.
            case ValueType.Int32:
            case ValueType.Int64:
            case ValueType.Double:
            case ValueType.Enum:
            case ValueType.String:
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or nothing for
    /// a column that reads the reader directly. Python has no block scope, so the
    /// assignment needs no declaration to sit under.
    /// </summary>
    private static string CursorOpen(SerialField sf, string tableName)
        => UsesCursor(sf)
            ? $"cursor = sheetman.ColumnCursor(reader, column, count, \"{tableName}.{sf.Name}\")"
            : "";

    /// <summary>
    /// The read for a scalar that goes through the cursor - which is what carries the
    /// encodings, and the lossless promotions with them.
    /// </summary>
    private static string CursorReadExpression(SerialField sf)
    {
        // Only the stored index is on the wire; the accessor fills the value in once
        // every table is loaded.
        if (sf.IsRef)
            return "cursor.next_i32()";

        switch (sf.ElementType)
        {
            // An enum travels as an int32 through the cursor, exactly as a raw one does.
            case ValueType.Enum:
                return $"{sf.FirstField.Enum.Name.ToPascalCase()}(cursor.next_i32())";

            case ValueType.Int32: return "cursor.next_i32()";
            case ValueType.Int64: return "cursor.next_i64()";
            case ValueType.Double: return "cursor.next_f64()";

            default: // String; UsesCursor admits nothing else here.
                return "cursor.next_string()";
        }
    }

    /// <summary>
    /// The constructor's assignments, so that a record is fully formed before it is read
    /// into and a consumer never meets a half-built one.
    /// </summary>
    private IReadOnlyList<string> Initializers(SerialField sf, string name)
    {
        if (sf.IsRef)
        {
            return sf.IsArray
                ? new[] { $"self.{name} = []", $"self.{name}_index = []" }
                : new[] { $"self.{name} = None", $"self.{name}_index = 0" };
        }

        if (sf.IsArray)
            return new[] { $"self.{name} = []" };

        return new[] { $"self.{name} = {DefaultValue(sf)}" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "False";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "sheetman.Uuid()";
            case ValueType.Enum: return $"{sf.FirstField.Enum.Name.ToPascalCase()}(0)";
            default: return "0";
        }
    }

    /// <summary>
    /// The rendered check_column call: kind, count, and the elements this member accepts -
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
            accepted = "sheetman.ELEMENT_I32,";
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
                case ValueType.Float: accepted = "sheetman.ELEMENT_F32,"; break;
                case ValueType.Bool: accepted = "sheetman.ELEMENT_BOOL,"; break;
                case ValueType.String: accepted = "sheetman.ELEMENT_STRING,"; break;
                case ValueType.Uuid: accepted = "sheetman.ELEMENT_UUID,"; break;
                case ValueType.Enum: accepted = "sheetman.ELEMENT_VARINT,"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "sheetman.ELEMENT_I64,"; break;

                default:
                    throw new SheetManException($"The python generator cannot check type `{sf.Type}`.");
            }
        }

        return $"sheetman.check_column(column, \"{tableName}.{sf.Name}\", {kind}, {count}, ({accepted}))";
    }

    private static string ReadKind(SerialField sf)
    {
        if (sf.IsVariableLengthArray)
            return "var_array";

        if (sf.IsArray)
            return sf.IsRef ? "serial_ref" : "serial";

        return sf.IsRef ? "scalar_ref" : "scalar";
    }

    private PythonAccessorView BuildAccessor() => new PythonAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,
        SlotNames = Tuple(_model.Tables.Select(table => PythonName(table.Name)).ToList()),

        Tables = _model.Tables.Select(table => new PythonTableSlotView
        {
            Name = PythonName(table.Name),
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
            .Select(x => new PythonCrossReferenceView
            {
                Table = PythonName(x.Table.Name),
                Fields = x.Fields.Select(sf => new PythonReferenceFieldView
                {
                    Name = PythonName(sf.Name),
                    RefTable = PythonName(sf.FirstField.ResolvedRefTable.Name),
                    RefLookup = PrimaryLookup(sf.FirstField.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + PythonName(sf.FirstField.ResolvedRefField.Name),
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
                return $"{sf.FirstField.Enum.Name.ToPascalCase()}(reader.read_enum())";

            case ValueType.ForeignRecord: return "reader.read_int32()";

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in nine other generators.
            default: return LanguageProfile.Python.ReadCall(sf.ElementType);
        }
    }

    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        switch (constant.Type)
        {
            case ValueType.String:
                return Quote((string)constant.Value);

            case ValueType.Bool:
                return (bool)constant.Value ? "True" : "False";

            case ValueType.Int32:
                return ((int)constant.Value).ToString(CultureInfo.InvariantCulture);

            case ValueType.Int64:
                return ((long)constant.Value).ToString(CultureInfo.InvariantCulture);

            case ValueType.Float:
                return ((float)constant.Value).ToString("R", CultureInfo.InvariantCulture);

            case ValueType.Double:
                return ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture);

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.TimeSpan:
                return ((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.Uuid:
                return "sheetman.Uuid(bytes([" + string.Join(", ",
                    ((Guid)constant.Value).ToByteArray()
                        .Select(b => b.ToString(CultureInfo.InvariantCulture))) + "]))";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}.{PythonName(label.Name)}";
            }

            default:
                throw new SheetManException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the python generator cannot render.");
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

    /// <summary>
    /// A Python tuple literal, with the trailing comma a one-element tuple needs.
    /// </summary>
    private static string Tuple(IReadOnlyList<string> items, bool quote = true)
    {
        if (items.Count == 0)
            return "";

        string rendered = string.Join(", ", items.Select(item => quote ? $"\"{item}\"" : item));

        return items.Count == 1 ? rendered + "," : rendered;
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// An attribute name.
    ///
    /// snake_case, and escaped when it lands on a keyword - which it can, because
    /// Python members are lowercase and so is nearly every Python keyword.
    /// </summary>
    private static string PythonName(string name) => LanguageProfile.Python.MemberName(name.ToSnakeCase());

    // `new`, and not the base one: each line goes through this target's own doc
    // escaping on the way out.
    private static new IReadOnlyList<string> CommentLines(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return Array.Empty<string>();

        return comment.Replace("\r\n", "\n").Split('\n').Select(DocText).ToArray();
    }

    /// <summary>
    /// Text safe to put inside a docstring.
    ///
    /// A sheet location contains a backslash on Windows, and a docstring is not raw, so
    /// the path reads as an escape sequence - which Python warns about today and will
    /// reject eventually. A triple quote inside one would end it.
    /// </summary>
    private static string DocText(string text)
        => (text ?? "").Replace("\\", "\\\\").Replace("\"\"\"", "\\\"\\\"\\\"");
}
