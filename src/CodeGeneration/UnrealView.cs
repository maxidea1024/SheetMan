using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>Everything the Unreal templates need, worked out in advance.</summary>
    internal sealed class UnrealFileView
    {
        /// <summary>Name of the accessor class, which also names the header and the .cpp.</summary>
        public string AccessorName { get; set; }

        /// <summary>
        /// The module's export macro, `MODULENAME_API`.
        ///
        /// Every public type carries it, or the module links but nothing outside it can
        /// reach the generated types.
        /// </summary>
        public string ApiMacro { get; set; }

        public IReadOnlyList<UnrealEnumView> Enums { get; set; }
        public IReadOnlyList<UnrealTableView> Tables { get; set; }
        public UnrealAccessorView Accessor { get; set; }
    }

    internal sealed class UnrealEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<UnrealEnumLabelView> Labels { get; set; }

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
        public bool BlueprintVisible { get; set; }

        /// <summary>The underlying type: `uint8` normally, `int32` when a label does not fit.</summary>
        public string UnderlyingType { get; set; }

        /// <summary>Which label pushed it past uint8, for the comment that says so.</summary>
        public string NotVisibleBecause { get; set; }
    }

    internal sealed class UnrealEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }

        /// <summary>What the editor shows, which is the label as the sheet spelled it.</summary>
        public string DisplayName { get; set; }

        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class UnrealTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// The indexed fields: the sheet's first column plus every one marked with `*`.
        /// </summary>
        public IReadOnlyList<UnrealIndexView> Indexes { get; set; }

        public IReadOnlyList<UnrealFieldView> Fields { get; set; }
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
        public string Member { get; set; }

        /// <summary>What the lookup names end in - `Index` gives `FindByIndex`.</summary>
        public string Suffix { get; set; }

        /// <summary>The key's type.</summary>
        public string KeyType { get; set; }

        /// <summary>
        /// The type the lookups take: a const reference where a copy would cost, the value
        /// itself where it would not.
        /// </summary>
        public string KeyParam { get; set; }

        /// <summary>The member holding the map from key to row position.</summary>
        public string MapName { get; set; }

        /// <summary>The local the read builds before publishing it.</summary>
        public string LocalName { get; set; }

        /// <summary>The field as the sheet spells it, for the doc comment.</summary>
        public string FieldName { get; set; }
    }

    internal sealed class UnrealFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>The member declaration, including its initializer.</summary>
        public string Declaration { get; set; }

        /// <summary>
        /// Whether the member carries a UPROPERTY.
        ///
        /// Almost always yes. A double does not, because UE4's header tool rejects the type
        /// outright and the generated module is meant to build on both UE4 and UE5.
        /// </summary>
        public bool BlueprintVisible { get; set; }

        /// <summary>Why it does not, written into the generated code beside the member.</summary>
        public string NotVisibleBecause { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        public int ElementCount { get; set; }

        /// <summary>
        /// `Read` for everything but an enum, which has its own overload.
        ///
        /// There is no conversion step beside it any more. The Unreal reader fills the
        /// member itself - an FString, an FGuid, an FDateTime - so a read is one line
        /// rather than a block that declares a temporary, fills it and converts.
        /// </summary>
        public string ReadCall { get; set; }

        /// <summary>The column's wire tag, which is what the read matches on.</summary>
        public int Tag { get; set; }

        /// <summary>The rendered CheckColumn call for this member.</summary>
        public string ColumnCheck { get; set; }

        /// <summary>
        /// Name for the local holding a variable length array's element count.
        ///
        /// Chosen so it cannot shadow a member of the same record. The loops themselves
        /// need no counter - they run until the array has the elements it should - but the
        /// count off the wire has to live somewhere.
        /// </summary>
        public string CountLocal { get; set; }
    }

    internal sealed class UnrealAccessorView
    {
        public string FileExtension { get; set; }

        /// <summary>
        /// The Blueprint function library's class name.
        /// </summary>
        /// <remarks>
        /// Built in the generator rather than the template, which produced
        /// `UFSheetManCoreLibrary` by putting `U` in front of an accessor already prefixed
        /// `F`. Unreal's prefix says what a type is - `U` for a UObject, `F` for a plain
        /// class - so the old one comes off before the new one goes on.
        /// </remarks>
        public string LibraryName { get; set; }

        public IReadOnlyList<UnrealTableSlotView> Tables { get; set; }
    }

    internal sealed class UnrealTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }

        /// <summary>The row struct, which the Blueprint library hands back by value.</summary>
        public string RecordName { get; set; }

        /// <summary>The table's name as the sheet spelled it, for the Blueprint category.</summary>
        public string RawName { get; set; }

        /// <summary>
        /// The primary index's lookup, which is what the Blueprint node calls.
        /// </summary>
        public string PrimaryLookup { get; set; }

        /// <summary>The primary index's key type, which the Blueprint node takes.</summary>
        public string PrimaryKeyType { get; set; }

        /// <summary>The primary index's key parameter type.</summary>
        public string PrimaryKeyParam { get; set; }

        /// <summary>The primary index's field name, as the sheet spells it.</summary>
        public string PrimaryFieldName { get; set; }

        public string DataFileName { get; set; }
    }
}
