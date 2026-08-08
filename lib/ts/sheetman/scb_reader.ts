// ---------------------------------------------------------------------------
// SheetMan Scb reader for TypeScript.
//
// Reads the .scb files produced by SheetMan's binary exporter. The format is
// defined by the C# writer in
// lib/Unity/SheetManForUnity/Assets/Plugins/SheetMan.Runtime, and this is the
// third implementation of the reading half of it, alongside the C# original and
// lib/cpp/sheetman/scb_reader.h:
//
//   fixed8      one byte
//   fixed32     four bytes, little endian
//   fixed64     eight bytes, little endian
//   varint32    seven bits per byte, high bit set while more bytes follow,
//               at most five bytes
//   counter32   zig-zag encoded int32 written as a varint32
//   string      counter32 byte length, then that many UTF-8 bytes
//   int32          fixed32
//   int64          fixed64
//   bool           fixed8, zero meaning false
//   float/double   fixed32 / fixed64 holding the IEEE-754 bit pattern
//   datetime       fixed64 of .NET ticks: 100 ns units since 0001-01-01
//   timespan       fixed64 of .NET ticks
//   uuid           sixteen bytes in .NET Guid layout
//
// Values are surfaced exactly as the JSON export renders them, so a generated
// table reads the same whichever source it was loaded from. That is why dates
// and durations come back as strings rather than ticks: the JSON export writes
// them that way, and one API is worth more than a marginally richer type on one
// of the two paths.
//
// No dependencies. Works over a Uint8Array, so it runs in Node and in a browser
// alike; only the convenience file read needs `fs`.
// ---------------------------------------------------------------------------

/** Thrown when a table file is truncated, malformed, or not a table file. */
export class ScbError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'ScbError'
  }
}

/**
 * Version stamped at the head of every table file by the exporter. 102 replaced 101
 * outright - a descriptor gained its encoding byte - before any 101 file had shipped.
 */
export const BINARY_FILE_FORMAT_VERSION = 102

// The wire's element types and kinds, as a column descriptor spells them.
export const ELEMENT_VARINT = 0
export const ELEMENT_BOOL = 1
export const ELEMENT_I32 = 2
export const ELEMENT_I64 = 3
export const ELEMENT_F32 = 4
export const ELEMENT_F64 = 5
export const ELEMENT_STRING = 6
export const ELEMENT_UUID = 7

export const KIND_SCALAR = 0
export const KIND_FIXED_ARRAY = 1
export const KIND_VAR_ARRAY = 2

// How a block's values are laid out. Raw is the layout 101 had; the others compress
// a column that repeats itself. spec/scb-v102-column-encoding.md is the contract.
export const ENCODING_RAW = 0
export const ENCODING_VARINT = 1
export const ENCODING_DELTA = 2
export const ENCODING_RLE = 3
export const ENCODING_DELTA_RLE = 4
export const ENCODING_DICT = 5
export const ENCODING_DICT_RLE = 6
export const ENCODING_DICT_FRONT = 7
export const ENCODING_DICT_FRONT_RLE = 8

/** One column as the file describes it. */
export interface ScbColumn {
  /** What identifies the column, instead of its position. */
  tag: number
  element: number
  kind: number
  /** How the block's values are laid out: one of the ENCODING_* constants. */
  encoding: number
  /** Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one. */
  count: number
  /** Total bytes of the column's block - what a skip advances by. */
  byteLength: number
}

/** Ticks between 0001-01-01 and the Unix epoch. */
const UNIX_EPOCH_TICKS = 621355968000000000n

/** .NET ticks per second. A tick is 100 ns. */
const TICKS_PER_SECOND = 10000000n

const TICKS_PER_DAY = 864000000000n
const TICKS_PER_HOUR = 36000000000n
const TICKS_PER_MINUTE = 600000000n

/** Sequential reader over a table file's bytes. */
export class ScbReader {
  private readonly data: Uint8Array
  private readonly view: DataView
  private offset = 0

  constructor(data: Uint8Array) {
    this.data = data
    this.view = new DataView(data.buffer, data.byteOffset, data.byteLength)
  }

  get position(): number { return this.offset }

