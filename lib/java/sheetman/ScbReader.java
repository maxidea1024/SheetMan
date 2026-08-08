// SheetMan's binary reader.
//
// Copied in beside the generated accessor so the emitted code needs nothing
// installed. Edit it in the SheetMan repository.
//
//
// Reads the .scb files SheetMan's binary exporter writes:
//
//   fixed8      one byte
//   fixed32     four bytes, little endian
//   fixed64     eight bytes, little endian
//   varint32    seven bits per byte, high bit set while more bytes follow,
//               at most five bytes
//   counter32   zig-zag encoded int written as a varint32
//   string      counter32 byte length, then that many UTF-8 bytes
//
// One of several readers of one format the exporter defines. The conformance corpus is
// what keeps them agreeing.
//
// Java has no unsigned types, which is the thing to be careful about here. A byte is
// signed, so a byte with the high bit set is negative and has to be masked before it is
// shifted into a varint. And the zig-zag undo shifts an int that is logically unsigned,
// so it needs the unsigned shift operator rather than the arithmetic one.

package sheetman;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/** Sequential reader over a table file's bytes. */
public final class ScbReader {

    /** Stamped at the head of every table file by the exporter. */
    /**
     * The format is column-oriented and self-describing: the header names every column
     * and how long its block is, and a reader that meets a version it does not know stops
     * rather than guessing. 102 replaced 101 outright - a descriptor gained its encoding
     * byte - before any 101 file had shipped.
     */
    public static final int FORMAT_VERSION = 102;

    // The wire's element types and kinds, as a column descriptor spells them.
    public static final int ELEMENT_VARINT = 0;
    public static final int ELEMENT_BOOL = 1;
    public static final int ELEMENT_I32 = 2;
    public static final int ELEMENT_I64 = 3;
    public static final int ELEMENT_F32 = 4;
    public static final int ELEMENT_F64 = 5;
    public static final int ELEMENT_STRING = 6;
    public static final int ELEMENT_UUID = 7;

    public static final int KIND_SCALAR = 0;
    public static final int KIND_FIXED_ARRAY = 1;
    public static final int KIND_VAR_ARRAY = 2;

    // How a block's values are laid out. Raw is the layout 101 had; the others compress
    // a column that repeats itself. spec/scb-v102-column-encoding.md is the contract.
    public static final int ENCODING_RAW = 0;
    public static final int ENCODING_VARINT = 1;
    public static final int ENCODING_DELTA = 2;
    public static final int ENCODING_RLE = 3;
    public static final int ENCODING_DELTA_RLE = 4;
    public static final int ENCODING_DICT = 5;
    public static final int ENCODING_DICT_RLE = 6;

    /** One column as the file describes it. */
    public static final class Column {
        /** What identifies the column, instead of its position. */
        public int tag;
        public int element;
        public int kind;
        /** How the block's values are laid out: one of the ENCODING_* constants. */
        public int encoding;
        /** Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one. */
        public int count;
        /** Total bytes of the column's block - what a skip advances by. */
        public int byteLength;
    }

    /** A table file is truncated, malformed, or not a table file. */
    /**
     * A lookup for a key no row carries.
     *
     * Thrown by the generated getBy*OrThrow lookups, which is where a caller has said the
     * key has to be there. findBy* answers the same question with null.
     *
     * Its own type rather than ScbException: nothing is wrong with the file, and a
     * caller catching one of these is not catching the other.
     */
    public static final class RecordNotFoundException extends RuntimeException {
        public RecordNotFoundException(String message) {
            super(message);
        }
    }

    public static final class ScbException extends RuntimeException {
        public ScbException(String message) {
            super(message);
        }
    }

    /**
     * A 128 bit identifier, stored in .NET Guid byte order.
     *
     * <p>That order is not plain big-endian: the first three components are little endian
     * and the trailing eight bytes are not, which is what toString has to account for.
     */
    public static final class Uuid {
        // Component order matching .NET's Guid.ToString("D").
        private static final int[] ORDER = {3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15};
        private static final char[] HEX = "0123456789abcdef".toCharArray();

        private final byte[] bytes;

        public Uuid(byte[] bytes) {
            this.bytes = bytes.clone();
        }

        public static Uuid empty() {
            return new Uuid(new byte[16]);
        }

        public byte[] bytes() {
            return bytes.clone();
        }

        @Override
        public String toString() {
            StringBuilder out = new StringBuilder(36);

            for (int position = 0; position < ORDER.length; position++) {
                if (position == 4 || position == 6 || position == 8 || position == 10) {
                    out.append('-');
                }

                // Masked, because a byte is signed and the high half would sign-extend.
                int value = bytes[ORDER[position]] & 0xFF;
                out.append(HEX[value >> 4]).append(HEX[value & 0x0F]);
            }

            return out.toString();
        }

