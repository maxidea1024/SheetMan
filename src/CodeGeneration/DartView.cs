using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>Everything the Dart template needs, worked out in advance.</summary>
    internal sealed class DartFileView
    {
        public IReadOnlyList<DartEnumView> Enums { get; set; }
        public IReadOnlyList<DartConstantSetView> ConstantSets { get; set; }
        public IReadOnlyList<DartTableView> Tables { get; set; }
        public DartAccessorView Accessor { get; set; }

        /// <summary>
        /// Every part this library is made of, as a `part` directive spells it: relative to the
        /// library file, forward slashes.
        /// </summary>
        /// <remarks>
        /// Built in the generator so the library and its parts cannot disagree about where each
        /// other are - which is a compile error in Dart and a path calculation nothing in a
        /// template could check.
        /// </remarks>
        public IReadOnlyList<string> Parts { get; set; }
    }

    /// <summary>
    /// One generated file, for the templates that render one thing.
    /// </summary>
    /// <remarks>
    /// A part carries no imports of its own - the library file holds them - so all a part
    /// needs is the library to say it belongs to, and its own subject.
    /// </remarks>
    internal sealed class DartPartView
    {
        /// <summary>
        /// The library this part belongs to, as the `part of` directive spells it: relative to
        /// the part's own directory.
        /// </summary>
        public string Library { get; set; }

        /// <summary>The table this file is for, when it is a table file.</summary>
        public DartTableView Table { get; set; }

        /// <summary>The enum this file is for, when it is an enum file.</summary>
        public DartEnumView Enumm { get; set; }

        /// <summary>The constant set this file is for, when it is a constants file.</summary>
        public DartConstantSetView Set { get; set; }
    }

    internal sealed class DartEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<DartEnumLabelView> Labels { get; set; }

        /// <summary>Label an undeclared value falls back to.</summary>
        public string DefaultLabel { get; set; }
    }

    internal sealed class DartEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>A comma, or the semicolon that ends an enum body with members after it.</summary>
        public string Separator { get; set; }
    }

    internal sealed class DartConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<DartConstantView> Constants { get; set; }
    }

    internal sealed class DartConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class DartTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// The indexed fields: the sheet's first column plus every one marked with `*`.
        /// </summary>
        public IReadOnlyList<DartIndexView> Indexes { get; set; }

        public IReadOnlyList<DartFieldView> Fields { get; set; }
    }

    /// <summary>
    /// One indexed field, and the lookups generated for it.
    /// </summary>
    internal sealed class DartIndexView
    {
        /// <summary>The record property holding the key.</summary>
        public string Member { get; set; }

        /// <summary>What the lookup names end in - `Index` gives `findByIndex`.</summary>
        public string Suffix { get; set; }

        /// <summary>The key's type.</summary>
        public string KeyType { get; set; }

        /// <summary>The property holding the map from key to row.</summary>
        public string MapName { get; set; }

        /// <summary>The field as the sheet spells it, for the exception message.</summary>
        public string FieldName { get; set; }
    }

    internal sealed class DartFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The property declarations, each with an initializer.
        ///
        /// Initialized rather than declared `lateinit`, because Dart's null safety would
        /// otherwise make every read of an unread record a runtime failure rather than a
        /// default value - which is what the other generated readers hand back.
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

    internal sealed class DartAccessorView
    {
        public string FileExtension { get; set; }
        public IReadOnlyList<DartTableSlotView> Tables { get; set; }
        public IReadOnlyList<DartCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class DartTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }

    internal sealed class DartCrossReferenceView
    {
        public string Table { get; set; }
        public IReadOnlyList<DartReferenceFieldView> Fields { get; set; }
    }

    internal sealed class DartReferenceFieldView
    {
        public string Name { get; set; }
        public string RefTable { get; set; }

        /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
        public string RefLookup { get; set; }

        public string Value { get; set; }
        public bool IsArray { get; set; }
    }
}
