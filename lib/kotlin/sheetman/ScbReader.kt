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
//   counter32   zig-zag encoded Int written as a varint32
//   string      counter32 byte length, then that many UTF-8 bytes
//
// One of several readers of one format the exporter defines. The conformance corpus is
// what keeps them agreeing.
//
// Kotlin inherits the JVM's signed byte, so the same two traps as the Java reader apply:
// a byte with its high bit set is negative and must be masked before it is shifted into
// a varint, and undoing the zig-zag fold needs `ushr` rather than `shr`. Kotlin does have
// unsigned types, but using them here would push UInt through every call site for no gain.

package sheetman

import java.io.File
import java.nio.charset.StandardCharsets

/** Stamped at the head of every table file by the exporter. */
// The format is column-oriented and self-describing: the header names every column
// and how long its block is, and a reader that meets a version it does not know stops
// rather than guessing. 102 replaced 101 outright - a descriptor gained its encoding
// byte - before any 101 file had shipped.
const val FORMAT_VERSION: Int = 102

// The wire element types and kinds, as a column descriptor spells them.
const val ELEMENT_VARINT = 0
const val ELEMENT_BOOL = 1
const val ELEMENT_I32 = 2
const val ELEMENT_I64 = 3
const val ELEMENT_F32 = 4
const val ELEMENT_F64 = 5
const val ELEMENT_STRING = 6
const val ELEMENT_UUID = 7

const val KIND_SCALAR = 0
const val KIND_FIXED_ARRAY = 1
const val KIND_VAR_ARRAY = 2

// How a block's values are laid out. Raw is the layout 101 had; the others compress
// a column that repeats itself. spec/scb-v102-column-encoding.md is the contract.
const val ENCODING_RAW = 0
const val ENCODING_VARINT = 1
const val ENCODING_DELTA = 2
const val ENCODING_RLE = 3
const val ENCODING_DELTA_RLE = 4
const val ENCODING_DICT = 5
const val ENCODING_DICT_RLE = 6

/** One column as the file describes it. */
class Column(
    /** What identifies the column, instead of its position. */
    val tag: Int,
    val element: Int,
    val kind: Int,
    /** How the block's values are laid out: one of the ENCODING_* constants. */
    val encoding: Int,
    /** Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one. */
    val count: Int,
    /** Total bytes of the column block - what a skip advances by. */
    val byteLength: Int,
)

/** A parsed header: the row count and the column descriptors that follow it. */
class Header(val rowCount: Int, val columns: List<Column>)

/** A table file is truncated, malformed, or not a table file. */
class ScbException(message: String) : RuntimeException(message)

/**
 * A lookup for a key no row carries.
 *
 * Thrown by the generated getBy*OrThrow lookups, which is where a caller has said the key
 * has to be there. findBy* answers the same question with null.
 *
 * Its own type rather than ScbException: nothing is wrong with the file, and a
 * caller catching one of these is not catching the other.
 */
class RecordNotFoundException(message: String) : RuntimeException(message)

/**
 * A 128 bit identifier, stored in .NET Guid byte order.
 *
 * That order is not plain big-endian: the first three components are little endian and
 * the trailing eight bytes are not, which is what toString has to account for.
 */
class Uuid(bytes: ByteArray = ByteArray(16)) {

    val bytes: ByteArray = bytes.copyOf()

    override fun toString(): String {
        val out = StringBuilder(36)

        for ((position, index) in ORDER.withIndex()) {
            if (position == 4 || position == 6 || position == 8 || position == 10) {
                out.append('-')
            }

            // Masked: a Byte is signed and the high half would sign-extend.
            val value = bytes[index].toInt() and 0xFF
            out.append(HEX[value shr 4]).append(HEX[value and 0x0F])
        }

        return out.toString()
    }

    override fun equals(other: Any?): Boolean = other is Uuid && bytes.contentEquals(other.bytes)

    override fun hashCode(): Int = bytes.contentHashCode()

    private companion object {
        // Component order matching .NET's Guid.ToString("D").
        val ORDER = intArrayOf(3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15)
        val HEX = "0123456789abcdef".toCharArray()
    }
}

/** Sequential reader over a table file's bytes. */
class ScbReader(private val data: ByteArray) {

    var position: Int = 0
        private set

    /**
     * Advances past bytes without interpreting them: an unknown column whole block.
     * The column-oriented layout is what makes this one call the entirety of skipping.
     */
    fun skip(byteCount: Int) {
        if (byteCount < 0 || byteCount > data.size - position) {
            throw ScbException(
                "cannot skip $byteCount bytes with ${data.size - position} remaining")
        }
        position += byteCount
    }

