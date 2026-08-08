// ---------------------------------------------------------------------------
// SheetMan Scb reader for C++17.
//
// Reads the .scb files produced by SheetMan's binary exporter. The format is
// defined by the C# writer in
// lib/Unity/SheetManForUnity/Assets/Plugins/SheetMan.Runtime, and this is a
// deliberate re-implementation of the reading half of it:
//
//   fixed8      one byte
//   fixed32     four bytes, little endian
//   fixed64     eight bytes, little endian
//   varint32    seven bits per byte, high bit set while more bytes follow,
//               at most five bytes
//   counter32   zig-zag encoded int32 written as a varint32, so small values
//               of either sign cost one byte
//   string      counter32 byte length, then that many UTF-8 bytes
//   int32/uint32   fixed32
//   int64          fixed64
//   bool           fixed8, zero meaning false
//   float/double   fixed32 / fixed64 holding the IEEE-754 bit pattern
//   datetime       fixed64 of .NET ticks: 100 ns units since 0001-01-01
//   timespan       fixed64 of .NET ticks
//   uuid           sixteen bytes in .NET Guid layout
//
// Header only, no dependencies beyond the standard library.
// ---------------------------------------------------------------------------

#ifndef SHEETMAN_SCB_READER_H
#define SHEETMAN_SCB_READER_H

#include <array>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <ios>
#include <stdexcept>
#include <string>
#include <vector>

namespace sheetman {
/// Thrown when a table file is truncated, malformed, or not a table file.
class ScbError : public std::runtime_error {
 public:
  explicit ScbError(const std::string& what) : std::runtime_error(what) {}
};

/// Thrown by a lookup for a key no row carries.
///
/// Raised by the generated get_by_*_or_throw lookups, which is where a caller has said
/// the key has to be there. find_by_* answers the same question with nullptr.
///
/// Its own type rather than ScbError: nothing is wrong with the file, and a
/// caller catching one of these is not catching the other.
class RecordNotFound : public std::runtime_error {
 public:
  explicit RecordNotFound(const std::string& what) : std::runtime_error(what) {}
};

/// A duration of .NET ticks: one tick is 100 nanoseconds.
///
/// The wire carries ticks, so this is the period that loses nothing. std::chrono
/// converts to anything coarser for free - `std::chrono::duration_cast<std::chrono::
/// seconds>(row.cooldown)` - and refuses the conversions that would silently round,
/// which is the reason for using it rather than a bare integer.
///
/// Not std::chrono::nanoseconds: TimeSpan's own maximum is 9.2e18 ticks, and that
/// many nanoseconds overflows a 64-bit count.
using TimeSpan = std::chrono::duration<std::int64_t, std::ratio<1, 10000000>>;

/// A point in time, in ticks, on the system clock.
///
/// The wire carries .NET ticks since 0001-01-01; this counts from the Unix epoch,
/// which is what every C++ clock and every C library function agrees on. The shift
/// happens once, in the reader.
///
/// It converts to `std::chrono::system_clock::time_point` with a `time_point_cast`,
/// so `std::chrono::system_clock::to_time_t` and the rest of the standard library
/// are one call away.
using DateTime = std::chrono::time_point<std::chrono::system_clock, TimeSpan>;

/// Ticks between 0001-01-01 and the Unix epoch, which is the whole of the
/// difference between .NET's zero and everybody else's.
constexpr std::int64_t kUnixEpochTicks = 621355968000000000LL;

/// The .NET tick count of a DateTime, for talking back to something that wants one.
inline std::int64_t to_net_ticks(DateTime value) {
  return value.time_since_epoch().count() + kUnixEpochTicks;
}

/// And the other way, for a caller building a value rather than reading one.
inline DateTime from_net_ticks(std::int64_t ticks) {
  return DateTime(TimeSpan(ticks - kUnixEpochTicks));
}

/// A 128 bit identifier, stored in .NET Guid byte order.
///
/// That order is not plain big-endian: the first three components are little
/// endian and the trailing eight bytes are not, which is what to_string has to
/// account for.
struct Uuid {
  std::array<std::uint8_t, 16> bytes{};

