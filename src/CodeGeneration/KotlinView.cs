using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>Everything the Kotlin template needs, worked out in advance.</summary>
internal sealed class KotlinFileView
{
    public required string PackageName { get; set; }

    /// <summary>Name of the accessor object.</summary>
    public required string AccessorName { get; set; }

    public required IReadOnlyList<KotlinEnumView> Enums { get; set; }
    public required IReadOnlyList<KotlinConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<KotlinTableView> Tables { get; set; }
    public required KotlinAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file, for the templates that render one thing.
/// </summary>
/// <remarks>
/// The output is a file per table, per enum and per constant set, and each of those
/// templates needs the package as well as its own subject. Handing each one only what it
/// is for means a template cannot reach a table it is not writing.
/// </remarks>
internal sealed class KotlinPartView
{
    public string? PackageName { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public KotlinTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public KotlinEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public KotlinConstantSetView? Set { get; set; }
}

internal sealed class KotlinEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<KotlinEnumLabelView> Labels { get; set; }

    /// <summary>Label an undeclared value falls back to.</summary>
    public required string DefaultLabel { get; set; }
}

internal sealed class KotlinEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>A comma, or the semicolon that ends an enum body with members after it.</summary>
    public required string Separator { get; set; }
}

internal sealed class KotlinConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<KotlinConstantView> Constants { get; set; }
}

internal sealed class KotlinConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class KotlinTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<KotlinIndexView> Indexes { get; set; }

    public required IReadOnlyList<KotlinFieldView> Fields { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class KotlinIndexView
{
    /// <summary>The record property holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `findByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's type.</summary>
    public required string KeyType { get; set; }

    /// <summary>The property holding the map from key to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the exception message.</summary>
    public required string FieldName { get; set; }
}

internal sealed class KotlinFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The property declarations, each with an initializer.
    ///
    /// Initialized rather than declared `lateinit`, because Kotlin's null safety would
    /// otherwise make every read of an unread record a runtime failure rather than a
    /// default value - which is what the other generated readers hand back.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
    /// `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The column wire tag.</summary>
    public required int Tag { get; set; }

    /// <summary>The rendered checkColumn call for this member.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or empty for a
    /// column that reads the reader directly.
    /// </summary>
    public required string CursorOpen { get; set; }

    public required int ElementCount { get; set; }

    public required string ReadScalar { get; set; }

    public required string ReadElement { get; set; }
}

internal sealed class KotlinAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<KotlinTableSlotView> Tables { get; set; }
    public required IReadOnlyList<KotlinCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class KotlinTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class KotlinCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<KotlinReferenceFieldView> Fields { get; set; }
}

internal sealed class KotlinReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}
