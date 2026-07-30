using System.Collections.Generic;
using SheetMan.Models;
using SheetMan.Recipe;

namespace SheetMan.Targets
{
    /// <summary>
    /// Implemented by every recipe entry that produces output.
    ///
    /// Exists so the registry can read an entry's target side without reflection, which
    /// also means a new entry type that forgets the property does not compile.
    /// </summary>
    public interface IOutputRecipe
    {
        /// <summary>
        /// Which side of the data this entry is built for: `cs` for both, or `c` / `s`.
        ///
        /// Text rather than the enum because it comes from JSON, and a typo should be
        /// reported against the recipe section it appeared in rather than silently
        /// deserializing to the default.
        /// </summary>
        string TargetSide { get; }
    }

    /// <summary>
    /// One unit of work: a single recipe entry, with the model already narrowed for it.
    /// </summary>
    public sealed class TargetContext
    {
        public TargetContext(Options options, RecipeModel recipe, Model model, IOutputRecipe entry, string section)
        {
            Options = options;
            Recipe = recipe;
            Model = model;
            Entry = entry;
            Section = section;
        }

        /// <summary>Command line options for the run.</summary>
        public Options Options { get; }

        /// <summary>The whole recipe, for the settings that apply across targets.</summary>
        public RecipeModel Recipe { get; }

        /// <summary>
        /// The model narrowed to <see cref="Entry"/>'s target side.
        ///
        /// The registry projects it, so a target cannot forget to - which used to be a
        /// line copied into each one.
        /// </summary>
        public Model Model { get; }

        /// <summary>The recipe entry being run.</summary>
        public IOutputRecipe Entry { get; }

        /// <summary>Dotted recipe path of the section this entry came from.</summary>
        public string Section { get; }
    }

    /// <summary>
    /// A target, as the registry sees it. Implement <see cref="Target{TEntry}"/> rather
    /// than this.
    /// </summary>
    public interface ITarget
    {
        /// <summary>This target's entries, in the order the recipe lists them.</summary>
        IEnumerable<IOutputRecipe> Entries(RecipeModel recipe);

        /// <summary>Runs one entry.</summary>
        void Run(TargetContext context);
    }

    /// <summary>
    /// Base for a target that reads one strongly typed recipe section.
    ///
    /// The generic parameter keeps the cast to the entry type in one place here instead of
    /// at the top of every target.
    /// </summary>
    public abstract class Target<TEntry> : ITarget
        where TEntry : class, IOutputRecipe
    {
        /// <summary>The recipe section this target reads.</summary>
        protected abstract IEnumerable<TEntry> Select(RecipeModel recipe);

        /// <summary>
        /// Runs one entry against <see cref="TargetContext.Model"/>, which is already
        /// narrowed to the entry's target side.
        /// </summary>
        protected abstract void Run(TargetContext context, TEntry entry);

        IEnumerable<IOutputRecipe> ITarget.Entries(RecipeModel recipe) => Select(recipe);

        void ITarget.Run(TargetContext context) => Run(context, (TEntry)context.Entry);
    }
}
