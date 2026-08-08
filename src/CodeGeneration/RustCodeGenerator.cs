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
/// Settings for the Rust target.
///
/// Declared beside its generator and reached through the recipe's `Targets` list, as
/// the Go one is.
/// </summary>
public sealed class RustRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Crate name the generated Cargo.toml declares. Also how a consumer refers to the
    /// generated types.
    /// </summary>
    public string CrateName { get; set; } = "gamedata";

    /// <summary>
    /// Whether to write a Cargo.toml beside the generated source.
    ///
    /// On by default, so the output builds as it stands. Turn it off when vendoring the
    /// module into a crate that already has one.
    /// </summary>
    public bool WriteCargoToml { get; set; } = true;

    /// <summary>Rust edition the generated Cargo.toml declares.</summary>
    public string Edition { get; set; } = "2021";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    /// </summary>
    /// <remarks>
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a program can take new data without being redeployed.
    ///
    /// Off by default, and here that means more than elsewhere: Rust's standard library
    /// has no HTTP client, so this is the one thing that puts a dependency in the
    /// generated Cargo.toml. Exactly one - `ureq` - because the manifest parser and the
    /// digest are written out in the module rather than pulled in. Leave it off and the
    /// crate builds with no registry access at all.
    /// </remarks>
    public bool WriteUpdater { get; set; } = false;

    /// <summary>
    /// The `ureq` requirement the generated Cargo.toml declares, when the updater is on.
    /// </summary>
    /// <remarks>
    /// A recipe setting rather than a constant, because the crate that has to build is
    /// the consumer's and its lockfile is theirs to pin.
    /// </remarks>
    public string UreqVersion { get; set; } = "2";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".scb";

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
/// Emits a Rust crate: a module per table, per enum and per constant set, the accessor, the
/// binary reader, and a lib.rs declaring the tree and re-exporting every type at the path it
/// had before the output was split.
///
/// References are kept as indices rather than resolved into borrows. A record holding a
/// reference to another record is a graph, and Rust will not let one own its
/// neighbours; the alternatives are lifetimes threaded through every generated type or
/// a reference-counted cell around every row. The index plus a lookup reads better and
/// costs the caller one call, which is the same trade the database exporters make.
///
/// The shape lives in templates/rust-*.sbn, one per kind of file, over the shared header in
/// rust-file-head.sbn. Which siblings a file brings into scope comes from
/// <see cref="TypeDependencies"/>.
/// </summary>
[SheetManTarget("rust", TargetKind.CodeGeneration, Order = 60)]
public class RustCodeGenerator : CodeGenerator<RustRecipe>
{
    private Model _model;
    private RustRecipe _recipe;

    protected override void Run(TargetContext context, RustRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;

        Generate();
        WriteBinaryReaderRuntime();

        if (_recipe.WriteCargoToml)
            WriteCargoToml();
    }

    /// <summary>Module holding the accessor, and so the file it is written to.</summary>
    /// <remarks>
    /// A constant set named `Tables` would want this file too. That is caught rather than
    /// silently resolved: <see cref="StagingFiles.WriteAllTextToFile"/> refuses to write two
    /// different files to one path.
    /// </remarks>
    private const string AccessorModule = "tables";

    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Rust into `{System.IO.Path.GetFullPath(SourceDir)}`");

