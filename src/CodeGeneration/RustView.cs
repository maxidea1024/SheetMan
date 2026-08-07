using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>
/// Everything Rust needs, worked out in advance.
///
/// Built whole and then dealt out one subject at a time, so the naming is decided in one
/// place whether or not the file it lands in holds anything else.
/// </summary>
internal sealed class RustFileView
{
    public required IReadOnlyList<RustEnumView> Enums { get; set; }
    public required IReadOnlyList<RustConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<RustTableView> Tables { get; set; }
    public required RustAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file: what it brings into scope, and the single thing it declares.
/// </summary>
internal sealed class RustPartView
{
    /// <summary>
    /// `use` lines, from <see cref="TypeDependencies"/> and from what the file's own text
    /// reaches for. Exact rather than generous, because an unused one is a warning.
    /// </summary>
    public IReadOnlyList<string>? Uses { get; set; }

    /// <summary>
    /// Lines for an inner doc comment, for the file whose whole contents are the subject.
    /// Empty for the files whose comment attaches to an item instead.
    /// </summary>
    public IReadOnlyList<string>? ModuleDoc { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public RustTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public RustEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public RustConstantSetView? Set { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public RustAccessorView? Accessor { get; set; }
}

internal sealed class RustEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<RustEnumLabelView> Labels { get; set; }
}

internal sealed class RustEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// Whether this label carries the `#[default]` attribute.
    ///
    /// Deriving Default on an enum needs exactly one variant marked, so the choice is
    /// made here rather than left to the template: the zero label when there is one,
    /// and the first otherwise.
    /// </summary>
    public required bool IsDefault { get; set; }
}

internal sealed class RustConstantSetView
{
    public required string ModuleName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<RustConstantView> Constants { get; set; }
}

internal sealed class RustConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class RustTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<RustIndexView> Indexes { get; set; }

    public required IReadOnlyList<RustFieldView> Fields { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class RustIndexView
{
    /// <summary>The record member holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `index` gives `find_by_index`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The map's key type.</summary>
    public required string KeyType { get; set; }

    /// <summary>
    /// The type the lookups take. `&amp;str` where the map is keyed by `String`, so a
    /// caller with a literal does not have to build one to ask a question.
    /// </summary>
    public required string KeyParam { get; set; }

    /// <summary>The key as the map wants it: `key` when already a borrow, `&amp;key` otherwise.</summary>
    public required string KeyBorrow { get; set; }

    /// <summary>The table member holding the map from key to row position.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the error message.</summary>
    public required string FieldName { get; set; }
}

internal sealed class RustFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The struct's field declarations, `name: type,` each.
    ///
    /// A reference contributes only its index. Resolving it into a borrow of another
    /// record would make the row own its neighbours, which Rust does not allow without
    /// lifetimes through every generated type or a cell around every row; the caller
    /// looks the index up instead.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
    /// `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The column wire tag.</summary>
    public required int Tag { get; set; }

    /// <summary>The rendered check_column call for this member.</summary>
    public required string ColumnCheck { get; set; }

    public required int ElementCount { get; set; }

    public required string ReadScalar { get; set; }

    public required string ReadElement { get; set; }
}

internal sealed class RustAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<RustTableSlotView> Tables { get; set; }
}

internal sealed class RustTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}
