using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>
/// Everything the C# template needs, worked out in advance.
///
/// Same division as the C++ view: the template decides where things go, and anything
/// that depends on the model - a type name, a read call, a rendered literal - arrives
/// already finished.
/// </summary>
internal sealed class CsFileView
{
    /// <summary>The namespace, or empty. The template wraps the file in it when set.</summary>
    public required string Namespace { get; set; }

    /// <summary>
    /// Extension the recipe told the exporter to write, which is what the accessor's read
    /// defaults to.
    /// </summary>
    /// <remarks>
    /// It was a `".scb"` literal in the template until this existed - so a recipe that set
    /// the extension on both the export and this target got the right file names out of the
    /// exporter and a reader that looked for the default anyway.
    /// </remarks>
    public required string FileExtension { get; set; }

    public required IReadOnlyList<CsTableView> Tables { get; set; }

    public required IReadOnlyList<CsEnumView> Enums { get; set; }

    public required IReadOnlyList<CsConstantSetView> ConstantSets { get; set; }

    /// <summary>
    /// Only the tables that reference another.
    ///
    /// A separate list rather than a test inside the template, because the blank line
    /// separating one table's resolution block from the next has to count these and not
    /// every table - which is what the hand-written version did with its own counter.
    /// </summary>
    public required IReadOnlyList<CsTableView> TablesWithReferences { get; set; }
}

/// <summary>
/// One generated file, for the templates that render one thing.
/// </summary>
/// <remarks>
/// The output is a file per table, per enum and per constant set, and each of those
/// templates needs the namespace as well as its own subject. Rather than hand every
/// template the whole model and trust it to loop over only the right part, each gets a
/// view holding exactly what it is for - so a template cannot reach a table it is not
/// writing.
///
/// One class with one payload property rather than four near-identical ones, because the
/// only thing they would differ in is the name of that property and Scriban addresses it
/// by name from the template.
/// </remarks>
internal sealed class CsPartView
{
    /// <summary>The namespace, or empty. The head template wraps the file in it when set.</summary>
    public string? Namespace { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public CsTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public CsEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public CsConstantSetView? Set { get; set; }
}

internal sealed class CsTableView
{
    /// <summary>Table name in Pascal case; the class is this plus `Table`.</summary>
    public required string Name { get; set; }

    /// <summary>Table name as the sheet spelled it, used in the data file name.</summary>
    public required string RawName { get; set; }

    /// <summary>Doc-comment lines, already split. Empty when the sheet had no comment.</summary>
    public required IReadOnlyList<string> Comment { get; set; }

    public required IReadOnlyList<CsFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from <see cref="Fields"/> because they are separate units:
    /// declaring a member is per field, and reading is per column. They are the same
    /// thing for every table written before records existed - a folded group is one
    /// column - and a record group is one column per member.
    ///
    /// Keeping them apart is what makes record support a different list rather than a
    /// second branch through the read path. See spec/nested-fields.md.
    /// </remarks>
    public required IReadOnlyList<CsColumnView> Columns { get; set; }

    /// <summary>The fields a lookup dictionary is built for.</summary>
    public required IReadOnlyList<CsFieldView> IndexedFields { get; set; }

    /// <summary>The fields that point at another table.</summary>
    public required IReadOnlyList<CsFieldView> ReferenceFields { get; set; }

    /// <summary>
    /// Whether the read needs a scratch int for enum casting.
    ///
    /// The reader hands back an int and the field is an enum, so one temporary is
    /// declared for the whole method rather than one per field.
    /// </summary>
    public required bool NeedsEnumTemp { get; set; }

    /// <summary>
    /// Whether the read declares the column cursor: true when any scalar column can
    /// arrive encoded, which is what the cursor exists to decode.
    /// </summary>
    public required bool NeedsCursor { get; set; }

    /// <summary>`"A", "B"` - the field-name array literal's contents.</summary>
    public required string FieldNameLiterals { get; set; }

    /// <summary>`r.A, r.B` - the value-map row's contents.</summary>
    public required string FieldValueExpressions { get; set; }
}

/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
/// <remarks>
/// Everything here answers "how is this column read", which is a question about the file.
/// What the value is stored in - the member's name, its type, its element count - comes
/// along because the read has to assign somewhere, but the shape of the declaration is
/// <see cref="CsFieldView"/>'s business.
/// </remarks>
internal sealed class CsColumnView
{
    /// <summary>The column's wire tag, which is how the read matches it in a file.</summary>
    public required int Tag { get; set; }

    /// <summary>
    /// The rendered CheckColumn call: kind, count and the elements this column accepts -
    /// its own, plus the lossless promotions.
    /// </summary>
    public required string ColumnCheck { get; set; }

    /// <summary>Which read shape applies: `var_array`, `serial` or `scalar`.</summary>
    public required string ReadKind { get; set; }

    /// <summary>
    /// The rendered cursor construction placed ahead of the row loop, or empty for a
    /// column that never arrives encoded and keeps reading the reader directly.
    /// </summary>
    public required string CursorOpen { get; set; }

