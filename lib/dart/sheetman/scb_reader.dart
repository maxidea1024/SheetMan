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
//   counter32   zig-zag encoded int32 written as a varint32
//   string      counter32 byte length, then that many UTF-8 bytes
//
// One of several readers of one format the exporter defines. The conformance corpus is
// what keeps them agreeing.
//
// Dart's int is 64 bits on the VM and a double on the web, where it carries 53. Anything
// wider than that is read as a BigInt here rather than an int, which is the same call the
// TypeScript reader makes and for the same reason: a value past 2^53 does not fail on the
// web, it comes back changed. That covers int64 and both tick counts, which reach 3.1e18.
//
// Dart has no single-precision float either, so a float32 read widens to a double holding
// the stored value.

import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

/// Stamped at the head of every table file by the exporter.
/// The format is column-oriented and self-describing: the header names every column
/// and how long its block is, and a reader that meets a version it does not know stops
/// rather than guessing.
// 102 replaced 101 outright - a descriptor gained its encoding byte - before any
// 101 file had shipped.
const int formatVersion = 102;

// The wire element types and kinds, as a column descriptor spells them.
const int elementVarint = 0;
const int elementBool = 1;
const int elementI32 = 2;
const int elementI64 = 3;
const int elementF32 = 4;
const int elementF64 = 5;
const int elementString = 6;
const int elementUuid = 7;

const int kindScalar = 0;
const int kindFixedArray = 1;
const int kindVarArray = 2;

// How a block's values are laid out. Raw is the layout 101 had; the others compress
// a column that repeats itself. spec/scb-v102-column-encoding.md is the contract.
const int encodingRaw = 0;
const int encodingVarint = 1;
const int encodingDelta = 2;
const int encodingRle = 3;
const int encodingDeltaRle = 4;
const int encodingDict = 5;
const int encodingDictRle = 6;

/// One column as the file describes it.
class Column {
  Column(this.tag, this.element, this.kind, this.encoding, this.count, this.byteLength);

  /// What identifies the column, instead of its position.
  final int tag;
  final int element;

  /// How the block's values are laid out: one of the encoding* constants.
  final int encoding;
  final int kind;

  /// Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one.
  final int count;

  /// Total bytes of the column block - what a skip advances by.
  final int byteLength;
}

/// A parsed header: the row count and the column descriptors that follow it.
class Header {
  Header(this.rowCount, this.columns);

  final int rowCount;
  final List<Column> columns;
}

/// A lookup for a key no row carries.
///
/// Thrown by the generated getBy*OrThrow lookups, which is where a caller has said the
/// key has to be there. findBy* answers the same question with null.
///
/// Its own type rather than ScbException: nothing is wrong with the file, and a
/// caller catching one of these is not catching the other.
class RecordNotFoundException implements Exception {
  RecordNotFoundException(this.message);

  final String message;

  @override
  String toString() => 'RecordNotFoundException: $message';
}

/// A table file is truncated, malformed, or not a table file.
class ScbException implements Exception {
  ScbException(this.message);

  final String message;

  @override
  String toString() => 'ScbException: $message';
}

/// A 128 bit identifier, stored in .NET Guid byte order.
///
/// That order is not plain big-endian: the first three components are little endian and
/// the trailing eight bytes are not, which is what toString has to account for.
class Uuid {
  Uuid(this.bytes);

  Uuid.empty() : bytes = Uint8List(16);

  final Uint8List bytes;

  // Component order matching .NET's Guid.ToString("D").
  static const List<int> _order = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15];

  @override
  String toString() {
    final out = StringBuffer();

    for (var position = 0; position < _order.length; position++) {
      if (position == 4 || position == 6 || position == 8 || position == 10) {
        out.write('-');
      }

      out.write(bytes[_order[position]].toRadixString(16).padLeft(2, '0'));
    }

    return out.toString();
  }

  @override
  bool operator ==(Object other) {
    if (other is! Uuid) return false;

    for (var i = 0; i < 16; i++) {
      if (bytes[i] != other.bytes[i]) return false;
    }

    return true;
  }

  @override
  int get hashCode => Object.hashAll(bytes);
}

/// Sequential reader over a table file's bytes.
class ScbReader {
  ScbReader(this._data)
      : _view = ByteData.view(_data.buffer, _data.offsetInBytes, _data.lengthInBytes);

  final Uint8List _data;
  final ByteData _view;

  int _position = 0;

  /// Bytes consumed so far.
  int get position => _position;