  std::string to_string() const {
    static const char* kHex = "0123456789abcdef";

    // Component order matching .NET's Guid.ToString("D").
    static const int kOrder[16] = {3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15};

    std::string out;
    out.reserve(36);

    for (int i = 0; i < 16; ++i) {
      if (i == 4 || i == 6 || i == 8 || i == 10) out.push_back('-');

      const std::uint8_t b = bytes[static_cast<std::size_t>(kOrder[i])];
      out.push_back(kHex[b >> 4]);
      out.push_back(kHex[b & 0x0F]);
    }

    return out;
  }

  friend bool operator==(const Uuid& a, const Uuid& b) { return a.bytes == b.bytes; }
  friend bool operator!=(const Uuid& a, const Uuid& b) { return !(a == b); }
};

/// Sequential reader over a table file's bytes.
///
/// Non-owning: the buffer has to outlive the reader. Every read either advances
/// the cursor or throws, so callers never have to check a return value.
// The wire element types and kinds, as a column descriptor spells them.
constexpr std::uint8_t kElementVarint = 0;
constexpr std::uint8_t kElementBool = 1;
constexpr std::uint8_t kElementI32 = 2;
constexpr std::uint8_t kElementI64 = 3;
constexpr std::uint8_t kElementF32 = 4;
constexpr std::uint8_t kElementF64 = 5;
constexpr std::uint8_t kElementString = 6;
constexpr std::uint8_t kElementUuid = 7;

constexpr std::uint8_t kKindScalar = 0;
constexpr std::uint8_t kKindFixedArray = 1;
constexpr std::uint8_t kKindVarArray = 2;

/// The little-endian scalars, read out of bytes the caller already holds.
///
/// Free functions rather than reader members because a dictionary entry is read
/// long after the cursor moved past it: the bytes are kept and turned into a value
/// only when a row asks. ScbReader's own reads are these plus a bounds check and an
/// advance, so a value taken from a dictionary and one taken from a raw block go
/// through the same arithmetic.
inline std::uint32_t load_fixed32(const std::uint8_t* at) {
  return static_cast<std::uint32_t>(at[0]) | (static_cast<std::uint32_t>(at[1]) << 8) |
         (static_cast<std::uint32_t>(at[2]) << 16) | (static_cast<std::uint32_t>(at[3]) << 24);
}

inline std::uint64_t load_fixed64(const std::uint8_t* at) {
  std::uint64_t value = 0;
  for (int i = 0; i < 8; ++i)
    value |= static_cast<std::uint64_t>(at[static_cast<std::size_t>(i)]) << (8 * i);

  return value;
}

class ScbReader {
 public:
  ScbReader(const std::uint8_t* data, std::size_t length)
      : data_(data), length_(length), position_(0) {}

  explicit ScbReader(const std::vector<std::uint8_t>& buffer)
      : ScbReader(buffer.data(), buffer.size()) {}

  std::size_t position() const { return position_; }
  std::size_t remaining() const { return length_ - position_; }

  std::uint8_t read_fixed8() {
    require(1);
    return data_[position_++];
  }

  std::uint32_t read_fixed32() {
    require(4);

    const std::uint32_t value = load_fixed32(data_ + position_);
    position_ += 4;
    return value;
  }

  std::uint64_t read_fixed64() {
    require(8);

    const std::uint64_t value = load_fixed64(data_ + position_);
    position_ += 8;
    return value;
  }

  /// Copies bytes out without interpreting them: a fixed-width dictionary entry, or
  /// the tail of a front-coded one. The caller decides later what they mean.
  void read_bytes(std::uint8_t* destination, std::size_t count) {
    require(count);

    if (count > 0) std::memcpy(destination, data_ + position_, count);
    position_ += count;
  }

