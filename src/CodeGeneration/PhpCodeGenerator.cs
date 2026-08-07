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
/// Settings for the PHP target.
///
/// Declared beside its generator and reached through the recipe's `Targets` list.
/// </summary>
public sealed class PhpRecipe : IOutputRecipe
{
    /// <summary>Directory the generated file and the reader are written into.</summary>
    public string Path { get; set; } = "";

    /// <summary>Namespace the generated file declares.</summary>
    public string Namespace { get; set; } = "GameData";

    /// <summary>Name of the accessor class, which also names the file.</summary>
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
    /// default: one that ships its data alongside its code has no use for it, it is the
    /// only generated file that reaches the network, and it is the only one that wants
    /// an extension - `ext-curl`.
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
/// Emits one PHP file holding every generated type, plus the binary reader.
///
/// PHP 8.1 or later, for two things worth having: backed enums, so an enum carries its
/// declared value rather than needing a lookup table beside it, and typed properties,
/// so a record says what it holds.
///
/// int64 is `int` and that is safe here, unlike in TypeScript and Dart: PHP's integer
/// is a full 64 bits on any 64 bit build, so 2^53+1 survives. What is not safe is
/// reading it with `unpack('P')`, which the reader explains.
///
/// The shape lives in templates/php.sbn.
/// </summary>
[SheetManTarget("php", TargetKind.CodeGeneration, Order = 87)]
public class PhpCodeGenerator : CodeGenerator<PhpRecipe>
{
    private Model _model;
    private PhpRecipe _recipe;

    protected override void Run(TargetContext context, PhpRecipe recipe)
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
    /// code inside a file that still parsed. The layout matches the C#, Kotlin and
    /// TypeScript targets.
    ///
    /// PHP is the one that needs wiring rather than just splitting: there is no autoloader
    /// here, so each file requires what it uses. A table requires the reader and any enum
    /// its properties are typed as, from <see cref="TypeDependencies"/>; the accessor requires
    /// every part, so a consumer still includes one file and gets the model.
    ///
    /// Not the tables a table references. A reference resolves to the other table's record or
    /// to one of its fields, and the accessor has required both files long before it links
    /// them - `require_once` would be harmless either way, but a require that is never the
    /// reason a name resolves is one more line for a reader to check.
    /// </remarks>
    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for PHP into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        // Root level, so the reader is one directory down and the parts are beside it.
        var accessorRequires = new List<string> { Require(0, "sheetman/LiteBinaryReader.php") };

        accessorRequires.AddRange(view.Enums.Select(e => Require(0, $"enums/{e.Name}.php")));
        accessorRequires.AddRange(view.ConstantSets.Select(s => Require(0, $"constants/{s.Name}.php")));
        accessorRequires.AddRange(view.Tables.Select(t => Require(0, $"tables/{t.TableName}.php")));

        Write(_recipe.AccessorName + ".php", "php-accessor.sbn", new PhpPartView
        {
            Namespace = _recipe.Namespace,
            Requires = accessorRequires,
            Tables = view.Tables,
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // One directory down, and it needs whatever enums its properties name.
            var requires = new List<string> { Require(1, "sheetman/LiteBinaryReader.php") };

            requires.AddRange(TypeDependencies.EnumsNamedBy(pair.model)
                .Select(enumm => Require(1, $"enums/{EnumName(enumm)}.php")));

            Write(System.IO.Path.Combine("tables", pair.rendered.TableName + ".php"), "php-table.sbn",
                  new PhpPartView
                  {
                      Namespace = _recipe.Namespace,
                      Requires = requires,
                      Table = pair.rendered,
                  });
        }

        // An enum and a constant class name nothing outside themselves: a backed enum is
        // its own declaration, and a constant renders as a literal.
        foreach (var enumm in view.Enums)
        {
            Write(System.IO.Path.Combine("enums", enumm.Name + ".php"), "php-enum.sbn",
                  new PhpPartView
                  {
                      Namespace = _recipe.Namespace,
                      Requires = Array.Empty<string>(),
                      Enumm = enumm,
                  });
        }

        foreach (var set in view.ConstantSets)
        {
            Write(System.IO.Path.Combine("constants", set.Name + ".php"), "php-constants.sbn",
                  new PhpPartView
                  {
                      Namespace = _recipe.Namespace,
                      Requires = Array.Empty<string>(),
                      Set = set,
                  });
        }
    }

