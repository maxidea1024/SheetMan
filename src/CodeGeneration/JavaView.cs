using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>Everything the Java template needs, worked out in advance.</summary>
    internal sealed class JavaFileView
    {
        public string PackageName { get; set; }

        /// <summary>Name of the accessor class, which every generated type nests inside.</summary>
        public string AccessorName { get; set; }

        public IReadOnlyList<JavaEnumView> Enums { get; set; }
        public IReadOnlyList<JavaConstantSetView> ConstantSets { get; set; }
        public IReadOnlyList<JavaTableView> Tables { get; set; }
        public JavaAccessorView Accessor { get; set; }
    }

    internal sealed class JavaEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<JavaEnumLabelView> Labels { get; set; }

        /// <summary>Label an undeclared value falls back to.</summary>
        public string DefaultLabel { get; set; }
    }

    internal sealed class JavaEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// What follows the constant: a comma, or the semicolon that ends the list. Decided
        /// here because Java's enum body needs one and not the other.
        /// </summary>
        public string Separator { get; set; }
    }

    internal sealed class JavaConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<JavaConstantView> Constants { get; set; }
    }

    internal sealed class JavaConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class JavaTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Name of the primary index field.</summary>
        public string IndexField { get; set; }

        public IReadOnlyList<JavaFieldView> Fields { get; set; }
    }

    internal sealed class JavaFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The field declarations. Two for a reference, which keeps the raw index beside
        /// the resolved value.
        /// </summary>
        public IReadOnlyList<string> Declarations { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        public int ElementCount { get; set; }

        /// <summary>Element type, which an array allocation names.</summary>
        public string ElementType { get; set; }

        public string ReadScalar { get; set; }

        public string ReadElement { get; set; }
    }

    internal sealed class JavaAccessorView
    {
        public string FileExtension { get; set; }
        public IReadOnlyList<JavaTableSlotView> Tables { get; set; }
        public IReadOnlyList<JavaCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class JavaTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }

    internal sealed class JavaCrossReferenceView
    {
        public string Table { get; set; }

        /// <summary>Record type of the table being walked, which the loop declares.</summary>
        public string RecordName { get; set; }

        public IReadOnlyList<JavaReferenceFieldView> Fields { get; set; }
    }

    internal sealed class JavaReferenceFieldView
    {
        public string Name { get; set; }
        public string RefTable { get; set; }

        /// <summary>Record type of the table being pointed at, which the lookup declares.</summary>
        public string RefRecordName { get; set; }

        public string Value { get; set; }
        public bool IsArray { get; set; }
    }
}
