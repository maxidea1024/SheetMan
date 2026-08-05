using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>Everything the PHP template needs, worked out in advance.</summary>
    internal sealed class PhpFileView
    {
        /// <summary>Namespace every generated type is declared in.</summary>
        public string Namespace { get; set; }

        public IReadOnlyList<PhpEnumView> Enums { get; set; }
        public IReadOnlyList<PhpConstantSetView> ConstantSets { get; set; }
        public IReadOnlyList<PhpTableView> Tables { get; set; }
        public PhpAccessorView Accessor { get; set; }
    }

    /// <summary>
    /// One generated file, for the templates that render one thing.
    /// </summary>
    /// <remarks>
    /// Carries the requires as finished lines. PHP has no autoloader here, so a split file
    /// has to require what it uses and how deep it sits decides the path - both worked out in
    /// the generator, because path arithmetic in a template is arithmetic nothing can test.
    /// </remarks>
    internal sealed class PhpPartView
    {
        public string Namespace { get; set; }

        /// <summary>Complete `require_once` lines, in the order they must run.</summary>
        public IReadOnlyList<string> Requires { get; set; }

        /// <summary>The table this file is for, when it is a table file.</summary>
        public PhpTableView Table { get; set; }

        /// <summary>The enum this file is for, when it is an enum file.</summary>
        public PhpEnumView Enumm { get; set; }

        /// <summary>The constant set this file is for, when it is a constants file.</summary>
        public PhpConstantSetView Set { get; set; }

        /// <summary>Every table, for the accessor.</summary>
        public IReadOnlyList<PhpTableView> Tables { get; set; }

        /// <summary>The accessor's own shape, for the accessor file.</summary>
        public PhpAccessorView Accessor { get; set; }
    }

    /// <summary>
    /// A backed enum.
    ///
    /// PHP has had these since 8.1 and they carry the declared value, so nothing here has
    /// to invent a lookup table the way the Ruby and Python outputs do.
    /// </summary>
    internal sealed class PhpEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// The case a value the sheet never declared falls back to.
        ///
        /// `from` throws on an undeclared value and a typed property cannot hold null, so a
        /// read goes through `tryFrom` and lands here instead - which is what every other
        /// generated reader does with the same situation.
        /// </summary>
        public string DefaultCase { get; set; }

        public IReadOnlyList<PhpEnumCaseView> Cases { get; set; }
    }

    internal sealed class PhpEnumCaseView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class PhpConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<PhpConstantView> Constants { get; set; }
    }

    internal sealed class PhpConstantView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class PhpTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Name of the primary index property.</summary>
        public string IndexField { get; set; }

        public IReadOnlyList<PhpFieldView> Fields { get; set; }
    }

    internal sealed class PhpFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The property declarations, each with its type and its initializer.
        ///
        /// A list, because a reference contributes two: the index that came off the wire
        /// and the record it is resolved to once every table is loaded.
        /// </summary>
        public IReadOnlyList<string> Declarations { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>The column wire tag.</summary>
        public int Tag { get; set; }

        /// <summary>The rendered checkColumn call for this member.</summary>
        public string ColumnCheck { get; set; }

        public int ElementCount { get; set; }

        public string ReadScalar { get; set; }
        public string ReadElement { get; set; }
    }

    internal sealed class PhpAccessorView
    {
        public string Name { get; set; }
        public string FileExtension { get; set; }
        public IReadOnlyList<PhpTableSlotView> Tables { get; set; }
        public IReadOnlyList<PhpCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class PhpTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }

    internal sealed class PhpCrossReferenceView
    {
        public string Table { get; set; }
        public IReadOnlyList<PhpReferenceFieldView> Fields { get; set; }
    }

    internal sealed class PhpReferenceFieldView
    {
        public string Name { get; set; }
        public string RefTable { get; set; }
        public string Value { get; set; }
        public bool IsArray { get; set; }
    }
}
