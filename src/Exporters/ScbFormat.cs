using SheetMan.Models;

using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.Exporters;

/// <summary>
/// The constants of the Scb format: what a column descriptor's wire byte means,
/// and how a model field maps onto it.
/// </summary>
/// <remarks>
/// The format is column-oriented and self-describing. The header carries one descriptor per
/// column - tag, wire, element count, byte length - and the data follows as one contiguous
/// block per column. That layout is what makes schema evolution safe to the point of being
/// boring: a reader that does not know a column advances past its block in one call, with
/// no per-type skip logic to get wrong, and a column is identified by its tag rather than
/// its position, so adding, removing, renaming and reordering columns are all invisible to
/// a reader built from a different generation of the model.
///
/// The wire byte packs two facts: the low four bits are the element type, the next two are
/// the kind. Element types are semantic, not just sizes - i32 and f32 are both four bytes,
/// but a reader promoting a value needs to know which interpretation it is widening.
///
/// Every reader carries the same table of constants. This file is the writer's copy and the
/// authoritative one; a change here is a format change and has to be made in the twelve
/// reader runtimes as well, which the conformance corpus and the format golden are there to
/// enforce.
/// </remarks>
public static class ScbFormat
{
    /// <summary>
    /// The format version stamped at the head of every table file.
    ///
    /// One version exists, and a reader that meets any other stops rather than guessing.
    /// There is no compatibility path to an older layout and none is planned: a file this
    /// build cannot read is a file to write again, not one to interpret.
    ///
    /// 102 replaced 101 outright - a descriptor gained its encoding byte - before any
    /// 101 file had shipped.
    /// </summary>
    public const uint Version = 102;

    // ------------------------------------------------------- element types

    /// <summary>Zig-zag varint, at most five bytes. Enums travel this way.</summary>
    public const byte ElementVarint = 0;

    public const byte ElementBool = 1;

    /// <summary>Four bytes little endian, interpreted as a signed integer.</summary>
    public const byte ElementI32 = 2;

    /// <summary>Eight bytes little endian: bigint, and datetime/timespan ticks.</summary>
    public const byte ElementI64 = 3;

    /// <summary>Four bytes, an IEEE-754 single's bit pattern.</summary>
    public const byte ElementF32 = 4;

    /// <summary>Eight bytes, an IEEE-754 double's bit pattern.</summary>
    public const byte ElementF64 = 5;

    /// <summary>A counter32 byte length followed by that many UTF-8 bytes.</summary>
    public const byte ElementString = 6;

    /// <summary>Sixteen bytes in .NET's Guid layout.</summary>
    public const byte ElementUuid = 7;

    // --------------------------------------------------------------- kinds

    /// <summary>One value per row.</summary>
    public const byte KindScalar = 0;

    /// <summary>A fixed number of elements per row; the count is in the descriptor.</summary>
    public const byte KindFixedArray = 1;

    /// <summary>Each row carries its own counter32 length ahead of its elements.</summary>
    public const byte KindVarArray = 2;

    // ----------------------------------------------------------- encodings
    //
    // How a column block's values are laid out, chosen per column by measuring every
    // applicable candidate and keeping the smallest (ties go to the lowest number).
    // The spec is spec/scb-v102-column-encoding.md; the reason is that a static
    // table's columns repeat themselves - the same string thousands of times, ids
    // that step by one - and one byte per column is all it costs to say so.

    /// <summary>The value stream as v101 wrote it. The only encoding for arrays.</summary>
    public const byte EncodingRaw = 0;

    /// <summary>Each value as a counter32. i32 scalars whose values are small.</summary>
    public const byte EncodingVarint = 1;

    /// <summary>First value, then counter32 deltas (32-bit wrapping). i32 scalars.</summary>
    public const byte EncodingDelta = 2;

    /// <summary>(counter32 run length, counter32 value) pairs. i32, varint and bool scalars.</summary>
    public const byte EncodingRle = 3;

    /// <summary>First value, then the delta stream run-length encoded. i32 scalars.</summary>
    public const byte EncodingDeltaRle = 4;

    /// <summary>
    /// A dictionary of the distinct values, then a counter32 index per row.
    /// </summary>
    /// <remarks>
    /// Parameterized by element: an entry is the value in its raw form, so a string
    /// dictionary holds length-prefixed UTF-8 and an f32 dictionary holds four bytes.
    /// That is why the dictionary reaches past strings without costing another encoding
    /// number - and it needs to, because a column of floats in design data is a handful
    /// of values repeated, whatever a single float looks like.
    /// </remarks>
    public const byte EncodingDict = 5;

    /// <summary>The dictionary, then the index stream run-length encoded.</summary>
    public const byte EncodingDictRle = 6;

    /// <summary>
    /// A sorted string dictionary whose entries state only what they do not share with
    /// the entry before, then a counter32 index per row.
    /// </summary>
    /// <remarks>
    /// Because design-data strings are rarely duplicates of each other and very often
    /// neighbours: `02_CRI_DAMAGE_FLOAT` beside `02_CRI_INT`, one skill tier beside the
    /// next. A dictionary still has to hold every one of them, but not every byte of
    /// every one - and on real data that is where most of the remaining bytes were.
    /// </remarks>
    public const byte EncodingDictFront = 7;

    /// <summary>The front-coded dictionary, then the index stream run-length encoded.</summary>
    public const byte EncodingDictFrontRle = 8;

    /// <summary>The wire byte: element in the low four bits, kind in the next two.</summary>
    public static byte Wire(byte element, byte kind) => (byte)(element | (kind << 4));

    public static byte ElementOf(byte wire) => (byte)(wire & 0x0F);
    public static byte KindOf(byte wire) => (byte)((wire >> 4) & 0x03);

    // ------------------------------------------------------------- mapping

    /// <summary>The element type a column's values travel as.</summary>
    public static byte ElementFor(WireColumn column)
    {
        // A reference is stored as the target's primary index, which the cooker
        // guarantees is an int32.
        if (column.IsRef)
            return ElementI32;

        switch (column.ElementType)
        {
            case ValueType.String: return ElementString;
            case ValueType.Bool: return ElementBool;
            case ValueType.Int32: return ElementI32;
            case ValueType.Int64: return ElementI64;
            case ValueType.Float: return ElementF32;
            case ValueType.Double: return ElementF64;

            // Both are .NET ticks, an i64 on the wire.
            case ValueType.DateTime: return ElementI64;
            case ValueType.TimeSpan: return ElementI64;

            case ValueType.Uuid: return ElementUuid;
            case ValueType.Enum: return ElementVarint;

            default:
                throw new SheetManException(
                    $"The binary exporter cannot map type `{column.Type}` onto a wire element.");
        }
    }

    /// <summary>The kind of a column, mirroring what the generators emit.</summary>
    public static byte KindFor(WireColumn column)
    {
        if (column.IsVariableLengthArray)
            return KindVarArray;

        return column.IsFixedArray ? KindFixedArray : KindScalar;
    }

    /// <summary>
    /// The descriptor's element count: 1 for a scalar, the element count for a fixed
    /// array, and 0 for a variable one, whose rows carry their own.
    /// </summary>
    public static int CountFor(WireColumn column)
    {
        if (column.IsVariableLengthArray)
            return 0;

        return column.Cells.Count;
    }
}
