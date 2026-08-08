using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>Everything the Dart template needs, worked out in advance.</summary>
internal sealed class DartFileView
{
    public required IReadOnlyList<DartEnumView> Enums { get; set; }
    public required IReadOnlyList<DartConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<DartTableView> Tables { get; set; }
    public required DartAccessorView Accessor { get; set; }

    /// <summary>
    /// Every part this library is made of, as a `part` directive spells it: relative to the
    /// library file, forward slashes.
    /// </summary>
    /// <remarks>
    /// Built in the generator so the library and its parts cannot disagree about where each
    /// other are - which is a compile error in Dart and a path calculation nothing in a
    /// template could check.
    /// </remarks>
    /// <summary>
    /// `part` directives, one per generated file.
    /// </summary>
    /// <remarks>
    /// Filled after the view is built, because the list is not known until every part
    /// file has been decided. Empty until then rather than required: what a caller
    /// cannot supply at construction is not something to demand there.
    /// </remarks>
    public IReadOnlyList<string> Parts { get; set; } = [];
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
    public string? Library { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public DartTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public DartEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public DartConstantSetView? Set { get; set; }
}

internal sealed class DartEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<DartEnumLabelView> Labels { get; set; }

    /// <summary>Label an undeclared value falls back to.</summary>
    public required string DefaultLabel { get; set; }
}

internal sealed class DartEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>A comma, or the semicolon that ends an enum body with members after it.</summary>
    public required string Separator { get; set; }
}

internal sealed class DartConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<DartConstantView> Constants { get; set; }
}

internal sealed class DartConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class DartTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<DartIndexView> Indexes { get; set; }

    public required IReadOnlyList<DartFieldView> Fields { get; set; }

    /// <summary>Whether any field reads through a column cursor.</summary>
    public required bool NeedsCursor { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class DartIndexView
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

internal sealed class DartFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The property declarations, each with an initializer.
    ///
    /// Initialized rather than declared `lateinit`, because Dart's null safety would
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

internal sealed class DartAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<DartTableSlotView> Tables { get; set; }
    public required IReadOnlyList<DartCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class DartTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class DartCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<DartReferenceFieldView> Fields { get; set; }
}

internal sealed class DartReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}