    /// <summary>
    /// A `require_once` line, relative to a file <paramref name="depth"/> directories below
    /// the output root.
    /// </summary>
    /// <remarks>
    /// Forward slashes, which PHP accepts on every platform - and which keep the generated
    /// text the same wherever the conversion ran.
    /// </remarks>
    private static string Require(int depth, string fromRoot)
    {
        string up = string.Concat(Enumerable.Repeat("/..", depth));

        return $"require_once __DIR__ . '{up}/{fromRoot}';";
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
            "SheetMan.Runtime.Php.LiteBinaryReader.php",
            System.IO.Path.Combine(_recipe.Path, "sheetman", "LiteBinaryReader.php"));

        // Asked for rather than assumed. It reaches the network, it wants `ext-curl`,
        // and it is of no use to a program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Php.SheetManUpdater.php",
                System.IO.Path.Combine(_recipe.Path, "sheetman", "SheetManUpdater.php"));
        }
    }

    // --------------------------------------------------------------- view

    private PhpFileView BuildView() => new PhpFileView
    {
        Namespace = _recipe.Namespace,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private PhpEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new PhpEnumView
        {
            Name = EnumName(enumm),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            DefaultCase = CaseName(fallback.Name),
            Cases = enumm.Labels.Select(label => new PhpEnumCaseView
            {
                Name = CaseName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };
    }

    private PhpConstantSetView BuildConstantSet(ConstantSet constantSet) => new PhpConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new PhpConstantView
        {
            Name = ConstantName(constant.Name),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private PhpTableView BuildTable(Table table) => new PhpTableView
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
    private IReadOnlyList<PhpIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf =>
        {
            string keyType = ResolvedElementType(sf);

            return new PhpIndexView
            {
                Member = PhpName(sf.Name),
                Suffix = sf.Name.ToPascalCase(),
                KeyType = keyType,

                // A PHP array is keyed by int or string and nothing else, so that is
                // what the docblock can honestly claim whatever the column holds.
                KeyDocType = keyType == "string" ? "string" : "int",

                MapName = "by" + sf.Name.ToPascalCase(),
                LocalName = "$by" + sf.Name.ToPascalCase(),
                FieldName = sf.Name.ToPascalCase(),
            };
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

    private PhpFieldView BuildField(Table table, SerialField sf)
    {
        string name = PhpName(sf.Name);

        return new PhpFieldView
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
    /// The property declarations, each typed and initialized.
    ///
    /// Initialized rather than left uninitialized, because reading a typed property
    /// that was never assigned is an Error in PHP - where every other generated reader
    /// hands back a default.
    /// </summary>
    private IReadOnlyList<string> Declarations(SerialField sf, string name)
    {
        string elementType = ResolvedElementType(sf);

        if (sf.IsRef)
        {
            // A reference contributes two properties: the index off the wire, and the
            // record it resolves to once every table is loaded. The resolved one is
            // nullable because a reference into a row that is not there stays null
            // rather than inventing a record.
            return sf.IsArray
                ? new[]
                {
                    $"/** @var list<?{elementType}> */",
                    $"public array ${name} = [];",
                    "",
                    "/** @var list<int> */",
                    $"public array ${name}Index = [];",
                }
                : new[]
                {
                    $"public ?{elementType} ${name} = null;",
                    "",
                    $"public int ${name}Index = 0;",
                };
        }

        if (sf.IsArray)
        {
            return new[]
            {
                $"/** @var list<{elementType}> */",
                $"public array ${name} = [];",
            };
        }

        // A uuid is the one scalar that cannot be defaulted in place: a property
        // initializer has to be a constant expression and `new Uuid(...)` is not. So
        // the property is nullable and starts null, which is also honest - it holds
        // nothing until the record is read.
        if (sf.ElementType == ValueType.Uuid)
            return new[] { $"public ?{elementType} ${name} = null;" };

        return new[] { $"public {elementType} ${name} = {DefaultValue(sf)};" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "''";
            case ValueType.Bool: return "false";
            case ValueType.Float:
            case ValueType.Double: return "0.0";

            case ValueType.Enum:
                return $"{EnumName(sf.FirstField.Enum)}::{DefaultCaseOf(sf.FirstField.Enum)}";

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
            ? "LiteBinaryReader::KIND_VAR_ARRAY"
            : (sf.Fields.Count > 1 ? "LiteBinaryReader::KIND_FIXED_ARRAY" : "LiteBinaryReader::KIND_SCALAR");

        int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

        string accepted;

        if (sf.IsRef)
            accepted = "LiteBinaryReader::ELEMENT_I32";
        else
        {
            switch (sf.ElementType)
            {
                case ValueType.Int32:
                    accepted = "LiteBinaryReader::ELEMENT_I32, LiteBinaryReader::ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "LiteBinaryReader::ELEMENT_I64, LiteBinaryReader::ELEMENT_I32, LiteBinaryReader::ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "LiteBinaryReader::ELEMENT_F64, LiteBinaryReader::ELEMENT_F32, LiteBinaryReader::ELEMENT_I32"; break;
                case ValueType.Float: accepted = "LiteBinaryReader::ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "LiteBinaryReader::ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "LiteBinaryReader::ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "LiteBinaryReader::ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "LiteBinaryReader::ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "LiteBinaryReader::ELEMENT_I64"; break;

                default:
                    throw new SheetManException($"The php generator cannot check type `{sf.Type}`.");
            }
        }

        return $"LiteBinaryReader::checkColumn($column, '{tableName}.{sf.Name}', {kind}, {count}, [{accepted}]);";
    }

    private static string ReadKind(SerialField sf)
    {
        if (sf.IsVariableLengthArray)
            return "var_array";

        if (sf.IsArray)
            return sf.IsRef ? "serial_ref" : "serial";

        return sf.IsRef ? "scalar_ref" : "scalar";
    }

    private PhpAccessorView BuildAccessor() => new PhpAccessorView
    {
        Name = _recipe.AccessorName.ToPascalCase(),
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new PhpTableSlotView
        {
            Name = PhpName(table.Name),
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
            .Select(x => new PhpCrossReferenceView
            {
                Table = PhpName(x.Table.Name),
                Fields = x.Fields.Select(sf => new PhpReferenceFieldView
                {
                    Name = PhpName(sf.Name),
                    RefTable = PhpName(sf.FirstField.ResolvedRefTable.Name),
                    RefLookup = PrimaryLookup(sf.FirstField.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "$target"
                        : "$target->" + PhpName(sf.FirstField.ResolvedRefField.Name),
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

            // Enum values travel zig-zag encoded. `tryFrom` rather than `from`, so a
            // value the sheet never declared lands on the fallback instead of throwing
            // - which is what the other generated readers do.
            case ValueType.Enum:
            {
                string name = EnumName(sf.FirstField.Enum);

                return $"{name}::tryFrom($reader->readEnum()) ?? {name}::{DefaultCaseOf(sf.FirstField.Enum)}";
            }

            case ValueType.ForeignRecord: return "$reader->readInt32()";

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in nine other generators.
            default: return LanguageProfile.Php.ReadCall(sf.ElementType);
        }
    }

    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return sf.FirstField.ResolvedRefTable.Name.ToPascalCase() + "Record";

        if (sf.ElementType == ValueType.Enum)
            return EnumName(sf.FirstField.Enum);

        return LanguageProfile.Php.ScalarTypeName(sf.FirstField.ElementType);
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

            // The text, not a Uuid: a class constant has to be a constant expression
            // and `new` is not one. The caller builds a Uuid from it if it wants one.
            case ValueType.Uuid:
                return Quote(((Guid)constant.Value).ToString());

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                return $"{EnumName(constant.Enum)}::{CaseName(label.Name)}";
            }

            default:
                throw new SheetManException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the php generator cannot render.");
        }
    }

    /// <summary>
    /// A single-quoted PHP string.
    ///
    /// Single quotes because they interpolate nothing: a value holding `$name` or a
    /// backslash escape would otherwise be evaluated rather than stored. Only the quote
    /// and the backslash need escaping inside them.
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

    // ------------------------------------------------------------- helpers

    private static string EnumName(Models.Enum enumm) => enumm.Name.ToPascalCase();

    /// <summary>An enum case, PascalCase as PHP's own enums are written.</summary>
    private static string CaseName(string name) => name.ToPascalCase();

    private static string DefaultCaseOf(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return CaseName(fallback.Name);
    }

    /// <summary>A class constant, SCREAMING_SNAKE_CASE as PHP writes them.</summary>
    private static string ConstantName(string name) => name.ToSnakeCase().ToUpperInvariant();

    /// <summary>A property name, camelCase.</summary>
    private static string PhpName(string name) => LanguageProfile.Php.MemberName(name.ToCamelCase());

}
