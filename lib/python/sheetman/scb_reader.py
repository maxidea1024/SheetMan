# SheetMan's binary reader.
#
# Copied in beside the generated accessor so the emitted code needs nothing
# installed. Edit it in the SheetMan repository.
#
#
# Reads the .scb files SheetMan's binary exporter writes:
#
#   fixed8      one byte
#   fixed32     four bytes, little endian
#   fixed64     eight bytes, little endian
#   varint32    seven bits per byte, high bit set while more bytes follow,
#               at most five bytes
#   counter32   zig-zag encoded int32 written as a varint32
#   string      counter32 byte length, then that many UTF-8 bytes
#
# One of several readers of one format the exporter defines. The conformance corpus is
# what keeps them agreeing.
#
# Two things about Python are worth knowing here. Its int is arbitrary precision, so a
# 64-bit value needs no special handling on the way in - unlike JavaScript, where the
# same value silently rounds. And it has no single-precision float, so a float32 read
# widens to a double; the value is exactly the one that was stored, just held in a
# wider type, and writing it back out at single precision is the caller's business.

import struct

# Stamped at the head of every table file by the exporter.
# The format is column-oriented and self-describing: the header names every column
# and how long its block is, and a reader that meets a version it does not know stops
# rather than guessing.
FORMAT_VERSION = 102

# The wire's element types and kinds, as a column descriptor spells them.
ELEMENT_VARINT = 0
ELEMENT_BOOL = 1
ELEMENT_I32 = 2
ELEMENT_I64 = 3
ELEMENT_F32 = 4
ELEMENT_F64 = 5
ELEMENT_STRING = 6
ELEMENT_UUID = 7

KIND_SCALAR = 0
KIND_FIXED_ARRAY = 1
KIND_VAR_ARRAY = 2

# How a block's values are laid out. Raw is the layout 101 had; the others compress
# a column that repeats itself. spec/scb-v102-column-encoding.md is the contract.
ENCODING_RAW = 0
ENCODING_VARINT = 1
ENCODING_DELTA = 2
ENCODING_RLE = 3
ENCODING_DELTA_RLE = 4
ENCODING_DICT = 5
ENCODING_DICT_RLE = 6


class Column:
    """One column as the file describes it."""

    __slots__ = ("tag", "element", "kind", "encoding", "count", "byte_length")

    def __init__(self, tag, element, kind, encoding, count, byte_length):
        self.tag = tag
        self.element = element
        self.kind = kind
        self.encoding = encoding
        self.count = count
        self.byte_length = byte_length


class ScbError(Exception):
    """A table file is truncated, malformed, or not a table file."""


class RecordNotFoundError(Exception):
    """A lookup for a key no row carries.

    Raised by the generated `get_by_*_or_throw` lookups, which is where a caller has
    said the key has to be there. `find_by_*` answers the same question with None.

    Its own type rather than ScbError: nothing is wrong with the file, and a
    caller catching one of these is not catching the other.
    """


class Uuid:
    """A 128 bit identifier, stored in .NET Guid byte order.

    That order is not plain big-endian: the first three components are little endian
    and the trailing eight bytes are not, which is what __str__ has to account for.
    """

    # Component order matching .NET's Guid.ToString("D").
    _ORDER = (3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15)

    __slots__ = ("raw",)

    def __init__(self, raw=None):
        self.raw = bytes(raw) if raw is not None else bytes(16)

    def __str__(self):
        out = []

        for position, index in enumerate(self._ORDER):
            if position in (4, 6, 8, 10):
                out.append("-")

            out.append("%02x" % self.raw[index])

        return "".join(out)

    def __repr__(self):
        return "Uuid('%s')" % self

    def __eq__(self, other):
        return isinstance(other, Uuid) and self.raw == other.raw

    def __hash__(self):
        return hash(self.raw)