  /// Advances past bytes without interpreting them: an unknown column whole block.
  /// The column-oriented layout is what makes this one call the entirety of skipping.
  void skip(int byteCount) {
    final remaining = _data.length - _position;

    if (byteCount < 0 || byteCount > remaining) {
      throw ScbException('cannot skip $byteCount bytes with $remaining remaining');
    }

    _position += byteCount;
  }

  // Promotions: a member reading a file element narrower than itself. Only the
  // mathematically lossless directions exist; checkColumn already refused the rest.

  /// An int member from i32 or varint.
  int readI32As(int element) =>
      element == elementI32 ? readInt32() : readCounter32();

  /// A 64-bit member from i64, i32 or varint. BigInt, as int64 is here: on the web a
  /// Dart int is a double and holds 53 bits.
  BigInt readI64As(int element) {
    if (element == elementI64) return readInt64();
    if (element == elementI32) return BigInt.from(readInt32());
    return BigInt.from(readCounter32());
  }

  /// A double member from f64, f32 or i32 - all exact in a double.
  double readF64As(int element) {
    if (element == elementF64) return readDouble();
    if (element == elementF32) return readFloat();
    return readInt32().toDouble();
  }

  /// Bytes left to read.
  int get remaining => _data.lengthInBytes - _position;

  int _take(int count) {
    if (remaining < count) {
      throw ScbException(
          'table data ended after $_position of ${_data.lengthInBytes} bytes '
          'while $count more were expected');
    }

    final start = _position;
    _position += count;

    return start;
  }

  int readUint8() => _data[_take(1)];

  bool readBool() => readUint8() != 0;

  int readInt32() => _view.getInt32(_take(4), Endian.little);

  int readUint32() => _view.getUint32(_take(4), Endian.little);

  /// A 64-bit integer, as a BigInt.
  ///
  /// Not an int: on the web that is a double and anything past 2^53 comes back changed
  /// rather than failing, which is the quietest way a table can be wrong.
  BigInt readInt64() {
    final at = _take(8);

    // Assembled from two 32-bit halves, because getInt64 throws on the web.
    final low = BigInt.from(_view.getUint32(at, Endian.little));
    final high = BigInt.from(_view.getInt32(at + 4, Endian.little));

    return (high << 32) | low;
  }

  /// A single-precision value, widened to a double holding the stored value.
  double readFloat() => _view.getFloat32(_take(4), Endian.little);

  double readDouble() => _view.getFloat64(_take(8), Endian.little);

  /// A length-prefixed UTF-8 string.
  String readString() {
    final length = readCounter32();

    if (length < 0) throw ScbException('string length is negative');
    if (length == 0) return '';

    final at = _take(length);
    return utf8.decode(Uint8List.sublistView(_data, at, at + length));
  }

  /// A timestamp as .NET ticks: 100 ns units since 0001-01-01.
  ///
  /// A BigInt, for the same reason as int64: the values reach 3.1e18, which the web's
  /// int cannot hold. DateTime would not do either - it keeps microseconds where a tick
  /// is a hundred nanoseconds.
  BigInt readDateTimeTicks() => readInt64();

  /// A duration as .NET ticks.
  BigInt readDurationTicks() => readInt64();

  Uuid readUuid() {
    final at = _take(16);
    return Uuid(Uint8List.fromList(_data.sublist(at, at + 16)));
  }

  /// An int32 written in as few bytes as its magnitude needed, either sign.
  int readOptimalInt32() {
    final encoded = _readVarint32();

    // Undoes the zig-zag fold by dividing rather than shifting, and negating rather than
    // xoring. Both bit operations are 32-bit on the web, and `encoded` reaches 2^32 - 1 -
    // so the idiomatic `(e >> 1) ^ -(e & 1)` is wrong there for half the range.
    final half = encoded ~/ 2;

    return encoded.isEven ? half : -half - 1;
  }

  /// A count, in the same encoding as readOptimalInt32.
  int readCounter32() => readOptimalInt32();

  /// An enum value, which travels zig-zag encoded rather than fixed width.
  int readEnum() => readOptimalInt32();

  int _readVarint32() {
    var value = 0;

    // Multiplied and added rather than shifted and ored: both of those are 32-bit on the
    // web, and the fifth byte of a varint lands past that.
    var scale = 1;

    for (var step = 0; step < 5; step++) {
      final b = readUint8();

      value += (b & 0x7F) * scale;

      if (b & 0x80 == 0) return value;

      scale *= 128;
    }

    throw ScbException('varint32 is longer than five bytes');
  }
}