  std::uint32_t read_varint32() {
    std::uint32_t value = 0;

    for (int shift = 0; shift < 35; shift += 7) {
      const std::uint8_t byte = read_fixed8();
      value |= static_cast<std::uint32_t>(byte & 0x7F) << shift;

      if ((byte & 0x80) == 0) return value;
    }

    throw ScbError("varint32 is longer than five bytes");
  }

  /// Zig-zag decoded int32: the encoding used for lengths and enum values, so
  /// that small negatives cost as little as small positives.
  std::int32_t read_counter32() {
    const std::uint32_t encoded = read_varint32();
    return static_cast<std::int32_t>(encoded >> 1) ^ -static_cast<std::int32_t>(encoded & 1);
  }

  // Advances past bytes without interpreting them: an unknown column's whole block.
  // The column-oriented layout is what makes this one call the entirety of skipping.
  void skip(std::int32_t byte_count) {
    if (byte_count < 0 || static_cast<std::size_t>(byte_count) > remaining()) {
      throw ScbError("cannot skip " + std::to_string(byte_count) + " bytes with " +
                            std::to_string(remaining()) + " remaining");
    }
    position_ += static_cast<std::size_t>(byte_count);
  }

  // Promotions: a member reading a file element narrower than itself. Only the
  // mathematically lossless directions exist; check_column already refused the rest.

  // An int32 member from i32 or varint.
  void read_i32_as(std::uint8_t element, std::int32_t& value) {
    if (element == kElementI32) {
      read(value);
    } else {
      value = read_counter32();
    }
  }

  // An int64 member from i64, i32 or varint.
  void read_i64_as(std::uint8_t element, std::int64_t& value) {
    if (element == kElementI64) {
      read(value);
    } else if (element == kElementI32) {
      std::int32_t narrower = 0;
      read(narrower);
      value = narrower;
    } else {
      value = read_counter32();
    }
  }

  // A double member from f64, f32 or i32 - all exact in a double.
  void read_f64_as(std::uint8_t element, double& value) {
    if (element == kElementF64) {
      read(value);
    } else if (element == kElementF32) {
      float single = 0.0f;
      read(single);
      value = single;
    } else {
      std::int32_t integer = 0;
      read(integer);
      value = integer;
    }
  }

  void read(bool& value) { value = read_fixed8() != 0; }
  void read(std::int32_t& value) { value = static_cast<std::int32_t>(read_fixed32()); }
  void read(std::uint32_t& value) { value = read_fixed32(); }
  void read(std::int64_t& value) { value = static_cast<std::int64_t>(read_fixed64()); }

  void read(float& value) {
    const std::uint32_t bits = read_fixed32();
    std::memcpy(&value, &bits, sizeof(value));
  }

  void read(double& value) {
    const std::uint64_t bits = read_fixed64();
    std::memcpy(&value, &bits, sizeof(value));
  }

  void read(std::string& value) {
    const std::int32_t length = read_counter32();
    if (length < 0) throw ScbError("string length is negative");

    require(static_cast<std::size_t>(length));

    value.assign(reinterpret_cast<const char*>(data_ + position_), static_cast<std::size_t>(length));
    position_ += static_cast<std::size_t>(length);
  }

  /// Ticks off the wire, shifted onto the Unix epoch as they arrive.
  void read(DateTime& value) {
    value = from_net_ticks(static_cast<std::int64_t>(read_fixed64()));
  }

  void read(TimeSpan& value) {
    value = TimeSpan(static_cast<std::int64_t>(read_fixed64()));
  }

  void read(Uuid& value) {
    require(16);
    std::memcpy(value.bytes.data(), data_ + position_, 16);
    position_ += 16;
  }

