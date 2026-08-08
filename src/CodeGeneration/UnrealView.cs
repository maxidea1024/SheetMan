using System.Collections.Generic;

namespace SheetMan.CodeGeneration;

/// <summary>Everything the Unreal templates need, worked out in advance.</summary>
internal sealed class UnrealFileView
{
    /// <summary>Name of the accessor class, which also names the header and the .cpp.</summary>
    public required string AccessorName { get; set; }

    /// <summary>
    /// The module's export macro, `MODULENAME_API`.
    ///
    /// Every public type carries it, or the module links but nothing outside it can
    /// reach the generated types.
    /// </summary>
    public required string ApiMacro { get; set; }

    public required IReadOnlyList<UnrealEnumView> Enums { get; set; }
    public required IReadOnlyList<UnrealTableView> Tables { get; set; }
    public required UnrealAccessorView Accessor { get; set; }
}

internal sealed class UnrealEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<UnrealEnumLabelView> Labels { get; set; }

    /// <summary>
    /// Whether this enum can be `UENUM(BlueprintType)`, which requires a uint8 underlying
    /// type and so every label between 0 and 255.
    /// </summary>
    /// <remarks>
    /// A label outside that range used to refuse the whole conversion. Which made the Unreal
    /// target the one that could not read a model the other eleven read - and the values are
    /// the sheet's, not something a generator gets to reject. It degrades instead: the enum
    /// widens to int32, stays a UENUM so it is still reflected and still serialises, and
    /// loses only its Blueprint visibility. The fields typed with it lose theirs too, because
    /// UHT will not expose a property whose type Blueprint cannot see.
    /// </remarks>
    public required bool BlueprintVisible { get; set; }

    /// <summary>The underlying type: `uint8` normally, `int32` when a label does not fit.</summary>
    public required string UnderlyingType { get; set; }

    /// <summary>Which label pushed it past uint8, for the comment that says so.</summary>
    public required string NotVisibleBecause { get; set; }
}

internal sealed class UnrealEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }

    /// <summary>What the editor shows, which is the label as the sheet spelled it.</summary>
    public required string DisplayName { get; set; }

    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class UnrealTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<UnrealIndexView> Indexes { get; set; }

    public required IReadOnlyList<UnrealFieldView> Fields { get; set; }

    /// <summary>
    /// Whether any column reads through the cursor, and so the read declares one.
    ///
    /// One cursor variable for the whole method: the switch's cases share a scope, and
    /// C++ does not allow a jump past a live constructor, so each encodable column
    /// opens the shared cursor rather than declaring its own.
    /// </summary>
    public required bool NeedsCursor { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
/// <remarks>
/// Two lookups rather than the three every other target gets. A module built with
/// exceptions disabled - which is every Unreal module unless its Build.cs says
/// otherwise - has nothing to throw, so there is no honest `GetBy...OrThrow` to
/// generate. The same reason the reader reports a malformed file with a flag.
/// </remarks>
internal sealed class UnrealIndexView
{
    /// <summary>The record member holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `FindByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's type.</summary>
    public required string KeyType { get; set; }

    /// <summary>
    /// The type the lookups take: a const reference where a copy would cost, the value
    /// itself where it would not.
    /// </summary>
    public required string KeyParam { get; set; }

    /// <summary>The member holding the map from key to row position.</summary>
    public required string MapName { get; set; }

    /// <summary>The local the read builds before publishing it.</summary>
    public required string LocalName { get; set; }

    /// <summary>The field as the sheet spells it, for the doc comment.</summary>
    public required string FieldName { get; set; }
}

internal sealed class UnrealFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>The member declaration, including its initializer.</summary>
    public required string Declaration { get; set; }

    /// <summary>
    /// Whether the member carries a UPROPERTY.
    ///
    /// Almost always yes. A double does not, because UE4's header tool rejects the type
    /// outright and the generated module is meant to build on both UE4 and UE5.
    /// </summary>
    public required bool BlueprintVisible { get; set; }

    /// <summary>Why it does not, written into the generated code beside the member.</summary>
    public required string NotVisibleBecause { get; set; }

    /// <summary>
    /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
    /// `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    public required int ElementCount { get; set; }

    /// <summary>
    /// `Read` for everything but an enum, which has its own overload.
    ///
    /// There is no conversion step beside it any more. The Unreal reader fills the
    /// member itself - an FString, an FGuid, an FDateTime - so a read is one line
    /// rather than a block that declares a temporary, fills it and converts.
    /// </summary>
    public required string ReadCall { get; set; }

    /// <summary>The column's wire tag, which is what the read matches on.</summary>
    public required int Tag { get; set; }

    /// <summary>The rendered CheckColumn call for this member.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The cursor Open call ahead of an encodable column's row loop, or empty for a
    /// column that reads the reader directly.
    /// </summary>
    public required string CursorOpen { get; set; }

    /// <summary>
    /// The one-line read through the cursor inside the row loop, or empty for a
    /// column that reads the reader directly.
    /// </summary>
    public required string CursorRead { get; set; }

    /// <summary>
    /// Name for the local holding a variable length array's element count.
    ///
    /// Chosen so it cannot shadow a member of the same record. The loops themselves
    /// need no counter - they run until the array has the elements it should - but the
    /// count off the wire has to live somewhere.
    /// </summary>
    public required string CountLocal { get; set; }
}

internal sealed class UnrealAccessorView
{
    public required string FileExtension { get; set; }

    /// <summary>
    /// The Blueprint function library's class name.
    /// </summary>
    /// <remarks>
    /// Built in the generator rather than the template, which produced
    /// `UFSheetManCoreLibrary` by putting `U` in front of an accessor already prefixed
    /// `F`. Unreal's prefix says what a type is - `U` for a UObject, `F` for a plain
    /// class - so the old one comes off before the new one goes on.
    /// </remarks>
    public required string LibraryName { get; set; }

    public required IReadOnlyList<UnrealTableSlotView> Tables { get; set; }
}

internal sealed class UnrealTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }

    /// <summary>The row struct, which the Blueprint library hands back by value.</summary>
    public required string RecordName { get; set; }

    /// <summary>The table's name as the sheet spelled it, for the Blueprint category.</summary>
    public required string RawName { get; set; }

    /// <summary>
    /// The primary index's lookup, which is what the Blueprint node calls.
    /// </summary>
    public required string PrimaryLookup { get; set; }

    /// <summary>The primary index's key type, which the Blueprint node takes.</summary>
    public required string PrimaryKeyType { get; set; }

    /// <summary>The primary index's key parameter type.</summary>
    public required string PrimaryKeyParam { get; set; }

    /// <summary>The primary index's field name, as the sheet spells it.</summary>
    public required string PrimaryFieldName { get; set; }

    public required string DataFileName { get; set; }
}
