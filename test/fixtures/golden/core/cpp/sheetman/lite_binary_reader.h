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

/// A .NET DateTime, kept as ticks so no precision is lost in transit.
///
/// One tick is 100 nanoseconds and the epoch is 0001-01-01T00:00:00. Conversion
/// to a std::chrono clock is left to the caller because the sensible target
/// depends on what the value means.
struct DateTime {
  std::int64_t ticks = 0;

  /// Seconds since the Unix epoch. 621355968000000000 is the tick count of
  /// 1970-01-01 in .NET's epoch.
  std::int64_t unix_seconds() const { return (ticks - 621355968000000000LL) / 10000000LL; }

  friend bool operator==(const DateTime& a, const DateTime& b) { return a.ticks == b.ticks; }
  friend bool operator!=(const DateTime& a, const DateTime& b) { return !(a == b); }
};

/// A .NET TimeSpan, kept as ticks of 100 nanoseconds.
struct TimeSpan {
  std::int64_t ticks = 0;

  std::int64_t total_milliseconds() const { return ticks / 10000LL; }

  friend bool operator==(const TimeSpan& a, const TimeSpan& b) { return a.ticks == b.ticks; }
  friend bool operator!=(const TimeSpan& a, const TimeSpan& b) { return !(a == b); }
};

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

  void read(DateTime& value) { value.ticks = static_cast<std::int64_t>(read_fixed64()); }
  void read(TimeSpan& value) { value.ticks = static_cast<std::int64_t>(read_fixed64()); }

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
constexpr std::uint32_t kBinaryFileFormatVersion = 100;

/// Reads and checks the file header, returning the row count that follows it.
///
/// The reserved byte is written as zero and is where compression or encryption
/// flags would go; a non-zero value means the file needs handling this build
/// does not have.
inline std::int32_t read_table_header(LiteBinaryReader& reader) {
  const std::uint32_t version = reader.read_fixed32();
  if (version != kBinaryFileFormatVersion) {
    throw LiteBinaryError("table format version " + std::to_string(version) + " is not supported (expected " +
                          std::to_string(kBinaryFileFormatVersion) + ")");
  }

  const std::uint8_t reserved = reader.read_fixed8();
  if (reserved != 0) throw LiteBinaryError("table declares unsupported features");

  const std::int32_t row_count = reader.read_counter32();
  if (row_count < 0) throw LiteBinaryError("table row count is negative");

  return row_count;
}

}  // namespace sheetman

#endif  // SHEETMAN_LITE_BINARY_READER_H