  /// Reads an enum as the underlying zig-zag encoded int32 the exporter writes.
  template <typename TEnum>
  void read_enum(TEnum& value) {
    value = static_cast<TEnum>(read_counter32());
  }

 private:
  void require(std::size_t count) const {
    if (remaining() < count) {
      throw ScbError("table data ended after " + std::to_string(position_) + " of " +
                            std::to_string(length_) + " bytes while " + std::to_string(count) +
                            " more were expected");
    }
  }

  const std::uint8_t* data_;
  std::size_t length_;
  std::size_t position_;
};

/// Reads a whole file into memory.
inline std::vector<std::uint8_t> read_all_bytes(const std::string& filename) {
  std::ifstream stream(filename, std::ios::binary | std::ios::ate);
  if (!stream) throw ScbError("cannot open `" + filename + "`");

  const std::streamsize size = stream.tellg();
  stream.seekg(0, std::ios::beg);

  std::vector<std::uint8_t> buffer(static_cast<std::size_t>(size));
  if (size > 0 && !stream.read(reinterpret_cast<char*>(buffer.data()), size))
    throw ScbError("cannot read `" + filename + "`");

  return buffer;
}

/// Version stamped at the head of every table file by the exporter.
// The format is column-oriented and self-describing: the header names every column
// and how long its block is, and a reader that meets a version it does not know stops
// rather than guessing. 102 replaced 101 outright - a descriptor gained its encoding
// byte - before any 101 file had shipped.
constexpr std::uint32_t kBinaryFileFormatVersion = 102;

// How a block's values are laid out. Raw is the layout 101 had; the others compress
// a column that repeats itself. spec/scb-v102-column-encoding.md is the contract.
constexpr std::uint8_t kEncodingRaw = 0;
constexpr std::uint8_t kEncodingVarint = 1;
constexpr std::uint8_t kEncodingDelta = 2;
constexpr std::uint8_t kEncodingRle = 3;
constexpr std::uint8_t kEncodingDeltaRle = 4;
constexpr std::uint8_t kEncodingDict = 5;
constexpr std::uint8_t kEncodingDictRle = 6;
constexpr std::uint8_t kEncodingDictFront = 7;
constexpr std::uint8_t kEncodingDictFrontRle = 8;

// One column as the file describes it.
struct Column {
  // What identifies the column, instead of its position.
  std::int32_t tag;
  std::uint8_t element;
  std::uint8_t kind;
  // How the block's values are laid out: one of the kEncoding* constants.
  std::uint8_t encoding;
  // Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one.
  std::int32_t count;
  // Total bytes of the column block - what a skip advances by.
  std::int32_t byte_length;
};

// A parsed header: the row count and the column descriptors that follow it.
struct Header {
  std::int32_t row_count;
  std::vector<Column> columns;
};

/// Reads and checks the file header, returning the row count that follows it.
///
/// The reserved byte is written as zero and is where compression or encryption
/// flags would go; a non-zero value means the file needs handling this build
/// does not have.
inline Header read_table_header(ScbReader& reader) {
  const std::uint32_t version = reader.read_fixed32();
  if (version != kBinaryFileFormatVersion) {
    throw ScbError("table format version " + std::to_string(version) + " is not supported (expected " +
                          std::to_string(kBinaryFileFormatVersion) + ")");
  }

  const std::uint8_t reserved = reader.read_fixed8();
  if (reserved != 0) throw ScbError("table declares unsupported features");

  Header header;
  header.row_count = reader.read_counter32();
  if (header.row_count < 0) throw ScbError("table row count is negative");

  const std::int32_t column_count = reader.read_counter32();
  if (column_count < 0) throw ScbError("table column count is negative");

  header.columns.reserve(static_cast<std::size_t>(column_count));

  for (std::int32_t at = 0; at < column_count; ++at) {
    Column column;
    column.tag = reader.read_counter32();

    const std::uint8_t wire = reader.read_fixed8();
    column.element = static_cast<std::uint8_t>(wire & 0x0f);
    column.kind = static_cast<std::uint8_t>((wire >> 4) & 0x03);

    column.encoding = reader.read_fixed8();

    column.count = reader.read_counter32();
    column.byte_length = static_cast<std::int32_t>(reader.read_fixed32());

    header.columns.push_back(column);
  }

  // What the descriptors say about the file, checked before anybody allocates for the
  // row count. The blocks are all that follows the header, so their declared lengths have
  // to add up to the bytes left. A raw block also costs at least one byte per row - a
  // varint's shortest form, an empty string's length prefix, a variable array's counter -
  // so a larger row count is one the exporter could not have written. An encoded block
  // has no such floor; its decode checks run sums and dictionary bounds instead.

  const std::int32_t available = static_cast<std::int32_t>(reader.remaining());
  std::int32_t declared = 0;

  for (const Column& column : header.columns) {
    if (column.byte_length < 0 || column.byte_length > available - declared) {
      throw ScbError("column tag " + std::to_string(column.tag) + " declares " +
                            std::to_string(column.byte_length) +
                            " bytes, which the file cannot hold");
    }

    declared += column.byte_length;

    if (column.encoding == kEncodingRaw && header.row_count > column.byte_length) {
      throw ScbError("the row count " + std::to_string(header.row_count) +
                            " is larger than column tag " + std::to_string(column.tag) +
                            " can hold in its " + std::to_string(column.byte_length) + " bytes");
    }
  }

  if (declared != available) {
    throw ScbError("the columns declare " + std::to_string(declared) + " bytes but " +
                          std::to_string(available) + " follow the header");
  }

  return header;
}

// The (element, encoding) pairs the spec defines. Arrays are always raw; integers
// take the integer encodings, strings the dictionary ones.
inline bool encoding_supported(const Column& column) {
  if (column.encoding == kEncodingRaw) return true;

  if (column.kind != kKindScalar) return false;

  switch (column.element) {
    case kElementBool:
    case kElementVarint:
      return column.encoding == kEncodingRle;

    case kElementI32:
      return column.encoding >= kEncodingVarint && column.encoding <= kEncodingDeltaRle;

    // The dictionary is parameterized by element, so these three reach it with
    // entries that are simply their own raw bytes.
    case kElementI64:
    case kElementF32:
    case kElementF64:
      return column.encoding == kEncodingDict || column.encoding == kEncodingDictRle;

    // And a string dictionary can additionally be front coded, which is meaningless
    // for a fixed-width element and refused for one.
    case kElementString:
      return column.encoding >= kEncodingDict && column.encoding <= kEncodingDictFrontRle;

    default:
      return false;
  }
}

// That a column is what the generated member expects, or a lossless promotion of it.
// Refusal is by name and both types, never by reading anyway.
inline void check_column(const Column& column, const char* field_name, std::uint8_t kind,
                         std::int32_t count, std::initializer_list<std::uint8_t> accepted) {
  if (column.kind != kind || (kind != kKindVarArray && column.count != count)) {
    throw ScbError(std::string(field_name) +
                          ": the file's column does not match the generated member's shape; "
                          "the schema changed shape, regenerate the code or rebuild the data");
  }

  // An encoding this build cannot decode - or one the spec does not define for this
  // element - is refused by name, exactly like an element it cannot read. An unknown
  // column's encoding never gets here - a skip is a skip whatever the block's layout.
  if (!encoding_supported(column)) {
    throw ScbError(std::string(field_name) + ": the file's column uses encoding " +
                          std::to_string(column.encoding) +
                          ", which this reader cannot decode for its element type; "
                          "regenerate the code or rebuild the data");
  }

  for (const std::uint8_t candidate : accepted) {
    if (column.element == candidate) return;
  }

  throw ScbError(std::string(field_name) + ": the file carries element type " +
                        std::to_string(column.element) +
                        ", which this member cannot read; the column changed type "
                        "incompatibly, regenerate the code or rebuild the data");
}

/// Reads one scalar column's values in row order, whatever the block's encoding.
///
/// The generated row loop stays a row loop; this is the one place that knows how a
/// delta accumulates, how long a run has left, or that a dictionary index is a
/// reference into strings decoded once. That last one matters beyond file size: a
/// hundred-thousand-row column with three distinct strings decodes three strings,
/// not a hundred thousand.
///
/// check_column has already refused any (element, encoding) pair the spec does not
/// define, so the switches here do not re-litigate that.
class ScbColumnCursor {
 public:
  ScbColumnCursor(ScbReader& reader, const Column& column, std::int32_t row_count,
                  const char* field_name)
      : reader_(reader),
        field_name_(field_name),
        element_(column.element),
        encoding_(column.encoding),
        rows_remaining_(row_count) {
    const bool plain_dictionary = encoding_ == kEncodingDict || encoding_ == kEncodingDictRle;

    const bool front_dictionary =
        encoding_ == kEncodingDictFront || encoding_ == kEncodingDictFrontRle;

    if (!plain_dictionary && !front_dictionary) return;

    const std::int32_t count = reader.read_counter32();
    if (count < 0) {
      throw ScbError(std::string(field_name) + ": the dictionary entry count is negative");
    }

    if (front_dictionary) {
      read_front_coded_dictionary(reader, count, field_name);
      return;
    }

    if (element_ == kElementString) {
      dictionary_.resize(static_cast<std::size_t>(count));

      for (std::int32_t at = 0; at < count; ++at)
        reader.read(dictionary_[static_cast<std::size_t>(at)]);

      return;
    }

    // A fixed-width element: the entries are the value's own bytes, so they are taken
    // as bytes and turned into values only when a row asks for one - which is what
    // makes a dictionary value identical to the one a raw block would have handed back.
    value_width_ = element_ == kElementF32 ? 4 : 8;
    value_dictionary_.resize(static_cast<std::size_t>(count) * value_width_);

    // Read in one go: the entries are adjacent and fixed width, so the block is the
    // concatenation of them.
    reader.read_bytes(value_dictionary_.data(), value_dictionary_.size());
  }

