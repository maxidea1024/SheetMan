using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

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

    /// <summary>
    /// What is exported, and out of which file. The two differ: a type keeps its Pascal
    /// name and the file it lives in is kebab-case.
    /// </summary>
    public IReadOnlyList<TsExportView> Enums { get; set; }
    public IReadOnlyList<TsExportView> Tables { get; set; }
    public IReadOnlyList<TsExportView> ConstantSets { get; set; }
}

internal sealed class TsExportView
{
    /// <summary>The exported name, as declared.</summary>
    public string Name { get; set; }

    /// <summary>The file it is in, without the extension.</summary>
    public string File { get; set; }
}

internal sealed class TsTableSetView
{
    public IReadOnlyList<TsTableSlotView> Tables { get; set; }

    /// <summary>
    /// Default extension of the binary data files, as the recipe told the exporter to
    /// write them.
    /// </summary>
    public string BinaryFileExtension { get; set; }

    /// <summary>
    /// The tables holding reference columns, and what each one has to be linked to.
    /// </summary>
    /// <remarks>
    /// Empty until now, and so was the method it renders: TypeScript generated the
    /// `setReference_*_INTERNAL` methods and never called one, so `record.categoryId`
    /// was the raw key from JSON or nothing at all from binary.
    /// </remarks>
    public IReadOnlyList<TsCrossReferenceView> CrossReferences { get; set; }
}

/// <summary>One table's reference columns, for the linking pass.</summary>
internal sealed class TsCrossReferenceView
{
    /// <summary>The accessor member holding the table.</summary>
    public string Table { get; set; }

    public IReadOnlyList<TsReferenceFieldView> Fields { get; set; }
}

internal sealed class TsReferenceFieldView
{
    /// <summary>The record's property name, which names the setter.</summary>
    public string PropName { get; set; }

    /// <summary>The record's backing member, which holds the key.</summary>
    public string FieldName { get; set; }

    /// <summary>The accessor member holding the table being pointed at.</summary>
    public string RefTable { get; set; }

    /// <summary>The referenced table's class name, which names the index member.</summary>
    public string RefTableType { get; set; }

    /// <summary>
    /// The referenced table's throwing lookup, which is what a key resolves through.
    /// </summary>
    public string RefLookup { get; set; }

    /// <summary>What the resolved reference yields: the row, or one of its fields.</summary>
    public string Value { get; set; }

    /// <summary>Whether the column is a fixed group of references rather than one.</summary>
    public bool IsArray { get; set; }

    /// <summary>How many, when it is a group.</summary>
    public int ElementCount { get; set; }
}

internal sealed class TsTableSlotView
{
    /// <summary>Accessor member name, camelCase and escaped.</summary>
    public string Member { get; set; }

    /// <summary>Table name as declared, which is also the class prefix.</summary>
    public string Name { get; set; }

    /// <summary>The file the table is in, without the extension.</summary>
    public string File { get; set; }
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

/// <summary>The fields that reference another table, and so get a wiring method.</summary>
    public IReadOnlyList<TsFieldView> ReferenceFields { get; set; }

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

    /// <summary>What the member is declared as, when no column fills it.</summary>
    public string DefaultValue { get; set; }

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