    /// <summary>The lines reading one element, at whatever depth the template places them.</summary>
    public required IReadOnlyList<string> ElementRead { get; set; }

    /// <summary>
    /// The cursor's run method for a scalar whose column can arrive run-length encoded,
    /// or empty for one that reads row by row.
    /// </summary>
    public required string RunCall { get; set; }

    /// <summary>
    /// The lines assigning one row from `value`, inside the loop <see cref="RunCall"/>
    /// opens. Empty exactly when <see cref="RunCall"/> is.
    /// </summary>
    public required IReadOnlyList<string> RunRead { get; set; }

    /// <summary>Backing field this column fills, including its leading underscore.</summary>
    public required string FieldName { get; set; }

    /// <summary>Element type name of that member.</summary>
    public required string FieldType { get; set; }

    /// <summary>
    /// Property name of the member, which is how the generated element-count constant
    /// (`Record.{PropName}_N`) is reached.
    /// </summary>
    public required string PropName { get; set; }
}

/// <summary>
/// One member of a record group: a field of the generated element type.
/// </summary>
internal sealed class CsRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Field name on the element type, Pascal cased.</summary>
    public required string PropName { get; set; }

    /// <summary>That field's type name.</summary>
    public required string FieldType { get; set; }

    /// <summary>
    /// What follows the declaration to initialize it, or nothing where C#'s own default
    /// is already an empty value.
    /// </summary>
    public required string Initializer { get; set; }

    /// <summary>Whether a comma follows in the element type's ToString.</summary>
    public required bool IsFirst { get; set; }
}

/// <summary>
/// One serial field, in every shape the generated class distinguishes.
/// </summary>
internal sealed class CsFieldView
{
    /// <summary>
    /// Whether this field is a record group, so the template declares an element type for
    /// it and the member is of that type rather than a primitive.
    /// </summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Name of the generated element type, for a record group. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// The group name plus `Entry`, declared inside `Record`. It cannot simply be the
    /// group name: that is already the property's name, and C# does not allow a nested
    /// type and a member to share one.
    /// </remarks>
    public required string RecordTypeName { get; set; }

    /// <summary>Fields of the element type. Empty unless <see cref="IsRecord"/>.</summary>
    public required IReadOnlyList<CsRecordMemberView> Members { get; set; }

    /// <summary>
    /// Whether the element type needs a factory that fills its string fields, because a
    /// struct cannot initialize its own.
    /// </summary>
    /// <remarks>
    /// Field initializers in a struct are C# 10 and need an explicit parameterless
    /// constructor; the generated code has to compile as C# 8, which is what Unity 2020.3
    /// accepts. So a static factory sets them instead, and the record's field initializer
    /// calls it.
    ///
    /// It is not cosmetic. A file written before a member existed carries no column for
    /// it, so nothing writes that field - and the guarantee everywhere else in this
    /// generator is that such a string arrives empty rather than null, because null is a
    /// crash one field later.
    /// </remarks>
    public required bool NeedsElementInit { get; set; }

    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Public property name.</summary>
    public required string PropName { get; set; }

    /// <summary>Private backing field name, including its leading underscore.</summary>
    public required string FieldName { get; set; }

    /// <summary>Element type name.</summary>
    public required string FieldType { get; set; }

    /// <summary>
    /// What follows the member's declaration to initialize it, or nothing when C#'s own
    /// default is already an empty value.
    /// </summary>
    public required string Initializer { get; set; }

    /// <summary>Element count of a serial field, which is its column count.</summary>
    public required int ElementCount { get; set; }

    /// <summary>Referenced table's class name, without the `Table` suffix.</summary>
    public required string RefTable { get; set; }

    /// <summary>
    /// The referenced table's throwing lookup, which is what a key resolves through.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `GetByIndexOrThrow`. The
    /// primary index is whatever the sheet put in the first column - its type is checked
    /// to be `int`, but its name is not - so a sheet that calls it `Id` generates
    /// `GetByIdOrThrow`, and the accessor is the only place that has to know.
    /// </remarks>
    public required string RefLookup { get; set; }

    /// <summary>Referenced field's property name, empty when the reference names a whole row.</summary>
    public required string RefField { get; set; }

    /// <summary>
    /// Which declaration shape applies: `array_ref`, `var_array`, `array`, `scalar_ref`
    /// or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>Type of the setter a resolved reference is assigned through.</summary>
    public required string ReferenceSetterType { get; set; }

    /// <summary>Whether the reference names a field of the target rather than the row.</summary>
    public required bool ReferencesField { get; set; }
}

internal sealed class CsEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CsEnumLabelView> Labels { get; set; }
}

internal sealed class CsEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Whether a trailing comma follows. C# allows one; the generator omits it.</summary>
    public required bool IsLast { get; set; }
}

internal sealed class CsConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CsConstantView> Constants { get; set; }
}

internal sealed class CsConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}