  /// The next int32 - which also serves enums, and reference indexes.
  std::int32_t next_i32() {
    --rows_remaining_;

    switch (encoding_) {
      case kEncodingRaw: {
        if (element_ == kElementI32) {
          std::int32_t exact = 0;
          reader_.read(exact);
          return exact;
        }

        return reader_.read_counter32();
      }

      case kEncodingVarint:
        return reader_.read_counter32();

      case kEncodingDelta: {
        // The addition wraps on purpose, mirroring the writer's wrapping subtraction;
        // together they are exact for every int32 pair. Done in unsigned arithmetic,
        // because signed overflow is undefined and unsigned wraps.
        if (started_) {
          previous_ = wrapping_add(previous_, reader_.read_counter32());
        } else {
          previous_ = reader_.read_counter32();
          started_ = true;
        }

        return previous_;
      }

      case kEncodingRle: {
        if (run_remaining_ == 0) read_run();

        --run_remaining_;
        return run_value_;
      }

      default: {  // kEncodingDeltaRle; check_column refused everything else.
        if (!started_) {
          previous_ = reader_.read_counter32();
          started_ = true;
          return previous_;
        }

        if (run_remaining_ == 0) read_run();

        --run_remaining_;
        previous_ = wrapping_add(previous_, run_value_);
        return previous_;
      }
    }
  }

