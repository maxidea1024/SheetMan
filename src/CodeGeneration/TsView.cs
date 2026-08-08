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
    public required string NamespaceOpen { get; set; }

    /// <summary>The matching closer, or empty.</summary>
    public required string NamespaceClose { get; set; }

    /// <summary>
    /// What is exported, and out of which file. The two differ: a type keeps its Pascal
    /// name and the file it lives in is kebab-case.
    /// </summary>
    public required IReadOnlyList<TsExportView> Enums { get; set; }
    public required IReadOnlyList<TsExportView> Tables { get; set; }
    public required IReadOnlyList<TsExportView> ConstantSets { get; set; }
}

internal sealed class TsExportView
{
    /// <summary>The exported name, as declared.</summary>
    public required string Name { get; set; }

    /// <summary>The file it is in, without the extension.</summary>
    public required string File { get; set; }
}

internal sealed class TsTableSetView
{
    public required IReadOnlyList<TsTableSlotView> Tables { get; set; }

    /// <summary>
    /// Default extension of the binary data files, as the recipe told the exporter to
    /// write them.
    /// </summary>
    public required string BinaryFileExtension { get; set; }

    /// <summary>
    /// The tables holding reference columns, and what each one has to be linked to.
    /// </summary>
    /// <remarks>
    /// Empty until now, and so was the method it renders: TypeScript generated the
    /// `setReference_*_INTERNAL` methods and never called one, so `record.categoryId`
    /// was the raw key from JSON or nothing at all from binary.
    /// </remarks>
    public required IReadOnlyList<TsCrossReferenceView> CrossReferences { get; set; }
}

/// <summary>One table's reference columns, for the linking pass.</summary>
internal sealed class TsCrossReferenceView
{
    /// <summary>The accessor member holding the table.</summary>
    public required string Table { get; set; }

    public required IReadOnlyList<TsReferenceFieldView> Fields { get; set; }
}

internal sealed class TsReferenceFieldView
{
    /// <summary>The record's property name, which names the setter.</summary>
    public required string PropName { get; set; }

    /// <summary>The record's backing member, which holds the key.</summary>
    public required string FieldName { get; set; }

    /// <summary>The accessor member holding the table being pointed at.</summary>
    public required string RefTable { get; set; }

    /// <summary>The referenced table's class name, which names the index member.</summary>
    public required string RefTableType { get; set; }

    /// <summary>
    /// The referenced table's throwing lookup, which is what a key resolves through.
    /// </summary>
    public required string RefLookup { get; set; }

    /// <summary>What the resolved reference yields: the row, or one of its fields.</summary>
    public required string Value { get; set; }

    /// <summary>Whether the column is a fixed group of references rather than one.</summary>
    public required bool IsArray { get; set; }

    /// <summary>How many, when it is a group.</summary>
    public required int ElementCount { get; set; }
}

internal sealed class TsTableSlotView
{
    /// <summary>Accessor member name, camelCase and escaped.</summary>
    public required string Member { get; set; }

    /// <summary>Table name as declared, which is also the class prefix.</summary>
    public required string Name { get; set; }

    /// <summary>The file the table is in, without the extension.</summary>
    public required string File { get; set; }
}

internal sealed class TsEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<TsEnumLabelView> Labels { get; set; }
}

internal sealed class TsEnumLabelView
{
    public required string Name { get; set; }

    /// <summary>The rendered initializer - a number, or the label's own name quoted.</summary>
    public required string Value { get; set; }

    public required IReadOnlyList<string> Comment { get; set; }
    public required bool IsLast { get; set; }
}

internal sealed class TsConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<string> Imports { get; set; }
    public required IReadOnlyList<TsConstantView> Constants { get; set; }
}

internal sealed class TsConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class TsTableView
{
    /// <summary>Table name as declared; the classes are this plus Record and Table.</summary>
    public required string Name { get; set; }

    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Import statements for the enums and records this module names.</summary>
    public required IReadOnlyList<string> Imports { get; set; }

    public required IReadOnlyList<TsFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the binary read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from <see cref="Fields"/>: declaring a member is per field and
    /// reading is per column. They are the same list for every table written before
    /// records existed, and a record group is one column per member.
    /// </remarks>
    public required IReadOnlyList<TsColumnView> Columns { get; set; }

/// <summary>The fields that reference another table, and so get a wiring method.</summary>
    public required IReadOnlyList<TsFieldView> ReferenceFields { get; set; }