/// Reads one scalar column's values in row order, whatever the block's encoding.
///
/// The generated row loop stays a row loop; this is the one place that knows how a
/// delta accumulates, how long a run has left, or that a dictionary index is a
/// reference into strings decoded once. That last one matters beyond file size: a
/// hundred-thousand-row column with three distinct strings allocates three strings,
/// not a hundred thousand.
///
/// checkColumn has already refused any (element, encoding) pair the spec does not
/// define, so the switches here do not re-litigate that.
class ScbColumnCursor {
  ScbColumnCursor(this._reader, Column column, int rowCount, this._fieldName)
      : _element = column.element,
        _encoding = column.encoding,
        _rowsRemaining = rowCount {
    if (_encoding == encodingDict || _encoding == encodingDictRle) {
      final count = _reader.readCounter32();

      if (count < 0) {
        throw ScbException('$_fieldName: the dictionary entry count is negative');
      }

      _dictionary = List<String>.generate(count, (_) => _reader.readString());
    }
  }

  final ScbReader _reader;
  final String _fieldName;
  final int _element;
  final int _encoding;

  /// The block's dictionary, decoded once and handed out per row.
  List<String>? _dictionary;

  // A run-length family's current run: what remains of it, and its value - which is a
  // plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
  int _runRemaining = 0;
  int _runValue = 0;

  // The delta family's accumulator, once _started.
  int _previous = 0;
  bool _started = false;

  // Rows not yet handed out. A run that claims more than this is corrupt, and catching
  // it here names the field instead of leaving it to the block-end check.
  int _rowsRemaining;

  /// The next int32 - which also serves enums, and reference indexes.
  int nextI32() {
    _rowsRemaining--;

    switch (_encoding) {
      case encodingRaw:
        return _element == elementI32 ? _reader.readInt32() : _reader.readOptimalInt32();

      case encodingVarint:
        return _reader.readOptimalInt32();

      case encodingDelta:
        // The addition wraps on purpose, mirroring the writer's wrapping subtraction;
        // together they are exact for every int32 pair.
        if (_started) {
          _previous = _wrap32(_previous + _reader.readOptimalInt32());
        } else {
          _previous = _reader.readOptimalInt32();
          _started = true;
        }

        return _previous;

      case encodingRle:
        if (_runRemaining == 0) _readRun();

        _runRemaining--;
        return _runValue;

      default: // encodingDeltaRle; checkColumn refused everything else.
        if (!_started) {
          _previous = _reader.readOptimalInt32();
          _started = true;
          return _previous;
        }

        if (_runRemaining == 0) _readRun();

        _runRemaining--;
        _previous = _wrap32(_previous + _runValue);
        return _previous;
    }
  }

  /// A 64-bit member: an i64 column is always raw, anything narrower decodes as int32.
  /// BigInt, for the same reason as readInt64.
  BigInt nextI64() =>
      _element == elementI64 ? _reader.readInt64() : BigInt.from(nextI32());

  /// A double member: float columns are always raw, an i32 column decodes then widens.
  double nextF64() {
    if (_element == elementF64) return _reader.readDouble();
    if (_element == elementF32) return _reader.readFloat();
    return nextI32().toDouble();
  }

  /// The next string - the dictionary's instance where the block has one.
  String nextString() {
    _rowsRemaining--;

    switch (_encoding) {
      case encodingRaw:
        return _reader.readString();

      case encodingDict:
        return _dictionaryEntry(_reader.readCounter32());

      default: // encodingDictRle
        if (_runRemaining == 0) _readRun();

        _runRemaining--;
        return _dictionaryEntry(_runValue);
    }
  }

  void _readRun() {
    final length = _reader.readCounter32();

    // + 1 because the row this run was read for is already counted out of
    // _rowsRemaining by its next call.
    if (length < 1 || length > _rowsRemaining + 1) {
      throw ScbException(
          '$_fieldName: a run of $length values cannot cover the '
          '${_rowsRemaining + 1} rows left in the column');
    }

    _runRemaining = length;
    _runValue = _reader.readOptimalInt32();
  }

  String _dictionaryEntry(int index) {
    final dictionary = _dictionary!;

    if (index < 0 || index >= dictionary.length) {
      throw ScbException(
          '$_fieldName: dictionary index $index is out of range - the '
          'dictionary holds ${dictionary.length} entries');
    }

    return dictionary[index];
  }

  /// The delta family's 32-bit wrap. Dart's int is wider than the format's (64 bits
  /// on the VM, 53 on the web), so the wrap is explicit: the low 32 bits of the sum,
  /// sign-extended. The mask is 32-bit on the web too - here that truncation is the
  /// point - while the sign extension is arithmetic, for the same reason
  /// readOptimalInt32 divides rather than shifts.
  static int _wrap32(int value) {
    value &= 0xFFFFFFFF;
    return value >= 0x80000000 ? value - 0x100000000 : value;
  }
}