  /// An int64 member: from an i64 column raw or through its dictionary, and from
  /// anything narrower by decoding an int32 and widening it.
  std::int64_t next_i64() {
    if (element_ != kElementI64) return next_i32();

    if (has_value_dictionary())
      return static_cast<std::int64_t>(load_fixed64(next_value_entry()));

    --rows_remaining_;

    std::int64_t exact = 0;
    reader_.read(exact);
    return exact;
  }

  /// A float member: raw, or the dictionary entry's exact bit pattern.
  float next_f32() {
    if (has_value_dictionary()) {
      const std::uint32_t bits = load_fixed32(next_value_entry());

      float value = 0.0f;
      std::memcpy(&value, &bits, sizeof(value));
      return value;
    }

    --rows_remaining_;

    float value = 0.0f;
    reader_.read(value);
    return value;
  }

  /// A double member: from f64 or f32 - either of them raw or dictionary-encoded -
  /// and from an i32 column by decoding and widening.
  double next_f64() {
    if (element_ == kElementF64) {
      if (has_value_dictionary()) {
        const std::uint64_t bits = load_fixed64(next_value_entry());

        double exact = 0.0;
        std::memcpy(&exact, &bits, sizeof(exact));
        return exact;
      }

      --rows_remaining_;

      double exact = 0.0;
      reader_.read(exact);
      return exact;
    }

    if (element_ == kElementF32) return next_f32();

    return next_i32();
  }

