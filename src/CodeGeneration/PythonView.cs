using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Everything Python needs, worked out in advance.
    ///
    /// Built whole and then dealt out one subject at a time, so the naming is decided in one
    /// place whether or not the file it lands in holds anything else.
    /// </summary>
    internal sealed class PythonFileView
    {
        public IReadOnlyList<PythonEnumView> Enums { get; set; }
        public IReadOnlyList<PythonConstantSetView> ConstantSets { get; set; }
        public IReadOnlyList<PythonTableView> Tables { get; set; }
        public PythonAccessorView Accessor { get; set; }
    }

    /// <summary>
    /// One generated file: the imports it needs, and the single thing it declares.
    /// </summary>
    internal sealed class PythonPartView
    {
        /// <summary>
        /// Relative imports naming the generated types this file uses, from
        /// <see cref="TypeDependencies"/>. The standard library ones every file gets are in the
        /// shared header.
        /// </summary>
        public IReadOnlyList<string> Imports { get; set; }

        /// <summary>The table this file is for, when it is a table file.</summary>
        public PythonTableView Table { get; set; }

        /// <summary>The enum this file is for, when it is an enum file.</summary>
        public PythonEnumView Enumm { get; set; }

        /// <summary>The constant set this file is for, when it is a constants file.</summary>
        public PythonConstantSetView Set { get; set; }

        /// <summary>The accessor's own shape, for the accessor file.</summary>
        public PythonAccessorView Accessor { get; set; }
    }

    internal sealed class PythonEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<PythonEnumLabelView> Labels { get; set; }

        /// <summary>
        /// The value an undeclared one falls back to: the zero label when there is one, and
        /// the first otherwise.
        /// </summary>
        public string DefaultValue { get; set; }
    }

    internal sealed class PythonEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class PythonConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<PythonConstantView> Constants { get; set; }
    }

    internal sealed class PythonConstantView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class PythonTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// The indexed fields: the sheet's first column plus every one marked with `*`.
        /// </summary>
        public IReadOnlyList<PythonIndexView> Indexes { get; set; }

        /// <summary>
        /// The table class's `__slots__`: the rows and one map per index, already quoted
        /// and comma separated.
        /// </summary>
        public string TableSlotNames { get; set; }

        /// <summary>
        /// The `__slots__` tuple's contents, already quoted and comma separated.
        ///
        /// Slots rather than a plain class: a table is tens of thousands of rows and a
        /// per-instance dictionary on each is the difference between tens of megabytes and
        /// a few.
        /// </summary>
        public string SlotNames { get; set; }

        /// <summary>Format string for `__repr__`.</summary>
        public string ReprFormat { get; set; }

        /// <summary>Values for `__repr__`, comma separated.</summary>
        public string ReprValues { get; set; }

        public IReadOnlyList<PythonFieldView> Fields { get; set; }
    }

    /// <summary>
    /// One indexed field, and the lookups generated for it.
    /// </summary>
    internal sealed class PythonIndexView
    {
        /// <summary>The record attribute holding the key.</summary>
        public string Member { get; set; }

        /// <summary>What the lookup names end in - `index` gives `find_by_index`.</summary>
        public string Suffix { get; set; }

        /// <summary>The table attribute holding the map from key to row.</summary>
        public string MapName { get; set; }

        /// <summary>The field as the sheet spells it, for the error message.</summary>
        public string FieldName { get; set; }
    }

    internal sealed class PythonFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The assignments the constructor makes, so that a record is fully formed before
        /// it is read into. Two for a reference, which keeps the raw index beside the value.
        /// </summary>
        public IReadOnlyList<string> Initializers { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>The column's wire tag.</summary>
        public int Tag { get; set; }

        /// <summary>The rendered check_column call for this member.</summary>
        public string ColumnCheck { get; set; }

        public int ElementCount { get; set; }

        public string ReadScalar { get; set; }

        public string ReadElement { get; set; }
    }

    internal sealed class PythonAccessorView
    {
        public string FileExtension { get; set; }

        /// <summary>The accessor's `__slots__` contents, already quoted and comma separated.</summary>
        public string SlotNames { get; set; }

        public IReadOnlyList<PythonTableSlotView> Tables { get; set; }
        public IReadOnlyList<PythonCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class PythonTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }

    internal sealed class PythonCrossReferenceView
    {
        public string Table { get; set; }
        public IReadOnlyList<PythonReferenceFieldView> Fields { get; set; }
    }

    internal sealed class PythonReferenceFieldView
    {
        public string Name { get; set; }
        public string RefTable { get; set; }

        /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
        public string RefLookup { get; set; }

        public string Value { get; set; }
        public bool IsArray { get; set; }
    }
}
