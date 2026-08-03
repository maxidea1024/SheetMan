using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>Everything the Kotlin template needs, worked out in advance.</summary>
    internal sealed class KotlinFileView
    {
        public string PackageName { get; set; }

        /// <summary>Name of the accessor object.</summary>
        public string AccessorName { get; set; }

        public IReadOnlyList<KotlinEnumView> Enums { get; set; }
        public IReadOnlyList<KotlinConstantSetView> ConstantSets { get; set; }
        public IReadOnlyList<KotlinTableView> Tables { get; set; }
        public KotlinAccessorView Accessor { get; set; }
    }

    internal sealed class KotlinEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<KotlinEnumLabelView> Labels { get; set; }

        /// <summary>Label an undeclared value falls back to.</summary>
        public string DefaultLabel { get; set; }
    }

    internal sealed class KotlinEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>A comma, or the semicolon that ends an enum body with members after it.</summary>
        public string Separator { get; set; }
    }

    internal sealed class KotlinConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<KotlinConstantView> Constants { get; set; }
    }

    internal sealed class KotlinConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class KotlinTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Name of the primary index property.</summary>
        public string IndexField { get; set; }

        public IReadOnlyList<KotlinFieldView> Fields { get; set; }
    }

    internal sealed class KotlinFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The property declarations, each with an initializer.
        ///
        /// Initialized rather than declared `lateinit`, because Kotlin's null safety would
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

    internal sealed class KotlinAccessorView
    {
        public string FileExtension { get; set; }
        public IReadOnlyList<KotlinTableSlotView> Tables { get; set; }
        public IReadOnlyList<KotlinCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class KotlinTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }

    internal sealed class KotlinCrossReferenceView
    {
        public string Table { get; set; }
        public IReadOnlyList<KotlinReferenceFieldView> Fields { get; set; }
    }

    internal sealed class KotlinReferenceFieldView
    {
        public string Name { get; set; }
        public string RefTable { get; set; }
        public string Value { get; set; }
        public bool IsArray { get; set; }
    }
}