  /// A bool member: one byte raw, or a run of them.
  bool next_bool() {
    if (encoding_ == kEncodingRle) return next_i32() != 0;

    --rows_remaining_;

    bool value = false;
    reader_.read(value);
    return value;
  }

  /// The next string - a copy of the dictionary's entry where the block has one.
  std::string next_string() {
    --rows_remaining_;

    switch (encoding_) {
      case kEncodingRaw: {
        std::string value;
        reader_.read(value);
        return value;
      }

      case kEncodingDict:
      case kEncodingDictFront:
        return dictionary_entry(reader_.read_counter32());

      default: {  // kEncodingDictRle and kEncodingDictFrontRle
        if (run_remaining_ == 0) read_run();

        --run_remaining_;
        return dictionary_entry(run_value_);
      }
    }
  }

 private:
  /// Whether the block carried a dictionary of fixed-width entries.
  ///
  /// The width, not the byte count: a dictionary of no entries is still a dictionary,
  /// and a row asking one for a value has to be told its index is out of range rather
  /// than fall through to a raw read that would misinterpret the index stream.
  bool has_value_dictionary() const { return value_width_ != 0; }

  /// A sorted dictionary whose entries state only what they do not share with the
  /// entry before them.
  ///
  /// Decoded into whole strings here rather than kept folded, because a row wants a
  /// string and the folding was only ever about the bytes on disk. The scratch buffer
  /// grows to the longest entry and is reused, so what is allocated is the strings
  /// themselves - one per distinct value, which is the point.
  void read_front_coded_dictionary(ScbReader& reader, std::int32_t count,
                                   const char* field_name) {
    dictionary_.resize(static_cast<std::size_t>(count));

    std::vector<std::uint8_t> scratch(64);
    std::int32_t previous_length = 0;

    for (std::int32_t at = 0; at < count; ++at) {
      const std::int32_t shared = reader.read_counter32();
      const std::int32_t rest = reader.read_counter32();

      if (shared < 0 || rest < 0 || shared > previous_length) {
        throw ScbError(std::string(field_name) + ": dictionary entry " + std::to_string(at) +
                       " shares " + std::to_string(shared) + " bytes with an entry of " +
                       std::to_string(previous_length));
      }

      const std::int32_t length = shared + rest;

      // Signed, so a length that overflowed int32 does not become an enormous
      // capacity; the read below refuses it by running out of bytes instead.
      if (length > static_cast<std::int32_t>(scratch.size())) {
        std::size_t capacity = scratch.size();
        while (capacity < static_cast<std::size_t>(length)) capacity *= 2;

        // Resize keeps what is already there, which is the prefix this entry shares.
        scratch.resize(capacity);
      }

      if (rest > 0) {
        reader.read_bytes(scratch.data() + static_cast<std::size_t>(shared),
                          static_cast<std::size_t>(rest));
      }

      dictionary_[static_cast<std::size_t>(at)].assign(
          reinterpret_cast<const char*>(scratch.data()), static_cast<std::size_t>(length));

      previous_length = length;
    }
  }

