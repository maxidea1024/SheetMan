// ---------------------------------------------------------------------------
// SheetMan LiteBinary reader for C++17.
//
// Reads the .table files produced by SheetMan's binary exporter. The format is
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

#ifndef SHEETMAN_LITE_BINARY_READER_H
#define SHEETMAN_LITE_BINARY_READER_H

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
class LiteBinaryError : public std::runtime_error {
 public:
  explicit LiteBinaryError(const std::string& what) : std::runtime_error(what) {}
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

class LiteBinaryReader {
 public:
  LiteBinaryReader(const std::uint8_t* data, std::size_t length)
      : data_(data), length_(length), position_(0) {}

  explicit LiteBinaryReader(const std::vector<std::uint8_t>& buffer)
      : LiteBinaryReader(buffer.data(), buffer.size()) {}

  std::size_t position() const { return position_; }
  std::size_t remaining() const { return length_ - position_; }

  std::uint8_t read_fixed8() {
    require(1);
    return data_[position_++];
  }

  std::uint32_t read_fixed32() {
    require(4);

    const std::uint32_t value = static_cast<std::uint32_t>(data_[position_ + 0]) |
                                (static_cast<std::uint32_t>(data_[position_ + 1]) << 8) |
                                (static_cast<std::uint32_t>(data_[position_ + 2]) << 16) |
                                (static_cast<std::uint32_t>(data_[position_ + 3]) << 24);
    position_ += 4;
    return value;
  }

  std::uint64_t read_fixed64() {
    require(8);

    std::uint64_t value = 0;
    for (int i = 0; i < 8; ++i)
      value |= static_cast<std::uint64_t>(data_[position_ + static_cast<std::size_t>(i)]) << (8 * i);

    position_ += 8;
    return value;
  }

  std::uint32_t read_varint32() {
    std::uint32_t value = 0;

    for (int shift = 0; shift < 35; shift += 7) {
      const std::uint8_t byte = read_fixed8();
      value |= static_cast<std::uint32_t>(byte & 0x7F) << shift;

      if ((byte & 0x80) == 0) return value;
    }

    throw LiteBinaryError("varint32 is longer than five bytes");
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
      throw LiteBinaryError("cannot skip " + std::to_string(byte_count) + " bytes with " +
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
    if (length < 0) throw LiteBinaryError("string length is negative");

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
      throw LiteBinaryError("table data ended after " + std::to_string(position_) + " of " +
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
  if (!stream) throw LiteBinaryError("cannot open `" + filename + "`");

  const std::streamsize size = stream.tellg();
  stream.seekg(0, std::ios::beg);

  std::vector<std::uint8_t> buffer(static_cast<std::size_t>(size));
  if (size > 0 && !stream.read(reinterpret_cast<char*>(buffer.data()), size))
    throw LiteBinaryError("cannot read `" + filename + "`");

  return buffer;
}

/// Version stamped at the head of every table file by the exporter.
// The format is column-oriented and self-describing: the header names every column
// and how long its block is, and a reader that meets a version it does not know stops
// rather than guessing.
constexpr std::uint32_t kBinaryFileFormatVersion = 101;

// One column as the file describes it.
struct Column {
  // What identifies the column, instead of its position.
  std::int32_t tag;
  std::uint8_t element;
  std::uint8_t kind;
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
inline Header read_table_header(LiteBinaryReader& reader) {
  const std::uint32_t version = reader.read_fixed32();
  if (version != kBinaryFileFormatVersion) {
    throw LiteBinaryError("table format version " + std::to_string(version) + " is not supported (expected " +
                          std::to_string(kBinaryFileFormatVersion) + ")");
  }

  const std::uint8_t reserved = reader.read_fixed8();
  if (reserved != 0) throw LiteBinaryError("table declares unsupported features");

  Header header;
  header.row_count = reader.read_counter32();
  if (header.row_count < 0) throw LiteBinaryError("table row count is negative");

  const std::int32_t column_count = reader.read_counter32();
  if (column_count < 0) throw LiteBinaryError("table column count is negative");

  header.columns.reserve(static_cast<std::size_t>(column_count));

  for (std::int32_t at = 0; at < column_count; ++at) {
    Column column;
    column.tag = reader.read_counter32();

    const std::uint8_t wire = reader.read_fixed8();
    column.element = static_cast<std::uint8_t>(wire & 0x0f);
    column.kind = static_cast<std::uint8_t>((wire >> 4) & 0x03);

    column.count = reader.read_counter32();
    column.byte_length = static_cast<std::int32_t>(reader.read_fixed32());

    header.columns.push_back(column);
  }

  // What the descriptors say about the file, checked before anybody allocates for the
  // row count. The blocks are all that follows the header, so their declared lengths have
  // to add up to the bytes left, and every row costs at least one byte in every block - a
  // varint's shortest form, an empty string's length prefix, a variable array's counter.
  // A row count larger than that is one the exporter could not have written.

  const std::int32_t available = static_cast<std::int32_t>(reader.remaining());
  std::int32_t declared = 0;

  for (const Column& column : header.columns) {
    if (column.byte_length < 0 || column.byte_length > available - declared) {
      throw LiteBinaryError("column tag " + std::to_string(column.tag) + " declares " +
                            std::to_string(column.byte_length) +
                            " bytes, which the file cannot hold");
    }

    declared += column.byte_length;

    if (header.row_count > column.byte_length) {
      throw LiteBinaryError("the row count " + std::to_string(header.row_count) +
                            " is larger than column tag " + std::to_string(column.tag) +
                            " can hold in its " + std::to_string(column.byte_length) + " bytes");
    }
  }

  if (declared != available) {
    throw LiteBinaryError("the columns declare " + std::to_string(declared) + " bytes but " +
                          std::to_string(available) + " follow the header");
  }

  return header;
}

// That a column is what the generated member expects, or a lossless promotion of it.
// Refusal is by name and both types, never by reading anyway.
inline void check_column(const Column& column, const char* field_name, std::uint8_t kind,
                         std::int32_t count, std::initializer_list<std::uint8_t> accepted) {
  if (column.kind != kind || (kind != kKindVarArray && column.count != count)) {
    throw LiteBinaryError(std::string(field_name) +
                          ": the file's column does not match the generated member's shape; "
                          "the schema changed shape, regenerate the code or rebuild the data");
  }

  for (const std::uint8_t candidate : accepted) {
    if (column.element == candidate) return;
  }

  throw LiteBinaryError(std::string(field_name) + ": the file carries element type " +
                        std::to_string(column.element) +
                        ", which this member cannot read; the column changed type "
                        "incompatibly, regenerate the code or rebuild the data");
}

// That a block was consumed exactly: a mismatch is a format disagreement, and stopping
// here names the column instead of corrupting the next.
inline void check_block_end(const LiteBinaryReader& reader, const Column& column,
                            std::size_t expected_end) {
  if (reader.position() != expected_end) {
    throw LiteBinaryError("column tag " + std::to_string(column.tag) +
                          ": the block's declared length and the bytes consumed disagree");
  }
}
}  // namespace sheetman

#endif  // SHEETMAN_LITE_BINARY_READER_H