        @Override
        public boolean equals(Object other) {
            return other instanceof Uuid && java.util.Arrays.equals(bytes, ((Uuid) other).bytes);
        }

        @Override
        public int hashCode() {
            return java.util.Arrays.hashCode(bytes);
        }
    }

    private final byte[] data;
    private int position;

    public ScbReader(byte[] data) {
        this.data = data;
        this.position = 0;
    }

    /** Bytes consumed so far. */
    /**
     * Advances past bytes without interpreting them: an unknown column's whole block.
     * The column-oriented layout is what makes this one call the entirety of skipping.
     */
    public void skip(int byteCount) {
        if (byteCount < 0 || byteCount > data.length - position) {
            throw new ScbException(
                "cannot skip " + byteCount + " bytes with " + (data.length - position) + " remaining");
        }
        position += byteCount;
    }

    // Promotions: a member reading a file element narrower than itself. Only the
    // mathematically lossless directions exist; checkColumn already refused the rest.

    /** An int member from i32 or varint. */
    public int readI32As(int element) {
        return element == ELEMENT_I32 ? readInt32() : readCounter32();
    }

    /** A long member from i64, i32 or varint. */
    public long readI64As(int element) {
        if (element == ELEMENT_I64) {
            return readInt64();
        }
        return element == ELEMENT_I32 ? readInt32() : readCounter32();
    }

    /** A double member from f64, f32 or i32 - all exact in a double. */
    public double readF64As(int element) {
        if (element == ELEMENT_F64) {
            return readDouble();
        }
        return element == ELEMENT_F32 ? readFloat() : readInt32();
    }

    public int position() {
        return position;
    }

    /** Bytes left to read. */
    public int remaining() {
        return data.length - position;
    }

    private int take(int count) {
        if (remaining() < count) {
            throw new ScbException(
                "table data ended after " + position + " of " + data.length
                    + " bytes while " + count + " more were expected");
        }

        int start = position;
        position += count;

        return start;
    }

    /** One byte, as an unsigned value in an int. */
    public int readUInt8() {
        return data[take(1)] & 0xFF;
    }

    public boolean readBool() {
        return readUInt8() != 0;
    }

    public int readInt32() {
        int at = take(4);

        return (data[at] & 0xFF)
            | ((data[at + 1] & 0xFF) << 8)
            | ((data[at + 2] & 0xFF) << 16)
            | ((data[at + 3] & 0xFF) << 24);
    }

    public long readInt64() {
        int at = take(8);

        long value = 0;
        for (int i = 7; i >= 0; i--) {
            value = (value << 8) | (data[at + i] & 0xFFL);
        }

        return value;
    }

    /**
     * A single-precision value as its stored bit pattern, so the value survives exactly
     * rather than through a decimal rendering.
     */
    public float readFloat() {
        return Float.intBitsToFloat(readInt32());
    }

    public double readDouble() {
        return Double.longBitsToDouble(readInt64());
    }

    /** A length-prefixed UTF-8 string. */
    public String readString() {
        int length = readCounter32();

        if (length < 0) {
            throw new ScbException("string length is negative");
        }

        if (length == 0) {
            return "";
        }

        int at = take(length);
        return new String(data, at, length, StandardCharsets.UTF_8);
    }

    /**
     * A timestamp as .NET ticks: 100 ns units since 0001-01-01.
     *
     * <p>Ticks rather than a java.time type, because a tick is finer than what Instant
     * stores in its nanosecond field only in principle - the real reason is that the
     * conversion is lossy in the other direction and a caller passing the value through
     * should not pay for it.
     */
    public long readDateTimeTicks() {
        return readInt64();
    }

    /** A duration as .NET ticks. */
    public long readDurationTicks() {
        return readInt64();
    }

    public Uuid readUuid() {
        int at = take(16);

        byte[] value = new byte[16];
        System.arraycopy(data, at, value, 0, 16);

        return new Uuid(value);
    }

    /** An int written in as few bytes as its magnitude needed, either sign. */
    public int readOptimalInt32() {
        int encoded = readVarint32();

        // Undoes the zig-zag fold: the low bit carried the sign. The shift has to be the
        // unsigned one - `encoded` is logically unsigned, and an arithmetic shift would
        // drag the sign bit down through it for any value above 2^31.
        return (encoded >>> 1) ^ -(encoded & 1);
    }

    /** A count, in the same encoding as readOptimalInt32. */
    public int readCounter32() {
        return readOptimalInt32();
    }

    /** An enum value, which travels zig-zag encoded rather than fixed width. */
    public int readEnum() {
        return readOptimalInt32();
    }

    private int readVarint32() {
        int value = 0;

        for (int shift = 0; shift < 35; shift += 7) {
            int b = readUInt8();
            value |= (b & 0x7F) << shift;

            if ((b & 0x80) == 0) {
                return value;
            }
        }

        throw new ScbException("varint32 is longer than five bytes");
    }