  /**
   * Advances past bytes without interpreting them: an unknown column's whole block.
   * The column-oriented layout is what makes this one call the entirety of skipping.
   */
  skip(byteCount: number): void {
    if (byteCount < 0 || byteCount > this.remaining) {
      throw new ScbError(`cannot skip ${byteCount} bytes with ${this.remaining} remaining`)
    }
    this.offset += byteCount
  }

  // Promotions: a member reading a file element narrower than itself. Only the
  // mathematically lossless directions exist; the column check already refused the rest.

  /** An int32 member from i32 or varint. */
  readI32As(element: number): number {
    return element === ELEMENT_I32 ? this.readInt32() : this.readCounter32()
  }

  /** An int64 member from i64, i32 or varint. Always a bigint, as int64 is here. */
  readI64As(element: number): bigint {
    if (element === ELEMENT_I64) return this.readInt64()
    if (element === ELEMENT_I32) return BigInt(this.readInt32())
    return BigInt(this.readCounter32())
  }

  /** A double member from f64, f32 or i32 - all exact in a double. */
  readF64As(element: number): number {
    if (element === ELEMENT_F64) return this.readDouble()
    if (element === ELEMENT_F32) return this.readFloat()
    return this.readInt32()
  }
  get remaining(): number { return this.data.length - this.offset }

  readFixed8(): number {
    this.require(1)
    return this.data[this.offset++]
  }

  readFixed32(): number {
    this.require(4)
    const value = this.view.getUint32(this.offset, true)
    this.offset += 4
    return value
  }

  readFixed64(): bigint {
    this.require(8)
    const value = this.view.getBigUint64(this.offset, true)
    this.offset += 8
    return value
  }

  readVarint32(): number {
    let value = 0

    for (let shift = 0; shift < 35; shift += 7) {
      const byte = this.readFixed8()

      // Shifting with `<<` is a 32-bit signed operation in JS, so the top
      // bits of a five-byte varint would land in the sign. Multiplying keeps
      // the arithmetic in the double range, where 32 bits fit exactly.
      value += (byte & 0x7f) * Math.pow(2, shift)

      if ((byte & 0x80) === 0) return value
    }

    throw new ScbError('varint32 is longer than five bytes')
  }

  /**
   * Zig-zag decoded int32: the encoding used for lengths and enum values, so
   * small negatives cost as little as small positives.
   */
  readCounter32(): number {
    const encoded = this.readVarint32()

    // `>>> 1` then a conditional negate, rather than the usual xor: the xor
    // form relies on 32-bit two's complement, which JS bitwise operators
    // provide only for values that already fit in a signed 32-bit int.
    const magnitude = Math.floor(encoded / 2)
    return (encoded & 1) === 1 ? -(magnitude + 1) : magnitude
  }

  readBool(): boolean {
    return this.readFixed8() !== 0
  }

  readInt32(): number {
    this.require(4)
    const value = this.view.getInt32(this.offset, true)
    this.offset += 4
    return value
  }

  /**
   * A 64-bit integer, as a BigInt.
   *
   * Not a `number`: a double holds only 53 bits of mantissa, so anything past
   * 2^53 comes back quietly wrong - which is exactly the class of corruption
   * the writer itself once had.
   */
  readInt64(): bigint {
    this.require(8)
    const value = this.view.getBigInt64(this.offset, true)
    this.offset += 8
    return value
  }

  readFloat(): number {
    this.require(4)
    const value = this.view.getFloat32(this.offset, true)
    this.offset += 4
    return value
  }

  readDouble(): number {
    this.require(8)
    const value = this.view.getFloat64(this.offset, true)
    this.offset += 8
    return value
  }

  /**
   * Advances past bytes and hands them back uninterpreted.
   *
   * A view onto the same buffer rather than a copy, so a dictionary of fixed-width
   * entries costs nothing to keep: the bytes are already in memory and nothing
   * mutates them.
   */
  readBytes(count: number): Uint8Array {
    if (count < 0) throw new ScbError(`cannot read ${count} bytes`)

    this.require(count)

    const bytes = this.data.subarray(this.offset, this.offset + count)
    this.offset += count

    return bytes
  }

  readString(): string {
    const length = this.readCounter32()
    if (length < 0) throw new ScbError('string length is negative')

    return decodeUtf8(this.readBytes(length))
  }