class Reader:
    """Sequential reader over a table file's bytes.

    Every read either advances the cursor or raises, so a caller need not check a
    return value.
    """

    __slots__ = ("_data", "_position")

    def __init__(self, data):
        self._data = data
        self._position = 0

    @property
    def position(self):
        """Bytes consumed so far."""
        return self._position

    @property
    def remaining(self):
        """Bytes left to read."""
        return len(self._data) - self._position

    def _take(self, count):
        if self.remaining < count:
            raise ScbError(
                "table data ended after %d of %d bytes while %d more were expected"
                % (self._position, len(self._data), count))

        start = self._position
        self._position += count

        return self._data[start:self._position]

    def read_uint8(self):
        return self._take(1)[0]

    def read_bool(self):
        return self.read_uint8() != 0

    def read_int32(self):
        return struct.unpack_from("<i", self._take(4))[0]

    def read_uint32(self):
        return struct.unpack_from("<I", self._take(4))[0]

    def read_int64(self):
        return struct.unpack_from("<q", self._take(8))[0]

    def read_float(self):
        """A single-precision value.

        Read as its stored bit pattern and widened to Python's float, which is a
        double. The value is exactly the one that was stored; printing it at double
        precision shows digits the original 32 bits never carried, which is why the
        conformance comparison narrows before comparing.
        """
        return struct.unpack_from("<f", self._take(4))[0]

    def read_double(self):
        return struct.unpack_from("<d", self._take(8))[0]

    def read_string(self):
        length = self.read_counter32()

        if length < 0:
            raise ScbError("string length is negative")

        if length == 0:
            return ""

        return self._take(length).decode("utf-8")

    def skip(self, byte_count):
        """Advances past bytes without interpreting them: an unknown column's block.

        The column-oriented layout is what makes this one call the entirety of skipping.
        """
        if byte_count < 0 or byte_count > self.remaining:
            raise ScbError(
                "cannot skip %d bytes with %d remaining" % (byte_count, self.remaining))
        self._position += byte_count

    # Promotions: a member reading a file element narrower than itself. Only the
    # mathematically lossless directions exist; check_column already refused the rest.

    def read_i32_as(self, element):
        """An int32 member from i32 or varint."""
        return self.read_int32() if element == ELEMENT_I32 else self.read_counter32()

    def read_i64_as(self, element):
        """An int64 member from i64, i32 or varint."""
        if element == ELEMENT_I64:
            return self.read_int64()
        if element == ELEMENT_I32:
            return self.read_int32()
        return self.read_counter32()

    def read_f64_as(self, element):
        """A double member from f64, f32 or i32 - all exact in a double."""
        if element == ELEMENT_F64:
            return self.read_double()
        if element == ELEMENT_F32:
            return self.read_float()
        return self.read_int32()

    def read_datetime_ticks(self):
        """A timestamp as .NET ticks: 100 ns units since 0001-01-01.

        Ticks rather than a datetime, because the corpus reaches 0001-01-01 and
        9999-12-31 and a tick is finer than datetime's microsecond, so the conversion
        would lose both ends and the last two digits.
        """
        return self.read_int64()

    def read_duration_ticks(self):
        """A duration as .NET ticks.

        Ticks rather than a timedelta: TimeSpan.MaxValue is about 29,000 years and
        timedelta tops out near 2,700,000 days.
        """
        return self.read_int64()

    def read_uuid(self):
        return Uuid(self._take(16))

    def read_optimal_int32(self):
        """An int32 written in as few bytes as its magnitude needed, either sign."""
        encoded = self._read_varint32()

        # Undoes the zig-zag fold: the low bit carried the sign.
        return (encoded >> 1) ^ -(encoded & 1)

    def read_counter32(self):
        """A count, in the same encoding as read_optimal_int32."""
        return self.read_optimal_int32()

    def read_enum(self):
        """An enum value, which travels zig-zag encoded rather than fixed width."""
        return self.read_optimal_int32()

    def _read_varint32(self):
        value = 0

        for shift in range(0, 35, 7):
            byte = self.read_uint8()
            value |= (byte & 0x7F) << shift

            if not byte & 0x80:
                return value

        raise ScbError("varint32 is longer than five bytes")


def _wrap_int32(value):
    """The low 32 bits of a value, sign extended.

    Python's int never overflows, so the wrapping the delta encoding is defined
    over - two's complement, 32 bits - has to be spelled out.
    """
    value &= 0xFFFFFFFF

    return value - 0x100000000 if value >= 0x80000000 else value


