using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>Everything the C templates need, worked out in advance.</summary>
internal sealed class CFileView
{
    /// <summary>
    /// What every generated identifier starts with.
    ///
    /// C has no namespaces, so the prefix is the whole of the collision avoidance.
    /// Taken from the accessor name, lower_snake_case.
    /// </summary>
    public required string Prefix { get; set; }

    /// <summary>The prefix in upper case, for the include guard and the enum constants.</summary>
    public required string UpperPrefix { get; set; }

    /// <summary>Name of the header, so the .c can include it.</summary>
    public required string HeaderName { get; set; }

    public required IReadOnlyList<CEnumView> Enums { get; set; }
    public required IReadOnlyList<CConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<CTableView> Tables { get; set; }
    public required CAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file: what it has to say at the top, and the single thing it declares.
/// </summary>
/// <remarks>
/// C is the one target where the top of the file is not bookkeeping. An include has to come
/// before what uses it, a struct member of struct type needs the complete type, and a header
/// included twice in one translation unit has to be harmless. So the three are separate
/// fields rather than one list of lines: they are answered differently and they go in a
/// particular order.
/// </remarks>
internal sealed class CPartView
{
    /// <summary>The include guard macro. Empty for a source file, which needs none.</summary>
    public string? Guard { get; set; }

    /// <summary>`#include` lines, in the order they have to appear.</summary>
    public IReadOnlyList<string>? Includes { get; set; }

    /// <summary>
    /// Forward declaration lines. Only the forward header itself has any; every other file
    /// includes that instead.
    /// </summary>
    public IReadOnlyList<string>? Forwards { get; set; }

    /// <summary>
    /// Whether to wrap the file in `extern "C"`.
    ///
    /// Only where it means something: a typedef, an enum and a struct have no linkage, so an
    /// enum header does not need it. A function declaration and an `extern const` do.
    /// </summary>
    public bool ExternC { get; set; }

    /// <summary>Record type names, for the forward header.</summary>
    public IReadOnlyList<string>? Records { get; set; }

    /// <summary>The table this file is for, when it is a table header or source.</summary>
    public CTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum header.</summary>
    public CEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants header or source.</summary>
    public CConstantSetView? Set { get; set; }

    /// <summary>The accessor's own shape, for its header and source.</summary>
    public CAccessorView? Accessor { get; set; }
}

/// <summary>
/// One constant set.
/// </summary>
/// <remarks>
/// C has nothing to nest a set in, so the constants themselves are flat and each carries its
/// set's name. They are still grouped here, because the set is the unit the sheets add and
/// remove and so the unit a file corresponds to.
/// </remarks>
internal sealed class CConstantSetView
{
    /// <summary>The set's name, PascalCase, which names its files.</summary>
    public required string Name { get; set; }

    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CConstantView> Constants { get; set; }
}

internal sealed class CEnumView
{
    /// <summary>Enum name as the sheet spelled it, PascalCase. Names its header.</summary>
    /// <remarks>
    /// Separate from <see cref="Name"/> because that one already carries the accessor prefix -
    /// it is the C type name - and a file named from it comes out as
    /// `X_EnumX_Flag_t.h`.
    /// </remarks>
    public required string RawName { get; set; }

    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CEnumLabelView> Labels { get; set; }
}

internal sealed class CEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

/// <summary>
/// One constant, already flattened out of its set.
///
/// C has nothing to nest them in, so the set's name becomes part of each constant's
/// name rather than a scope around them.
/// </summary>
internal sealed class CConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// Whether the value can be an initializer in a header.
    ///
    /// A uuid cannot: it is a struct, and one defined in a header would be a separate
    /// object in every translation unit that included it. Those go in the .c and are
    /// declared `extern` instead.
    /// </summary>
    public required bool IsExtern { get; set; }
}

internal sealed class CTableView
{
    public required string RawName { get; set; }

    /// <summary>The record struct's name, already prefixed.</summary>
    public required string RecordName { get; set; }

    /// <summary>The table struct's name, already prefixed.</summary>
    public required string TableName { get; set; }

    /// <summary>What the table's functions are called, minus the verb.</summary>
    public required string FunctionPrefix { get; set; }

    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<CIndexView> Indexes { get; set; }

    /// <summary>Whether any member holds strings, and so needs the pre-read pass.</summary>
    public required bool HasStringFields { get; set; }

