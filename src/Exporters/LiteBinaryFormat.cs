using SheetMan.Models;

using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.Exporters
{
    /// <summary>
    /// The constants of the LiteBinary format: what a column descriptor's wire byte means,
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
    public static class LiteBinaryFormat
    {
        /// <summary>
        /// The format version stamped at the head of every table file.
        ///
        /// One version exists, and a reader that meets any other stops rather than guessing.
        /// There is no compatibility path to an older layout and none is planned: a file this
        /// build cannot read is a file to write again, not one to interpret.
        /// </summary>
        public const uint Version = 101;

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

        /// <summary>The wire byte: element in the low four bits, kind in the next two.</summary>
        public static byte Wire(byte element, byte kind) => (byte)(element | (kind << 4));

        public static byte ElementOf(byte wire) => (byte)(wire & 0x0F);
        public static byte KindOf(byte wire) => (byte)((wire >> 4) & 0x03);

        // ------------------------------------------------------------- mapping

        /// <summary>The element type a serial field's values travel as.</summary>
        public static byte ElementFor(SerialField sf)
        {
            // A reference is stored as the target's primary index, which the cooker
            // guarantees is an int32.
            if (sf.IsRef)
                return ElementI32;

            switch (sf.ElementType)
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
                        $"The binary exporter cannot map type `{sf.Type}` onto a wire element.");
            }
        }

        /// <summary>The kind of a serial field's column, mirroring what the generators emit.</summary>
        public static byte KindFor(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return KindVarArray;

            return sf.Fields.Count > 1 ? KindFixedArray : KindScalar;
        }

        /// <summary>
        /// The descriptor's element count: 1 for a scalar, the column count for a fixed
        /// array, and 0 for a variable one, whose rows carry their own.
        /// </summary>
        public static int CountFor(SerialField sf)
        {
            if (sf.IsVariableLengthArray)
                return 0;

            return sf.Fields.Count;
        }
    }
}