  /**
   * A date, formatted the way the JSON export writes one, so both read paths
   * of a generated table yield the same string.
   */
  readDateTime(): string {
    return formatDateTimeTicks(this.readFixed64())
  }

  /**
   * A duration, formatted the way the JSON export writes one.
   *
   * Read signed, unlike a date: a duration may be negative.
   */
  readTimeSpan(): string {
    return formatTimeSpanTicks(this.readInt64())
  }

  /** A uuid in its canonical text form. */
  readUuid(): string {
    return formatUuid(this.readBytes(16))
  }

  /** An enum value, which travels zig-zag encoded rather than fixed width. */
  readEnum(): number {
    return this.readCounter32()
  }

  private require(count: number): void {
    if (this.remaining < count) {
      throw new ScbError(
        `table data ended after ${this.offset} of ${this.data.length} bytes ` +
        `while ${count} more were expected`)
    }
  }
}

/**
 * A sorted dictionary whose entries state only what they do not share with the
 * entry before them.
 *
 * Decoded into whole strings here rather than kept folded, because a row wants a
 * string and the folding was only ever about the bytes on disk. The scratch buffer
 * grows to the longest entry and is reused, so the allocations are the strings
 * themselves - one per distinct value, which is the point.
 */
function readFrontCodedDictionary(
  reader: ScbReader, count: number, fieldName: string): string[] {
  const entries: string[] = []
  let scratch = new Uint8Array(64)
  let previousLength = 0

  for (let at = 0; at < count; at++) {
    const shared = reader.readCounter32()
    const rest = reader.readCounter32()

    if (shared < 0 || rest < 0 || shared > previousLength) {
      throw new ScbError(
        `${fieldName}: dictionary entry ${at} shares ${shared} bytes with an entry ` +
        `of ${previousLength}`)
    }

    const length = shared + rest

    if (length > scratch.length) {
      let capacity = scratch.length
      while (capacity < length) capacity *= 2

      const grown = new Uint8Array(capacity)
      grown.set(scratch)
      scratch = grown
    }

    if (rest > 0) scratch.set(reader.readBytes(rest), shared)

    entries.push(length === 0 ? '' : decodeUtf8(scratch.subarray(0, length)))
    previousLength = length
  }

  return entries
}

/**
 * Reads one scalar column's values in row order, whatever the block's encoding.
 *
 * The generated row loop stays a row loop; this is the one place that knows how
 * a delta accumulates, how long a run has left, or that a dictionary index is a
 * reference into strings decoded once. That last one matters beyond file size: a
 * hundred-thousand-row column with three distinct strings allocates three strings,
 * not a hundred thousand.
 *
 * checkColumn has already refused any (element, encoding) pair the spec does not
 * define, so the switches here do not re-litigate that.
 */
export class ScbColumnCursor {
  private readonly reader: ScbReader
  private readonly fieldName: string
  private readonly element: number
  private readonly encoding: number

  /**
   * The block's dictionary, decoded once and handed out per row.
   *
   * One of the two is filled when the block has a dictionary at all, chosen by the
   * element: strings are decoded to instances that rows then share, and a
   * fixed-width element keeps its raw bytes so the value is reconstructed exactly
   * as the raw layout would have read it.
   */
  private readonly dictionary: string[] = []

  private readonly valueDictionary: DataView | null = null
  private readonly valueWidth: number = 0

  // A run-length family's current run: what remains of it, and its value - which
  // is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE.
  private runRemaining = 0
  private runValue = 0

  // The delta family's accumulator, once started.
  private previous = 0
  private started = false

  // Rows not yet handed out. A run that claims more than this is corrupt, and
  // catching it here names the field instead of leaving it to the block-end check.
  private rowsRemaining: number

