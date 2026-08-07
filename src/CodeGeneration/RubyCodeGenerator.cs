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
/// Settings for the Ruby target.
///
/// Declared beside its generator and reached through the recipe's `Targets` list.
/// </summary>
public sealed class RubyRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>Module every generated type is nested in.</summary>
    public string ModuleName { get; set; } = "GameData";

    /// <summary>Base name of the generated file, without its extension.</summary>
    public string AccessorName { get; set; } = "sheetman_data";

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
/// Emits one Ruby file holding every generated type, plus the binary reader beside it.
///
/// Enums are modules of integer constants rather than a class per label: the value is
/// what travels on the wire, comparisons against it are what consuming code does, and
/// a module of constants is what Ruby reaches for.
///
/// The shape lives in templates/ruby.sbn.
/// </summary>
[SheetManTarget("ruby", TargetKind.CodeGeneration, Order = 75)]
public class RubyCodeGenerator : CodeGenerator<RubyRecipe>
{
    private Model _model;
    private RubyRecipe _recipe;

    protected override void Run(TargetContext context, RubyRecipe recipe)
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
    /// The module is reopened in every file rather than nested from one place, which is how
    /// Ruby works: `module X` opens X whether or not something else already did.
    ///
    /// Requiring is the part that needs care. Ruby has no autoloader here, so the accessor
    /// requires every part - and a table requires the reader, because its `read` names it.
    /// File names are snake_case, as Ruby writes them.
    /// </remarks>
    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Ruby into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        // Forward slashes, which `require_relative` takes on every platform, and no
        // extension, which is how Ruby spells it.
        var parts = new List<string> { "sheetman/lite_binary_reader" };

        parts.AddRange(view.Enums.Select(e => "enums/" + e.Name.ToSnakeCase()));
        parts.AddRange(view.ConstantSets.Select(s => "constants/" + s.Name.ToSnakeCase()));
        parts.AddRange(view.Tables.Select(t => "tables/" + t.TableName.ToSnakeCase()));

        Write(_recipe.AccessorName + ".rb", "ruby-accessor.sbn", new RubyPartView
        {
            ModuleName = _recipe.ModuleName,
            Requires = parts,
            Accessor = view.Accessor,
        });

        foreach (var table in view.Tables)
        {
            Write(System.IO.Path.Combine("tables", table.TableName.ToSnakeCase() + ".rb"),
                  "ruby-table.sbn", new RubyPartView
                  {
                      ModuleName = _recipe.ModuleName,

                      // One directory down, and its `read` names the reader.
                      Requires = new[] { "../sheetman/lite_binary_reader" },
                      Table = table,
                  });
        }

        // An enum module and a constant module name nothing outside themselves.
        foreach (var enumm in view.Enums)
        {
            Write(System.IO.Path.Combine("enums", enumm.Name.ToSnakeCase() + ".rb"),
                  "ruby-enum.sbn", new RubyPartView
                  {
                      ModuleName = _recipe.ModuleName,
                      Requires = Array.Empty<string>(),
                      Enumm = enumm,
                  });
        }

