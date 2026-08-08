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
# Ruby's Integer is arbitrary precision, so a 64-bit value needs no special handling on
# the way in. It has no single-precision float, so a float32 read widens to a Float,
# which is a double - the value is exactly the one stored, held in a wider type.

module Sheetman
  # Stamped at the head of every table file by the exporter.
  # The format is column-oriented and self-describing: the header names every column
  # and how long its block is, and a reader that meets a version it does not know stops
  # rather than guessing.
  # 102 replaced 101 outright - a descriptor gained its encoding byte - before any
  # 101 file had shipped.
  FORMAT_VERSION = 102

  # The wire element types and kinds, as a column descriptor spells them.
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
  ENCODING_DICT_FRONT = 7
  ENCODING_DICT_FRONT_RLE = 8

  # One column as the file describes it.
  Column = Struct.new(:tag, :element, :kind, :encoding, :count, :byte_length)

  # A table file is truncated, malformed, or not a table file.
  class ScbError < StandardError; end

  # A lookup for a key no row carries.
  #
  # Raised by the generated `get_by_*_or_throw` lookups, which is where a caller has
  # said the key has to be there. `find_by_*` answers the same question with nil.
  #
  # Its own class rather than ScbError: nothing is wrong with the file, and a
  # caller rescuing one of these is not rescuing the other.
  class RecordNotFoundError < StandardError; end

  # A 128 bit identifier, stored in .NET Guid byte order.
  #
  # That order is not plain big-endian: the first three components are little endian and
  # the trailing eight bytes are not, which is what to_s has to account for.
  class Uuid
    # Component order matching .NET's Guid.ToString("D").
    ORDER = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15].freeze

    attr_reader :bytes

    def initialize(bytes = ("\0" * 16).b)
      @bytes = bytes.b
    end

    def to_s
      out = +''

      ORDER.each_with_index do |index, position|
        out << '-' if [4, 6, 8, 10].include?(position)
        out << format('%02x', @bytes.getbyte(index))
      end

      out
    end

    def ==(other)
      other.is_a?(Uuid) && other.bytes == @bytes
    end

    alias eql? ==

    def hash
      @bytes.hash
    end
  end

  # Sequential reader over a table file's bytes.
  #
  # Every read either advances the cursor or raises, so a caller need not check a return
  # value.
  class Reader
    attr_reader :position

    # Advances past bytes without interpreting them: an unknown column whole block.
    # The column-oriented layout is what makes this one call the entirety of skipping.
    def skip(byte_count)
      if byte_count.negative? || byte_count > remaining
        raise ScbError, "cannot skip #{byte_count} bytes with #{remaining} remaining"
      end

      @position += byte_count
    end

    # Promotions: a member reading a file element narrower than itself. Only the
    # mathematically lossless directions exist; check_column already refused the rest.

    # An int member from i32 or varint.
    def read_i32_as(element)
      element == ELEMENT_I32 ? read_int32 : read_counter32
    end

    # A 64-bit member from i64, i32 or varint.
    def read_i64_as(element)
      case element
      when ELEMENT_I64 then read_int64
      when ELEMENT_I32 then read_int32
      else read_counter32
      end
    end

    # A float member from f64, f32 or i32 - all exact in a Ruby Float.
    def read_f64_as(element)
      case element
      when ELEMENT_F64 then read_double
      when ELEMENT_F32 then read_float
      else read_int32
      end
    end

    def initialize(data)
      @data = data.b
      @position = 0
    end

    # Bytes left to read.
    def remaining
      @data.bytesize - @position
    end

    def read_uint8
      take(1).getbyte(0)
    end

    def read_bool
      read_uint8 != 0
    end

    def read_int32
      take(4).unpack1('l<')
    end

    def read_uint32
      take(4).unpack1('L<')
    end

    def read_int64
      take(8).unpack1('q<')
    end

    # A single-precision value.
    #
    # Read as its stored bit pattern and widened to a Float, which is a double. Printing
    # it shows digits the original 32 bits never carried, which is why the conformance
    # comparison narrows before comparing.
    def read_float
      take(4).unpack1('e')
    end

    def read_double
      take(8).unpack1('E')
    end

    # Bytes, uninterpreted.
    #
    # What a column cursor reads a dictionary with: a fixed-width entry it keeps as the
    # value's own bytes, and the bytes a front-coded entry states for itself. Bounds
    # checked like every other read, because it goes through the same one place.
    def read_bytes(count)
      take(count)
    end

    # A length-prefixed UTF-8 string.
    def read_string
      length = read_counter32
      raise ScbError, 'string length is negative' if length.negative?
      return '' if length.zero?

      take(length).force_encoding(Encoding::UTF_8)
    end

    # A timestamp as .NET ticks: 100 ns units since 0001-01-01.
    #
    # Ticks rather than a Time: a tick is finer than what Time keeps, and the corpus
    # reaches both 0001-01-01 and 9999-12-31.
    def read_datetime_ticks
      read_int64
    end

    # A duration as .NET ticks.
    def read_duration_ticks
      read_int64
    end

    def read_uuid
      Uuid.new(take(16))
    end

    # An int32 written in as few bytes as its magnitude needed, either sign.
    def read_optimal_int32
      encoded = read_varint32

      # Undoes the zig-zag fold: the low bit carried the sign.
      (encoded >> 1) ^ -(encoded & 1)
    end

    # A count, in the same encoding as read_optimal_int32.
    def read_counter32
      read_optimal_int32
    end

    # An enum value, which travels zig-zag encoded rather than fixed width.
    def read_enum
      read_optimal_int32
    end

    private

    def take(count)
      if remaining < count
        raise ScbError,
              "table data ended after #{@position} of #{@data.bytesize} bytes " \
              "while #{count} more were expected"
      end

      slice = @data.byteslice(@position, count)
      @position += count

      slice
    end

    def read_varint32
      value = 0

      shift = 0
      while shift < 35
        byte = read_uint8
        value |= (byte & 0x7F) << shift

        return value if (byte & 0x80).zero?

        shift += 7
      end

      raise ScbError, 'varint32 is longer than five bytes'
    end
  end

  # Reads one scalar column's values in row order, whatever the block's encoding.
  #
  # The generated row loop stays a row loop; this is the one place that knows how a
  # delta accumulates, how long a run has left, or that a dictionary index is a
  # reference into strings decoded once. That last one matters beyond file size: a
  # hundred-thousand-row column with three distinct strings allocates three strings,
  # not a hundred thousand - and the rows share them, which is safe because a record
  # only ever reads what it was handed.
  #
  # check_column has already refused any (element, encoding) pair the spec does not
  # define, so the dispatches here do not re-litigate that.
  class ColumnCursor
    def initialize(reader, column, row_count, field_name)
      @reader = reader
      @field_name = field_name
      @element = column.element
      @encoding = column.encoding

      # A run-length family's current run: what remains of it, and its value - which
      # is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
      @run_remaining = 0
      @run_value = 0

      # The delta family's accumulator, once @started.
      @previous = 0
      @started = false

      # Rows not yet handed out. A run that claims more than this is corrupt, and
      # catching it here names the field instead of leaving it to the block-end check.
      @rows_remaining = row_count

      # The block's dictionary, decoded once and handed out per row.
      #
      # One of the two is set when the block has a dictionary at all, chosen by the
      # element: strings are decoded to instances that rows then share, and a
      # fixed-width element keeps its raw bytes so the value is reconstructed exactly
      # as the raw layout would have read it.
      @dictionary = nil
      @value_dictionary = nil

      plain_dictionary = @encoding == ENCODING_DICT || @encoding == ENCODING_DICT_RLE
      front_dictionary = @encoding == ENCODING_DICT_FRONT || @encoding == ENCODING_DICT_FRONT_RLE

      return unless plain_dictionary || front_dictionary

      count = reader.read_counter32
      raise ScbError, "#{field_name}: the dictionary entry count is negative" if count.negative?

      if front_dictionary
        @dictionary = read_front_coded_dictionary(reader, count, field_name)
      elsif @element == ELEMENT_STRING
        @dictionary = Array.new(count) { reader.read_string }
      else
        # A fixed-width element: the entries are the value's own bytes, so they are
        # taken as bytes and turned into values only when a row asks for one.
        width = @element == ELEMENT_F32 ? 4 : 8
        @value_dictionary = Array.new(count) { reader.read_bytes(width) }
      end
    end

    # The next int32 - which also serves enums, and reference indexes.
    def next_i32
      @rows_remaining -= 1

      case @encoding
      when ENCODING_RAW
        @element == ELEMENT_I32 ? @reader.read_int32 : @reader.read_optimal_int32
      when ENCODING_VARINT
        @reader.read_optimal_int32
      when ENCODING_DELTA
        # The addition wraps on purpose, mirroring the writer's wrapping subtraction;
        # together they are exact for every int32 pair.
        if @started
          @previous = wrap_int32(@previous + @reader.read_optimal_int32)
        else
          @previous = @reader.read_optimal_int32
          @started = true
        end

        @previous
      when ENCODING_RLE
        read_run if @run_remaining.zero?

        @run_remaining -= 1
        @run_value
      else # ENCODING_DELTA_RLE; check_column refused everything else.
        unless @started
          @previous = @reader.read_optimal_int32
          @started = true
          return @previous
        end

        read_run if @run_remaining.zero?

        @run_remaining -= 1
        @previous = wrap_int32(@previous + @run_value)
      end
    end

    # A 64-bit member: from an i64 column raw or through its dictionary, and from
    # anything narrower by decoding an int32 and widening it.
    def next_i64
      return next_i32 unless @element == ELEMENT_I64
      return next_value_entry.unpack1('q<') if @value_dictionary

      @rows_remaining -= 1
      @reader.read_int64
    end

    # A single-precision member: raw, or the dictionary entry's exact bit pattern.
    #
    # Either way the 32 stored bits widen to a Float, which is a double - the value is
    # the one stored, held in a wider type.
    def next_f32
      return next_value_entry.unpack1('e') if @value_dictionary

      @rows_remaining -= 1
      @reader.read_float
    end

    # A float member: from f64 or f32 - either of them raw or dictionary-encoded - and
    # from an i32 column by decoding and widening.
    def next_f64
      case @element
      when ELEMENT_F64
        return next_value_entry.unpack1('E') if @value_dictionary

        @rows_remaining -= 1
        @reader.read_double
      when ELEMENT_F32
        next_f32
      else
        next_i32
      end
    end

    # A bool member: one byte raw, or a run of them.
    def next_bool
      return next_i32 != 0 if @encoding == ENCODING_RLE

      @rows_remaining -= 1
      @reader.read_bool
    end

    # The next string - the dictionary's instance where the block has one.
    def next_string
      @rows_remaining -= 1

      case @encoding
      when ENCODING_RAW
        @reader.read_string
      when ENCODING_DICT, ENCODING_DICT_FRONT
        dictionary_entry(@reader.read_counter32)
      else # ENCODING_DICT_RLE and ENCODING_DICT_FRONT_RLE
        read_run if @run_remaining.zero?

        @run_remaining -= 1
        dictionary_entry(@run_value)
      end
    end

    private

    # A sorted dictionary whose entries state only what they do not share with the
    # entry before them.
    #
    # Decoded into whole strings here rather than kept folded, because a row wants a
    # string and the folding was only ever about the bytes on disk. The allocations
    # are the strings themselves - one per distinct value, which is the point.
    def read_front_coded_dictionary(reader, count, field_name)
      previous = ''.b

      Array.new(count) do |at|
        shared = reader.read_counter32
        rest = reader.read_counter32

        if shared.negative? || rest.negative? || shared > previous.bytesize
          raise ScbError,
                "#{field_name}: dictionary entry #{at} shares #{shared} bytes with an " \
                "entry of #{previous.bytesize}"
        end

        # The bytes shared with the entry before, then the ones this entry states.
        entry = previous.byteslice(0, shared)
        entry << reader.read_bytes(rest) if rest.positive?
        previous = entry

        entry.dup.force_encoding(Encoding::UTF_8)
      end
    end

    # The bytes of the next row's dictionary entry, for a fixed-width element.
    def next_value_entry
      @rows_remaining -= 1

      index =
        if @encoding == ENCODING_DICT
          @reader.read_counter32
        else
          read_run if @run_remaining.zero?

          @run_remaining -= 1
          @run_value
        end

      if index.negative? || index >= @value_dictionary.length
        raise ScbError,
              "#{@field_name}: dictionary index #{index} is out of range - the " \
              "dictionary holds #{@value_dictionary.length} entries"
      end

      @value_dictionary[index]
    end

    def read_run
      length = @reader.read_counter32

      # + 1 because the row this run was read for is already counted out of
      # @rows_remaining by its next_* call.
      if length < 1 || length > @rows_remaining + 1
        raise ScbError,
              "#{@field_name}: a run of #{length} values cannot cover the " \
              "#{@rows_remaining + 1} rows left in the column"
      end

      @run_remaining = length
      @run_value = @reader.read_optimal_int32
    end

    def dictionary_entry(index)
      if index.negative? || index >= @dictionary.length
        raise ScbError,
              "#{@field_name}: dictionary index #{index} is out of range - the " \
              "dictionary holds #{@dictionary.length} entries"
      end

      @dictionary[index]
    end

    # A 32-bit wrapping sum. Ruby's Integer never overflows on its own, so the wrap
    # the format asks for is spelled out: keep the low 32 bits and sign-extend.
    def wrap_int32(value)
      value &= 0xFFFFFFFF
      value >= 0x80000000 ? value - 0x100000000 : value
    end
  end

  # Reads and checks a table file's header, returning the row count that follows it.
  #
  # The reserved byte is written as zero and is where compression or encryption flags
  # would go; a non-zero value means the file needs handling this build does not have.
  def self.read_table_header(reader)
    version = reader.read_uint32

    unless version == FORMAT_VERSION
      raise ScbError,
            "table format version #{version} is not supported (expected #{FORMAT_VERSION})"
    end

    raise ScbError, 'table declares unsupported features' unless reader.read_uint8.zero?

    count = reader.read_counter32
    raise ScbError, 'table row count is negative' if count.negative?

    column_count = reader.read_counter32
    raise ScbError, 'table column count is negative' if column_count.negative?

    columns = Array.new(column_count) do
      tag = reader.read_counter32
      wire = reader.read_uint8
      encoding = reader.read_uint8
      element_count = reader.read_counter32
      byte_length = reader.read_uint32
      Column.new(tag, wire & 0x0F, (wire >> 4) & 0x03, encoding, element_count, byte_length)
    end

    # What the descriptors say about the file, checked before anybody allocates for the
    # row count. The blocks are all that follows the header, so their declared lengths have
    # to add up to the bytes left. A raw block also costs at least one byte per
    # row - a varint's shortest form, an empty string's length prefix, a variable
    # array's counter - so a larger row count is one the exporter could not have
    # written. An encoded block has no such floor; its decode checks run sums and
    # dictionary bounds instead.

    available = reader.remaining
    declared = 0

    columns.each do |column|
      if column.byte_length.negative? || column.byte_length > available - declared
        raise ScbError,
              "column tag #{column.tag} declares #{column.byte_length} bytes, which the file " \
              'cannot hold'
      end

      declared += column.byte_length

      if column.encoding == ENCODING_RAW && count > column.byte_length
        raise ScbError,
              "the row count #{count} is larger than column tag #{column.tag} can hold in its " \
              "#{column.byte_length} bytes"
      end
    end

    if declared != available
      raise ScbError,
            "the columns declare #{declared} bytes but #{available} follow the header"
    end

    [count, columns]
  end

  # That a column is what the generated member expects, or a lossless promotion of it.
  # Refusal is by name and both types, never by reading anyway.
  def self.check_column(column, field_name, kind, count, accepted)
    if column.kind != kind || (kind != KIND_VAR_ARRAY && column.count != count)
      raise ScbError,
            "#{field_name}: the file column (kind #{column.kind}, count #{column.count}) " \
            "does not match the generated member (kind #{kind}, count #{count}). The schema " \
            'changed shape; regenerate the code or rebuild the data.'
    end

    # An encoding this build cannot decode - or one the spec does not define for
    # this element - is refused by name, exactly like an element it cannot read.
    # An unknown column's encoding never gets here - a skip is a skip whatever the
    # block's layout.
    unless encoding_supported?(column)
      raise ScbError,
            "#{field_name}: the file's column uses encoding #{column.encoding}, which this " \
            'reader cannot decode for its element type. Regenerate the code or rebuild the data.'
    end

    return if accepted.include?(column.element)

    raise ScbError,
          "#{field_name}: the file carries element type #{column.element}, which this member " \
          "cannot read (accepts #{accepted}). The column changed type incompatibly; " \
          'regenerate the code or rebuild the data.'
  end

  # The (element, encoding) pairs the spec defines. Arrays are always raw; integers
  # take the integer encodings, strings the dictionary ones.
  def self.encoding_supported?(column)
    return true if column.encoding == ENCODING_RAW
    return false unless column.kind == KIND_SCALAR

    case column.element
    when ELEMENT_BOOL, ELEMENT_VARINT
      column.encoding == ENCODING_RLE
    when ELEMENT_I32
      column.encoding >= ENCODING_VARINT && column.encoding <= ENCODING_DELTA_RLE
    # The dictionary is parameterized by element, so these three reach it with
    # entries that are simply their own raw bytes.
    when ELEMENT_I64, ELEMENT_F32, ELEMENT_F64
      column.encoding == ENCODING_DICT || column.encoding == ENCODING_DICT_RLE
    # And a string dictionary can additionally be front coded, which is meaningless
    # for a fixed-width element and refused for one.
    when ELEMENT_STRING
      column.encoding >= ENCODING_DICT && column.encoding <= ENCODING_DICT_FRONT_RLE
    else
      false
    end
  end

  # That a block was consumed exactly: a mismatch is a format disagreement, and stopping
  # here names the column instead of corrupting the next.
  def self.check_block_end(reader, column, expected_end)
    return if reader.position == expected_end

    raise ScbError,
          "column tag #{column.tag}: its block declared #{column.byte_length} bytes but the " \
          "read ended #{expected_end - reader.position} bytes short of its boundary"
  end

  # Reads a whole file into memory.
  def self.read_all_bytes(filename)
    File.binread(filename)
  end
end