  /// The bytes of the next row's dictionary entry, for a fixed-width element.
  const std::uint8_t* next_value_entry() {
    --rows_remaining_;

    std::int32_t index = 0;

    if (encoding_ == kEncodingDict) {
      index = reader_.read_counter32();
    } else {  // kEncodingDictRle; encoding_supported refused the front-coded ones here.
      if (run_remaining_ == 0) read_run();

      --run_remaining_;
      index = run_value_;
    }

    const std::size_t count = value_dictionary_.size() / value_width_;

    if (index < 0 || static_cast<std::size_t>(index) >= count) {
      throw ScbError(std::string(field_name_) + ": dictionary index " + std::to_string(index) +
                     " is out of range - the dictionary holds " + std::to_string(count) +
                     " entries");
    }

    return value_dictionary_.data() + static_cast<std::size_t>(index) * value_width_;
  }

  /// int32 addition modulo 2^32, matching the writer's wrapping subtraction.
  static std::int32_t wrapping_add(std::int32_t a, std::int32_t b) {
    return static_cast<std::int32_t>(static_cast<std::uint32_t>(a) +
                                     static_cast<std::uint32_t>(b));
  }

  void read_run() {
    const std::int32_t length = reader_.read_counter32();

    // + 1 because the row this run was read for is already counted out of
    // rows_remaining_ by its next_* call.
    if (length < 1 || length > rows_remaining_ + 1) {
      throw ScbError(std::string(field_name_) + ": a run of " + std::to_string(length) +
                            " values cannot cover the " + std::to_string(rows_remaining_ + 1) +
                            " rows left in the column");
    }

    run_remaining_ = length;
    run_value_ = reader_.read_counter32();
  }

  const std::string& dictionary_entry(std::int32_t index) const {
    if (index < 0 || static_cast<std::size_t>(index) >= dictionary_.size()) {
      throw ScbError(std::string(field_name_) + ": dictionary index " + std::to_string(index) +
                            " is out of range - the dictionary holds " +
                            std::to_string(dictionary_.size()) + " entries");
    }

    return dictionary_[static_cast<std::size_t>(index)];
  }

  ScbReader& reader_;
  const char* field_name_;
  std::uint8_t element_;
  std::uint8_t encoding_;

  /// The block's dictionary, decoded once and handed out per row.
  ///
  /// One of the two is filled when the block has a dictionary at all, chosen by the
  /// element: strings are decoded to values that rows then copy, and a fixed-width
  /// element keeps its raw bytes so the value is reconstructed exactly as the raw
  /// layout would have read it.
  std::vector<std::string> dictionary_;

  std::vector<std::uint8_t> value_dictionary_;
  std::size_t value_width_ = 0;

  // A run-length family's current run: what remains of it, and its value - which is
  // a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
  std::int32_t run_remaining_ = 0;
  std::int32_t run_value_ = 0;

  // The delta family's accumulator, once started_.
  std::int32_t previous_ = 0;
  bool started_ = false;

  // Rows not yet handed out. A run that claims more than this is corrupt, and
  // catching it here names the field instead of leaving it to the block-end check.
  std::int32_t rows_remaining_;
};

// That a block was consumed exactly: a mismatch is a format disagreement, and stopping
// here names the column instead of corrupting the next.
inline void check_block_end(const ScbReader& reader, const Column& column,
                            std::size_t expected_end) {
  if (reader.position() != expected_end) {
    throw ScbError("column tag " + std::to_string(column.tag) +
                          ": the block's declared length and the bytes consumed disagree");
  }
}
}  // namespace sheetman

#endif  // SHEETMAN_SCB_READER_H