    /**
     * Reads and checks a table file's header, returning the row count that follows it.
     *
     * <p>The reserved byte is written as zero and is where compression or encryption flags
     * would go; a non-zero value means the file needs handling this build does not have.
     */
    public static Header readTableHeader(ScbReader reader) {
        int version = reader.readInt32();

        if (version != FORMAT_VERSION) {
            throw new ScbException(
                "table format version " + Integer.toUnsignedString(version)
                    + " is not supported (expected " + FORMAT_VERSION + ")");
        }

        if (reader.readUInt8() != 0) {
            throw new ScbException("table declares unsupported features");
        }

        Header header = new Header();
        header.rowCount = reader.readCounter32();

        if (header.rowCount < 0) {
            throw new ScbException("table row count is negative");
        }

        int columnCount = reader.readCounter32();

        if (columnCount < 0) {
            throw new ScbException("table column count is negative");
        }

        header.columns = new Column[columnCount];

        for (int at = 0; at < columnCount; at++) {
            Column column = new Column();
            column.tag = reader.readCounter32();

            int wire = reader.readUInt8();
            column.element = wire & 0x0F;
            column.kind = (wire >> 4) & 0x03;

            column.encoding = reader.readUInt8();

            column.count = reader.readCounter32();
            column.byteLength = reader.readInt32();

            header.columns[at] = column;
        }

        // What the descriptors say about the file, checked before anybody allocates for the
        // row count. The blocks are all that follows the header, so their declared lengths have
        // to add up to the bytes left. A raw block also costs at least one byte per row - a
        // varint's shortest form, an empty string's length prefix, a variable array's counter -
        // so a larger row count is one the exporter could not have written. An encoded block
        // has no such floor; its decode checks run sums and dictionary bounds instead.

        int available = reader.remaining();
        int declared = 0;

        for (Column column : header.columns) {
            if (column.byteLength < 0 || column.byteLength > available - declared) {
                throw new ScbException(String.format(
                    "column tag %d declares %d bytes, which the file cannot hold",
                    column.tag, column.byteLength));
            }

            declared += column.byteLength;

            if (column.encoding == ENCODING_RAW && header.rowCount > column.byteLength) {
                throw new ScbException(String.format(
                    "the row count %d is larger than column tag %d can hold in its %d bytes",
                    header.rowCount, column.tag, column.byteLength));
            }
        }

        if (declared != available) {
            throw new ScbException(String.format(
                "the columns declare %d bytes but %d follow the header", declared, available));
        }

        return header;
    }

    /** A parsed header: the row count and the column descriptors that follow it. */
    public static final class Header {
        public int rowCount;
        public Column[] columns;
    }

    /**
     * That a column is what the generated member expects, or a lossless promotion of it.
     * Refusal is by name and both types, never by reading anyway.
     */
    public static void checkColumn(Column column, String fieldName, int kind, int count, int... accepted) {
        if (column.kind != kind || (kind != KIND_VAR_ARRAY && column.count != count)) {
            throw new ScbException(
                fieldName + ": the file's column (kind " + column.kind + ", count " + column.count
                    + ") does not match the generated member (kind " + kind + ", count " + count
                    + "). The schema changed shape; regenerate the code or rebuild the data.");
        }

        // An encoding this build cannot decode is refused by name, exactly like an element
        // it cannot read. An unknown column's encoding never gets here - a skip is a skip
        // whatever the block's layout.
        if (column.encoding != ENCODING_RAW) {
            throw new ScbException(
                fieldName + ": the file's column uses encoding " + column.encoding + ", which "
                    + "this reader does not support. Regenerate the code or rebuild the data.");
        }

        for (int candidate : accepted) {
            if (column.element == candidate) {
                return;
            }
        }

        throw new ScbException(
            fieldName + ": the file carries element type " + column.element + ", which this member "
                + "cannot read. The column changed type incompatibly; regenerate the code or "
                + "rebuild the data.");
    }

    /**
     * That a block was consumed exactly: a mismatch is a format disagreement, and stopping
     * here names the column instead of corrupting the next.
     */
    public static void checkBlockEnd(ScbReader reader, Column column, int expectedEnd) {
        if (reader.position() != expectedEnd) {
            throw new ScbException(
                "column tag " + column.tag + ": its block declared " + column.byteLength
                    + " bytes but the read ended " + (expectedEnd - reader.position())
                    + " bytes short of its boundary");
        }
    }

    /** Reads a whole file into memory. */
    public static byte[] readAllBytes(Path filename) {
        try {
            return Files.readAllBytes(filename);
        } catch (IOException e) {
            throw new ScbException("could not read " + filename + ": " + e.getMessage());
        }
    }
}