  constructor(reader: ScbReader, column: ScbColumn, rowCount: number, fieldName: string) {
    this.reader = reader
    this.fieldName = fieldName
    this.element = column.element
    this.encoding = column.encoding
    this.rowsRemaining = rowCount

    const plainDictionary =
      this.encoding === ENCODING_DICT || this.encoding === ENCODING_DICT_RLE

    const frontDictionary =
      this.encoding === ENCODING_DICT_FRONT || this.encoding === ENCODING_DICT_FRONT_RLE

    if (!plainDictionary && !frontDictionary) return

    const count = reader.readCounter32()
    if (count < 0) throw new ScbError(`${fieldName}: the dictionary entry count is negative`)

    if (frontDictionary) {
      this.dictionary = readFrontCodedDictionary(reader, count, fieldName)
      return
    }

    if (this.element === ELEMENT_STRING) {
      for (let at = 0; at < count; at++)
        this.dictionary.push(reader.readString())

      return
    }

    // A fixed-width element: the entries are the value's own bytes, laid out one
    // after another, so they are taken as bytes and turned into values only when a
    // row asks for one.
    this.valueWidth = this.element === ELEMENT_F32 ? 4 : 8

    const bytes = reader.readBytes(count * this.valueWidth)
    this.valueDictionary = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  }

  /** The next int32 - which also serves enums, and reference indexes. */
  nextI32(): number {
    this.rowsRemaining--

    switch (this.encoding) {
      case ENCODING_RAW:
        return this.element === ELEMENT_I32 ? this.reader.readInt32() : this.reader.readCounter32()

      case ENCODING_VARINT:
        return this.reader.readCounter32()

      case ENCODING_DELTA: {
        // The addition wraps on purpose, mirroring the writer's wrapping
        // subtraction; together they are exact for every int32 pair. `| 0`
        // is the wrap: it folds the double-range sum back into an int32.
        if (this.started) {
          this.previous = (this.previous + this.reader.readCounter32()) | 0
        } else {
          this.previous = this.reader.readCounter32()
          this.started = true
        }

        return this.previous
      }

      case ENCODING_RLE: {
        if (this.runRemaining === 0) this.readRun()

        this.runRemaining--
        return this.runValue
      }

      default: { // ENCODING_DELTA_RLE; checkColumn refused everything else.
        if (!this.started) {
          this.previous = this.reader.readCounter32()
          this.started = true
          return this.previous
        }

        if (this.runRemaining === 0) this.readRun()

        this.runRemaining--
        this.previous = (this.previous + this.runValue) | 0
        return this.previous
      }
    }
  }

  /**
   * An int64 member: from an i64 column raw or through its dictionary, and from
   * anything narrower by decoding an int32 and widening it.
   *
   * A dictionary entry is the eight bytes the raw layout would have carried, so it
   * is read back as a little-endian BigInt exactly as readInt64 does.
   */
  nextI64(): bigint {
    if (this.element !== ELEMENT_I64) return BigInt(this.nextI32())

    if (this.valueDictionary !== null)
      return this.valueDictionary.getBigInt64(this.nextValueEntry(), true)

    this.rowsRemaining--
    return this.reader.readInt64()
  }

  /** A float member: raw, or the dictionary entry's exact bit pattern. */
  nextF32(): number {
    if (this.valueDictionary !== null)
      return this.valueDictionary.getFloat32(this.nextValueEntry(), true)

    this.rowsRemaining--
    return this.reader.readFloat()
  }

  /**
   * A double member: from f64 or f32 - either of them raw or dictionary-encoded -
   * and from an i32 column by decoding and widening.
   */
  nextF64(): number {
    if (this.element === ELEMENT_F64) {
      if (this.valueDictionary !== null)
        return this.valueDictionary.getFloat64(this.nextValueEntry(), true)

      this.rowsRemaining--
      return this.reader.readDouble()
    }

    if (this.element === ELEMENT_F32) return this.nextF32()

    return this.nextI32()
  }

  /** A bool member: one byte raw, or a run of them. */
  nextBool(): boolean {
    if (this.encoding === ENCODING_RLE) return this.nextI32() !== 0

    this.rowsRemaining--
    return this.reader.readBool()
  }

  /**
   * Where the next row's dictionary entry starts, for a fixed-width element: a byte
   * offset into the entries kept as they were written.
   */
  private nextValueEntry(): number {
    this.rowsRemaining--

    let index: number

    if (this.encoding === ENCODING_DICT) {
      index = this.reader.readCounter32()
    } else {
      if (this.runRemaining === 0) this.readRun()

      this.runRemaining--
      index = this.runValue
    }

    const count = this.valueDictionary!.byteLength / this.valueWidth

    if (index < 0 || index >= count) {
      throw new ScbError(
        `${this.fieldName}: dictionary index ${index} is out of range - the ` +
        `dictionary holds ${count} entries`)
    }

    return index * this.valueWidth
  }