            /// <summary>The fields a lookup map is built for.</summary>
    public required IReadOnlyList<TsFieldView> IndexedFields { get; set; }

    /// <summary>
    /// Whether the read declares the column cursor: true when any scalar column can
    /// arrive encoded, which is what the cursor exists to decode.
    /// </summary>
    public required bool NeedsCursor { get; set; }
}

/// <summary>
/// One column of a data file, as the binary read switch sees it.
/// </summary>
/// <remarks>
/// Everything here answers "how is this column read". Where the value lands comes along
/// because the read has to assign somewhere; the shape of the declaration is
/// <see cref="TsFieldView"/>'s business. spec/nested-fields.md has the split.
/// </remarks>
internal sealed class TsColumnView
{
    /// <summary>The column's wire tag.</summary>
    public required int Tag { get; set; }

    /// <summary>The rendered checkColumn call.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The rendered cursor construction placed ahead of the row loop, or empty for a
    /// column that never arrives encoded.
    /// </summary>
    public required string CursorOpen { get; set; }

    /// <summary>
    /// Which read shape applies: `var_array`, `array_ref`, `array`, `scalar_ref`,
    /// `record_array_member`, `record_member` or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The expression reading one value of the element type.</summary>
    public required string BinaryRead { get; set; }

    /// <summary>Backing member this column fills, including its leading underscore.</summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// The field of the element type this column fills, with a leading dot, or empty when
    /// the column is not a record member.
    /// </summary>
    public required string MemberAccess { get; set; }

    /// <summary>Element count of a fixed array.</summary>
    public required int ElementCount { get; set; }

    /// <summary>Referenced table's name, for the stored-index member.</summary>
    public required string RefTable { get; set; }
}

/// <summary>
/// One member of a record group: a property of the generated element interface.
/// </summary>
internal sealed class TsRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Property name on the element interface, camelCase and escaped.</summary>
    public required string PropName { get; set; }

    /// <summary>That property's type.</summary>
    public required string FieldType { get; set; }

    /// <summary>
    /// Its type in the JSON export, which is not always the member type: a 64-bit integer
    /// is exported as a string because JSON's single numeric type would round it.
    /// </summary>
    public required string JsonWireType { get; set; }

    /// <summary>An empty value of the member's own type.</summary>
    public required string DefaultValue { get; set; }
}

/// <summary>
/// One serial field, in every shape the generated module distinguishes.
/// </summary>
internal sealed class TsFieldView
{
    /// <summary>
    /// Whether this field is a record group, so the module declares an element interface
    /// for it and the member is of that type.
    /// </summary>
    public required bool IsRecord { get; set; }

    /// <summary>Name of the generated element interface, for a record group.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>Properties of that interface. Empty unless <see cref="IsRecord"/>.</summary>
    public required IReadOnlyList<TsRecordMemberView> Members { get; set; }

    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Public accessor name, camelCase and escaped.</summary>
    public required string PropName { get; set; }

    /// <summary>Private backing member, including its leading underscore.</summary>
    public required string FieldName { get; set; }

    /// <summary>Property name in Pascal case, used for the index map members.</summary>
    public required string PascalName { get; set; }

    /// <summary>Member type.</summary>
    public required string FieldType { get; set; }

    /// <summary>What the member is declared as, when no column fills it.</summary>
    public required string DefaultValue { get; set; }

    /// <summary>
    /// Type the value has in the JSON export, which is not always the member type: a
    /// 64-bit integer is exported as a string, because JSON's single numeric type is a
    /// double and would round it.
    /// </summary>
    public required string JsonWireType { get; set; }

    public required int ElementCount { get; set; }

    /// <summary>Referenced table's name, without a suffix.</summary>
    public required string RefTable { get; set; }

    /// <summary>
    /// Which declaration shape applies: `array_ref`, `var_array`, `array`, `scalar_ref`
    /// or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    public required bool IsArray { get; set; }

    /// <summary>Type of the setter a resolved reference is assigned through.</summary>
    public required string ReferenceSetterType { get; set; }

    /// <summary>
    /// Whether the reference names a whole row rather than one of its fields.
    ///
    /// The two setters differ by a semicolon, which is arbitrary but is what the
    /// generated modules have always contained.
    /// </summary>
    public required bool ReferenceIsRecord { get; set; }

    /// <summary>The assignment reading this field out of a named JSON row.</summary>
    public required string FromNamedRow { get; set; }

    /// <summary>The statements reading this field out of a compact JSON row.</summary>
    public required IReadOnlyList<string> FromCompactRow { get; set; }
}