class ColumnCursor:
    """Reads one scalar column's values in row order, whatever the block's encoding.

    The generated row loop stays a row loop; this is the one place that knows how a
    delta accumulates, how long a run has left, or that a dictionary index is a
    reference into strings decoded once. That last one matters beyond file size: a
    hundred-thousand-row column with three distinct strings holds three str objects,
    not a hundred thousand.

    check_column has already refused any (element, encoding) pair the spec does not
    define, so the branches here do not re-litigate that.
    """

    __slots__ = ("_reader", "_field_name", "_element", "_encoding", "_dictionary",
                 "_run_remaining", "_run_value", "_previous", "_started",
                 "_rows_remaining")

    def __init__(self, reader, column, row_count, field_name):
        self._reader = reader
        self._field_name = field_name
        self._element = column.element
        self._encoding = column.encoding

        # A run-length family's current run: what remains of it, and its value - which
        # is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
        self._run_remaining = 0
        self._run_value = 0

        # The delta family's accumulator, once started.
        self._previous = 0
        self._started = False

        # Rows not yet handed out. A run that claims more than this is corrupt, and
        # catching it here names the field instead of leaving it to the block-end check.
        self._rows_remaining = row_count

        if column.encoding in (ENCODING_DICT, ENCODING_DICT_RLE):
            count = reader.read_counter32()

            if count < 0:
                raise ScbError(
                    "%s: the dictionary entry count is negative" % field_name)

            # Decoded once and handed out per row.
            self._dictionary = [reader.read_string() for _ in range(count)]
        else:
            self._dictionary = None

    def next_i32(self):
        """The next int32 - which also serves enums, and reference indexes."""
        self._rows_remaining -= 1
        encoding = self._encoding

        if encoding == ENCODING_RAW:
            if self._element == ELEMENT_I32:
                return self._reader.read_int32()
            return self._reader.read_optimal_int32()

        if encoding == ENCODING_VARINT:
            return self._reader.read_optimal_int32()

        if encoding == ENCODING_DELTA:
            # The addition wraps on purpose, mirroring the writer's wrapping
            # subtraction; together they are exact for every int32 pair.
            if self._started:
                self._previous = _wrap_int32(
                    self._previous + self._reader.read_optimal_int32())
            else:
                self._previous = self._reader.read_optimal_int32()
                self._started = True

            return self._previous

        if encoding == ENCODING_RLE:
            if self._run_remaining == 0:
                self._read_run()

            self._run_remaining -= 1
            return self._run_value

        # ENCODING_DELTA_RLE; check_column refused everything else.
        if not self._started:
            self._previous = self._reader.read_optimal_int32()
            self._started = True
            return self._previous

        if self._run_remaining == 0:
            self._read_run()

        self._run_remaining -= 1
        self._previous = _wrap_int32(self._previous + self._run_value)
        return self._previous

    def next_i64(self):
        """An int64 member: an i64 column is always raw, anything narrower decodes as int32."""
        if self._element == ELEMENT_I64:
            return self._reader.read_int64()

        return self.next_i32()

    def next_f64(self):
        """A double member: float columns are always raw, an i32 column decodes then widens."""
        if self._element == ELEMENT_F64:
            return self._reader.read_double()

        if self._element == ELEMENT_F32:
            return self._reader.read_float()

        return self.next_i32()

    def next_string(self):
        """The next string - the dictionary's instance where the block has one."""
        self._rows_remaining -= 1

        if self._encoding == ENCODING_RAW:
            return self._reader.read_string()

        if self._encoding == ENCODING_DICT:
            return self._dictionary_entry(self._reader.read_counter32())

        # ENCODING_DICT_RLE
        if self._run_remaining == 0:
            self._read_run()

        self._run_remaining -= 1
        return self._dictionary_entry(self._run_value)

    def _read_run(self):
        length = self._reader.read_counter32()

        # + 1 because the row this run was read for is already counted out of
        # _rows_remaining by its next_* call.
        if length < 1 or length > self._rows_remaining + 1:
            raise ScbError(
                "%s: a run of %d values cannot cover the %d rows left in the column"
                % (self._field_name, length, self._rows_remaining + 1))

        self._run_remaining = length
        self._run_value = self._reader.read_optimal_int32()

    def _dictionary_entry(self, index):
        if index < 0 or index >= len(self._dictionary):
            raise ScbError(
                "%s: dictionary index %d is out of range - the dictionary holds %d entries"
                % (self._field_name, index, len(self._dictionary)))

        return self._dictionary[index]