  /** The next string - the dictionary's instance where the block has one. */
  nextString(): string {
    this.rowsRemaining--

    switch (this.encoding) {
      case ENCODING_RAW:
        return this.reader.readString()

      case ENCODING_DICT:
      case ENCODING_DICT_FRONT:
        return this.dictionaryEntry(this.reader.readCounter32())

      default: { // ENCODING_DICT_RLE and ENCODING_DICT_FRONT_RLE
        if (this.runRemaining === 0) this.readRun()

        this.runRemaining--
        return this.dictionaryEntry(this.runValue)
      }
    }
  }

  private readRun(): void {
    const length = this.reader.readCounter32()

    // + 1 because the row this run was read for is already counted out of
    // rowsRemaining by its next call.
    if (length < 1 || length > this.rowsRemaining + 1) {
      throw new ScbError(
        `${this.fieldName}: a run of ${length} values cannot cover the ` +
        `${this.rowsRemaining + 1} rows left in the column`)
    }

    this.runRemaining = length
    this.runValue = this.reader.readCounter32()
  }

  private dictionaryEntry(index: number): string {
    if (index < 0 || index >= this.dictionary.length) {
      throw new ScbError(
        `${this.fieldName}: dictionary index ${index} is out of range - the ` +
        `dictionary holds ${this.dictionary.length} entries`)
    }

    return this.dictionary[index]
  }
}

/**
 * Reads and checks the file header, returning the row count that follows it.
 *
 * The reserved byte is written as zero and is where compression or encryption
 * flags would go; a non-zero value means the file needs handling this build does
 * not have.
 */
export function readTableHeader(reader: ScbReader): { rowCount: number, columns: ScbColumn[] } {
  const version = reader.readFixed32()
  if (version !== BINARY_FILE_FORMAT_VERSION) {
    throw new ScbError(
      `table format version ${version} is not supported ` +
      `(expected ${BINARY_FILE_FORMAT_VERSION})`)
  }

  const reserved = reader.readFixed8()
  if (reserved !== 0) throw new ScbError('table declares unsupported features')

  const rowCount = reader.readCounter32()
  if (rowCount < 0) throw new ScbError('table row count is negative')

  const columnCount = reader.readCounter32()
  if (columnCount < 0) throw new ScbError('table column count is negative')

  const columns: ScbColumn[] = []
  for (let at = 0; at < columnCount; ++at) {
    const tag = reader.readCounter32()
    const wire = reader.readFixed8()
    const encoding = reader.readFixed8()
    const count = reader.readCounter32()
    const byteLength = reader.readFixed32()
    columns.push({ tag, element: wire & 0x0f, kind: (wire >> 4) & 0x03, encoding, count, byteLength })
  }

  // What the descriptors say about the file, checked before anybody allocates for the
  // row count. The blocks are all that follows the header, so their declared lengths have
  // to add up to the bytes left. A raw block also costs at least one byte per row - a
  // varint's shortest form, an empty string's length prefix, a variable array's counter -
  // so a larger row count is one the exporter could not have written. An encoded block
  // has no such floor; its decode checks run sums and dictionary bounds instead.

  const available = reader.remaining
  let declared = 0

  for (const column of columns) {
    if (column.byteLength < 0 || column.byteLength > available - declared) {
      throw new ScbError(
        `column tag ${column.tag} declares ${column.byteLength} bytes, which the file cannot hold`)
    }

    declared += column.byteLength

    if (column.encoding === ENCODING_RAW && rowCount > column.byteLength) {
      throw new ScbError(
        `the row count ${rowCount} is larger than column tag ${column.tag} can hold in ` +
        `its ${column.byteLength} bytes`)
    }
  }

  if (declared !== available) {
    throw new ScbError(
      `the columns declare ${declared} bytes but ${available} follow the header`)
  }

  return { rowCount, columns }
}

