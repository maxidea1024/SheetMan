using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Everything the C++ template needs, worked out in advance.
    ///
    /// The division is deliberate: anything that depends on the model - a read call for a
    /// particular element type, a default initializer, an escaped identifier - is computed
    /// here and arrives as a finished string, and the template only decides where things go.
    /// A template that had to reason about value types would be as hard to read as the
    /// printer calls it replaced, and harder to debug.
    /// </summary>
    internal sealed class CppFileView
    {
        public string IncludeGuard { get; set; }

        /// <summary>`namespace x {` lines, outermost first. Empty when no namespace is set.</summary>
        public IReadOnlyList<string> NamespaceOpen { get; set; }

        /// <summary>The matching closers, innermost first.</summary>
        public IReadOnlyList<string> NamespaceClose { get; set; }

        public IReadOnlyList<CppEnumView> Enums { get; set; }

        public IReadOnlyList<CppConstantSetView> ConstantSets { get; set; }

        public IReadOnlyList<CppTableView> Tables { get; set; }

        public CppAccessorView Accessor { get; set; }
    }

    internal sealed class CppEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }

        /// <summary>Comment text, already split into lines; the template adds the `///`.</summary>
        public IReadOnlyList<string> Comment { get; set; }

        public IReadOnlyList<CppEnumLabelView> Labels { get; set; }
    }

    internal sealed class CppEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class CppConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<CppConstantView> Constants { get; set; }
    }

    internal sealed class CppConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class CppTableView
    {
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Name of the primary index member, escaped.</summary>
        public string IndexField { get; set; }

        public IReadOnlyList<CppFieldView> Fields { get; set; }
    }

    /// <summary>
    /// One serial field, in the five shapes the generated read distinguishes.
    /// </summary>
    internal sealed class CppFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// The member declarations. Two lines for a reference, which keeps the raw index
        /// beside the resolved value.
        /// </summary>
        public IReadOnlyList<string> Declarations { get; set; }

        /// <summary>
        /// Which read shape applies: `scalar`, `scalar_ref`, `serial`, `serial_ref` or
        /// `var_array`. A string rather than a flag set because the template selects on it,
        /// and five names read better there than four booleans.
        /// </summary>
        public string Kind { get; set; }

        public string Name { get; set; }

        /// <summary>Element count of a serial field, which is its column count.</summary>
        public int ElementCount { get; set; }

        /// <summary>What an unresolved reference holds until it is linked.</summary>
        public string RefDefault { get; set; }

        /// <summary>The read call for a scalar, without its semicolon.</summary>
        public string ReadScalar { get; set; }

        /// <summary>The read call for element `i` of a serial field.</summary>
        public string ReadElement { get; set; }

        /// <summary>
        /// The read call for element `i` of a variable-length array, whose index needs the
        /// cast that a serial field's does not.
        /// </summary>
        public string ReadVarElement { get; set; }
    }

    internal sealed class CppAccessorView
    {
        public string FileExtension { get; set; }

        public IReadOnlyList<CppTableSlotView> Tables { get; set; }

        public IReadOnlyList<CppCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class CppTableSlotView
    {
        /// <summary>Escaped member name of the table within the accessor.</summary>
        public string Name { get; set; }

        public string TableName { get; set; }

        /// <summary>Table name as the exporter spells the data file, unescaped.</summary>
        public string DataFileName { get; set; }
    }

    internal sealed class CppCrossReferenceView
    {
        /// <summary>Escaped accessor member holding the table whose records are linked.</summary>
        public string Table { get; set; }

        public IReadOnlyList<CppReferenceFieldView> Fields { get; set; }
    }

    internal sealed class CppReferenceFieldView
    {
        public string Name { get; set; }

        /// <summary>Escaped accessor member of the table being pointed at.</summary>
        public string RefTable { get; set; }

        /// <summary>What the resolved reference yields - the record, or one of its fields.</summary>
        public string Value { get; set; }

        public string RefDefault { get; set; }

        /// <summary>Whether the field holds several references.</summary>
        public bool IsArray { get; set; }
    }
}
