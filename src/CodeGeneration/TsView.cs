using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Views for the TypeScript templates.
    ///
    /// Unlike the other three generators this one writes a module per entity, so there is a
    /// view per output file rather than one for the whole thing.
    /// </summary>
    internal sealed class TsIndexView
    {
        /// <summary>`namespace X {` line, or empty when no namespace is set.</summary>
        public string NamespaceOpen { get; set; }

        /// <summary>The matching closer, or empty.</summary>
        public string NamespaceClose { get; set; }

        public IReadOnlyList<string> EnumNames { get; set; }
        public IReadOnlyList<string> TableNames { get; set; }
        public IReadOnlyList<string> ConstantSetNames { get; set; }
    }

    internal sealed class TsTableSetView
    {
        public IReadOnlyList<TsTableSlotView> Tables { get; set; }
    }

    internal sealed class TsTableSlotView
    {
        /// <summary>Accessor member name, camelCase and escaped.</summary>
        public string Member { get; set; }

        /// <summary>Table name as declared, which is also the class prefix.</summary>
        public string Name { get; set; }
    }

    internal sealed class TsUpdaterView
    {
    }

    internal sealed class TsEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<TsEnumLabelView> Labels { get; set; }
    }

    internal sealed class TsEnumLabelView
    {
        public string Name { get; set; }

        /// <summary>The rendered initializer - a number, or the label's own name quoted.</summary>
        public string Value { get; set; }

        public IReadOnlyList<string> Comment { get; set; }
        public bool IsLast { get; set; }
    }

    internal sealed class TsConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<string> Imports { get; set; }
        public IReadOnlyList<TsConstantView> Constants { get; set; }
    }

    internal sealed class TsConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class TsTableView
    {
        /// <summary>Table name as declared; the classes are this plus Record and Table.</summary>
        public string Name { get; set; }

        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Import statements for the enums and records this module names.</summary>
        public IReadOnlyList<string> Imports { get; set; }

        public IReadOnlyList<TsFieldView> Fields { get; set; }

        /// <summary>The fields a lookup map is built for.</summary>
        public IReadOnlyList<TsFieldView> IndexedFields { get; set; }
    }

    /// <summary>
    /// One serial field, in every shape the generated module distinguishes.
    /// </summary>
    internal sealed class TsFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Public accessor name, camelCase and escaped.</summary>
        public string PropName { get; set; }

        /// <summary>Private backing member, including its leading underscore.</summary>
        public string FieldName { get; set; }

        /// <summary>Property name in Pascal case, used for the index map members.</summary>
        public string PascalName { get; set; }

        /// <summary>Member type.</summary>
        public string FieldType { get; set; }

        /// <summary>
        /// Type the value has in the JSON export, which is not always the member type: a
        /// 64-bit integer is exported as a string, because JSON's single numeric type is a
        /// double and would round it.
        /// </summary>
        public string JsonWireType { get; set; }

        public int ElementCount { get; set; }

        /// <summary>Referenced table's name, without a suffix.</summary>
        public string RefTable { get; set; }

        /// <summary>
        /// Which declaration shape applies: `array_ref`, `var_array`, `array`, `scalar_ref`
        /// or `scalar`.
        /// </summary>
        public string Kind { get; set; }

        public bool IsArray { get; set; }

        /// <summary>Type of the setter a resolved reference is assigned through.</summary>
        public string ReferenceSetterType { get; set; }

        /// <summary>
        /// Whether the reference names a whole row rather than one of its fields.
        ///
        /// The two setters differ by a semicolon, which is arbitrary but is what the
        /// generated modules have always contained.
        /// </summary>
        public bool ReferenceIsRecord { get; set; }

        /// <summary>The assignment reading this field out of a named JSON row.</summary>
        public string FromNamedRow { get; set; }

        /// <summary>The statements reading this field out of a compact JSON row.</summary>
        public IReadOnlyList<string> FromCompactRow { get; set; }

        /// <summary>The call reading one value of the element type from binary.</summary>
        public string BinaryRead { get; set; }

        /// <summary>The column's wire tag.</summary>
        public int Tag { get; set; }

        /// <summary>The rendered checkColumn call for this member.</summary>
        public string ColumnCheck { get; set; }
    }
}