/**
 * That a column is what the generated member expects, or a lossless promotion of it.
 * Refusal is by name and both types, never by reading anyway.
 */
export function checkColumn(
  column: ScbColumn, fieldName: string, kind: number, count: number, accepted: number[]): void {
  if (column.kind !== kind || (kind !== KIND_VAR_ARRAY && column.count !== count)) {
    throw new ScbError(
      `${fieldName}: the file's column (kind ${column.kind}, count ${column.count}) does not ` +
      `match the generated member (kind ${kind}, count ${count}). The schema changed shape; ` +
      'regenerate the code or rebuild the data.')
  }
  // An encoding this build cannot decode - or one the spec does not define for this
  // element - is refused by name, exactly like an element it cannot read. An unknown
  // column's encoding never gets here - a skip is a skip whatever the block's layout.
  if (!encodingSupported(column)) {
    throw new ScbError(
      `${fieldName}: the file's column uses encoding ${column.encoding}, which this ` +
      'reader cannot decode for its element type. Regenerate the code or rebuild the data.')
  }
  if (!accepted.includes(column.element)) {
    throw new ScbError(
      `${fieldName}: the file carries element type ${column.element}, which this member ` +
      `cannot read (accepts: ${accepted.join(', ')}). The column changed type incompatibly; ` +
      'regenerate the code or rebuild the data.')
  }
}

/**
 * The (element, encoding) pairs the spec defines. Arrays are always raw;
 * integers take the integer encodings, strings the dictionary ones.
 */
function encodingSupported(column: ScbColumn): boolean {
  if (column.encoding === ENCODING_RAW) return true

  if (column.kind !== KIND_SCALAR) return false

  switch (column.element) {
    case ELEMENT_BOOL:
    case ELEMENT_VARINT:
      return column.encoding === ENCODING_RLE

    case ELEMENT_I32:
      return column.encoding >= ENCODING_VARINT && column.encoding <= ENCODING_DELTA_RLE

    // The dictionary is parameterized by element, so these three reach it with
    // entries that are simply their own raw bytes.
    case ELEMENT_I64:
    case ELEMENT_F32:
    case ELEMENT_F64:
      return column.encoding === ENCODING_DICT || column.encoding === ENCODING_DICT_RLE

    // And a string dictionary can additionally be front coded, which is meaningless
    // for a fixed-width element and refused for one.
    case ELEMENT_STRING:
      return column.encoding >= ENCODING_DICT && column.encoding <= ENCODING_DICT_FRONT_RLE

    default:
      return false
  }
}

/**
 * That a block was consumed exactly: a mismatch is a format disagreement, and stopping
 * here names the column instead of corrupting the next.
 */
export function checkBlockEnd(reader: ScbReader, column: ScbColumn, expectedEnd: number): void {
  if (reader.position !== expectedEnd) {
    throw new ScbError(
      `column tag ${column.tag}: its block declared ${column.byteLength} bytes but the read ` +
      `ended ${expectedEnd - reader.position} bytes short of its boundary`)
  }
}

// Declared here rather than pulled from @types/node.
//
// This file is a module, so the declaration is local to it: a consumer that does
// have @types/node is unaffected, and one that does not - a browser project, say -
// is not made to install it for a function they will never call.
declare function require(moduleName: string): any

/**
 * Reads a whole file into memory.
 *
 * Node only, and resolved lazily, so the module still loads in a browser where the
 * binary arrives from fetch rather than the filesystem. Pass the bytes to a table's
 * readBinaryFrom in that case.
 */
export function readAllBytes(filename: string): Uint8Array {
  const fs = require('fs')
  return new Uint8Array(fs.readFileSync(filename))
}

// --------------------------------------------------------------- formatting

/**
 * Decodes UTF-8.
 *
 * TextDecoder where it exists, which is everywhere modern, with a hand-rolled
 * fallback so the reader does not depend on the host providing it.
 */