        // The accessor joins paths and delegates; the errors it returns come from the tables
        // already wrapped.
        Write(AccessorModule, "rust-accessor.sbn", new RustPartView
        {
            Uses = Uses(new[] { "std::path::Path" }, reader: true)
                .Concat(_model.Tables.Select(TableUse)).ToList(),
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A table indexes its rows and opens its own file, and names the enums its
            // fields are typed with. Not the tables it references: a reference is kept as an
            // index, so a record never names another record's type.
            Write(TableModule(pair.model), "rust-table.sbn", new RustPartView
            {
                Uses = Uses(new[] { "std::collections::HashMap", "std::path::Path" }, reader: true)
                    .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumUse)).ToList(),
                Table = pair.rendered,
            });
        }

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            // An enum is a leaf: it names nothing but the integers it is built from.
            Write(EnumModule(pair.model), "rust-enum.sbn", new RustPartView
            {
                Uses = Array.Empty<string>(),
                Enumm = pair.rendered,
            });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant set names no standard library type, reaches the reader only for a
            // uuid - whose value is rendered as a `sheetman::Uuid` literal - and names an
            // enum when one of its constants is typed with one.
            Write(pair.rendered.ModuleName, "rust-constants.sbn", new RustPartView
            {
                Uses = Uses(Array.Empty<string>(), reader: NamesUuid(pair.model))
                    .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumUse)).ToList(),
                ModuleDoc = pair.rendered.Comment,
                Set = pair.rendered,
            });
        }

        WriteLib(view);
    }

    /// <summary>
    /// Where the crate's source goes: src/, because the reader sits beside the generated
    /// files and `mod sheetman;` only resolves if it does.
    /// </summary>
    private string SourceDir => System.IO.Path.Combine(_recipe.Path, "src");

    /// <summary>
    /// Flat inside src/ rather than in submodule directories.
    ///
    /// A Rust module can be a directory, but only with a mod.rs or a same-named file beside
    /// it, and every path a consumer writes would gain a level for nothing. The names carry
    /// the grouping instead, as they do for Go and Python.
    /// </summary>
    private void Write(string module, string templateName, RustPartView view)
    {
        string full = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(SourceDir, module + ".rs"));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    /// <summary>
    /// Writes lib.rs: the crate lints, the module tree, and the re-exports.
    /// </summary>
    /// <remarks>
    /// The re-exports are why this is worth doing rather than declaring the modules and
    /// leaving it there. Before the split every generated type was declared in lib.rs, so a
    /// consumer wrote `gamedata::VectorsRecord`. `pub use` keeps that path exactly, and the
    /// module a type lives in becomes an implementation detail nobody has to follow.
    ///
    /// A constant set is the exception: it was already a module of its own, so it stays one
    /// and its path is unchanged without any re-export.
    /// </remarks>
    private void WriteLib(RustFileView view)
    {
        var text = new StringBuilder();

        text.Append("// ------------------------------------------------------------------------------\n");
        text.Append($"// {GeneratedFileMarker.TextWithWarning}\n");
        text.Append("//\n");
        text.Append("// Changes to this file may cause incorrect behavior and will be lost if the code is\n");
        text.Append("// regenerated.\n");
        text.Append("// ------------------------------------------------------------------------------\n");
        text.Append('\n');

        // Crate scope, so no generated file repeats them. Generated code is allowed to
        // declare more than a given consumer uses, and clippy's opinions are not this
        // tool's to answer for.
        text.Append("#![allow(dead_code)]\n");
        text.Append("#![allow(clippy::all)]\n");
        text.Append('\n');

        // One declaration for the whole runtime. The updater is a child of it
        // rather than a sibling, so `sheetman` is the one name a consumer has to
        // know for anything that is not their own data - the same shape the other
        // targets get from a `sheetman/` directory.
        text.Append("pub mod sheetman;\n");

        Section(text, "The enums.", view.Enums.Count > 0);

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            text.Append("mod ").Append(EnumModule(pair.model)).Append(";\n");
            text.Append("pub use ").Append(EnumModule(pair.model))
                .Append("::").Append(pair.rendered.Name).Append(";\n");
        }

        Section(text, "The constant sets, each keeping the module path it always had.",
                view.ConstantSets.Count > 0);

        foreach (var set in view.ConstantSets)
            text.Append("pub mod ").Append(set.ModuleName).Append(";\n");

        Section(text, "A record and a table type per table.", view.Tables.Count > 0);

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            text.Append("mod ").Append(TableModule(pair.model)).Append(";\n");
            text.Append("pub use ").Append(TableModule(pair.model))
                .Append("::{").Append(pair.rendered.RecordName)
                .Append(", ").Append(pair.rendered.TableName).Append("};\n");
        }

        Section(text, "The accessor.", true);

        text.Append("mod ").Append(AccessorModule).Append(";\n");
        text.Append("pub use ").Append(AccessorModule).Append("::Tables;\n");

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(SourceDir, "lib.rs")),
            text.ToString());
    }

    private static void Section(StringBuilder text, string heading, bool any)
    {
        if (!any)
            return;

        text.Append('\n');
        text.Append("// ").Append(heading).Append('\n');
    }

    // ------------------------------------------------------- module layout

    private static string TableModule(Table table) => table.Name.ToSnakeCase() + "_table";
    private static string EnumModule(Models.Enum enumm) => "enum_" + enumm.Name.ToSnakeCase();

    private static string TableUse(Table table)
        => $"use crate::{TableModule(table)}::{table.Name.ToPascalCase()}Table;";

    private static string EnumUse(Models.Enum enumm)
        => $"use crate::{EnumModule(enumm)}::{enumm.Name.ToPascalCase()};";

    /// <summary>
    /// The standard library and reader uses a file needs, in the order rustfmt groups them:
    /// std first, then the crate's own.
    /// </summary>
    private static IEnumerable<string> Uses(IReadOnlyList<string> standard, bool reader)
    {
        foreach (var path in standard)
            yield return $"use {path};";

        if (reader)
            yield return "use crate::sheetman;";
    }

    /// <summary>
    /// Whether a constant set has a uuid in it, which is the only way its file reaches the
    /// reader - the value renders as a `sheetman::Uuid` literal.
    /// </summary>
    private static bool NamesUuid(ConstantSet set)
        => set.Constants.Any(constant => constant.Type == ValueType.Uuid);

    private void WriteBinaryReaderRuntime()
    {
        string runtime = System.IO.Path.Combine(SourceDir, "sheetman");

        WriteBinaryReaderRuntime(
            "SheetMan.Runtime.Rust.scb_reader.rs",
            System.IO.Path.Combine(runtime, "scb_reader.rs"));

        // Asked for rather than assumed. It reaches the network, and it is the only
        // thing in this output that wants a crate.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Rust.updater.rs",
                System.IO.Path.Combine(runtime, "updater.rs"));
        }

        // The module file for that directory. Two lines, so that `use crate::sheetman`
        // keeps naming the reader's own symbols - which is what every generated file
        // reaches for - while the updater sits under it as `sheetman::updater`.
        var module = new StringBuilder();

        module.Append("// ------------------------------------------------------------------------------\n");
        module.Append($"// {GeneratedFileMarker.TextWithWarning}\n");
        module.Append("// ------------------------------------------------------------------------------\n");
        module.Append('\n');
        module.Append("mod scb_reader;\n");
        module.Append("pub use scb_reader::*;\n");

        if (_recipe.WriteUpdater)
            module.Append("\npub mod updater;\n");

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(runtime, "mod.rs")),
            module.ToString());
    }

    private void WriteCargoToml()
    {
        var text = new StringBuilder();
        text.Append("[package]\n");
        text.Append("name = \"").Append(_recipe.CrateName).Append("\"\n");
        text.Append("version = \"0.0.0\"\n");
        text.Append("edition = \"").Append(_recipe.Edition).Append("\"\n");
        text.Append('\n');
        if (_recipe.WriteUpdater)
        {
            text.Append("# One dependency, and only because `WriteUpdater` is on: Rust's standard\n");
            text.Append("# library has no HTTP client. The manifest parser and the MD5 are written\n");
            text.Append("# out in src/updater.rs rather than pulled in, so this is the whole of it.\n");
            text.Append("# Turn the updater off and this section is empty again.\n");
            text.Append("[dependencies]\n");
            text.Append("ureq = \"").Append(_recipe.UreqVersion).Append("\"\n");
        }
        else
        {
            text.Append("# No dependencies on purpose: the reader is core and std only, so the\n");
            text.Append("# generated crate builds without registry access.\n");
            text.Append("[dependencies]\n");
        }

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, "Cargo.toml")),
            text.ToString());
    }

    // --------------------------------------------------------------- view

    private RustFileView BuildView() => new RustFileView
    {
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = new RustAccessorView
        {
            FileExtension = _recipe.BinaryTableFileExtension,
            Tables = _model.Tables.Select(table => new RustTableSlotView
            {
                Name = RustName(table.Name),
                TableName = table.Name.ToPascalCase() + "Table",

                // Unescaped: this one names the file the exporter wrote.
                DataFileName = table.Name,
            }).ToList(),
        },
    };

    private RustEnumView BuildEnum(Models.Enum enumm)
    {
        // Deriving Default needs exactly one variant marked, so the zero label gets it
        // when there is one and the first otherwise.
        int defaultIndex = enumm.Labels.FindIndex(label => label.Value == 0);
        if (defaultIndex < 0)
            defaultIndex = 0;

        return new RustEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            Labels = enumm.Labels.Select((label, index) => new RustEnumLabelView
            {
                Name = label.Name.ToPascalCase(),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
                IsDefault = index == defaultIndex,
            }).ToList(),
        };
    }

    private RustConstantSetView BuildConstantSet(ConstantSet constantSet) => new RustConstantSetView
    {
        ModuleName = constantSet.Name.ToSnakeCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new RustConstantView
        {
            // Rust constants are SCREAMING_SNAKE_CASE, and the compiler warns otherwise.
            Name = constant.Name.ToSnakeCase().ToUpperInvariant(),
            Type = ConstantTypeName(constant),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private RustTableView BuildTable(Table table) => new RustTableView
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
    private IReadOnlyList<RustIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf =>
        {
            string keyType = ToRustTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull);
            bool owned = keyType == "String";

            return new RustIndexView
            {
                Member = RustName(sf.Name),
                Suffix = sf.Name.ToSnakeCase(),
                KeyType = keyType,
                KeyParam = owned ? "&str" : keyType,
                KeyBorrow = owned ? "key" : "&key",
                MapName = "by_" + sf.Name.ToSnakeCase(),
                FieldName = sf.Name.ToPascalCase(),
            };
        }).ToList();

    private RustFieldView BuildField(Table table, SerialField sf)
    {
        string name = RustName(sf.Name);
        string elementType = ToRustTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull);

        return new RustFieldView
        {
            Comment = CommentLines(sf.FirstField.Comment),
            Name = name,
            Kind = ReadKind(sf),
            Tag = sf.FirstField.Tag.Value,
            ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(sf, table.Name.ToPascalCase()),
            ElementCount = sf.Fields.Count,
            Declarations = Declarations(sf, name, elementType),
            ReadScalar = ScalarReadExpression(sf),
            ReadElement = ReadExpression(sf),
        };
    }

    private IReadOnlyList<string> Declarations(SerialField sf, string name, string elementType)
    {
        if (sf.IsRef)
        {
            // Only the index. See the type remarks for why it is not resolved.
            return sf.IsArray
                ? new[] { $"{name}_index: Vec<i32>," }
                : new[] { $"{name}_index: i32," };
        }

        return sf.IsArray
            ? new[] { $"{name}: Vec<{elementType}>," }
            : new[] { $"{name}: {elementType}," };
    }

    /// <summary>
    /// The rendered check_column call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(SerialField sf, string tableName)
    {
        string kind = sf.IsVariableLengthArray
            ? "sheetman::KIND_VAR_ARRAY"
            : (sf.Fields.Count > 1 ? "sheetman::KIND_FIXED_ARRAY" : "sheetman::KIND_SCALAR");

        int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

        string accepted;

        if (sf.IsRef)
            accepted = "sheetman::ELEMENT_I32";
        else
        {
            switch (sf.ElementType)
            {
                case ValueType.Int32:
                    accepted = "sheetman::ELEMENT_I32, sheetman::ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "sheetman::ELEMENT_I64, sheetman::ELEMENT_I32, sheetman::ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "sheetman::ELEMENT_F64, sheetman::ELEMENT_F32, sheetman::ELEMENT_I32"; break;
                case ValueType.Float: accepted = "sheetman::ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "sheetman::ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "sheetman::ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "sheetman::ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "sheetman::ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "sheetman::ELEMENT_I64"; break;

                default:
                    throw new SheetManException($"The rust generator cannot check type `{sf.Type}`.");
            }
        }

        return $"sheetman::check_column(column, \"{tableName}.{sf.Name}\", {kind}, {count}, &[{accepted}])?;";
    }

    private static string ReadKind(SerialField sf)
    {
        if (sf.IsVariableLengthArray)
            return "var_array";

        if (sf.IsArray)
            return sf.IsRef ? "serial_ref" : "serial";

        return sf.IsRef ? "scalar_ref" : "scalar";
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
            // Int64 and Double are here for their promotions as well as their own
            // dictionaries: the file may carry an i32 column - encoded - where the
            // member has since widened.
            case ValueType.Int32:
            case ValueType.Int64:
            case ValueType.Double:
            case ValueType.Float:
            case ValueType.Bool:
            case ValueType.Enum:
            case ValueType.String:

            // Ticks are an i64 column, so they meet the i64 dictionary like any other.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return true;

            // Uuid is the one scalar left raw: sixteen-byte entries rarely repeat
            // enough to pay for the index beside them.
            default:
                return false;
        }
    }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or nothing for
    /// a column that reads the reader directly. The match arm is its own scope, so the
    /// binding lives exactly as long as the column it decodes.
    /// </summary>
    private static string CursorOpen(SerialField sf, string tableName)
        => UsesCursor(sf)
            ? "let mut cursor = sheetman::ScbColumnCursor::new(" +
              $"&mut reader, column, header.row_count, \"{tableName}.{sf.Name}\")?;"
            : "";

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The expression that reads one scalar value: through the cursor where the column
    /// can arrive encoded - which also carries the lossless promotions - and straight
    /// off the reader otherwise. Arrays are always raw and keep <see cref="ReadExpression"/>.
    /// </summary>
    private string ScalarReadExpression(SerialField sf)
    {
        if (!UsesCursor(sf))
            return ReadExpression(sf);

        if (sf.IsRef)
            return "cursor.next_i32()?";

        switch (sf.ElementType)
        {
            case ValueType.Enum:
                return $"{sf.FirstField.Enum.Name.ToPascalCase()}::from_value(cursor.next_i32()?)" +
                       ".unwrap_or_default()";

            case ValueType.Int32: return "cursor.next_i32()?";
            case ValueType.Int64: return "cursor.next_i64()?";
            case ValueType.Double: return "cursor.next_f64()?";
            case ValueType.Float: return "cursor.next_f32()?";
            case ValueType.Bool: return "cursor.next_bool()?";

            // Ticks, which is what the member holds - std has no date type - so the
            // i64 column's value is the member, dictionary or not.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "cursor.next_i64()?";

            default: return "cursor.next_string()?";
        }
    }

    private string ReadExpression(SerialField sf)
    {
        switch (sf.ElementType)
        {

            // Enum values travel zig-zag encoded. A value the sheet never declared
            // falls back to the default rather than failing the whole read, matching
            // what the other generated readers do with an unknown label.
            case ValueType.Enum:
                return $"{sf.FirstField.Enum.Name.ToPascalCase()}::from_value(reader.read_enum()?)" +
                       ".unwrap_or_default()";

            case ValueType.ForeignRecord: return "reader.read_i32()?";

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in nine other generators.
            default: return LanguageProfile.Rust.ReadCall(sf.ElementType);
        }
    }

    private string ToRustTypeName(ValueType type, Models.Enum enumm)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return enumm.Name.ToPascalCase();

            // A reference is carried as the target row's index.
            case ValueType.ForeignRecord:
                return "i32";

            default:
                return LanguageProfile.Rust.ScalarTypeName(type);
        }
    }

    /// <summary>
    /// The type of a constant, which is not always the type of a field.
    ///
    /// A `String` cannot be a constant - it allocates - so a string constant is a
    /// static string slice instead.
    /// </summary>
    private string ConstantTypeName(ConstantSet.Constant constant)
        => constant.Type == ValueType.String
            ? "&'static str"
            : ToRustTypeName(constant.Type, constant.Enum);

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
                return ((long)constant.Value).ToString(CultureInfo.InvariantCulture);

            case ValueType.Float:
                return Suffixed(((float)constant.Value).ToString("R", CultureInfo.InvariantCulture));

            case ValueType.Double:
                return Suffixed(((double)constant.Value).ToString("R", CultureInfo.InvariantCulture));

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.TimeSpan:
                return ((TimeSpan)constant.Value).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.Uuid:
                return "sheetman::Uuid([" + string.Join(", ",
                    ((Guid)constant.Value).ToByteArray()
                        .Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + "])";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}::{label.Name.ToPascalCase()}";
            }

            default:
                throw new SheetManException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the rust generator cannot render.");
        }
    }

    /// <summary>
    /// Gives a rendered float a decimal point when it has none.
    ///
    /// `3` is an integer literal in Rust and will not initialize an f32; `3.0` will.
    /// A value in exponent form already parses as a float.
    /// </summary>
    private static string Suffixed(string rendered)
        => rendered.Contains('.') || rendered.Contains('E') || rendered.Contains('e')
            ? rendered
            : rendered + ".0";

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
                literal.Append(@"\u{").Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append('}');
            else
                literal.Append(c);
        }

        return literal.Append('"').ToString();
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// A struct member name.
    ///
    /// snake_case, and escaped when it lands on a keyword - which it can, unlike Go and
    /// C#, because Rust members are lowercase and so is every Rust keyword.
    /// </summary>
    private static string RustName(string name) => LanguageProfile.Rust.MemberName(name.ToSnakeCase());

}