def read_table_header(reader):
    """Reads and checks a table file's header.

    Returns (row_count, columns): the column descriptors the data blocks follow.
    """
    version = reader.read_uint32()
    if version != FORMAT_VERSION:
        raise ScbError(
            "table format version %d is not supported (expected %d)"
            % (version, FORMAT_VERSION))

    if reader.read_uint8() != 0:
        raise ScbError("table declares unsupported features")

    count = reader.read_counter32()
    if count < 0:
        raise ScbError("table row count is negative")

    column_count = reader.read_counter32()
    if column_count < 0:
        raise ScbError("table column count is negative")

    columns = []
    for _ in range(column_count):
        tag = reader.read_counter32()
        wire = reader.read_uint8()
        encoding = reader.read_uint8()
        element_count = reader.read_counter32()
        byte_length = reader.read_uint32()
        columns.append(
            Column(tag, wire & 0x0F, (wire >> 4) & 0x03, encoding, element_count, byte_length))

    # What the descriptors say about the file, checked before anybody allocates for the
    # row count. The blocks are all that follows the header, so their declared lengths have
    # to add up to the bytes left. A raw block also costs at least one byte per
    # row - a varint's shortest form, an empty string's length prefix, a variable
    # array's counter - so a larger row count is one the exporter could not have
    # written. An encoded block has no such floor; its decode checks run sums and
    # dictionary bounds instead.

    available = reader.remaining
    declared = 0

    for column in columns:
        if column.byte_length < 0 or column.byte_length > available - declared:
            raise ScbError(
                "column tag %d declares %d bytes, which the file cannot hold"
                % (column.tag, column.byte_length))

        declared += column.byte_length

        if column.encoding == ENCODING_RAW and count > column.byte_length:
            raise ScbError(
                "the row count %d is larger than column tag %d can hold in its %d bytes"
                % (count, column.tag, column.byte_length))

    if declared != available:
        raise ScbError(
            "the columns declare %d bytes but %d follow the header" % (declared, available))

    return count, columns


def check_column(column, field_name, kind, count, accepted):
    """That a column is what the generated member expects, or a lossless promotion.

    Refusal is by name and both types, never by reading anyway.
    """
    if column.kind != kind or (kind != KIND_VAR_ARRAY and column.count != count):
        raise ScbError(
            "%s: the file's column (kind %d, count %d) does not match the generated member "
            "(kind %d, count %d). The schema changed shape; regenerate the code or rebuild "
            "the data." % (field_name, column.kind, column.count, kind, count))

    # An encoding this build cannot decode - or one the spec does not define for
    # this element - is refused by name, exactly like an element it cannot read.
    # An unknown column's encoding never gets here - a skip is a skip whatever the
    # block's layout.
    if not _encoding_supported(column):
        raise ScbError(
            "%s: the file's column uses encoding %d, which this reader cannot decode "
            "for its element type. Regenerate the code or rebuild the data."
            % (field_name, column.encoding))

    if column.element not in accepted:
        raise ScbError(
            "%s: the file carries element type %d, which this member cannot read "
            "(accepts %s). The column changed type incompatibly; regenerate the code or "
            "rebuild the data." % (field_name, column.element, accepted))


def _encoding_supported(column):
    """The (element, encoding) pairs the spec defines.

    Arrays are always raw; integers take the integer encodings, strings the
    dictionary ones.
    """
    if column.encoding == ENCODING_RAW:
        return True

    if column.kind != KIND_SCALAR:
        return False

    if column.element == ELEMENT_VARINT:
        return column.encoding == ENCODING_RLE

    if column.element == ELEMENT_I32:
        return ENCODING_VARINT <= column.encoding <= ENCODING_DELTA_RLE

    if column.element == ELEMENT_STRING:
        return column.encoding in (ENCODING_DICT, ENCODING_DICT_RLE)

    return False


def check_block_end(reader, column, expected_end):
    """That a block was consumed exactly.

    A mismatch is a format disagreement, and stopping here names the column instead of
    corrupting the next.
    """
    if reader.position != expected_end:
        raise ScbError(
            "column tag %d: its block declared %d bytes but the read ended %d bytes short "
            "of its boundary" % (column.tag, column.byte_length, expected_end - reader.position))


def read_all_bytes(filename):
    """Reads a whole file into memory."""
    with open(filename, "rb") as handle:
        return handle.read()
