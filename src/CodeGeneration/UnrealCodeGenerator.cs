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
    /// Settings for the Unreal target.
    ///
    /// Declared beside its generator and reached through the recipe's `Targets` list.
    /// </summary>
    public sealed class UnrealRecipe : IOutputRecipe
    {
        /// <summary>
        /// Directory the module is written into. The module's own directory is created
        /// underneath it, so this is usually a project's `Source` or a plugin's.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Module name. Names the directory, the Build.cs and the export macro, and is what
        /// another module lists as a dependency.
        /// </summary>
        public string ModuleName { get; set; } = "SheetManData";

        /// <summary>
        /// Name of the accessor class, which also names the header and the .cpp.
        /// </summary>
        public string AccessorName { get; set; } = "FSheetManData";

        /// <summary>
        /// Whether to write the module's Build.cs.
        ///
        /// On by default, so the output is a module a project can add as it stands. Turn it
        /// off to generate into a module that already exists.
        /// </summary>
        public bool WriteBuildFile { get; set; } = true;

            /// <summary>
            /// Whether to write the data updater beside the reader.
            ///
            /// It fetches the manifest and the changed data files over HTTP and keeps a local
            /// copy current, so a build can take new data without shipping a new one. Off by
            /// default: it puts the HTTP module into the generated Build.cs, and a project
            /// that ships its data inside the .pak has no use for either.
            /// </summary>
            public bool WriteUpdater { get; set; } = false;

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
    /// Emits an Unreal module: USTRUCT rows, UENUM enums, a static accessor class, and an
    /// Unreal binary reader.
    ///
    /// Its own reader rather than the plain C++ one. That one was shared here at first, on
    /// the grounds that the wire format lives in it and the conformance corpus already
    /// checks it - but sharing it meant an Unreal module full of std::string, std::vector
    /// and a SheetMan Uuid struct, every one of which the engine already provides. The cost
    /// was two allocations for each string cell and a text parse for each uuid, converting
    /// into what FString and FGuid already were. Worse, that reader reports failure by
    /// throwing, and an Unreal module is built with exceptions disabled: a malformed table
    /// file terminated the process from inside a function whose signature promised a bool.
    ///
    /// So `lib/unreal` is a sibling of `lib/cpp`, not a wrapper around it. The format is
    /// unchanged, so the corpus still applies.
    ///
    /// Written to work on both UE4 and UE5, which costs one thing: a double member carries
    /// no UPROPERTY, because UE4's header tool rejects the type outright. The field is read
    /// and usable from C++ either way; it is only Blueprint that cannot see it.
    ///
    /// The shapes live in templates/unreal.sbn and templates/unreal-cpp.sbn.
    /// </summary>
    [SheetManTarget("unreal", TargetKind.CodeGeneration, Order = 90)]
    public class UnrealCodeGenerator : CodeGenerator<UnrealRecipe>
    {
        private Model _model;
        private UnrealRecipe _recipe;

        protected override void Run(TargetContext context, UnrealRecipe recipe)
        {
            if (string.IsNullOrEmpty(recipe.Path))
                return;

            SweepStaleOutput(recipe.Path, recipe.Sweep);

            _recipe = recipe;
            _model = context.Model;


            var view = BuildView();

            Write(System.IO.Path.Combine("Public", _recipe.AccessorName + ".h"), "unreal.sbn", view);
            Write(System.IO.Path.Combine("Private", _recipe.AccessorName + ".cpp"), "unreal-cpp.sbn", view);

            WriteBinaryReaderRuntime();

            if (_recipe.WriteBuildFile)
                WriteBuildFile();
        }

        private string ModuleDir => System.IO.Path.Combine(_recipe.Path, _recipe.ModuleName);

        private void Write(string relative, string templateName, UnrealFileView view)
        {
            string filename = System.IO.Path.GetFullPath(System.IO.Path.Combine(ModuleDir, relative));

            Log.Information($"Generating codes for Unreal into `{filename}`");

            StagingFiles.WriteAllTextToFile(filename, TemplateEngine.Render(templateName, view));
        }

        /// <summary>
        /// A BlueprintType enum's underlying type must be uint8, so a label outside 0 to 255
        /// cannot be one - and the enum widens to int32 and gives up Blueprint instead.
        /// </summary>
        /// <remarks>
        /// This used to throw and refuse the whole conversion, which made the Unreal target the
        /// only one that could not read a model the other eleven read. The values belong to the
        /// sheet: an enum of network error codes or bit flags is ordinary, and a code generator
        /// does not get to reject it.
        ///
        /// So it degrades, and says which label did it. The enum stays a `UENUM`, so it is still
        /// reflected and still serialises; it loses `BlueprintType`, and the fields typed with it
        /// lose their `UPROPERTY`, because UHT will not expose a property Blueprint cannot see.
        /// Everything remains readable from C++, which is where the data is used.
        ///
        /// Warned rather than silent, because a project that wanted the enum in Blueprint would
        /// otherwise find out from a missing pin.
        /// </remarks>
        private Models.Enum.Label OutOfBlueprintRange(Models.Enum enumm)
        {
            foreach (var label in enumm.Labels)
            {
                if (label.Value < 0 || label.Value > 255)
                    return label;
            }

            return null;
        }

        private void WriteBinaryReaderRuntime()
        {
            // Public, because the generated header includes it and anything including that
            // header needs to find it.
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Unreal.SheetManLiteBinaryReader.h",
                System.IO.Path.Combine(ModuleDir, "Public", "SheetManLiteBinaryReader.h"));

            // Asked for rather than assumed: it reaches the network, and it is what puts the
            // HTTP module into this module's dependencies.
            if (_recipe.WriteUpdater)
            {
                WriteBinaryReaderRuntime(
                    "SheetMan.Runtime.Unreal.SheetManUpdater.h",
                    System.IO.Path.Combine(ModuleDir, "Public", "SheetManUpdater.h"));

                WriteBinaryReaderRuntime(
                    "SheetMan.Runtime.Unreal.SheetManUpdater.cpp",
                    System.IO.Path.Combine(ModuleDir, "Private", "SheetManUpdater.cpp"));
            }
        }

        /// <summary>
        /// Writes the module's Build.cs, so the output is a module a project can add as it
        /// stands rather than a pile of files somebody has to wire up.
        /// </summary>
        private void WriteBuildFile()
        {
            var text = new StringBuilder();

            text.Append("// Generated by SheetMan. DO NOT EDIT.\n");
            text.Append('\n');
            text.Append("using UnrealBuildTool;\n");
            text.Append('\n');
            text.Append("public class ").Append(_recipe.ModuleName).Append(" : ModuleRules\n");
            text.Append("{\n");
            text.Append("    public ").Append(_recipe.ModuleName).Append("(ReadOnlyTargetRules Target) : base(Target)\n");
            text.Append("    {\n");
            text.Append("        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;\n");
            text.Append('\n');
            text.Append("        // Core for FString, TArray, FGuid, FDateTime and the file helpers;\n");
            text.Append("        // CoreUObject for the reflection the USTRUCTs need; Engine for\n");
            text.Append("        // UBlueprintFunctionLibrary, which is what makes the rows reachable\n");
            text.Append("        // from a Blueprint graph at all.\n");
            text.Append("        //\n");
            text.Append("        // No bEnableExceptions: the reader reports a malformed file by returning\n");
            text.Append("        // false, so this module builds with the engine's defaults.\n");

            if (_recipe.WriteUpdater)
            {
                text.Append("        //\n");
                text.Append("        // HTTP is here because the updater is: it fetches the manifest and the\n");
                text.Append("        // changed data files. Turn WriteUpdater off and this goes with it.\n");
            }

            text.Append("        PublicDependencyModuleNames.AddRange(\n");

            // HTTP only when the updater is written. A module that does not patch its data
            // should not carry a dependency on the transport that would.
            text.Append(_recipe.WriteUpdater
                ? "            new string[] { \"Core\", \"CoreUObject\", \"Engine\", \"HTTP\" });\n"
                : "            new string[] { \"Core\", \"CoreUObject\", \"Engine\" });\n");
            text.Append("    }\n");
            text.Append("}\n");

            StagingFiles.WriteAllTextToFile(
                System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(ModuleDir, _recipe.ModuleName + ".Build.cs")),
                text.ToString());
        }

        // --------------------------------------------------------------- view

        private UnrealFileView BuildView() => new UnrealFileView
        {
            AccessorName = _recipe.AccessorName,
            ApiMacro = _recipe.ModuleName.ToUpperInvariant() + "_API",
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = new UnrealAccessorView
            {
                FileExtension = _recipe.BinaryTableFileExtension,
                LibraryName = LibraryName(),
                Tables = _model.Tables.Select(table => new UnrealTableSlotView
                {
                    Name = table.Name.ToPascalCase(),
                    TableName = TableName(table),
                    RecordName = RecordName(table),
                    RawName = table.Name,
                    PrimaryLookup = "FindBy" + PrimaryIndex(table).Name.ToPascalCase(),
                    PrimaryKeyType = Indexes(table)[0].KeyType,
                    PrimaryKeyParam = Indexes(table)[0].KeyParam,
                    PrimaryFieldName = PrimaryIndex(table).Name.ToPascalCase(),

                    // Unescaped: this one names the file the exporter wrote.
                    DataFileName = table.Name,
                }).ToList(),
            },
        };

        private UnrealEnumView BuildEnum(Models.Enum enumm)
        {
            var offender = OutOfBlueprintRange(enumm);

            if (offender != null)
            {
                Log.Warning(
                    $"Enum `{enumm.Name}` label `{offender.Name}` has value {offender.Value}, which does " +
                    "not fit the uint8 a BlueprintType enum has to be. Generating it as a plain int32 " +
                    "UENUM instead: readable from C++, not visible in Blueprint, and neither are the " +
                    "fields typed with it.");
            }

            return new UnrealEnumView
        {
            Name = EnumName(enumm),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            BlueprintVisible = offender == null,
            UnderlyingType = offender == null ? "uint8" : "int32",
            NotVisibleBecause = offender == null
                ? null
                : $"label `{offender.Name}` is {offender.Value}, and a BlueprintType enum is uint8.",
            Labels = enumm.Labels.Select(label => new UnrealEnumLabelView
            {
                Name = label.Name.ToPascalCase(),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                DisplayName = label.Name,
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };
        }

        private UnrealTableView BuildTable(Table table)
        {
            // Worked out before the fields, because a local the generated code declares
            // must not land on a member name. `Index` is the usual name of a primary key
            // here, so a loop counter called that would shadow it in every table that has
            // one - legal, and unambiguous, but not what a generator should emit.
            var members = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sf in table.SerialFields)
            {
                string member = MemberName(sf.FirstField, sf.Name);

                members.Add(sf.IsRef ? member + "Index" : member);
            }

            return new UnrealTableView
            {
                RawName = table.Name,
                RecordName = RecordName(table),
                TableName = TableName(table),
                Location = table.Location.ToString(),
                Comment = CommentLines(table.Comment),
                Indexes = Indexes(table),
                Fields = table.SerialFields.Select(sf => BuildField(table, sf, members)).ToList(),
            };
        }

        /// <summary>
        /// A name for a local the generated code declares, not taken by any member.
        ///
        /// Almost always the preferred one. The suffix only appears for a sheet that has a
        /// column of that name, and then it is still a name and not a collision.
        /// </summary>
        private static string LocalName(string preferred, ICollection<string> members)
        {
            if (!members.Contains(preferred))
                return preferred;

            for (int suffix = 2; ; suffix++)
            {
                string candidate = preferred + suffix.ToString(CultureInfo.InvariantCulture);

                if (!members.Contains(candidate))
                    return candidate;
            }
        }

        private UnrealFieldView BuildField(Table table, SerialField sf, ICollection<string> members)
        {
            string name = MemberName(sf.FirstField, sf.Name);

            return new UnrealFieldView
            {
                CountLocal = LocalName("ElementCount", members),
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                Tag = sf.FirstField.Tag.Value,
                ColumnCheck = ColumnCheck(sf, table.Name.ToPascalCase()),
                ElementCount = sf.Fields.Count,
                Declaration = Declaration(sf, name),

                // Two reasons a member is written without a UPROPERTY, and it is written either
                // way: the value is read and usable from C++, and only Blueprint cannot see it.
                //
                // UE4's header tool rejects a double property and UE5 accepts one, so a double is
                // left unreflected to build on both.
                //
                // And an enum whose labels do not fit uint8 is not a BlueprintType, so UHT will
                // not expose a property of that type either - the enum's own degradation carries
                // through to every field declared with it.
                BlueprintVisible = sf.ElementType != ValueType.Double && !NamesAWideEnum(sf),
                NotVisibleBecause = sf.ElementType == ValueType.Double
                    ? "UE4's header tool does not accept a double property."
                    : WideEnumReason(sf),

                ReadCall = ReadCall(sf),
            };
        }

        /// <summary>
        /// Whether this field is declared with an enum that had to widen past uint8.
        /// </summary>
        private bool NamesAWideEnum(SerialField sf)
            => sf.ElementType == ValueType.Enum && OutOfBlueprintRange(sf.FirstField.Enum) != null;

        private string WideEnumReason(SerialField sf)
        {
            if (sf.ElementType != ValueType.Enum)
                return null;

            var offender = OutOfBlueprintRange(sf.FirstField.Enum);

            return offender == null
                ? null
                : $"`{EnumName(sf.FirstField.Enum)}` is not a BlueprintType - label `{offender.Name}` " +
                  $"is {offender.Value}, and a BlueprintType enum is uint8.";
        }

        /// <summary>
        /// Which reader method fills this field.
        ///
        /// Every type but an enum resolves by overload, because the Unreal reader has an
        /// overload per engine type rather than one that fills a standard C++ value the
        /// caller then converts. An enum cannot: its underlying type is what travels, so
        /// it goes through the template.
        ///
        /// A reference contributes an int32 index, which is an ordinary overload.
        /// </summary>
        private static string ReadCall(SerialField sf)
        {
            if (sf.IsRef)
                return "ReadAs";

            switch (sf.ElementType)
            {
                case ValueType.Enum:
                    return "ReadEnumAs";

                case ValueType.String:
                case ValueType.Bool:
                case ValueType.Int32:
                case ValueType.Int64:
                case ValueType.Float:
                case ValueType.Double:
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                case ValueType.Uuid:
                    return "ReadAs";

                default:
                    throw new SheetManException($"The unreal generator cannot read type `{sf.Type}`.");
            }
        }

        /// <summary>
        /// The rendered CheckColumn call: kind, count, and the elements this member accepts -
        /// its own plus the lossless promotions, decided here at generation time rather than
        /// in the reader.
        /// </summary>
        private static string ColumnCheck(SerialField sf, string tableName)
        {
            string kind = sf.IsVariableLengthArray
                ? "SheetMan::KindVarArray"
                : (sf.Fields.Count > 1 ? "SheetMan::KindFixedArray" : "SheetMan::KindScalar");

            int count = sf.IsVariableLengthArray ? 0 : sf.Fields.Count;

            string[] accepted;

            if (sf.IsRef)
                accepted = new[] { "ElementI32" };
            else
            {
                switch (sf.ElementType)
                {
                    case ValueType.Int32:
                        accepted = new[] { "ElementI32", "ElementVarint" }; break;
                    case ValueType.Int64:
                        accepted = new[] { "ElementI64", "ElementI32", "ElementVarint" }; break;
                    case ValueType.Double:
                        accepted = new[] { "ElementF64", "ElementF32", "ElementI32" }; break;
                    case ValueType.Float: accepted = new[] { "ElementF32" }; break;
                    case ValueType.Bool: accepted = new[] { "ElementBool" }; break;
                    case ValueType.String: accepted = new[] { "ElementString" }; break;
                    case ValueType.Uuid: accepted = new[] { "ElementUuid" }; break;
                    case ValueType.Enum: accepted = new[] { "ElementVarint" }; break;

                    // Ticks are exact i64: reading an int as a datetime would be lossless
                    // and semantically wrong, so no promotion.
                    case ValueType.DateTime:
                    case ValueType.TimeSpan:
                        accepted = new[] { "ElementI64" }; break;

                    default:
                        throw new SheetManException($"The unreal generator cannot check type `{sf.Type}`.");
                }
            }

            string mask = string.Join(
                " | ", accepted.Select(name => $"SheetMan::ElementMask(SheetMan::{name})"));

            return $"SheetMan::CheckColumn(Reader, Column, TEXT(\"{tableName}.{sf.Name}\"), " +
                   $"{kind}, {count}, {mask});";
        }

        /// <summary>
        /// The member declaration.
        ///
        /// A reference contributes only its index; resolving it into a pointer would put a
        /// raw pointer inside a USTRUCT, which the garbage collector does not track. The
        /// caller looks it up, as in the Rust output.
        /// </summary>
        private string Declaration(SerialField sf, string name)
        {
            if (sf.IsRef)
                return sf.IsArray ? $"TArray<int32> {name}Index;" : $"int32 {name}Index = 0;";

            string elementType = ToUnrealTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull);

            if (sf.IsArray)
                return $"TArray<{elementType}> {name};";

            return $"{elementType} {name}{DefaultInitializer(sf.FirstField)};";
        }

        private string DefaultInitializer(Field field)
        {
            switch (field.ElementType)
            {
                // These default-construct themselves.
                case ValueType.String:
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                case ValueType.Uuid:
                    return "";

                case ValueType.Bool: return " = false";
                case ValueType.Float: return " = 0.0f";
                case ValueType.Double: return " = 0.0";
                case ValueType.Enum: return $" = static_cast<{EnumName(field.Enum)}>(0)";
                default: return " = 0";
            }
        }

        private static string ReadKind(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return "var_array";

            if (sf.IsArray)
                return sf.IsRef ? "serial_ref" : "serial";

            return sf.IsRef ? "scalar_ref" : "scalar";
        }

        // ----------------------------------------------------------- rendering

        private string ToUnrealTypeName(ValueType type, Models.Enum enumm)
        {
            switch (ValueTypes.ElementOf(type))
            {
                case ValueType.Enum:
                    return EnumName(enumm);

                // A reference is carried as the target row's index.
                case ValueType.ForeignRecord:
                    return "int32";

                default:
                    return LanguageProfile.Unreal.ScalarTypeName(type);
            }
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// The Blueprint function library's name: `USheetManDataLibrary` for an accessor
        /// called `FSheetManData`.
        ///
        /// Unreal's prefix says what a type is - `U` for a UObject, `F` for a plain class -
        /// so the accessor's `F` comes off before the library's `U` goes on. Prefixing
        /// blindly gave `UFSheetManDataLibrary`.
        /// </summary>
        private string LibraryName()
        {
            string name = _recipe.AccessorName;

            // Only when it is a prefix rather than the first letter of a word: `FSheetMan`
            // loses its F, and `Foo` does not.
            if (name.Length > 1 && name[0] == 'F' && char.IsUpper(name[1]))
                name = name.Substring(1);

            return "U" + name + "Library";
        }

        /// <summary>Unreal prefixes an enum with E.</summary>
        private static string EnumName(Models.Enum enumm) => "E" + enumm.Name.ToPascalCase();

        /// <summary>Unreal prefixes a struct with F. A row is a struct.</summary>
        private static string RecordName(Table table) => "F" + table.Name.ToPascalCase() + "Row";

        /// <summary>The table class is a plain C++ class, which Unreal also prefixes with F.</summary>
        private static string TableName(Table table) => "F" + table.Name.ToPascalCase() + "Table";

        /// <summary>
        /// The indexed fields of a table: the sheet's first column, plus every one marked
        /// with a `*`.
        /// </summary>
        private IReadOnlyList<UnrealIndexView> Indexes(Table table)
            => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf =>
            {
                string keyType = ToUnrealTypeName(sf.FirstField.ElementType, sf.FirstField.EnumOrNull);
                bool copyCosts = keyType == "FString";

                return new UnrealIndexView
                {
                    Member = MemberName(sf.FirstField, sf.Name),
                    Suffix = sf.Name.ToPascalCase(),
                    KeyType = keyType,
                    KeyParam = copyCosts ? "const " + keyType + "&" : keyType,
                    MapName = "By" + sf.Name.ToPascalCase(),
                    LocalName = "LoadedBy" + sf.Name.ToPascalCase(),
                    FieldName = sf.Name.ToPascalCase(),
                };
            }).ToList();

        /// <summary>The field a `foreign` column's key is looked up in: the first index.</summary>
        private static SerialField PrimaryIndex(Table table)
            => table.SerialFields.First(sf => sf.IsIndexer);

        private static string MemberName(Field field) => MemberName(field, field.Name);

        /// <summary>
        /// A member name, PascalCase as Unreal writes them.
        ///
        /// A boolean gets the `b` prefix the engine's own style uses, which is worth
        /// following here because the generated types show up beside the engine's in the
        /// editor.
        /// </summary>
        private static string MemberName(Field field, string name)
        {
            string cased = LanguageProfile.Unreal.MemberName(name.ToPascalCase());

            return field.ElementType == ValueType.Bool ? "b" + cased : cased;
        }

    }
}