function decodeUtf8(bytes: Uint8Array): string {
  if (typeof TextDecoder !== 'undefined') {
    return new TextDecoder('utf-8').decode(bytes)
  }

  let out = ''
  let i = 0

  while (i < bytes.length) {
    const b0 = bytes[i++]

    if (b0 < 0x80) {
      out += String.fromCharCode(b0)
    } else if (b0 < 0xe0) {
      out += String.fromCharCode(((b0 & 0x1f) << 6) | (bytes[i++] & 0x3f))
    } else if (b0 < 0xf0) {
      out += String.fromCharCode(
        ((b0 & 0x0f) << 12) | ((bytes[i++] & 0x3f) << 6) | (bytes[i++] & 0x3f))
    } else {
      const codePoint =
        ((b0 & 0x07) << 18) | ((bytes[i++] & 0x3f) << 12) |
        ((bytes[i++] & 0x3f) << 6) | (bytes[i++] & 0x3f)
      out += String.fromCodePoint(codePoint)
    }
  }

  return out
}

function pad(value: number, width: number): string {
  return value.toString().padStart(width, '0')
}

/**
 * Formats .NET ticks as the ISO 8601 text the JSON export produces.
 *
 * The stored value has no time zone - the sheet said nothing about one - so it is
 * rendered as a local-looking timestamp with no offset, matching what the JSON
 * export writes for a DateTime of unspecified kind.
 *
 * Exported because a date column is an i64 one, so it can arrive encoded and its
 * ticks then come from the cursor rather than from a direct read.
 */
export function formatDateTimeTicks(ticks: bigint): string {
  const sinceEpoch = ticks - UNIX_EPOCH_TICKS

  // Split before converting, so the sub-second part keeps full tick resolution
  // rather than being rounded into a millisecond.
  let seconds = sinceEpoch / TICKS_PER_SECOND
  let subTicks = sinceEpoch % TICKS_PER_SECOND

  if (subTicks < 0n) {
    subTicks += TICKS_PER_SECOND
    seconds -= 1n
  }

  // Read the calendar fields in UTC: the value carries no offset, so treating it
  // as UTC and reading it back the same way round-trips the wall clock exactly.
  const date = new Date(Number(seconds) * 1000)

  const text =
    `${pad(date.getUTCFullYear(), 4)}-${pad(date.getUTCMonth() + 1, 2)}-${pad(date.getUTCDate(), 2)}` +
    `T${pad(date.getUTCHours(), 2)}:${pad(date.getUTCMinutes(), 2)}:${pad(date.getUTCSeconds(), 2)}`

  if (subTicks === 0n) return text

  // Seven digits with trailing zeros trimmed, which is how .NET renders a
  // fractional second.
  return `${text}.${subTicks.toString().padStart(7, '0').replace(/0+$/, '')}`
}

/**
 * Formats .NET ticks as the duration text the JSON export produces:
 * `[-][d.]hh:mm:ss[.fffffff]`.
 *
 * Exported for the same reason as the date one above.
 */
export function formatTimeSpanTicks(ticks: bigint): string {
  const negative = ticks < 0n
  let remaining = negative ? -ticks : ticks

  const days = remaining / TICKS_PER_DAY
  remaining %= TICKS_PER_DAY

  const hours = remaining / TICKS_PER_HOUR
  remaining %= TICKS_PER_HOUR

  const minutes = remaining / TICKS_PER_MINUTE
  remaining %= TICKS_PER_MINUTE

  const seconds = remaining / TICKS_PER_SECOND
  const subTicks = remaining % TICKS_PER_SECOND

  let text = `${pad(Number(hours), 2)}:${pad(Number(minutes), 2)}:${pad(Number(seconds), 2)}`

  // Days and the fraction are both omitted when zero, as .NET does.
  if (days !== 0n) text = `${days}.${text}`
  if (subTicks !== 0n) text += `.${subTicks.toString().padStart(7, '0').replace(/0+$/, '')}`

  return negative ? `-${text}` : text
}

/**
 * Formats sixteen bytes in .NET Guid layout as canonical text.
 *
 * That layout is not plain big-endian: the first three components are little
 * endian and the trailing eight bytes are not, which is what the index order
 * below accounts for.
 */
function formatUuid(bytes: Uint8Array): string {
  const order = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15]

  let out = ''

  for (let i = 0; i < 16; i++) {
    if (i === 4 || i === 6 || i === 8 || i === 10) out += '-'
    out += bytes[order[i]].toString(16).padStart(2, '0')
  }

  return out
}