    // Promotions: a member reading a file element narrower than itself. Only the
    // mathematically lossless directions exist; checkColumn already refused the rest.

    /** An Int member from i32 or varint. */
    fun readI32As(element: Int): Int =
        if (element == ELEMENT_I32) readInt32() else readCounter32()

    /** A Long member from i64, i32 or varint. */
    fun readI64As(element: Int): Long = when (element) {
        ELEMENT_I64 -> readInt64()
        ELEMENT_I32 -> readInt32().toLong()
        else -> readCounter32().toLong()
    }

    /** A Double member from f64, f32 or i32 - all exact in a Double. */
    fun readF64As(element: Int): Double = when (element) {
        ELEMENT_F64 -> readDouble()
        ELEMENT_F32 -> readFloat().toDouble()
        else -> readInt32().toDouble()
    }


    /** Bytes left to read. */
    val remaining: Int get() = data.size - position

    private fun take(count: Int): Int {
        if (remaining < count) {
            throw ScbException(
                "table data ended after $position of ${data.size} bytes while $count more were expected")
        }

        val start = position
        position += count

        return start
    }

    /** One byte, as an unsigned value in an Int. */
    fun readUInt8(): Int = data[take(1)].toInt() and 0xFF

    fun readBool(): Boolean = readUInt8() != 0

    fun readInt32(): Int {
        val at = take(4)

        return (data[at].toInt() and 0xFF) or
            ((data[at + 1].toInt() and 0xFF) shl 8) or
            ((data[at + 2].toInt() and 0xFF) shl 16) or
            ((data[at + 3].toInt() and 0xFF) shl 24)
    }

    fun readInt64(): Long {
        val at = take(8)

        var value = 0L
        for (i in 7 downTo 0) {
            value = (value shl 8) or (data[at + i].toLong() and 0xFF)
        }

        return value
    }

    /**
     * A single-precision value as its stored bit pattern, so the value survives exactly
     * rather than through a decimal rendering.
     */
    fun readFloat(): Float = Float.fromBits(readInt32())

    fun readDouble(): Double = Double.fromBits(readInt64())

    /** A length-prefixed UTF-8 string. */
    fun readString(): String {
        val length = readCounter32()

        if (length < 0) throw ScbException("string length is negative")
        if (length == 0) return ""

        val at = take(length)
        return String(data, at, length, StandardCharsets.UTF_8)
    }

    /**
     * A timestamp as .NET ticks: 100 ns units since 0001-01-01.
     *
     * Ticks rather than a java.time type: the conversion is lossy coming back, and a
     * caller passing the value through should not pay for it.
     */
    fun readDateTimeTicks(): Long = readInt64()

    /** A duration as .NET ticks. */
    fun readDurationTicks(): Long = readInt64()

    fun readUuid(): Uuid {
        val at = take(16)
        return Uuid(data.copyOfRange(at, at + 16))
    }

    /** An Int written in as few bytes as its magnitude needed, either sign. */
    fun readOptimalInt32(): Int {
        val encoded = readVarint32()

        // Undoes the zig-zag fold: the low bit carried the sign. The shift has to be the
        // unsigned one - `encoded` is logically unsigned, and an arithmetic shift would
        // drag the sign bit down through it for any value above 2^31.
        return (encoded ushr 1) xor -(encoded and 1)
    }

    /** A count, in the same encoding as readOptimalInt32. */
    fun readCounter32(): Int = readOptimalInt32()

    /** An enum value, which travels zig-zag encoded rather than fixed width. */
    fun readEnum(): Int = readOptimalInt32()

    private fun readVarint32(): Int {
        var value = 0

        var shift = 0
        while (shift < 35) {
            val b = readUInt8()
            value = value or ((b and 0x7F) shl shift)

            if (b and 0x80 == 0) return value

            shift += 7
        }

        throw ScbException("varint32 is longer than five bytes")
    }
}

/**
 * Reads one scalar column's values in row order, whatever the block's encoding.
 *
 * The generated row loop stays a row loop; this is the one place that knows how a delta
 * accumulates, how long a run has left, or that a dictionary index is a reference into
 * strings decoded once. That last one matters beyond file size: a hundred-thousand-row
 * column with three distinct strings allocates three strings, not a hundred thousand.
 *
 * checkColumn has already refused any (element, encoding) pair the spec does not define,
 * so the branches here do not re-litigate that.
 */
