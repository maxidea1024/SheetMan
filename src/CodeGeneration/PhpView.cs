using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>Everything the PHP template needs, worked out in advance.</summary>
internal sealed class PhpFileView
{
    /// <summary>Namespace every generated type is declared in.</summary>
    public required string Namespace { get; set; }

    public required IReadOnlyList<PhpEnumView> Enums { get; set; }
    public required IReadOnlyList<PhpConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<PhpTableView> Tables { get; set; }
    public required PhpAccessorView Accessor { get; set; }
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
    public string? Namespace { get; set; }

    /// <summary>Complete `require_once` lines, in the order they must run.</summary>
    public IReadOnlyList<string>? Requires { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public PhpTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public PhpEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public PhpConstantSetView? Set { get; set; }

    /// <summary>Every table, for the accessor.</summary>
    public IReadOnlyList<PhpTableView>? Tables { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public PhpAccessorView? Accessor { get; set; }
}

/// <summary>
/// A backed enum.
///
/// PHP has had these since 8.1 and they carry the declared value, so nothing here has
/// to invent a lookup table the way the Ruby and Python outputs do.
/// </summary>
internal sealed class PhpEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The case a value the sheet never declared falls back to.
    ///
    /// `from` throws on an undeclared value and a typed property cannot hold null, so a
    /// read goes through `tryFrom` and lands here instead - which is what every other
    /// generated reader does with the same situation.
    /// </summary>
    public required string DefaultCase { get; set; }

    public required IReadOnlyList<PhpEnumCaseView> Cases { get; set; }
}

internal sealed class PhpEnumCaseView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class PhpConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<PhpConstantView> Constants { get; set; }
}

internal sealed class PhpConstantView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class PhpTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<PhpIndexView> Indexes { get; set; }

    public required IReadOnlyList<PhpFieldView> Fields { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class PhpIndexView
{
    /// <summary>The record property holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `findByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's type, as a parameter declaration.</summary>
    public required string KeyType { get; set; }

    /// <summary>The key's type for a docblock, which wants the array's key type.</summary>
    public required string KeyDocType { get; set; }

    /// <summary>The property holding the map from key to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The local the read builds before publishing it.</summary>
    public required string LocalName { get; set; }

    /// <summary>The field as the sheet spells it, for the exception message.</summary>
    public required string FieldName { get; set; }
}

internal sealed class PhpFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The property declarations, each with its type and its initializer.
    ///
    /// A list, because a reference contributes two: the index that came off the wire
    /// and the record it is resolved to once every table is loaded.
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

    public required string ReadScalar { get; set; }
    public required string ReadElement { get; set; }
}

internal sealed class PhpAccessorView
{
    public required string Name { get; set; }
    public required string FileExtension { get; set; }
    public required IReadOnlyList<PhpTableSlotView> Tables { get; set; }
    public required IReadOnlyList<PhpCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class PhpTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class PhpCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<PhpReferenceFieldView> Fields { get; set; }
}

internal sealed class PhpReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}
