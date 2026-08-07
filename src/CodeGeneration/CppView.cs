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

    /// <summary>
    /// One generated header: its guard, what it includes, the namespace, and the single thing it
    /// declares.
    /// </summary>
    /// <remarks>
    /// Header only, so an include here is a real dependency rather than something a source file
    /// happened to pull in first - which is why they are worked out rather than handed to every
    /// file alike. An include a file does not need is a compile the consumer pays for on every
    /// translation unit that reaches it.
    /// </remarks>
    internal sealed class CppPartView
    {
        public string IncludeGuard { get; set; }

        /// <summary>`#include` lines, standard library first and then this tool's own.</summary>
        public IReadOnlyList<string> Includes { get; set; }

        public IReadOnlyList<string> NamespaceOpen { get; set; }
        public IReadOnlyList<string> NamespaceClose { get; set; }

        /// <summary>Record type names, for the forward header.</summary>
        public IReadOnlyList<string> Records { get; set; }

        /// <summary>The table this file is for, when it is a table header.</summary>
        public CppTableView Table { get; set; }

        /// <summary>The enum this file is for, when it is an enum header.</summary>
        public CppEnumView Enumm { get; set; }

        /// <summary>The constant set this file is for, when it is a constants header.</summary>
        public CppConstantSetView Set { get; set; }

        /// <summary>The accessor's own shape, for the accessor header.</summary>
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
        /// <summary>Table name as the sheet spelled it. Names the table's header.</summary>
        public string RawName { get; set; }

        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// The indexed fields: the sheet's first column plus every one marked with `*`.
        /// </summary>
        public IReadOnlyList<CppIndexView> Indexes { get; set; }

        public IReadOnlyList<CppFieldView> Fields { get; set; }
    }

    /// <summary>
    /// One indexed field, and the lookups generated for it.
    /// </summary>
    internal sealed class CppIndexView
    {
        /// <summary>The record member holding the key, escaped.</summary>
        public string Member { get; set; }

        /// <summary>What the lookup names end in - `index` gives `find_by_index`.</summary>
        public string Suffix { get; set; }

        /// <summary>The map's key type.</summary>
        public string KeyType { get; set; }

        /// <summary>
        /// The type the lookups take: a const reference where a copy would cost, the value
        /// itself where it would not.
        /// </summary>
        public string KeyParam { get; set; }

        /// <summary>The member holding the map from key to row position.</summary>
        public string MapName { get; set; }

        /// <summary>How the key reaches the message, since a std::string concatenates and a number does not.</summary>
        public string KeyText { get; set; }

        /// <summary>The field as the sheet spells it, for the exception message.</summary>
        public string FieldName { get; set; }
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

        /// <summary>The column wire tag.</summary>
        public int Tag { get; set; }

        /// <summary>The rendered check_column call for this member.</summary>
        public string ColumnCheck { get; set; }

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

        /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
        public string RefLookup { get; set; }

        /// <summary>What the resolved reference yields - the record, or one of its fields.</summary>
        public string Value { get; set; }

        public string RefDefault { get; set; }

        /// <summary>Whether the field holds several references.</summary>
        public bool IsArray { get; set; }
    }
}