    public required IReadOnlyList<CFieldView> Fields { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
/// <remarks>
/// Two lookups rather than the three every other target gets. C has nothing to throw,
/// so there is no honest `GetBy...OrThrow` to generate - a caller that needs the row
/// to be there checks the NULL, which is the same check it would write anyway.
/// </remarks>
internal sealed class CIndexView
{
    /// <summary>The record member holding the key, escaped.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `...FindByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's type, as a parameter declaration.</summary>
    public required string KeyType { get; set; }

    /// <summary>The runtime's entry type for this key: `sm_index_entry` and its kin.</summary>
    public required string EntryType { get; set; }

    /// <summary>The runtime's sort for this key.</summary>
    public required string SortCall { get; set; }

    /// <summary>The runtime's bisection for this key.</summary>
    public required string FindCall { get; set; }

    /// <summary>The table member holding the sorted entries.</summary>
    public required string ArrayName { get; set; }

    /// <summary>The field as the sheet spells it, for the doc comment.</summary>
    public required string FieldName { get; set; }
}

internal sealed class CFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The member declarations.
    ///
    /// A list, because a variable length array contributes a pointer and a count, and a
    /// reference contributes an index as well as the resolved pointer.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
    /// `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// Whether this member holds strings, and so needs pointing at something before
    /// the read.
    /// </summary>
    /// <remarks>
    /// The arena hands back zeroed memory, which for a `const char*` is NULL - and a
    /// column the file does not carry leaves it that way. Every other language gives an
    /// empty string there; in C a NULL reaches printf and takes the process with it, so
    /// the generated parse points every string member at "" before reading a column.
    /// </remarks>
    public required bool IsString { get; set; }

    /// <summary>The column's wire tag, which is what the read matches on.</summary>
    public required int Tag { get; set; }

    /// <summary>The rendered sm_check_column call for this member.</summary>
    public required string ColumnCheck { get; set; }

    public required int ElementCount { get; set; }

    /// <summary>The element's C type, for the array allocations.</summary>
    public required string ElementType { get; set; }

    /// <summary>The complete reader call for a scalar member.</summary>
    public required string ReadScalar { get; set; }

    /// <summary>The complete reader call for one element of an array member.</summary>
    public required string ReadElement { get; set; }

    /// <summary>
    /// Whether reading needs a scratch int32 first, which only an enum does.
    ///
    /// The reader hands back the underlying value and the member is the enum type, so
    /// there is a cast in between and nowhere to put it in a single call.
    /// </summary>
    public required bool NeedsScratch { get; set; }

    /// <summary>The enum's type name, when this field is one.</summary>
    public required string EnumType { get; set; }
}

internal sealed class CAccessorView
{
    /// <summary>The prefix its functions carry: `SheetManData`, giving `SheetManData_Free`.</summary>
    public required string Name { get; set; }

    /// <summary>Its struct's name, which is the prefix with the type suffix.</summary>
    public required string TypeName { get; set; }

    public required string FileExtension { get; set; }
    public required IReadOnlyList<CTableSlotView> Tables { get; set; }
    public required IReadOnlyList<CCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class CTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string FunctionPrefix { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class CCrossReferenceView
{
    public required string Table { get; set; }
    public required string FunctionPrefix { get; set; }

    /// <summary>The record struct being walked, which the loop declares a pointer to.</summary>
    public required string RecordName { get; set; }

    public required IReadOnlyList<CReferenceFieldView> Fields { get; set; }
}

internal sealed class CReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }
    public required string RefFunctionPrefix { get; set; }

    /// <summary>
    /// The referenced table's primary lookup, prefix and all, which is what a key
    /// resolves through.
    /// </summary>
    public required string RefLookup { get; set; }

    /// <summary>The referenced record's struct, which the resolved member points at.</summary>
    public required string RefRecordName { get; set; }

    /// <summary>What the resolved member is assigned, with `target` naming the row.</summary>
    public required string Value { get; set; }

    public required bool IsArray { get; set; }

    /// <summary>
    /// How many elements the resolution loop runs over.
    ///
    /// A literal for a serial field, whose length is fixed at generation, and the
    /// record's own count member for a variable length one.
    /// </summary>
    public required string CountExpression { get; set; }
}
