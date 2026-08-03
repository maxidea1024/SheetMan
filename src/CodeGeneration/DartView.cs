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

        /// <summary>Name of the primary index property.</summary>
        public string IndexField { get; set; }

        public IReadOnlyList<DartFieldView> Fields { get; set; }
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
        public string Value { get; set; }
        public bool IsArray { get; set; }
    }
}
