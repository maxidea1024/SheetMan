using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>
/// Everything the Go template needs, worked out in advance.
/// </summary>
internal sealed class GoFileView
{
    public required string PackageName { get; set; }

    public required IReadOnlyList<GoEnumView> Enums { get; set; }
    public required IReadOnlyList<GoConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<GoTableView> Tables { get; set; }
    public required GoAccessorView Accessor { get; set; }
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
    public string? PackageName { get; set; }

    /// <summary>Import lines, already quoted, with a blank entry where gofmt wants a gap.</summary>
    public IReadOnlyList<string>? Imports { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public GoTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public GoEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public GoConstantSetView? Set { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public GoAccessorView? Accessor { get; set; }
}

internal sealed class GoEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<GoEnumLabelView> Labels { get; set; }
}

internal sealed class GoEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class GoConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<GoConstantView> Constants { get; set; }
}

internal sealed class GoConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class GoTableView
{
    /// <summary>Table name as the sheet spelled it, used in the data file name.</summary>
    public required string RawName { get; set; }

    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<GoIndexView> Indexes { get; set; }

    public required IReadOnlyList<GoFieldView> Fields { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class GoIndexView
{
    /// <summary>The record member holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `FindByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's Go type.</summary>
    public required string KeyType { get; set; }

    /// <summary>The table member holding the map from key to row position.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the error message.</summary>
    public required string FieldName { get; set; }
}

internal sealed class GoFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The member declarations. Two for a reference, which keeps the raw index beside
    /// the resolved value.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
    /// `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The column's wire tag.</summary>
    public required int Tag { get; set; }

    /// <summary>The rendered CheckColumn call for this member.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or empty for a
    /// column that reads the reader directly.
    /// </summary>
    public required string CursorOpen { get; set; }

    /// <summary>Element count of a serial field, which is its column count.</summary>
    public required int ElementCount { get; set; }

    /// <summary>The slice type a make call needs.</summary>
    public required string ArrayType { get; set; }

    /// <summary>The read call for a scalar.</summary>
    public required string ReadScalar { get; set; }

    /// <summary>The read call for one element of an array.</summary>
    public required string ReadElement { get; set; }
}

internal sealed class GoAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<GoTableSlotView> Tables { get; set; }
    public required IReadOnlyList<GoCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class GoTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class GoCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<GoReferenceFieldView> Fields { get; set; }
}

internal sealed class GoReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}
