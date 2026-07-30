using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SheetMan.Sources;
using SheetMan.Targets;

namespace SheetMan.Recipe
{
    /// <summary>
    /// Writes the starting recipe that `--new-recipe` produces.
    ///
    /// It used to serialize a default <see cref="RecipeModel"/>, which meant every list came
    /// out as `[]`. That names the sections but not the settings, so the reader learns that
    /// `Exports.Binary` exists and nothing about what belongs in it - the note left on the
    /// option said as much.
    ///
    /// So each list gets one entry with its defaults filled in, and the file opens with the
    /// registered source and target ids. Every field is then visible with the value it would
    /// take, and an entry left as-is is inert: a blank path or connection string is how a
    /// target is switched off.
    ///
    /// The entries are produced by walking the model rather than from a template, so a
    /// setting added to the model appears here without anyone remembering to add it.
    /// </summary>
    internal static class RecipeSkeleton
    {
        public static void WriteToFile(string filename)
        {
            var recipe = new RecipeModel();

            FillLists(recipe);

            string json = JsonConvert.SerializeObject(recipe, Formatting.Indented);

            File.WriteAllText(filename, Header() + json + Environment.NewLine);
        }

        private static string Header()
        {
            var header = new StringBuilder();

            header.AppendLine("// SheetMan recipe, created by --new-recipe.");
            header.AppendLine("//");
            header.AppendLine("// `//` comments are allowed anywhere in this file.");
            header.AppendLine("//");
            header.AppendLine("// Each list below holds one entry with its default settings, so that every option");
            header.AppendLine("// is visible. Fill in the ones you want and delete the rest - though an entry with");
            header.AppendLine("// a blank Path or ConnectionString is treated as switched off, so leaving one in");
            header.AppendLine("// place costs nothing.");
            header.AppendLine("//");
            header.AppendLine("// Output can also be listed by target name, which is the only form available to");
            header.AppendLine("// targets that have no section of their own:");
            header.AppendLine("//");
            header.AppendLine("//   \"Targets\": [ { \"Type\": \"csharp\", \"Path\": \"./out/cs\" } ]");
            header.AppendLine("//");
            header.AppendLine($"// Sources: {SourceRegistry.KnownIds}");
            header.AppendLine($"// Targets: {TargetRegistry.KnownIds}");
            header.AppendLine();

            return header.ToString();
        }

        /// <summary>
        /// Gives every empty entry list one default-constructed element, recursing through
        /// the recipe's groups.
        /// </summary>
        private static void FillLists(object owner)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            var members = new List<MemberInfo>();
            members.AddRange(owner.GetType().GetProperties(flags));
            members.AddRange(owner.GetType().GetFields(flags));

            foreach (var member in members)
            {
                var type = TypeOf(member);
                object value = ValueOf(member, owner);

                if (value == null)
                    continue;

                if (IsEntryList(type))
                {
                    var elementType = type.GetGenericArguments()[0];

                    // `Targets` holds raw JSON, because its element type is not known until
                    // the entry's `Type` is read. An empty object here would be a `Targets`
                    // entry naming no target, which the registry rejects - so the header
                    // shows the shape instead.
                    if (elementType == typeof(JObject))
                        continue;

                    var list = (IList)value;
                    if (list.Count == 0)
                        list.Add(Activator.CreateInstance(elementType));

                    continue;
                }

                if (IsRecipeGroup(type))
                    FillLists(value);
            }
        }

        private static bool IsEntryList(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

        /// <summary>
        /// One of the recipe's own grouping objects - `Sources`, `Exports`, `CodeGenerations`
        /// - as opposed to a setting.
        /// </summary>
        private static bool IsRecipeGroup(Type type)
            => type.IsClass
               && type != typeof(string)
               && !type.IsGenericType
               && type.Assembly == typeof(RecipeModel).Assembly;

        private static Type TypeOf(MemberInfo member)
            => member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

        private static object ValueOf(MemberInfo member, object owner)
            => member is PropertyInfo property ? property.GetValue(owner) : ((FieldInfo)member).GetValue(owner);
    }
}
