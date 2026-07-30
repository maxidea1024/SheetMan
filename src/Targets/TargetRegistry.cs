using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Serilog;
using SheetMan.Models;
using SheetMan.Recipe;

namespace SheetMan.Targets
{
    /// <summary>One registered target and the metadata its attribute declared.</summary>
    public sealed class TargetDescriptor
    {
        internal TargetDescriptor(string id, TargetKind kind, string section, int order, ITarget target)
        {
            Id = id;
            Kind = kind;
            Section = section;
            Order = order;
            Target = target;
        }

        /// <summary>Stable short name, such as `binary` or `csharp`.</summary>
        public string Id { get; }

        /// <summary>What the target produces.</summary>
        public TargetKind Kind { get; }

        /// <summary>Dotted recipe path this target reads, such as `Exports.Binary`.</summary>
        public string Section { get; }

        /// <summary>Sort key within a kind.</summary>
        public int Order { get; }

        /// <summary>The target itself.</summary>
        public ITarget Target { get; }

        public override string ToString() => $"{Id} ({Section})";
    }

    /// <summary>
    /// One recipe entry paired with the target that will run it.
    /// </summary>
    public readonly struct PlannedTarget
    {
        internal PlannedTarget(TargetDescriptor descriptor, IOutputRecipe entry)
        {
            Descriptor = descriptor;
            Entry = entry;
        }

        public TargetDescriptor Descriptor { get; }

        public IOutputRecipe Entry { get; }

        /// <summary>The entry's target side, resolved and reported against its section.</summary>
        public TargetSide Side => RecipeTargetSide.Of(Entry.TargetSide, Descriptor.Section);
    }

    /// <summary>
    /// Every output target in this assembly, found by attribute.
    ///
    /// This replaced a run of hand-written `if (recipe.X.Y.Count > 0)` blocks in
    /// <see cref="Program"/>, plus a second hand-written list in the validation pass that
    /// had to name the same sections again. The two lists had drifted: all four database
    /// sections were missing from the validation one, so a recipe whose only server-side
    /// output was a database export had its cross-side references left unchecked. Deriving
    /// both from one registry is what stops that from recurring.
    /// </summary>
    public static class TargetRegistry
    {
        private static readonly Lazy<IReadOnlyList<TargetDescriptor>> LazyAll =
            new Lazy<IReadOnlyList<TargetDescriptor>>(Discover);

        /// <summary>
        /// All registered targets, ordered by kind, then <see cref="TargetDescriptor.Order"/>,
        /// then id.
        /// </summary>
        public static IReadOnlyList<TargetDescriptor> All => LazyAll.Value;

        /// <summary>
        /// Every entry the recipe asks for, in the order they will run.
        ///
        /// Both the run and the validation pass read this, so they cannot disagree about
        /// what the recipe requested.
        /// </summary>
        public static IEnumerable<PlannedTarget> Plan(RecipeModel recipe)
        {
            foreach (var descriptor in All)
            {
                foreach (var entry in descriptor.Target.Entries(recipe))
                    yield return new PlannedTarget(descriptor, entry);
            }
        }

        /// <summary>
        /// Runs every entry the recipe asks for.
        ///
        /// The model is narrowed here rather than inside each target, so a target reads
        /// only what its entry is entitled to and none of them can forget to project.
        /// </summary>
        public static void RunAll(Options options, RecipeModel recipe, Model model)
        {
            foreach (var planned in Plan(recipe))
            {
                var sided = model.ProjectTo(planned.Side);

                planned.Descriptor.Target.Run(
                    new TargetContext(options, recipe, sided, planned.Entry, planned.Descriptor.Section));
            }
        }

        private static IReadOnlyList<TargetDescriptor> Discover()
        {
            var descriptors = new List<TargetDescriptor>();

            foreach (var type in typeof(TargetRegistry).Assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<SheetManTargetAttribute>();
                if (attribute == null)
                    continue;

                if (type.IsAbstract || !typeof(ITarget).IsAssignableFrom(type))
                {
                    throw new SheetManException(
                        $"`{type.Name}` is marked [SheetManTarget] but is not a concrete {nameof(ITarget)}.");
                }

                // A recipe section that does not exist would surface much later, as a
                // section name quoted in an error message that no one can find in their
                // recipe. Cheaper to refuse to start.
                VerifySectionExists(attribute.Section);

                descriptors.Add(new TargetDescriptor(
                    attribute.Id,
                    attribute.Kind,
                    attribute.Section,
                    attribute.Order,
                    (ITarget)Activator.CreateInstance(type)));
            }

            var duplicate = descriptors.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                                       .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new SheetManException($"Two targets both claim the id `{duplicate.Key}`.");

            descriptors.Sort((left, right) =>
            {
                int byKind = left.Kind.CompareTo(right.Kind);
                if (byKind != 0)
                    return byKind;

                int byOrder = left.Order.CompareTo(right.Order);
                if (byOrder != 0)
                    return byOrder;

                return string.CompareOrdinal(left.Id, right.Id);
            });

            Log.Debug($"Registered {descriptors.Count} target(s): {string.Join(", ", descriptors.Select(d => d.Id))}");

            return descriptors;
        }

        /// <summary>
        /// Walks a dotted path such as `Exports.Binary` down <see cref="RecipeModel"/>'s
        /// properties, throwing if any step is not there.
        /// </summary>
        private static void VerifySectionExists(string section)
        {
            var current = typeof(RecipeModel);

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            foreach (var part in section.Split('.'))
            {
                // Fields as well as properties, because the recipe model has held both -
                // `CodeGenerations` was a public field while every other group was a
                // property, and Newtonsoft serializes either, so nothing had ever forced
                // the two into agreement.
                var property = current.GetProperty(part, flags);

                Type next = property != null
                    ? property.PropertyType
                    : current.GetField(part, flags)?.FieldType;

                if (next == null)
                {
                    throw new SheetManException(
                        $"Target declares recipe section `{section}`, but `{current.Name}` has no `{part}`.");
                }

                current = next;
            }
        }
    }
}