class ColumnCursor(
    private val reader: ScbReader,
    column: Column,
    rowCount: Int,
    private val fieldName: String,
) {
    private val element: Int = column.element
    private val encoding: Int = column.encoding

    // A run-length family's current run: what remains of it, and its value - which is a
    // plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
    private var runRemaining = 0
    private var runValue = 0

    // The delta family's accumulator, once started.
    private var previous = 0
    private var started = false

    // Rows not yet handed out. A run that claims more than this is corrupt, and catching
    // it here names the field instead of leaving it to the block-end check.
    private var rowsRemaining = rowCount

    /** The block's dictionary, decoded once and handed out per row. */
    private val dictionary: Array<String> =
        if (encoding == ENCODING_DICT || encoding == ENCODING_DICT_RLE) {
            val count = reader.readCounter32()
            if (count < 0) {
                throw ScbException("$fieldName: the dictionary entry count is negative")
            }

            Array(count) { reader.readString() }
        } else {
            EMPTY_DICTIONARY
        }

    /** The next Int - which also serves enums, and reference indexes. */
    fun nextI32(): Int {
        rowsRemaining--

        return when (encoding) {
            ENCODING_RAW ->
                if (element == ELEMENT_I32) reader.readInt32() else reader.readOptimalInt32()

            ENCODING_VARINT -> reader.readOptimalInt32()

            ENCODING_DELTA -> {
                // The addition wraps on purpose - Kotlin's Int does - mirroring the
                // writer's wrapping subtraction; together they are exact for every
                // Int pair.
                if (started) {
                    previous += reader.readOptimalInt32()
                } else {
                    previous = reader.readOptimalInt32()
                    started = true
                }

                previous
            }

            ENCODING_RLE -> {
                if (runRemaining == 0) readRun()

                runRemaining--
                runValue
            }

            else -> { // ENCODING_DELTA_RLE; checkColumn refused everything else.
                if (!started) {
                    previous = reader.readOptimalInt32()
                    started = true
                } else {
                    if (runRemaining == 0) readRun()

                    runRemaining--
                    previous += runValue
                }

                previous
            }
        }
    }

    /** A Long member: an i64 column is always raw, anything narrower decodes as Int. */
    fun nextI64(): Long =
        if (element == ELEMENT_I64) reader.readInt64() else nextI32().toLong()

    /** A Double member: float columns are always raw, an i32 column decodes then widens. */
    fun nextF64(): Double = when (element) {
        ELEMENT_F64 -> reader.readDouble()
        ELEMENT_F32 -> reader.readFloat().toDouble()
        else -> nextI32().toDouble()
    }

    /** The next string - the dictionary's instance where the block has one. */
    fun nextString(): String {
        rowsRemaining--

        return when (encoding) {
            ENCODING_RAW -> reader.readString()

            ENCODING_DICT -> dictionaryEntry(reader.readCounter32())

            else -> { // ENCODING_DICT_RLE
                if (runRemaining == 0) readRun()

                runRemaining--
                dictionaryEntry(runValue)
            }
        }
    }

    private fun readRun() {
        val length = reader.readCounter32()

        // + 1 because the row this run was read for is already counted out of
        // rowsRemaining by its next* call.
        if (length < 1 || length > rowsRemaining + 1) {
            throw ScbException(
                "$fieldName: a run of $length values cannot cover the " +
                    "${rowsRemaining + 1} rows left in the column")
        }

        runRemaining = length
        runValue = reader.readOptimalInt32()
    }

    private fun dictionaryEntry(index: Int): String {
        if (index < 0 || index >= dictionary.size) {
            throw ScbException(
                "$fieldName: dictionary index $index is out of range - the " +
                    "dictionary holds ${dictionary.size} entries")
        }

        return dictionary[index]
    }

    private companion object {
        // One shared empty array for the encodings that carry no dictionary, so the
        // property can stay non-nullable without an allocation per cursor.
        val EMPTY_DICTIONARY = emptyArray<String>()
    }
}

/**
 * Reads and checks a table file's header, returning the row count that follows it.
 *
 * The reserved byte is written as zero and is where compression or encryption flags would
 * go; a non-zero value means the file needs handling this build does not have.
 */
