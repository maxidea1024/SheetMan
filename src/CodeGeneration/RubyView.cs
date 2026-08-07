using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>Everything the Ruby template needs, worked out in advance.</summary>
    internal sealed class RubyFileView
    {
        /// <summary>Module every generated type is nested in.</summary>
        public string ModuleName { get; set; }

        public IReadOnlyList<RubyEnumView> Enums { get; set; }
        public IReadOnlyList<RubyConstantSetView> ConstantSets { get; set; }
        public IReadOnlyList<RubyTableView> Tables { get; set; }
        public RubyAccessorView Accessor { get; set; }
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
        public string ModuleName { get; set; }

        /// <summary>Paths for `require_relative`, without the extension Ruby does not want.</summary>
        public IReadOnlyList<string> Requires { get; set; }

        /// <summary>The table this file is for, when it is a table file.</summary>
        public RubyTableView Table { get; set; }

        /// <summary>The enum this file is for, when it is an enum file.</summary>
        public RubyEnumView Enumm { get; set; }

        /// <summary>The constant set this file is for, when it is a constants file.</summary>
        public RubyConstantSetView Set { get; set; }

        /// <summary>The accessor's own shape, for the accessor file.</summary>
        public RubyAccessorView Accessor { get; set; }
    }

    internal sealed class RubyEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<RubyEnumLabelView> Labels { get; set; }
    }

    internal sealed class RubyEnumLabelView
    {
        /// <summary>The constant, SCREAMING_SNAKE_CASE as Ruby writes them.</summary>
        public string Name { get; set; }

        /// <summary>The same label as a symbol, which the value-to-name map holds.</summary>
        public string Symbol { get; set; }

        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class RubyConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<RubyConstantView> Constants { get; set; }
    }

    internal sealed class RubyConstantView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class RubyTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// The indexed fields: the sheet's first column plus every one marked with `*`.
        /// </summary>
        public IReadOnlyList<RubyIndexView> Indexes { get; set; }

        /// <summary>The `attr_accessor` list, already as symbols and comma separated.</summary>
        public string AccessorNames { get; set; }

        public IReadOnlyList<RubyFieldView> Fields { get; set; }
    }

    /// <summary>
    /// One indexed field, and the lookups generated for it.
    /// </summary>
    internal sealed class RubyIndexView
    {
        /// <summary>The record accessor holding the key.</summary>
        public string Member { get; set; }

        /// <summary>What the lookup names end in - `index` gives `find_by_index`.</summary>
        public string Suffix { get; set; }

        /// <summary>The instance variable holding the map from key to row.</summary>
        public string MapName { get; set; }

        /// <summary>The field as the sheet spells it, for the error message.</summary>
        public string FieldName { get; set; }
    }

    internal sealed class RubyFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The assignments the constructor makes, so a record is fully formed before it is
        /// read into.
        /// </summary>
        public IReadOnlyList<string> Initializers { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>The column wire tag.</summary>
        public int Tag { get; set; }

        /// <summary>The rendered check_column call for this member.</summary>
        public string ColumnCheck { get; set; }

        public int ElementCount { get; set; }

        public string ReadScalar { get; set; }

        public string ReadElement { get; set; }
    }

    internal sealed class RubyAccessorView
    {
        public string FileExtension { get; set; }

        /// <summary>The `attr_reader` list, already as symbols and comma separated.</summary>
        public string ReaderNames { get; set; }

        public IReadOnlyList<RubyTableSlotView> Tables { get; set; }
        public IReadOnlyList<RubyCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class RubyTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }

    internal sealed class RubyCrossReferenceView
    {
        public string Table { get; set; }
        public IReadOnlyList<RubyReferenceFieldView> Fields { get; set; }
    }

    internal sealed class RubyReferenceFieldView
    {
        public string Name { get; set; }
        public string RefTable { get; set; }

        /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
        public string RefLookup { get; set; }

        public string Value { get; set; }
        public bool IsArray { get; set; }
    }
}