/// Reads and checks a table file's header, returning the row count that follows it.
///
/// The reserved byte is written as zero and is where compression or encryption flags
/// would go; a non-zero value means the file needs handling this build does not have.
Header readTableHeader(ScbReader reader) {
  final version = reader.readUint32();

  if (version != formatVersion) {
    throw ScbException(
        'table format version $version is not supported (expected $formatVersion)');
  }

  if (reader.readUint8() != 0) {
    throw ScbException('table declares unsupported features');
  }

  final count = reader.readCounter32();
  if (count < 0) throw ScbException('table row count is negative');

  final columnCount = reader.readCounter32();
  if (columnCount < 0) throw ScbException('table column count is negative');

  final columns = <Column>[];

  for (var at = 0; at < columnCount; at++) {
    final tag = reader.readCounter32();
    final wire = reader.readUint8();
    final encoding = reader.readUint8();
    final elementCount = reader.readCounter32();
    final byteLength = reader.readUint32();
    columns.add(
        Column(tag, wire & 0x0f, (wire >> 4) & 0x03, encoding, elementCount, byteLength));
  }

  // What the descriptors say about the file, checked before anybody allocates for the
  // row count. The blocks are all that follows the header, so their declared lengths have
  // to add up to the bytes left. A raw block also costs at least one byte per row - a
  // varint's shortest form, an empty string's length prefix, a variable array's counter -
  // so a larger row count is one the exporter could not have written. An encoded block
  // has no such floor; its decode checks run sums and dictionary bounds instead.

  final available = reader.remaining;
  var declared = 0;

  for (final column in columns) {
    if (column.byteLength < 0 || column.byteLength > available - declared) {
      throw ScbException(
          'column tag ${column.tag} declares ${column.byteLength} bytes, which the file '
          'cannot hold');
    }

    declared += column.byteLength;

    if (column.encoding == encodingRaw && count > column.byteLength) {
      throw ScbException(
          'the row count $count is larger than column tag ${column.tag} can hold in its '
          '${column.byteLength} bytes');
    }
  }

  if (declared != available) {
    throw ScbException(
        'the columns declare $declared bytes but $available follow the header');
  }

  return Header(count, columns);
}

/// That a column is what the generated member expects, or a lossless promotion of it.
/// Refusal is by name and both types, never by reading anyway.
void checkColumn(Column column, String fieldName, int kind, int count, List<int> accepted) {
  if (column.kind != kind || (kind != kindVarArray && column.count != count)) {
    throw ScbException(
        '$fieldName: the file column (kind ${column.kind}, count ${column.count}) does not '
        'match the generated member (kind $kind, count $count). The schema changed shape; '
        'regenerate the code or rebuild the data.');
  }

  // An encoding this build cannot decode - or one the spec does not define for this
  // element - is refused by name, exactly like an element it cannot read. An unknown
  // column's encoding never gets here - a skip is a skip whatever the block's layout.
  if (!_encodingSupported(column)) {
    throw ScbException(
        "$fieldName: the file's column uses encoding ${column.encoding}, which this "
        'reader cannot decode for its element type. Regenerate the code or rebuild '
        'the data.');
  }

  if (!accepted.contains(column.element)) {
    throw ScbException(
        '$fieldName: the file carries element type ${column.element}, which this member '
        'cannot read (accepts $accepted). The column changed type incompatibly; regenerate '
        'the code or rebuild the data.');
  }
}

/// The (element, encoding) pairs the spec defines. Arrays are always raw; integers
/// take the integer encodings, strings the dictionary ones.
bool _encodingSupported(Column column) {
  if (column.encoding == encodingRaw) return true;
  if (column.kind != kindScalar) return false;

  switch (column.element) {
    case elementVarint:
      return column.encoding == encodingRle;

    case elementI32:
      return column.encoding >= encodingVarint && column.encoding <= encodingDeltaRle;

    case elementString:
      return column.encoding == encodingDict || column.encoding == encodingDictRle;

    default:
      return false;
  }
}

/// That a block was consumed exactly: a mismatch is a format disagreement, and stopping
/// here names the column instead of corrupting the next.
void checkBlockEnd(ScbReader reader, Column column, int expectedEnd) {
  if (reader.position != expectedEnd) {
    throw ScbException(
        'column tag ${column.tag}: its block declared ${column.byteLength} bytes but the '
        'read ended ${expectedEnd - reader.position} bytes short of its boundary');
  }
}

/// Reads a whole file into memory.
Uint8List readAllBytes(String filename) => File(filename).readAsBytesSync();
