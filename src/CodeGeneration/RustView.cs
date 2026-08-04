using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Everything Rust needs, worked out in advance.
    ///
    /// Built whole and then dealt out one subject at a time, so the naming is decided in one
    /// place whether or not the file it lands in holds anything else.
    /// </summary>
    internal sealed class RustFileView
    {
        public IReadOnlyList<RustEnumView> Enums { get; set; }
        public IReadOnlyList<RustConstantSetView> ConstantSets { get; set; }
        public IReadOnlyList<RustTableView> Tables { get; set; }
        public RustAccessorView Accessor { get; set; }
    }

    /// <summary>
    /// One generated file: what it brings into scope, and the single thing it declares.
    /// </summary>
    internal sealed class RustPartView
    {
        /// <summary>
        /// `use` lines, from <see cref="TypeDependencies"/> and from what the file's own text
        /// reaches for. Exact rather than generous, because an unused one is a warning.
        /// </summary>
        public IReadOnlyList<string> Uses { get; set; }

        /// <summary>
        /// Lines for an inner doc comment, for the file whose whole contents are the subject.
        /// Empty for the files whose comment attaches to an item instead.
        /// </summary>
        public IReadOnlyList<string> ModuleDoc { get; set; }

        /// <summary>The table this file is for, when it is a table file.</summary>
        public RustTableView Table { get; set; }

        /// <summary>The enum this file is for, when it is an enum file.</summary>
        public RustEnumView Enumm { get; set; }

        /// <summary>The constant set this file is for, when it is a constants file.</summary>
        public RustConstantSetView Set { get; set; }

        /// <summary>The accessor's own shape, for the accessor file.</summary>
        public RustAccessorView Accessor { get; set; }
    }

    internal sealed class RustEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<RustEnumLabelView> Labels { get; set; }
    }

    internal sealed class RustEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// Whether this label carries the `#[default]` attribute.
        ///
        /// Deriving Default on an enum needs exactly one variant marked, so the choice is
        /// made here rather than left to the template: the zero label when there is one,
        /// and the first otherwise.
        /// </summary>
        public bool IsDefault { get; set; }
    }

    internal sealed class RustConstantSetView
    {
        public string ModuleName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<RustConstantView> Constants { get; set; }
    }

    internal sealed class RustConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class RustTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Name of the primary index member.</summary>
        public string IndexField { get; set; }

        public IReadOnlyList<RustFieldView> Fields { get; set; }
    }

    internal sealed class RustFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The struct's field declarations, `name: type,` each.
        ///
        /// A reference contributes only its index. Resolving it into a borrow of another
        /// record would make the row own its neighbours, which Rust does not allow without
        /// lifetimes through every generated type or a cell around every row; the caller
        /// looks the index up instead.
        /// </summary>
        public IReadOnlyList<string> Declarations { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        public int ElementCount { get; set; }

        public string ReadScalar { get; set; }

        public string ReadElement { get; set; }
    }

    internal sealed class RustAccessorView
    {
        public string FileExtension { get; set; }
        public IReadOnlyList<RustTableSlotView> Tables { get; set; }
    }

    internal sealed class RustTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }
}
