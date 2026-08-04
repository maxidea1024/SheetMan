using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Everything the Go template needs, worked out in advance.
    /// </summary>
    internal sealed class GoFileView
    {
        public string PackageName { get; set; }

        public IReadOnlyList<GoEnumView> Enums { get; set; }
        public IReadOnlyList<GoConstantSetView> ConstantSets { get; set; }
        public IReadOnlyList<GoTableView> Tables { get; set; }
        public GoAccessorView Accessor { get; set; }
    }

    /// <summary>
    /// One generated file, for the templates that render one thing.
    /// </summary>
    /// <remarks>
    /// Carries its own imports, because an unused one does not compile in Go - every other
    /// language here could hand each file the same list.
    /// </remarks>
    internal sealed class GoPartView
    {
        public string PackageName { get; set; }

        /// <summary>Import lines, already quoted, with a blank entry where gofmt wants a gap.</summary>
        public IReadOnlyList<string> Imports { get; set; }

        /// <summary>The table this file is for, when it is a table file.</summary>
        public GoTableView Table { get; set; }

        /// <summary>The enum this file is for, when it is an enum file.</summary>
        public GoEnumView Enumm { get; set; }

        /// <summary>The constant set this file is for, when it is a constants file.</summary>
        public GoConstantSetView Set { get; set; }

        /// <summary>The accessor's own shape, for the accessor file.</summary>
        public GoAccessorView Accessor { get; set; }
    }

    internal sealed class GoEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<GoEnumLabelView> Labels { get; set; }
    }

    internal sealed class GoEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class GoConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<GoConstantView> Constants { get; set; }
    }

    internal sealed class GoConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class GoTableView
    {
        /// <summary>Table name as the sheet spelled it, used in the data file name.</summary>
        public string RawName { get; set; }

        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Name of the primary index member.</summary>
        public string IndexField { get; set; }

        public IReadOnlyList<GoFieldView> Fields { get; set; }
    }

    internal sealed class GoFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The member declarations. Two for a reference, which keeps the raw index beside
        /// the resolved value.
        /// </summary>
        public IReadOnlyList<string> Declarations { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>Element count of a serial field, which is its column count.</summary>
        public int ElementCount { get; set; }

        /// <summary>The slice type a make call needs.</summary>
        public string ArrayType { get; set; }

        /// <summary>The read call for a scalar.</summary>
        public string ReadScalar { get; set; }

        /// <summary>The read call for one element of an array.</summary>
        public string ReadElement { get; set; }
    }

    internal sealed class GoAccessorView
    {
        public string FileExtension { get; set; }
        public IReadOnlyList<GoTableSlotView> Tables { get; set; }
        public IReadOnlyList<GoCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class GoTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }

    internal sealed class GoCrossReferenceView
    {
        public string Table { get; set; }
        public IReadOnlyList<GoReferenceFieldView> Fields { get; set; }
    }

    internal sealed class GoReferenceFieldView
    {
        public string Name { get; set; }
        public string RefTable { get; set; }
        public string Value { get; set; }
        public bool IsArray { get; set; }
    }
}