        foreach (var set in view.ConstantSets)
        {
            Write(System.IO.Path.Combine("constants", set.Name.ToSnakeCase() + ".rb"),
                  "ruby-constants.sbn", new RubyPartView
                  {
                      ModuleName = _recipe.ModuleName,
                      Requires = Array.Empty<string>(),
                      Set = set,
                  });
        }
    }

    private void Write(string relative, string templateName, object view)
    {
        string filename = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(_recipe.Path, relative));

        StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render(templateName, view));
    }

    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "SheetMan.Runtime.Ruby.lite_binary_reader.rb",
            System.IO.Path.Combine(_recipe.Path, "sheetman", "lite_binary_reader.rb"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Ruby.updater.rb",
                System.IO.Path.Combine(_recipe.Path, "sheetman", "updater.rb"));
        }
    }

    // --------------------------------------------------------------- view

    private RubyFileView BuildView() => new RubyFileView
    {
        ModuleName = _recipe.ModuleName.ToPascalCase(),
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private RubyEnumView BuildEnum(Models.Enum enumm) => new RubyEnumView
    {
        Name = enumm.Name.ToPascalCase(),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select(label => new RubyEnumLabelView
        {
            Name = ConstantName(label.Name),
            Symbol = RubyName(label.Name),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            Comment = CommentLines(label.Comment),
        }).ToList(),
    };

    private RubyConstantSetView BuildConstantSet(ConstantSet constantSet) => new RubyConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new RubyConstantView
        {
            Name = ConstantName(constant.Name),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private RubyTableView BuildTable(Table table)
    {
        // A reference contributes its index as well as its value, and both are read
        // from outside the record when references are linked.
        var accessors = new List<string>();

        foreach (var sf in table.SerialFields)
        {
            accessors.Add(RubyName(sf.Name));

            if (sf.IsRef)
                accessors.Add(RubyName(sf.Name) + "_index");
        }

        return new RubyTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = table.Location.ToString(),
            Comment = CommentLines(table.Comment),
            Indexes = Indexes(table),
            AccessorNames = Symbols(accessors),
            Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),
        };
    }

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<RubyIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf => new RubyIndexView
        {
            Member = RubyName(sf.Name),
            Suffix = sf.Name.ToSnakeCase(),
            MapName = "@by_" + sf.Name.ToSnakeCase(),
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

    private RubyFieldView BuildField(Table table, SerialField sf)
    {
        string name = RubyName(sf.Name);

        return new RubyFieldView
        {
            Comment = CommentLines(sf.FirstField.Comment),
            Name = name,
            Kind = ReadKind(sf),
            Tag = sf.FirstField.Tag.Value,
            ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
            ElementCount = sf.Fields.Count,
            Initializers = Initializers(sf, name),
            ReadScalar = ReadExpression(sf),
            ReadElement = ReadExpression(sf),
        };
    }

    private IReadOnlyList<string> Initializers(SerialField sf, string name)
    {
        if (sf.IsRef)
        {
            return sf.IsArray
                ? new[] { $"@{name} = []", $"@{name}_index = []" }
                : new[] { $"@{name} = nil", $"@{name}_index = 0" };
        }

        if (sf.IsArray)
            return new[] { $"@{name} = []" };

        return new[] { $"@{name} = {DefaultValue(sf)}" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "Sheetman::Uuid.new";
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
            ? "Sheetman::KIND_VAR_ARRAY"
            : (sf.Fields.Count > 1 ? "Sheetman::KIND_FIXED_ARRAY" : "Sheetman::KIND_SCALAR");

        int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

        string accepted;

        if (sf.IsRef)
            accepted = "Sheetman::ELEMENT_I32";
        else
        {
            switch (sf.ElementType)
            {
                case ValueType.Int32:
                    accepted = "Sheetman::ELEMENT_I32, Sheetman::ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "Sheetman::ELEMENT_I64, Sheetman::ELEMENT_I32, Sheetman::ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "Sheetman::ELEMENT_F64, Sheetman::ELEMENT_F32, Sheetman::ELEMENT_I32"; break;
                case ValueType.Float: accepted = "Sheetman::ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "Sheetman::ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "Sheetman::ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "Sheetman::ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "Sheetman::ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "Sheetman::ELEMENT_I64"; break;

                default:
                    throw new SheetManException($"The ruby generator cannot check type `{sf.Type}`.");
            }
        }

        return $"Sheetman.check_column(column, '{tableName}.{sf.Name}', {kind}, {count}, [{accepted}])";
    }

    private static string ReadKind(SerialField sf)
    {
        if (sf.IsVariableLengthArray)
            return "var_array";

        if (sf.IsArray)
            return sf.IsRef ? "serial_ref" : "serial";

        return sf.IsRef ? "scalar_ref" : "scalar";
    }

    private RubyAccessorView BuildAccessor() => new RubyAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,
        ReaderNames = Symbols(_model.Tables.Select(table => RubyName(table.Name)).ToList()),

        Tables = _model.Tables.Select(table => new RubyTableSlotView
        {
            Name = RubyName(table.Name),
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
            .Select(x => new RubyCrossReferenceView
            {
                Table = RubyName(x.Table.Name),
                Fields = x.Fields.Select(sf => new RubyReferenceFieldView
                {
                    Name = RubyName(sf.Name),
                    RefTable = RubyName(sf.FirstField.ResolvedRefTable.Name),
                    RefLookup = PrimaryLookup(sf.FirstField.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + RubyName(sf.FirstField.ResolvedRefField.Name),
                    IsArray = sf.IsArray,
                }).ToList(),
            })
            .ToList(),
    };

    // ----------------------------------------------------------- rendering

    private string ReadExpression(SerialField sf)
    {
        return sf.ElementType switch
        {
            // Enum values travel zig-zag encoded rather than fixed width, and arrive as
            // the integer the sheet declared.
            ValueType.Enum => "reader.read_enum",
            ValueType.ForeignRecord => "reader.read_int32",
            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in nine other generators.
            _ => LanguageProfile.Ruby.ReadCall(sf.ElementType),
        };
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
                return "Sheetman::Uuid.new([" + string.Join(", ",
                    ((Guid)constant.Value).ToByteArray()
                        .Select(b => b.ToString(CultureInfo.InvariantCulture))) + "].pack('C*'))";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}::{ConstantName(label.Name)}";
            }

            default:
                throw new SheetManException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the ruby generator cannot render.");
        }
    }

    /// <summary>
    /// A single-quoted Ruby literal, which interpolates nothing and so needs only two
    /// characters escaped.
    /// </summary>
    private static string Quote(string value)
    {
        var literal = new StringBuilder("'");

        foreach (var c in value ?? "")
        {
            if (c == '\'' || c == '\\')
                literal.Append('\\');

            literal.Append(c);
        }

        return literal.Append('\'').ToString();
    }

    private static string Symbols(IReadOnlyList<string> names)
        => string.Join(", ", names.Select(name => ":" + name));

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// An attribute name.
    ///
    /// snake_case, and escaped when it lands on a keyword - which it can, because Ruby
    /// members are lowercase and so is nearly every Ruby keyword.
    /// </summary>
    private static string RubyName(string name) => LanguageProfile.Ruby.MemberName(name.ToSnakeCase());

    /// <summary>A constant, SCREAMING_SNAKE_CASE as Ruby writes them.</summary>
    private static string ConstantName(string name) => name.ToSnakeCase().ToUpperInvariant();

}
