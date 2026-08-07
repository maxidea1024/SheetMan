using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>
/// Everything Java needs, worked out in advance.
///
/// Built whole and then dealt out one subject at a time, so the naming is decided in one
/// place whether or not the file it lands in holds anything else.
/// </summary>
internal sealed class JavaFileView
{
    public required string PackageName { get; set; }

    /// <summary>Name of the accessor class, and so of its file.</summary>
    public required string AccessorName { get; set; }

    public required IReadOnlyList<JavaEnumView> Enums { get; set; }
    public required IReadOnlyList<JavaConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<JavaTableView> Tables { get; set; }
    public required JavaAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file: the package it declares, its imports, and the single type in it.
/// </summary>
internal sealed class JavaPartView
{
    public string? PackageName { get; set; }

    /// <summary>Set only for the accessor's file, which is the one named after it.</summary>
    public string? AccessorName { get; set; }

    /// <summary>
    /// Import lines, with a blank entry where Java convention wants a gap. Nothing here ever
    /// imports another generated type: they are all one package.
    /// </summary>
    public IReadOnlyList<string>? Imports { get; set; }

    /// <summary>
    /// The table this file is for, when it is a record file or a table file. Both are
    /// rendered from the same view, since both are named from it.
    /// </summary>
    public JavaTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public JavaEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public JavaConstantSetView? Set { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public JavaAccessorView? Accessor { get; set; }
}

internal sealed class JavaEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<JavaEnumLabelView> Labels { get; set; }

    /// <summary>Label an undeclared value falls back to.</summary>
    public required string DefaultLabel { get; set; }
}

internal sealed class JavaEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// What follows the constant: a comma, or the semicolon that ends the list. Decided
    /// here because Java's enum body needs one and not the other.
    /// </summary>
    public required string Separator { get; set; }
}

internal sealed class JavaConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<JavaConstantView> Constants { get; set; }
}

internal sealed class JavaConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class JavaTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<JavaIndexView> Indexes { get; set; }

    public required IReadOnlyList<JavaFieldView> Fields { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class JavaIndexView
{
    /// <summary>The record field holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `findByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The map's key type, boxed where the field is a primitive.</summary>
    public required string KeyType { get; set; }

    /// <summary>
    /// The type the lookups take, which is the field's own - a caller passing an `int`
    /// should not have to think about the box the map needs.
    /// </summary>
    public required string KeyParam { get; set; }

    /// <summary>The field holding the map from key to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the exception message.</summary>
    public required string FieldName { get; set; }
}

internal sealed class JavaFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The field declarations. Two for a reference, which keeps the raw index beside
    /// the resolved value.
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

    public required int ElementCount { get; set; }

    /// <summary>Element type, which an array allocation names.</summary>
    public required string ElementType { get; set; }

    public required string ReadScalar { get; set; }

    public required string ReadElement { get; set; }
}

internal sealed class JavaAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<JavaTableSlotView> Tables { get; set; }
    public required IReadOnlyList<JavaCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class JavaTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class JavaCrossReferenceView
{
    public required string Table { get; set; }

    /// <summary>Record type of the table being walked, which the loop declares.</summary>
    public required string RecordName { get; set; }

    public required IReadOnlyList<JavaReferenceFieldView> Fields { get; set; }
}

internal sealed class JavaReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    /// <summary>Record type of the table being pointed at, which the lookup declares.</summary>
    public required string RefRecordName { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}
