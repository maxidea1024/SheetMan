using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>Everything the Ruby template needs, worked out in advance.</summary>
internal sealed class RubyFileView
{
    /// <summary>Module every generated type is nested in.</summary>
    public required string ModuleName { get; set; }

    public required IReadOnlyList<RubyEnumView> Enums { get; set; }
    public required IReadOnlyList<RubyConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<RubyTableView> Tables { get; set; }
    public required RubyAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file, for the templates that render one thing.
/// </summary>
/// <remarks>
/// Carries its requires as paths, already relative to its own directory. Ruby has no
/// autoloader here, so a split file requires what it uses - worked out in the generator,
/// because path arithmetic in a template is arithmetic nothing can test.
/// </remarks>
internal sealed class RubyPartView
{
    public string? ModuleName { get; set; }

    /// <summary>Paths for `require_relative`, without the extension Ruby does not want.</summary>
    public IReadOnlyList<string>? Requires { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public RubyTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public RubyEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public RubyConstantSetView? Set { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public RubyAccessorView? Accessor { get; set; }
}

internal sealed class RubyEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<RubyEnumLabelView> Labels { get; set; }
}

internal sealed class RubyEnumLabelView
{
    /// <summary>The constant, SCREAMING_SNAKE_CASE as Ruby writes them.</summary>
    public required string Name { get; set; }

    /// <summary>The same label as a symbol, which the value-to-name map holds.</summary>
    public required string Symbol { get; set; }

    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class RubyConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<RubyConstantView> Constants { get; set; }
}

internal sealed class RubyConstantView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class RubyTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<RubyIndexView> Indexes { get; set; }

    /// <summary>The `attr_accessor` list, already as symbols and comma separated.</summary>
    public required string AccessorNames { get; set; }

    public required IReadOnlyList<RubyFieldView> Fields { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class RubyIndexView
{
    /// <summary>The record accessor holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `index` gives `find_by_index`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The instance variable holding the map from key to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the error message.</summary>
    public required string FieldName { get; set; }
}

internal sealed class RubyFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The assignments the constructor makes, so a record is fully formed before it is
    /// read into.
    /// </summary>
    public required IReadOnlyList<string> Initializers { get; set; }

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

internal sealed class RubyAccessorView
{
    public required string FileExtension { get; set; }

    /// <summary>The `attr_reader` list, already as symbols and comma separated.</summary>
    public required string ReaderNames { get; set; }

    public required IReadOnlyList<RubyTableSlotView> Tables { get; set; }
    public required IReadOnlyList<RubyCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class RubyTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class RubyCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<RubyReferenceFieldView> Fields { get; set; }
}

internal sealed class RubyReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}
