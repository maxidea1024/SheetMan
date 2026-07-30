using System;

namespace SheetMan.Targets
{
    /// <summary>
    /// What a target produces, which decides when it runs.
    ///
    /// Exports run before code generation because the generated readers are written to
    /// expect the data files that the exporters produce; when a run is inspected by hand
    /// it reads better for the data to already be there.
    /// </summary>
    public enum TargetKind
    {
        /// <summary>Writes data: files or database storage.</summary>
        Export,

        /// <summary>Writes source code, or documentation about the data.</summary>
        CodeGeneration,
    }

    /// <summary>
    /// Marks a class as a SheetMan output target and gives the registry what it needs to
    /// drive it.
    ///
    /// Adding a target means adding one file with this attribute on it. Nothing else in
    /// the codebase is edited - not <see cref="Program"/>, not the validation pass. That
    /// is the point: the old shape needed a target's name written out in three separate
    /// places, and the database exporters shipped with one of the three missing, so their
    /// target side was never validated.
    ///
    /// Discovery is a scan of this assembly, deliberately. Loading targets from external
    /// assemblies would mean a plugin contract to keep stable across versions, for a tool
    /// whose targets all live in this repository anyway.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class SheetManTargetAttribute : Attribute
    {
        public SheetManTargetAttribute(string id, TargetKind kind, string section)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Kind = kind;
            Section = section ?? throw new ArgumentNullException(nameof(section));
        }

        /// <summary>
        /// Stable short name, lower case. This is what a recipe writes in a dynamic
        /// target entry and what `--help` lists, so changing one is a breaking change.
        /// </summary>
        public string Id { get; }

        /// <summary>What the target produces.</summary>
        public TargetKind Kind { get; }

        /// <summary>
        /// Dotted path of the recipe section this target reads, such as `Exports.Binary`.
        ///
        /// Quoted verbatim in error messages, so it has to match the property name in
        /// <see cref="Recipe.RecipeModel"/> exactly - which the registry checks at startup
        /// rather than trusting.
        /// </summary>
        public string Section { get; }

        /// <summary>
        /// Sort key within a kind; lower runs first. Ties break on <see cref="Id"/> so the
        /// order is total and a run's log is reproducible.
        ///
        /// Targets are independent - each writes to its own destination - so this exists
        /// to keep output stable rather than to satisfy a dependency.
        /// </summary>
        public int Order { get; set; }
    }
}
