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
        /// Extension of the table files the generated reader opens. Must match what the
        /// binary exporter was told to write.
        /// </summary>
        public string BinaryTableFileExtension { get; set; } = ".table";

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

            _recipe = recipe;
            _model = context.Model;

            VerifyEnumsFitBlueprint();

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
        /// cannot be represented.
        ///
        /// Checked here rather than left to the header tool, which reports it as a parse
        /// failure some distance from the value that caused it - and only after a build has
        /// been started.
        /// </summary>
        private void VerifyEnumsFitBlueprint()
        {
            foreach (var enumm in _model.Enums)
            {
                foreach (var label in enumm.Labels)
                {
                    if (label.Value >= 0 && label.Value <= 255)
                        continue;

                    throw new SheetManException(label.Location,
                        $"Enum `{enumm.Name}` label `{label.Name}` has value {label.Value}, which the " +
                        "unreal target cannot represent: a BlueprintType enum is uint8, so every label " +
                        "must be between 0 and 255.");
                }
            }
        }

        private void WriteBinaryReaderRuntime()
        {
            // Public, because the generated header includes it and anything including that
            // header needs to find it.
            WriteBinaryReaderRuntime(
                "SheetMan.Runtime.Unreal.SheetManLiteBinaryReader.h",
                System.IO.Path.Combine(ModuleDir, "Public", "SheetManLiteBinaryReader.h"));
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
            text.Append("        // Nothing else, and no bEnableExceptions: the reader reports a malformed\n");
            text.Append("        // file by returning false, so this module builds with the engine's defaults.\n");
            text.Append("        PublicDependencyModuleNames.AddRange(\n");
            text.Append("            new string[] { \"Core\", \"CoreUObject\", \"Engine\" });\n");
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

                    // Unescaped: this one names the file the exporter wrote.
                    DataFileName = table.Name,
                }).ToList(),
            },
        };

        private UnrealEnumView BuildEnum(Models.Enum enumm) => new UnrealEnumView
        {
            Name = EnumName(enumm),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            Labels = enumm.Labels.Select(label => new UnrealEnumLabelView
            {
                Name = label.Name.ToPascalCase(),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                DisplayName = label.Name,
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };

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
                IndexField = MemberName(table.Fields[0]),
                Fields = table.SerialFields.Select(sf => BuildField(sf, members)).ToList(),
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

        private UnrealFieldView BuildField(SerialField sf, ICollection<string> members)
        {
            string name = MemberName(sf.FirstField, sf.Name);

            return new UnrealFieldView
            {
                CountLocal = LocalName("ElementCount", members),
                Comment = CommentLines(sf.FirstField.Comment),
                Name = name,
                Kind = ReadKind(sf),
                ElementCount = sf.Fields.Count,
                Declaration = Declaration(sf, name),

                // UE4's header tool rejects a double UPROPERTY and UE5 accepts one. The
                // member is written either way and left unreflected, which builds on both -
                // the value is read and usable from C++, and only Blueprint cannot see it.
                BlueprintVisible = sf.ElementType != ValueType.Double,
                NotVisibleBecause = "UE4's header tool does not accept a double property.",

                ReadCall = ReadCall(sf),
            };
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
                return "Read";

            switch (sf.ElementType)
            {
                case ValueType.Enum:
                    return "ReadEnum";

                case ValueType.String:
                case ValueType.Bool:
                case ValueType.Int32:
                case ValueType.Int64:
                case ValueType.Float:
                case ValueType.Double:
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                case ValueType.Uuid:
                    return "Read";

                default:
                    throw new SheetManException($"The unreal generator cannot read type `{sf.Type}`.");
            }
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