fun readTableHeader(reader: ScbReader): Header {
    val version = reader.readInt32()

    if (version != FORMAT_VERSION) {
        throw ScbException(
            "table format version ${version.toUInt()} is not supported (expected $FORMAT_VERSION)")
    }

    if (reader.readUInt8() != 0) {
        throw ScbException("table declares unsupported features")
    }

    val count = reader.readCounter32()
    if (count < 0) throw ScbException("table row count is negative")

    val columnCount = reader.readCounter32()
    if (columnCount < 0) throw ScbException("table column count is negative")

    val columns = ArrayList<Column>(columnCount)

    repeat(columnCount) {
        val tag = reader.readCounter32()
        val wire = reader.readUInt8()
        val encoding = reader.readUInt8()
        val elementCount = reader.readCounter32()
        val byteLength = reader.readInt32()
        columns.add(
            Column(tag, wire and 0x0F, (wire shr 4) and 0x03, encoding, elementCount, byteLength))
    }

    // What the descriptors say about the file, checked before anybody allocates for the
    // row count. The blocks are all that follows the header, so their declared lengths have
    // to add up to the bytes left. A raw block also costs at least one byte per row - a
    // varint's shortest form, an empty string's length prefix, a variable array's counter -
    // so a larger row count is one the exporter could not have written. An encoded block
    // has no such floor; its decode checks run sums and dictionary bounds instead.

    val available = reader.remaining
    var declared = 0

    for (column in columns) {
        if (column.byteLength < 0 || column.byteLength > available - declared) {
            throw ScbException(
                "column tag ${column.tag} declares ${column.byteLength} bytes, which the file " +
                    "cannot hold")
        }

        declared += column.byteLength

        if (column.encoding == ENCODING_RAW && count > column.byteLength) {
            throw ScbException(
                "the row count $count is larger than column tag ${column.tag} can hold in its " +
                    "${column.byteLength} bytes")
        }
    }

    if (declared != available) {
        throw ScbException(
            "the columns declare $declared bytes but $available follow the header")
    }

    return Header(count, columns)
}

/**
 * That a column is what the generated member expects, or a lossless promotion of it.
 * Refusal is by name and both types, never by reading anyway.
 */
fun checkColumn(column: Column, fieldName: String, kind: Int, count: Int, vararg accepted: Int) {
    if (column.kind != kind || (kind != KIND_VAR_ARRAY && column.count != count)) {
        throw ScbException(
            "$fieldName: the file column (kind ${column.kind}, count ${column.count}) does not " +
                "match the generated member (kind $kind, count $count). The schema changed shape; " +
                "regenerate the code or rebuild the data.")
    }

    // An encoding this build cannot decode - or one the spec does not define for this
    // element - is refused by name, exactly like an element it cannot read. An unknown
    // column's encoding never gets here - a skip is a skip whatever the block's layout.
    if (!encodingSupported(column)) {
        throw ScbException(
            "$fieldName: the file's column uses encoding ${column.encoding}, which this " +
                "reader cannot decode for its element type. Regenerate the code or rebuild " +
                "the data.")
    }

    if (column.element !in accepted) {
        throw ScbException(
            "$fieldName: the file carries element type ${column.element}, which this member " +
                "cannot read (accepts ${accepted.joinToString()}). The column changed type " +
                "incompatibly; regenerate the code or rebuild the data.")
    }
}

/**
 * The (element, encoding) pairs the spec defines. Arrays are always raw; integers take
 * the integer encodings, strings the dictionary ones.
 */
private fun encodingSupported(column: Column): Boolean {
    if (column.encoding == ENCODING_RAW) return true
    if (column.kind != KIND_SCALAR) return false

    return when (column.element) {
        ELEMENT_VARINT -> column.encoding == ENCODING_RLE
        ELEMENT_I32 -> column.encoding in ENCODING_VARINT..ENCODING_DELTA_RLE
        ELEMENT_STRING ->
            column.encoding == ENCODING_DICT || column.encoding == ENCODING_DICT_RLE
        else -> false
    }
}

/**
 * That a block was consumed exactly: a mismatch is a format disagreement, and stopping
 * here names the column instead of corrupting the next.
 */
fun checkBlockEnd(reader: ScbReader, column: Column, expectedEnd: Int) {
    if (reader.position != expectedEnd) {
        throw ScbException(
            "column tag ${column.tag}: its block declared ${column.byteLength} bytes but the " +
                "read ended ${expectedEnd - reader.position} bytes short of its boundary")
    }
}

/** Reads a whole file into memory. */
fun readAllBytes(filename: String): ByteArray = File(filename).readBytes()
