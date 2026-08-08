/* ---------------------------------------------------------------------------
 * SheetMan Scb reader for C99.
 *
 * Reads the .scb files produced by SheetMan's binary exporter. The format is
 * defined by the C# writer in src/Exporters/ScbWriter.cs, and this is a
 * deliberate re-implementation of the reading half of it:
 *
 *   fixed8      one byte
 *   fixed32     four bytes, little endian
 *   fixed64     eight bytes, little endian
 *   varint32    seven bits per byte, high bit set while more bytes follow,
 *               at most five bytes
 *   counter32   zig-zag encoded int32 written as a varint32, so small values
 *               of either sign cost one byte
 *   string      counter32 byte length, then that many UTF-8 bytes
 *   int32/uint32   fixed32
 *   int64          fixed64
 *   bool           fixed8, zero meaning false
 *   float/double   fixed32 / fixed64 holding the IEEE-754 bit pattern
 *   datetime       fixed64 of .NET ticks: 100 ns units since 0001-01-01
 *   timespan       fixed64 of .NET ticks
 *   uuid           sixteen bytes in .NET Guid layout
 *
 * Two things this has to answer that the other readers do not.
 *
 * Who owns the strings. Every table owns one arena; the records point into it
 * and a table is freed in one call. The alternative - a malloc per string and
 * a matching free somewhere - is how a generated API becomes a leak nobody can
 * find. The arena is a chain of blocks that are never reallocated, so a pointer
 * handed out stays valid until the whole table goes.
 *
 * How failure is reported. C has nothing to throw, so a read returns false and
 * the reader remembers why. Failure is sticky: the first read that runs out of
 * data records the reason and every read after it does nothing, which is what
 * lets generated code read a record's twenty fields in a row and ask once.
 *
 * Header only. Define SHEETMAN_SCB_IMPLEMENTATION in exactly one
 * translation unit before including it to get the function bodies; the
 * generated .c file does that for you.
 * ---------------------------------------------------------------------------
 */

#ifndef SHEETMAN_SCB_READER_H
#define SHEETMAN_SCB_READER_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Version stamped at the head of every table file by the exporter.
 *
 * The format is column-oriented and self-describing: the header names every column
 * and how long its block is, and a reader that meets a version it does not know
 * stops rather than guessing. 102 replaced 101 outright - a descriptor gained its
 * encoding byte - before any 101 file had shipped. */
#define SHEETMAN_BINARY_FILE_FORMAT_VERSION 102u

/* The wire element types and kinds, as a column descriptor spells them. */
#define SM_ELEMENT_VARINT 0
#define SM_ELEMENT_BOOL 1
#define SM_ELEMENT_I32 2
#define SM_ELEMENT_I64 3
#define SM_ELEMENT_F32 4
#define SM_ELEMENT_F64 5
#define SM_ELEMENT_STRING 6
#define SM_ELEMENT_UUID 7

#define SM_KIND_SCALAR 0
#define SM_KIND_FIXED_ARRAY 1
#define SM_KIND_VAR_ARRAY 2

/* How a block's values are laid out. Raw is the layout 101 had; the others compress
 * a column that repeats itself. spec/scb-v102-column-encoding.md is the contract. */
#define SM_ENCODING_RAW 0
#define SM_ENCODING_VARINT 1
#define SM_ENCODING_DELTA 2
#define SM_ENCODING_RLE 3
#define SM_ENCODING_DELTA_RLE 4
#define SM_ENCODING_DICT 5
#define SM_ENCODING_DICT_RLE 6
#define SM_ENCODING_DICT_FRONT 7
#define SM_ENCODING_DICT_FRONT_RLE 8

/* One element type as a bit, so the set a member accepts is one integer argument.
 * A set rather than an array because the generated code has to spell it inline, and
 * C89 has no array literal to spell it with. */
#define SM_ELEMENT_MASK(element) (1u << (element))

/* One column as the file describes it. */
typedef struct sm_column {
  /* What identifies the column, instead of its position. */
  int32_t tag;
  uint8_t element;
  uint8_t kind;
  /* How the block's values are laid out: one of the SM_ENCODING_* constants. */
  uint8_t encoding;
  /* Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one. */
  int32_t count;
  /* Total bytes of the column block - what a skip advances by. */
  int32_t byte_length;
} sm_column;

/* Longest message the reader will keep. Truncated rather than allocated: a
 * reader that has just run out of memory is a poor time to ask for more. */
#define SHEETMAN_ERROR_MAX 256

/* A 128 bit identifier, in .NET Guid byte order.
 *
 * That order is not plain big-endian: the first three components are little
 * endian and the trailing eight bytes are not, which is what sm_uuid_to_string
 * has to account for. */
typedef struct sm_uuid {
  uint8_t bytes[16];
} sm_uuid;

/* Renders a uuid in the 8-4-4-4-12 form, matching .NET's Guid.ToString("D").
 * `out` must have room for 37 characters, the terminator included. */
void sm_uuid_to_string(const sm_uuid* value, char out[37]);

/* A block of memory a table owns.
 *
 * Blocks are never reallocated, so every pointer handed out of an arena stays
 * valid until sm_arena_free. That is the property the generated records depend
 * on: they hold interior pointers and nothing fixes them up. */
typedef struct sm_block {
  struct sm_block* next;
  size_t used;
  size_t capacity;
  unsigned char* bytes;
} sm_block;

typedef struct sm_arena {
  sm_block* head;
} sm_arena;

/* Hands back zeroed, aligned storage, or NULL when the allocator refuses. */
void* sm_arena_alloc(sm_arena* arena, size_t size);

/* Releases every block. Safe on a zeroed arena, and safe to call twice. */
void sm_arena_free(sm_arena* arena);

/* Sequential reader over a table file's bytes.
 *
 * Non-owning: the buffer has to outlive the reader. Strings are copied into the
 * arena, so the buffer may be released once a table is loaded. */
typedef struct sm_reader {
  const uint8_t* data;
  int32_t length;
  int32_t position;
  bool failed;
  char error[SHEETMAN_ERROR_MAX];

  /* Where strings and arrays are copied to. May be NULL for a reader that
   * only reads scalars. */
  sm_arena* arena;
} sm_reader;

void sm_reader_init(sm_reader* reader, const uint8_t* data, int32_t length, sm_arena* arena);

/* True once any read has run out of data or found the file malformed. */
bool sm_failed(const sm_reader* reader);

/* Why the first failure happened, or "" while nothing has gone wrong. */
const char* sm_error(const sm_reader* reader);

bool sm_read_bool(sm_reader* reader, bool* out);
bool sm_read_int32(sm_reader* reader, int32_t* out);
bool sm_read_uint32(sm_reader* reader, uint32_t* out);
bool sm_read_int64(sm_reader* reader, int64_t* out);
bool sm_read_float(sm_reader* reader, float* out);
bool sm_read_double(sm_reader* reader, double* out);
bool sm_read_uuid(sm_reader* reader, sm_uuid* out);

/* Ticks, for both. Not a time type: a sheet reaches 0001-01-01 and TimeSpan's
 * full range, and C has nothing that holds either without loss. */
bool sm_read_datetime(sm_reader* reader, int64_t* out_ticks);
bool sm_read_timespan(sm_reader* reader, int64_t* out_ticks);

/* Copies UTF-8 bytes into the arena and hands back a terminated pointer.
 *
 * A value holding an embedded NUL is refused rather than truncated. C has no
 * way to carry one in a `const char*`, and half a string returned as the whole
 * of it is the kind of failure this format's readers exist to avoid. */
bool sm_read_string(sm_reader* reader, const char** out);

/* The zig-zag encoded count in front of a variable length array. */
bool sm_read_counter32(sm_reader* reader, int32_t* out);

/* An enum, which travels as its underlying zig-zag encoded int32. */
bool sm_read_enum(sm_reader* reader, int32_t* out);

/* Reads and checks the file header, handing back the row count that follows.
 *
 * The reserved byte is written as zero and is where compression or encryption
 * flags would go; a non-zero value means the file needs handling this build
 * does not have. */
/* Reads and checks a table file's header. The descriptors are allocated from the
 * reader's arena; *out_columns is left NULL when the table has none. */
bool sm_read_table_header(sm_reader* reader, int32_t* out_row_count,
             sm_column** out_columns, int32_t* out_column_count);

/* Advances past bytes without interpreting them: an unknown column's whole block.
 * The column-oriented layout is what makes this one call the entirety of skipping. */
bool sm_skip(sm_reader* reader, int32_t byte_count);

/* Promotions: a member reading a file element narrower than itself. Only the
 * mathematically lossless directions exist; sm_check_column already refused the rest. */
bool sm_read_i32_as(sm_reader* reader, uint8_t element, int32_t* out);
bool sm_read_i64_as(sm_reader* reader, uint8_t element, int64_t* out);
bool sm_read_f64_as(sm_reader* reader, uint8_t element, double* out);

/* That a column is what the generated member expects, or a lossless promotion of it.
 * Refusal is by name and both types, never by reading anyway. `accepted` is the set the
 * member can read, built out of SM_ELEMENT_MASK. */
bool sm_check_column(sm_reader* reader, const sm_column* column, const char* field_name,
          uint8_t kind, int32_t count, unsigned accepted);

/* That a block was consumed exactly: a mismatch is a format disagreement, and stopping
 * here names the column instead of corrupting the next. */
bool sm_check_block_end(sm_reader* reader, const sm_column* column, int32_t expected_end);

/* Reads one scalar column's values in row order, whatever the block's encoding.
 *
 * The generated row loop stays a row loop; this is the one place that knows how a
 * delta accumulates, how long a run has left, or that a dictionary index is a
 * reference into strings decoded once. That last one matters beyond file size: a
 * hundred-thousand-row column with three distinct strings copies three strings into
 * the arena, not a hundred thousand.
 *
 * sm_check_column has already refused any (element, encoding) pair the spec does not
 * define, so the functions here do not re-litigate that. Sticky like every read: once
 * the reader has failed, every next does nothing and returns false. */
typedef struct sm_cursor {
  sm_reader* reader;
  const char* field_name;
  uint8_t element;
  uint8_t encoding;

  /* The block's dictionary, decoded into the arena once and handed out per row.
   *
   * Which of the two the block filled is decided by its element: a string is
   * decoded to one copy in the arena that every row holding it points at - and a
   * front coded dictionary is decoded here too, because the folding was only ever
   * about the bytes on disk. */
  const char** dictionary;
  int32_t dictionary_count;

  /* A fixed-width element keeps its entries as the raw bytes they were written
   * as, and a row turns one into a value only when it asks for it - so the value
   * is reconstructed exactly as the raw layout would have read it.
   *
   * `value_width` is non-zero for exactly the blocks that have one, which is what
   * the next functions test: a dictionary of no entries is still a dictionary. */
  const uint8_t* value_dictionary;
  int32_t value_width;
  int32_t value_count;

  /* A run-length family's current run: what remains of it, and its value - which
   * is a plain value for RLE, a delta for DELTA_RLE, an index for DICT_RLE. */
  int32_t run_remaining;
  int32_t run_value;

  /* The delta family's accumulator, once started. */
  int32_t previous;
  bool started;

  /* Rows not yet handed out. A run that claims more than this is corrupt, and
   * catching it here names the field instead of leaving it to the block-end check. */
  int32_t rows_remaining;
} sm_cursor;

/* Opens a cursor over one column's block, right after sm_check_column. A DICT family
 * block decodes its dictionary here, once. `field_name` is kept for error messages
 * and has to outlive the cursor - generated code passes a literal. */
bool sm_cursor_init(sm_cursor* cursor, sm_reader* reader, const sm_column* column,
          int32_t row_count, const char* field_name);

/* The next int32 - which also serves enums, and reference indexes. */
bool sm_cursor_next_i32(sm_cursor* cursor, int32_t* out);

/* An int64 member: an i64 column raw or through its dictionary, and anything
 * narrower by decoding an int32 and widening it. Ticks read through this one, so a
 * datetime or a timespan column meets the i64 dictionary like any other. */
bool sm_cursor_next_i64(sm_cursor* cursor, int64_t* out);

/* A float member: raw, or the dictionary entry's exact bit pattern. */
bool sm_cursor_next_f32(sm_cursor* cursor, float* out);

/* A double member: from f64 or f32 - either of them raw or dictionary-encoded -
 * and from an i32 column by decoding and widening. */
bool sm_cursor_next_f64(sm_cursor* cursor, double* out);

/* A bool member: one byte raw, or a run of them. */
bool sm_cursor_next_bool(sm_cursor* cursor, bool* out);

/* The next string - the dictionary's copy where the block has one, so rows that
 * repeat a value share one pointer into the arena. */
bool sm_cursor_next_string(sm_cursor* cursor, const char** out);

/* Reads a whole file. The caller frees the buffer with sm_free_bytes. */
bool sm_read_all_bytes(const char* filename, uint8_t** out_data, int32_t* out_length);

void sm_free_bytes(uint8_t* data);

/* How much to reserve up front for a count that came off the wire.
 *
 * A corrupt count of two billion would otherwise be an immediate allocation of
 * that many elements, which fails long before the reader notices the file is
 * short. */
int32_t sm_reserve_bound(int32_t count);

/* Whether a row count can possibly be honest.
 *
 * `min_row_bytes` is what one row costs at its very smallest, which the
 * generator knows because every field encodes to at least one byte. A count
 * larger than the bytes left could not have been written by the exporter, and
 * believing it means allocating for rows that are not there.
 *
 * Checked before the allocation rather than discovered during it. */
bool sm_row_count_is_plausible(const sm_reader* reader, int32_t row_count, int32_t min_row_bytes);

/* One row's key, and where the row is.
 *
 * C has no map, so a table keeps these sorted and looks a key up by bisection.
 * A linear scan would be simpler and would turn every lookup into a walk of the
 * whole table.
 *
 * Four families rather than one, because a table indexes whatever column the
 * sheet marked and those columns are not all int32_t. Which one a generated
 * table uses is decided at generation time, from the field's own type:
 *
 *   sm_index_entry         int, enum, bool
 *   sm_index64_entry       bigint, datetime, timespan
 *   sm_string_index_entry  string
 *   sm_uuid_index_entry    uuid
 *
 * They are four copies of the same twenty lines. A macro would fold them into
 * one, at the price of a generated table declaring its index through an
 * expansion nobody can step into - and these are the four the format can
 * produce, not an open set. */
typedef struct sm_index_entry {
  int32_t key;
  int32_t position;
} sm_index_entry;

void sm_index_sort(sm_index_entry* entries, int32_t count);

/* The row's position, or -1 when the table holds no such key. */
int32_t sm_index_find(const sm_index_entry* entries, int32_t count, int32_t key);

typedef struct sm_index64_entry {
  int64_t key;
  int32_t position;
} sm_index64_entry;

void sm_index64_sort(sm_index64_entry* entries, int32_t count);

/* The row's position, or -1 when the table holds no such key. */
int32_t sm_index64_find(const sm_index64_entry* entries, int32_t count, int64_t key);

/* The key points into the table's arena, which owns it for as long as the table
 * does - so the entry borrows rather than copies. */
typedef struct sm_string_index_entry {
  const char* key;
  int32_t position;
} sm_string_index_entry;

void sm_string_index_sort(sm_string_index_entry* entries, int32_t count);

/* The row's position, or -1 when the table holds no such key. */
int32_t sm_string_index_find(const sm_string_index_entry* entries, int32_t count,
                             const char* key);

typedef struct sm_uuid_index_entry {
  sm_uuid key;
  int32_t position;
} sm_uuid_index_entry;

void sm_uuid_index_sort(sm_uuid_index_entry* entries, int32_t count);

/* The row's position, or -1 when the table holds no such key. */
int32_t sm_uuid_index_find(const sm_uuid_index_entry* entries, int32_t count,
                           sm_uuid key);

/* Marks the reader failed with a message of the caller's own.
 *
 * For the generated code, which allocates and so can fail where the reader by
 * itself cannot. Always returns false, so it reads as `return sm_fail_with(...)`.
 * Sticky like every other failure: an earlier reason is kept. */
bool sm_fail_with(sm_reader* reader, const char* message);

/* Writes "context: message" into a caller's buffer, truncating rather than
 * allocating. Does nothing when there is no buffer, so a caller that does not
 * want the detail passes NULL. */
void sm_copy_error(char* error, size_t error_size, const char* context, const char* message);

#ifdef __cplusplus
}
#endif

/* ------------------------------------------------------------ implementation */

#ifdef SHEETMAN_SCB_IMPLEMENTATION

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef __cplusplus
extern "C" {
#endif

#define SHEETMAN_ARENA_MIN_BLOCK 4096

void sm_uuid_to_string(const sm_uuid* value, char out[37]) {
  static const char hex[] = "0123456789abcdef";

  /* Component order matching .NET's Guid.ToString("D"): the first three
   * groups are little endian, the last eight bytes are in order. */
  static const int order[16] = { 3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15 };

  int at = 0;
  int i;

  for (i = 0; i < 16; ++i) {
    uint8_t b;

    if (i == 4 || i == 6 || i == 8 || i == 10)
      out[at++] = '-';

    b = value->bytes[order[i]];
    out[at++] = hex[b >> 4];
    out[at++] = hex[b & 0x0F];
  }

  out[at] = '\0';
}

void* sm_arena_alloc(sm_arena* arena, size_t size) {
  sm_block* block;
  size_t capacity;
  void* result;

  /* Everything the generated code stores is at most eight bytes wide, so
   * rounding to eight satisfies all of it without needing max_align_t. */
  size = (size + 7u) & ~(size_t)7u;

  if (size == 0)
    size = 8;

  block = arena->head;

  if (block == NULL || block->capacity - block->used < size) {
    capacity = size > SHEETMAN_ARENA_MIN_BLOCK ? size : SHEETMAN_ARENA_MIN_BLOCK;

    block = (sm_block*)malloc(sizeof(sm_block));
    if (block == NULL)
      return NULL;

    block->bytes = (unsigned char*)calloc(1, capacity);
    if (block->bytes == NULL) {
      free(block);
      return NULL;
    }

    block->next = arena->head;
    block->used = 0;
    block->capacity = capacity;

    arena->head = block;
  }

  result = block->bytes + block->used;
  block->used += size;

  return result;
}

void sm_arena_free(sm_arena* arena) {
  sm_block* block = arena->head;

  while (block != NULL) {
    sm_block* next = block->next;

    free(block->bytes);
    free(block);

    block = next;
  }

  arena->head = NULL;
}

void sm_reader_init(sm_reader* reader, const uint8_t* data, int32_t length, sm_arena* arena) {
  reader->data = data;
  reader->length = length;
  reader->position = 0;
  reader->failed = false;
  reader->error[0] = '\0';
  reader->arena = arena;
}

bool sm_failed(const sm_reader* reader) { return reader->failed; }

const char* sm_error(const sm_reader* reader) { return reader->error; }

/* The first failure is the informative one; everything after it is a
 * consequence of reading past the end. */
static bool sm_fail(sm_reader* reader, const char* format, ...) {
  if (!reader->failed) {
    va_list args;

    reader->failed = true;

    va_start(args, format);
    vsnprintf(reader->error, sizeof reader->error, format, args);
    va_end(args);
  }

  return false;
}

static bool sm_require(sm_reader* reader, int32_t count) {
  if (reader->failed)
    return false;

  if (count < 0 || reader->length - reader->position < count) {
    return sm_fail(reader,
           "table data ended after %d of %d bytes while %d more were expected",
           reader->position, reader->length, count);
  }

  return true;
}

static bool sm_read_fixed8(sm_reader* reader, uint8_t* out) {
  if (!sm_require(reader, 1))
    return false;

  *out = reader->data[reader->position++];
  return true;
}

/* The two fixed widths, assembled from bytes rather than copied over the host's
 * own layout: the file is little endian wherever it is read. Split from the reads
 * below because a dictionary entry is those same bytes sitting in the arena. */
static uint32_t sm_load_fixed32(const uint8_t* at) {
  return (uint32_t)at[0]
    | (uint32_t)at[1] << 8
    | (uint32_t)at[2] << 16
    | (uint32_t)at[3] << 24;
}

static uint64_t sm_load_fixed64(const uint8_t* at) {
  uint64_t value = 0;
  int i;

  for (i = 0; i < 8; ++i)
    value |= (uint64_t)at[i] << (8 * i);

  return value;
}

static bool sm_read_fixed32(sm_reader* reader, uint32_t* out) {
  if (!sm_require(reader, 4))
    return false;

  *out = sm_load_fixed32(reader->data + reader->position);

  reader->position += 4;
  return true;
}

static bool sm_read_fixed64(sm_reader* reader, uint64_t* out) {
  if (!sm_require(reader, 8))
    return false;

  *out = sm_load_fixed64(reader->data + reader->position);

  reader->position += 8;
  return true;
}

static bool sm_read_varint32(sm_reader* reader, uint32_t* out) {
  uint32_t value = 0;
  int shift;

  for (shift = 0; shift < 35; shift += 7) {
    uint8_t byte;

    if (!sm_read_fixed8(reader, &byte))
      return false;

    value |= (uint32_t)(byte & 0x7F) << shift;

    if ((byte & 0x80) == 0) {
      *out = value;
      return true;
    }
  }

  return sm_fail(reader, "varint32 is longer than five bytes");
}

bool sm_read_counter32(sm_reader* reader, int32_t* out) {
  uint32_t encoded;

  if (!sm_read_varint32(reader, &encoded))
    return false;

  /* Zig-zag: the sign lives in the low bit, so small negatives cost as little
   * as small positives. The cast through uint32_t keeps the negation defined. */
  *out = (int32_t)(encoded >> 1) ^ -(int32_t)(encoded & 1u);
  return true;
}

bool sm_read_enum(sm_reader* reader, int32_t* out) { return sm_read_counter32(reader, out); }

bool sm_read_bool(sm_reader* reader, bool* out) {
  uint8_t byte;

  if (!sm_read_fixed8(reader, &byte))
    return false;

  *out = byte != 0;
  return true;
}

bool sm_read_int32(sm_reader* reader, int32_t* out) {
  uint32_t bits;

  if (!sm_read_fixed32(reader, &bits))
    return false;

  *out = (int32_t)bits;
  return true;
}

bool sm_read_uint32(sm_reader* reader, uint32_t* out) { return sm_read_fixed32(reader, out); }

bool sm_read_int64(sm_reader* reader, int64_t* out) {
  uint64_t bits;

  if (!sm_read_fixed64(reader, &bits))
    return false;

  *out = (int64_t)bits;
  return true;
}

bool sm_read_float(sm_reader* reader, float* out) {
  uint32_t bits;

  if (!sm_read_fixed32(reader, &bits))
    return false;

  memcpy(out, &bits, sizeof *out);
  return true;
}

bool sm_read_double(sm_reader* reader, double* out) {
  uint64_t bits;

  if (!sm_read_fixed64(reader, &bits))
    return false;

  memcpy(out, &bits, sizeof *out);
  return true;
}

bool sm_read_datetime(sm_reader* reader, int64_t* out_ticks) {
  return sm_read_int64(reader, out_ticks);
}

bool sm_read_timespan(sm_reader* reader, int64_t* out_ticks) {
  return sm_read_int64(reader, out_ticks);
}

bool sm_read_uuid(sm_reader* reader, sm_uuid* out) {
  if (!sm_require(reader, 16))
    return false;

  memcpy(out->bytes, reader->data + reader->position, 16);
  reader->position += 16;
  return true;
}

bool sm_read_string(sm_reader* reader, const char** out) {
  int32_t length;
  char* copy;

  if (!sm_read_counter32(reader, &length))
    return false;

  if (length < 0)
    return sm_fail(reader, "string length %d is negative", length);

  if (!sm_require(reader, length))
    return false;

  if (memchr(reader->data + reader->position, 0, (size_t)length) != NULL) {
    return sm_fail(reader,
           "a string holds a NUL byte, which cannot be carried in a C string");
  }

  if (reader->arena == NULL)
    return sm_fail(reader, "a string was read through a reader with no arena");

  copy = (char*)sm_arena_alloc(reader->arena, (size_t)length + 1);
  if (copy == NULL)
    return sm_fail(reader, "out of memory reading a string of %d bytes", length);

  memcpy(copy, reader->data + reader->position, (size_t)length);
  copy[length] = '\0';

  reader->position += length;
  *out = copy;
  return true;
}

bool sm_read_table_header(sm_reader* reader, int32_t* out_row_count,
             sm_column** out_columns, int32_t* out_column_count) {
  uint32_t version;
  uint8_t reserved;
  int32_t column_count;
  int32_t at;
  sm_column* columns;

  *out_row_count = 0;
  *out_columns = NULL;
  *out_column_count = 0;

  if (!sm_read_fixed32(reader, &version))
    return false;

  if (version != SHEETMAN_BINARY_FILE_FORMAT_VERSION) {
    return sm_fail(reader, "table format version %u is not supported (expected %u)",
           (unsigned)version, (unsigned)SHEETMAN_BINARY_FILE_FORMAT_VERSION);
  }

  if (!sm_read_fixed8(reader, &reserved))
    return false;

  if (reserved != 0)
    return sm_fail(reader, "table declares unsupported features");

  if (!sm_read_counter32(reader, out_row_count))
    return false;

  if (*out_row_count < 0) {
    int32_t bad = *out_row_count;

    *out_row_count = 0;
    return sm_fail(reader, "table row count %d is negative", bad);
  }

  if (!sm_read_counter32(reader, &column_count))
    return false;

  if (column_count < 0)
    return sm_fail(reader, "table column count %d is negative", column_count);

  if (column_count == 0)
    return true;

  columns = (sm_column*)sm_arena_alloc(reader->arena, (size_t)column_count * sizeof *columns);
  if (columns == NULL)
    return sm_fail(reader, "out of memory allocating the column descriptors");

  for (at = 0; at < column_count; ++at) {
    uint8_t wire = 0;
    uint8_t encoding = 0;
    uint32_t byte_length = 0;

    (void)sm_read_counter32(reader, &columns[at].tag);
    (void)sm_read_fixed8(reader, &wire);
    (void)sm_read_fixed8(reader, &encoding);
    (void)sm_read_counter32(reader, &columns[at].count);
    (void)sm_read_fixed32(reader, &byte_length);

    columns[at].element = (uint8_t)(wire & 0x0f);
    columns[at].kind = (uint8_t)((wire >> 4) & 0x03);
    columns[at].encoding = encoding;
    columns[at].byte_length = (int32_t)byte_length;
  }

  if (sm_failed(reader))
    return false;

  /* What the descriptors themselves say about the file, checked before the generated
   * code allocates for the row count. The blocks are all that follows the header, so
   * their declared lengths have to add up to the bytes left. A raw block also costs
   * at least one byte per row - a varint's shortest form, an empty string's length
   * prefix, a variable array's counter - so a larger row count is one the exporter
   * could not have written. An encoded block has no such floor; its decode checks
   * run sums and dictionary bounds instead. */
  {
    int32_t remaining = reader->length - reader->position;
    int32_t declared = 0;

    for (at = 0; at < column_count; ++at) {
      if (columns[at].byte_length < 0 || columns[at].byte_length > remaining - declared) {
        return sm_fail(reader,
               "column tag %d declares %d bytes, which the file cannot hold",
               columns[at].tag, columns[at].byte_length);
      }

      declared += columns[at].byte_length;

      if (columns[at].encoding == SM_ENCODING_RAW
          && *out_row_count > columns[at].byte_length) {
        int32_t bad = *out_row_count;

        *out_row_count = 0;
        return sm_fail(reader,
               "the row count %d is larger than column tag %d can hold in "
               "its %d bytes", bad, columns[at].tag, columns[at].byte_length);
      }
    }

    if (declared != remaining) {
      return sm_fail(reader,
             "the columns declare %d bytes but %d follow the header",
             declared, remaining);
    }
  }

  *out_columns = columns;
  *out_column_count = column_count;

  return true;
}

bool sm_skip(sm_reader* reader, int32_t byte_count) {
  if (sm_failed(reader))
    return false;

  if (byte_count < 0 || byte_count > reader->length - reader->position)
    return sm_fail(reader, "cannot skip %d bytes with %d remaining",
           byte_count, reader->length - reader->position);

  reader->position += byte_count;

  return true;
}

bool sm_read_i32_as(sm_reader* reader, uint8_t element, int32_t* out) {
  if (element == SM_ELEMENT_I32)
    return sm_read_int32(reader, out);

  return sm_read_counter32(reader, out);
}

bool sm_read_i64_as(sm_reader* reader, uint8_t element, int64_t* out) {
  if (element == SM_ELEMENT_I64)
    return sm_read_int64(reader, out);

  {
    int32_t narrower = 0;
    bool ok = (element == SM_ELEMENT_I32)
      ? sm_read_int32(reader, &narrower)
      : sm_read_counter32(reader, &narrower);

    *out = narrower;
    return ok;
  }
}

bool sm_read_f64_as(sm_reader* reader, uint8_t element, double* out) {
  if (element == SM_ELEMENT_F64)
    return sm_read_double(reader, out);

  if (element == SM_ELEMENT_F32) {
    float single = 0.0f;
    bool ok = sm_read_float(reader, &single);

    *out = single;
    return ok;
  }

  {
    int32_t integer = 0;
    bool ok = sm_read_int32(reader, &integer);

    *out = integer;
    return ok;
  }
}

/* The element codes in a mask, as "2, 0", for a message that has to say what the
 * member would have taken. */
static void sm_describe_elements(unsigned accepted, char* out, size_t out_size) {
  size_t at = 0;
  int element;

  if (out_size == 0)
    return;

  for (element = 0; element < 16; ++element) {
    if ((accepted & SM_ELEMENT_MASK(element)) == 0)
      continue;

    if (at > 0 && at + 2 < out_size) {
      out[at++] = ',';
      out[at++] = ' ';
    }

    if (at + 1 < out_size)
      out[at++] = (char)('0' + element);
  }

  out[at] = '\0';
}

/* The (element, encoding) pairs the spec defines. Arrays are always raw; integers
 * take the integer encodings, strings the dictionary ones. */
static bool sm_encoding_supported(const sm_column* column) {
  if (column->encoding == SM_ENCODING_RAW)
    return true;

  if (column->kind != SM_KIND_SCALAR)
    return false;

  switch (column->element) {
  case SM_ELEMENT_BOOL:
  case SM_ELEMENT_VARINT:
    return column->encoding == SM_ENCODING_RLE;

  case SM_ELEMENT_I32:
    return column->encoding >= SM_ENCODING_VARINT
      && column->encoding <= SM_ENCODING_DELTA_RLE;

  /* The dictionary is parameterized by element, so these three reach it with
   * entries that are simply their own raw bytes. */
  case SM_ELEMENT_I64:
  case SM_ELEMENT_F32:
  case SM_ELEMENT_F64:
    return column->encoding == SM_ENCODING_DICT
      || column->encoding == SM_ENCODING_DICT_RLE;

  /* And a string dictionary can additionally be front coded, which is meaningless
   * for a fixed-width element and refused for one. */
  case SM_ELEMENT_STRING:
    return column->encoding >= SM_ENCODING_DICT
      && column->encoding <= SM_ENCODING_DICT_FRONT_RLE;

  default:
    return false;
  }
}

bool sm_check_column(sm_reader* reader, const sm_column* column, const char* field_name,
          uint8_t kind, int32_t count, unsigned accepted) {
  char elements[48];

  if (sm_failed(reader))
    return false;

  if (column->kind != kind || (kind != SM_KIND_VAR_ARRAY && column->count != count)) {
    return sm_fail(reader,
           "%s: the file column (kind %d, count %d) does not match the generated "
           "member (kind %d, count %d). The schema changed shape; regenerate the "
           "code or rebuild the data.",
           field_name, (int)column->kind, column->count, (int)kind, count);
  }

  /* An encoding this build cannot decode - or one the spec does not define for this
   * element - is refused by name, exactly like an element it cannot read. An unknown
   * column's encoding never gets here - a skip is a skip whatever the block's
   * layout. */
  if (!sm_encoding_supported(column)) {
    return sm_fail(reader,
           "%s: the file's column uses encoding %d, which this reader cannot decode "
           "for its element type. Regenerate the code or rebuild the data.",
           field_name, (int)column->encoding);
  }

  if ((accepted & SM_ELEMENT_MASK(column->element)) != 0)
    return true;

  sm_describe_elements(accepted, elements, sizeof elements);

  return sm_fail(reader,
         "%s: the file carries element type %d, which this member cannot read "
         "(accepts %s). The column changed type incompatibly; regenerate the code "
         "or rebuild the data.",
         field_name, (int)column->element, elements);
}

bool sm_check_block_end(sm_reader* reader, const sm_column* column, int32_t expected_end) {
  if (sm_failed(reader))
    return false;

  if (reader->position != expected_end) {
    return sm_fail(reader,
           "column tag %d: its block declared %d bytes but the read ended %d "
           "bytes short of its boundary",
           column->tag, column->byte_length, expected_end - reader->position);
  }

  return true;
}

/* The array of pointers a string dictionary hands out of, allocated once. */
static bool sm_cursor_alloc_dictionary(sm_cursor* cursor, int32_t count) {
  sm_reader* reader = cursor->reader;

  if (reader->arena == NULL)
    return sm_fail(reader, "a string was read through a reader with no arena");

  cursor->dictionary = (const char**)sm_arena_alloc(
    reader->arena, (size_t)count * sizeof *cursor->dictionary);

  if (cursor->dictionary == NULL)
    return sm_fail(reader, "%s: out of memory allocating the dictionary", cursor->field_name);

  return true;
}

/* A plain string dictionary: each entry is the value in its raw form, a length
 * and then its bytes. */
static bool sm_cursor_read_string_dictionary(sm_cursor* cursor, int32_t count) {
  int32_t at;

  if (!sm_cursor_alloc_dictionary(cursor, count))
    return false;

  for (at = 0; at < count; ++at) {
    if (!sm_read_string(cursor->reader, &cursor->dictionary[at]))
      return false;
  }

  cursor->dictionary_count = count;
  return true;
}

/* A sorted dictionary whose entries state only what they do not share with the
 * entry before them.
 *
 * Decoded into whole strings here rather than kept folded, because a row wants a
 * string and the folding was only ever about the bytes on disk. Each entry is built
 * straight into the arena out of the one before it - which is already sitting there,
 * terminated - so there is no scratch buffer to grow and free. */
static bool sm_cursor_read_front_dictionary(sm_cursor* cursor, int32_t count) {
  sm_reader* reader = cursor->reader;
  int32_t previous_length = 0;
  int32_t at;

  if (!sm_cursor_alloc_dictionary(cursor, count))
    return false;

  for (at = 0; at < count; ++at) {
    int32_t shared = 0;
    int32_t rest = 0;
    int32_t length;
    char* entry;

    if (!sm_read_counter32(reader, &shared) || !sm_read_counter32(reader, &rest))
      return false;

    if (shared < 0 || rest < 0 || shared > previous_length) {
      return sm_fail(reader,
             "%s: dictionary entry %d shares %d bytes with an entry of %d",
             cursor->field_name, at, shared, previous_length);
    }

    /* Before the addition, not only for the copy: it bounds `rest` by the bytes
     * left, and an entry is never longer than the dictionary's own bytes plus
     * what is left of the file - so the sum cannot leave int32_t. */
    if (!sm_require(reader, rest))
      return false;

    if (memchr(reader->data + reader->position, 0, (size_t)rest) != NULL) {
      return sm_fail(reader,
             "a string holds a NUL byte, which cannot be carried in a C string");
    }

    length = shared + rest;

    entry = (char*)sm_arena_alloc(reader->arena, (size_t)length + 1);
    if (entry == NULL) {
      return sm_fail(reader, "%s: out of memory decoding a dictionary entry of %d bytes",
             cursor->field_name, length);
    }

    /* The shared bytes come from the entry before it, which is why a `shared`
     * larger than that entry is refused rather than clamped. */
    if (shared > 0)
      memcpy(entry, cursor->dictionary[at - 1], (size_t)shared);

    if (rest > 0)
      memcpy(entry + shared, reader->data + reader->position, (size_t)rest);

    entry[length] = '\0';
    reader->position += rest;

    cursor->dictionary[at] = entry;
    previous_length = length;
  }

  cursor->dictionary_count = count;
  return true;
}

/* A fixed-width element: the entries are the value's own bytes, so they are kept as
 * bytes and turned into values only when a row asks for one. */
static bool sm_cursor_read_value_dictionary(sm_cursor* cursor, int32_t count) {
  sm_reader* reader = cursor->reader;
  int32_t width = cursor->element == SM_ELEMENT_F32 ? 4 : 8;
  int32_t bytes = 0;
  uint8_t* copy;

  cursor->value_width = width;
  cursor->value_count = count;

  if (count == 0)
    return true;

  /* The division rather than a multiplication, which would overflow for exactly
   * the corrupt count this is here to catch. */
  if (count > (reader->length - reader->position) / width) {
    return sm_fail(reader,
           "%s: a dictionary of %d entries is larger than the file can hold",
           cursor->field_name, count);
  }

  bytes = count * width;

  if (reader->arena == NULL)
    return sm_fail(reader, "a dictionary was read through a reader with no arena");

  copy = (uint8_t*)sm_arena_alloc(reader->arena, (size_t)bytes);
  if (copy == NULL)
    return sm_fail(reader, "%s: out of memory allocating the dictionary", cursor->field_name);

  memcpy(copy, reader->data + reader->position, (size_t)bytes);
  reader->position += bytes;

  cursor->value_dictionary = copy;
  return true;
}

bool sm_cursor_init(sm_cursor* cursor, sm_reader* reader, const sm_column* column,
          int32_t row_count, const char* field_name) {
  bool plain_dictionary;
  bool front_dictionary;

  cursor->reader = reader;
  cursor->field_name = field_name;
  cursor->element = column->element;
  cursor->encoding = column->encoding;
  cursor->dictionary = NULL;
  cursor->dictionary_count = 0;
  cursor->value_dictionary = NULL;
  cursor->value_width = 0;
  cursor->value_count = 0;
  cursor->run_remaining = 0;
  cursor->run_value = 0;
  cursor->previous = 0;
  cursor->started = false;
  cursor->rows_remaining = row_count;

  if (sm_failed(reader))
    return false;

  plain_dictionary = cursor->encoding == SM_ENCODING_DICT
    || cursor->encoding == SM_ENCODING_DICT_RLE;

  front_dictionary = cursor->encoding == SM_ENCODING_DICT_FRONT
    || cursor->encoding == SM_ENCODING_DICT_FRONT_RLE;

  if (!plain_dictionary && !front_dictionary)
    return true;

  {
    int32_t count = 0;

    if (!sm_read_counter32(reader, &count))
      return false;

    if (count < 0)
      return sm_fail(reader, "%s: the dictionary entry count is negative", field_name);

    /* Every entry costs at least one byte on the wire - a string's length prefix,
     * a front coded entry's two counters, a fixed-width value's own bytes - so a
     * count the bytes left cannot cover is one the exporter could not have
     * written. Checked here because the allocation comes before the reads that
     * would catch it. */
    if (count > reader->length - reader->position) {
      return sm_fail(reader,
             "%s: a dictionary of %d entries is larger than the file can hold",
             field_name, count);
    }

    if (front_dictionary)
      return count == 0 ? true : sm_cursor_read_front_dictionary(cursor, count);

    /* A fixed-width element's dictionary is bytes, and it stays a dictionary at
     * no entries at all - which is why the width, not the pointer, is what says
     * the block has one. */
    if (cursor->element != SM_ELEMENT_STRING)
      return sm_cursor_read_value_dictionary(cursor, count);

    return count == 0 ? true : sm_cursor_read_string_dictionary(cursor, count);
  }
}

/* The next run of a run-length family: its length, checked against the rows the
 * column has left, then its value. */
static bool sm_cursor_read_run(sm_cursor* cursor) {
  int32_t length = 0;

  if (!sm_read_counter32(cursor->reader, &length))
    return false;

  /* + 1 because the row this run was read for is already counted out of
   * rows_remaining by its next call. */
  if (length < 1 || length > cursor->rows_remaining + 1) {
    return sm_fail(cursor->reader,
           "%s: a run of %d values cannot cover the %d rows left in the column",
           cursor->field_name, length, cursor->rows_remaining + 1);
  }

  cursor->run_remaining = length;

  return sm_read_counter32(cursor->reader, &cursor->run_value);
}

static bool sm_cursor_dictionary_entry(const sm_cursor* cursor, int32_t index,
             const char** out) {
  if (index < 0 || index >= cursor->dictionary_count) {
    return sm_fail(cursor->reader,
           "%s: dictionary index %d is out of range - the dictionary holds %d entries",
           cursor->field_name, index, cursor->dictionary_count);
  }

  *out = cursor->dictionary[index];
  return true;
}

/* The bytes of the next row's dictionary entry, for a fixed-width element.
 *
 * The one place a value-dictionary row is counted out, so every member reading
 * through it - i64, f32, f64 - decrements exactly once whichever way it came. */
static bool sm_cursor_next_value_entry(sm_cursor* cursor, const uint8_t** out) {
  int32_t index = 0;

  cursor->rows_remaining--;

  if (cursor->encoding == SM_ENCODING_DICT) {
    if (!sm_read_counter32(cursor->reader, &index))
      return false;
  } else {
    if (cursor->run_remaining == 0 && !sm_cursor_read_run(cursor))
      return false;

    cursor->run_remaining--;
    index = cursor->run_value;
  }

  if (index < 0 || index >= cursor->value_count) {
    return sm_fail(cursor->reader,
           "%s: dictionary index %d is out of range - the dictionary holds %d entries",
           cursor->field_name, index, cursor->value_count);
  }

  *out = cursor->value_dictionary + (size_t)index * (size_t)cursor->value_width;
  return true;
}

bool sm_cursor_next_i32(sm_cursor* cursor, int32_t* out) {
  sm_reader* reader = cursor->reader;

  if (sm_failed(reader))
    return false;

  cursor->rows_remaining--;

  switch (cursor->encoding) {
  case SM_ENCODING_RAW:
    if (cursor->element == SM_ELEMENT_I32)
      return sm_read_int32(reader, out);

    return sm_read_counter32(reader, out);

  case SM_ENCODING_VARINT:
    return sm_read_counter32(reader, out);

  case SM_ENCODING_DELTA: {
    int32_t value = 0;

    if (!sm_read_counter32(reader, &value))
      return false;

    /* The addition wraps on purpose, mirroring the writer's wrapping subtraction;
     * together they are exact for every int32 pair. On uint32_t, because signed
     * overflow is undefined in C. */
    if (cursor->started) {
      cursor->previous = (int32_t)((uint32_t)cursor->previous + (uint32_t)value);
    } else {
      cursor->previous = value;
      cursor->started = true;
    }

    *out = cursor->previous;
    return true;
  }

  case SM_ENCODING_RLE:
    if (cursor->run_remaining == 0 && !sm_cursor_read_run(cursor))
      return false;

    cursor->run_remaining--;
    *out = cursor->run_value;
    return true;

  default: /* SM_ENCODING_DELTA_RLE; sm_check_column refused everything else. */
    if (!cursor->started) {
      if (!sm_read_counter32(reader, &cursor->previous))
        return false;

      cursor->started = true;
      *out = cursor->previous;
      return true;
    }

    if (cursor->run_remaining == 0 && !sm_cursor_read_run(cursor))
      return false;

    cursor->run_remaining--;
    cursor->previous = (int32_t)((uint32_t)cursor->previous + (uint32_t)cursor->run_value);
    *out = cursor->previous;
    return true;
  }
}

bool sm_cursor_next_i64(sm_cursor* cursor, int64_t* out) {
  if (cursor->element != SM_ELEMENT_I64) {
    int32_t narrower = 0;
    bool ok = sm_cursor_next_i32(cursor, &narrower);

    *out = narrower;
    return ok;
  }

  if (sm_failed(cursor->reader))
    return false;

  if (cursor->value_width != 0) {
    const uint8_t* entry = NULL;

    if (!sm_cursor_next_value_entry(cursor, &entry))
      return false;

    *out = (int64_t)sm_load_fixed64(entry);
    return true;
  }

  cursor->rows_remaining--;

  return sm_read_int64(cursor->reader, out);
}

bool sm_cursor_next_f32(sm_cursor* cursor, float* out) {
  if (sm_failed(cursor->reader))
    return false;

  if (cursor->value_width != 0) {
    const uint8_t* entry = NULL;
    uint32_t bits;

    if (!sm_cursor_next_value_entry(cursor, &entry))
      return false;

    /* Through memcpy, as the raw read is: the entry's bytes are the value's bit
     * pattern, and reading them as a float any other way is a type C does not
     * let one object have. */
    bits = sm_load_fixed32(entry);
    memcpy(out, &bits, sizeof *out);
    return true;
  }

  cursor->rows_remaining--;

  return sm_read_float(cursor->reader, out);
}

bool sm_cursor_next_f64(sm_cursor* cursor, double* out) {
  if (cursor->element == SM_ELEMENT_F32) {
    float single = 0.0f;
    bool ok = sm_cursor_next_f32(cursor, &single);

    *out = single;
    return ok;
  }

  if (cursor->element != SM_ELEMENT_F64) {
    int32_t integer = 0;
    bool ok = sm_cursor_next_i32(cursor, &integer);

    *out = integer;
    return ok;
  }

  if (sm_failed(cursor->reader))
    return false;

  if (cursor->value_width != 0) {
    const uint8_t* entry = NULL;
    uint64_t bits;

    if (!sm_cursor_next_value_entry(cursor, &entry))
      return false;

    bits = sm_load_fixed64(entry);
    memcpy(out, &bits, sizeof *out);
    return true;
  }

  cursor->rows_remaining--;

  return sm_read_double(cursor->reader, out);
}

bool sm_cursor_next_bool(sm_cursor* cursor, bool* out) {
  if (cursor->encoding == SM_ENCODING_RLE) {
    int32_t value = 0;
    bool ok = sm_cursor_next_i32(cursor, &value);

    *out = value != 0;
    return ok;
  }

  if (sm_failed(cursor->reader))
    return false;

  cursor->rows_remaining--;

  return sm_read_bool(cursor->reader, out);
}

bool sm_cursor_next_string(sm_cursor* cursor, const char** out) {
  sm_reader* reader = cursor->reader;

  if (sm_failed(reader))
    return false;

  cursor->rows_remaining--;

  switch (cursor->encoding) {
  case SM_ENCODING_RAW:
    return sm_read_string(reader, out);

  /* A front coded dictionary was decoded to whole strings at construction, so from
   * here it is the same dictionary as any other. */
  case SM_ENCODING_DICT:
  case SM_ENCODING_DICT_FRONT: {
    int32_t index = 0;

    if (!sm_read_counter32(reader, &index))
      return false;

    return sm_cursor_dictionary_entry(cursor, index, out);
  }

  default: /* SM_ENCODING_DICT_RLE and SM_ENCODING_DICT_FRONT_RLE */
    if (cursor->run_remaining == 0 && !sm_cursor_read_run(cursor))
      return false;

    cursor->run_remaining--;
    return sm_cursor_dictionary_entry(cursor, cursor->run_value, out);
  }
}

/* MSVC deprecates fopen and a project built with warnings as errors will not
 * take it. Branching here rather than defining _CRT_SECURE_NO_WARNINGS, which
 * a header has no business turning off for whoever includes it. */
static FILE* sm_fopen_read(const char* filename) {
#if defined(_MSC_VER)
  FILE* file = NULL;

  if (fopen_s(&file, filename, "rb") != 0)
    return NULL;

  return file;
#else
  return fopen(filename, "rb");
#endif
}

bool sm_read_all_bytes(const char* filename, uint8_t** out_data, int32_t* out_length) {
  FILE* file;
  long size;
  uint8_t* buffer;

  *out_data = NULL;
  *out_length = 0;

  file = sm_fopen_read(filename);
  if (file == NULL)
    return false;

  if (fseek(file, 0, SEEK_END) != 0) {
    fclose(file);
    return false;
  }

  size = ftell(file);

  if (size < 0 || size > 0x7FFFFFFF || fseek(file, 0, SEEK_SET) != 0) {
    fclose(file);
    return false;
  }

  /* One byte over, so a zero-length file still gets a non-NULL pointer and
   * the caller's "did the allocation work" check means what it says. */
  buffer = (uint8_t*)malloc((size_t)size + 1);
  if (buffer == NULL) {
    fclose(file);
    return false;
  }

  if (size > 0 && fread(buffer, 1, (size_t)size, file) != (size_t)size) {
    free(buffer);
    fclose(file);
    return false;
  }

  fclose(file);

  *out_data = buffer;
  *out_length = (int32_t)size;
  return true;
}

void sm_free_bytes(uint8_t* data) { free(data); }

int32_t sm_reserve_bound(int32_t count) {
  const int32_t max_up_front = 65536;

  if (count < 0)
    return 0;

  return count < max_up_front ? count : max_up_front;
}

bool sm_row_count_is_plausible(const sm_reader* reader, int32_t row_count, int32_t min_row_bytes) {
  int32_t remaining;

  if (row_count < 0)
    return false;

  if (min_row_bytes <= 0)
    return true;

  remaining = reader->length - reader->position;

  return row_count <= remaining / min_row_bytes;
}

bool sm_fail_with(sm_reader* reader, const char* message) {
  return sm_fail(reader, "%s", message);
}

void sm_copy_error(char* error, size_t error_size, const char* context, const char* message) {
  if (error == NULL || error_size == 0)
    return;

  if (message == NULL || message[0] == '\0')
    snprintf(error, error_size, "%s", context != NULL ? context : "");
  else if (context == NULL || context[0] == '\0')
    snprintf(error, error_size, "%s", message);
  else
    snprintf(error, error_size, "%s: %s", context, message);
}

static int sm_index_compare(const void* left, const void* right) {
  const int32_t a = ((const sm_index_entry*)left)->key;
  const int32_t b = ((const sm_index_entry*)right)->key;

  /* Not a - b: that overflows for keys at opposite ends of the range, and the
   * result feeds straight into qsort's ordering. */
  return a < b ? -1 : (a > b ? 1 : 0);
}

void sm_index_sort(sm_index_entry* entries, int32_t count) {
  if (entries != NULL && count > 1)
    qsort(entries, (size_t)count, sizeof *entries, sm_index_compare);
}

int32_t sm_index_find(const sm_index_entry* entries, int32_t count, int32_t key) {
  int32_t low = 0;
  int32_t high = count - 1;

  if (entries == NULL)
    return -1;

  while (low <= high) {
    /* low + (high - low) / 2 rather than (low + high) / 2, which overflows
     * once a table passes about a billion rows. */
    int32_t middle = low + (high - low) / 2;
    int32_t at = entries[middle].key;

    if (at == key)
      return entries[middle].position;

    if (at < key)
      low = middle + 1;
    else
      high = middle - 1;
  }

  return -1;
}

static int sm_index64_compare(const void* left, const void* right) {
  const int64_t a = ((const sm_index64_entry*)left)->key;
  const int64_t b = ((const sm_index64_entry*)right)->key;

  return a < b ? -1 : (a > b ? 1 : 0);
}

void sm_index64_sort(sm_index64_entry* entries, int32_t count) {
  if (entries != NULL && count > 1)
    qsort(entries, (size_t)count, sizeof *entries, sm_index64_compare);
}

int32_t sm_index64_find(const sm_index64_entry* entries, int32_t count, int64_t key) {
  int32_t low = 0;
  int32_t high = count - 1;

  if (entries == NULL)
    return -1;

  while (low <= high) {
    int32_t middle = low + (high - low) / 2;
    int64_t at = entries[middle].key;

    if (at == key)
      return entries[middle].position;

    if (at < key)
      low = middle + 1;
    else
      high = middle - 1;
  }

  return -1;
}

static int sm_string_index_compare(const void* left, const void* right) {
  const char* a = ((const sm_string_index_entry*)left)->key;
  const char* b = ((const sm_string_index_entry*)right)->key;

  /* Never NULL: a string member that the file carried no column for is set to
   * the empty literal before the read, which is what keeps this from being the
   * one comparison that has to check. */
  return strcmp(a, b);
}

void sm_string_index_sort(sm_string_index_entry* entries, int32_t count) {
  if (entries != NULL && count > 1)
    qsort(entries, (size_t)count, sizeof *entries, sm_string_index_compare);
}

int32_t sm_string_index_find(const sm_string_index_entry* entries, int32_t count,
                             const char* key) {
  int32_t low = 0;
  int32_t high = count - 1;

  if (entries == NULL || key == NULL)
    return -1;

  while (low <= high) {
    int32_t middle = low + (high - low) / 2;
    int at = strcmp(entries[middle].key, key);

    if (at == 0)
      return entries[middle].position;

    if (at < 0)
      low = middle + 1;
    else
      high = middle - 1;
  }

  return -1;
}

static int sm_uuid_index_compare(const void* left, const void* right) {
  const sm_uuid* a = &((const sm_uuid_index_entry*)left)->key;
  const sm_uuid* b = &((const sm_uuid_index_entry*)right)->key;

  /* Byte order, not .NET's display order. Nothing here shows the key to anyone;
   * it only has to be a total order, and the same one on every platform. */
  return memcmp(a->bytes, b->bytes, sizeof a->bytes);
}

void sm_uuid_index_sort(sm_uuid_index_entry* entries, int32_t count) {
  if (entries != NULL && count > 1)
    qsort(entries, (size_t)count, sizeof *entries, sm_uuid_index_compare);
}

int32_t sm_uuid_index_find(const sm_uuid_index_entry* entries, int32_t count,
                           sm_uuid key) {
  int32_t low = 0;
  int32_t high = count - 1;

  if (entries == NULL)
    return -1;

  while (low <= high) {
    int32_t middle = low + (high - low) / 2;
    int at = memcmp(entries[middle].key.bytes, key.bytes, sizeof key.bytes);

    if (at == 0)
      return entries[middle].position;

    if (at < 0)
      low = middle + 1;
    else
      high = middle - 1;
  }

  return -1;
}

#ifdef __cplusplus
}
#endif

#endif /* SHEETMAN_SCB_IMPLEMENTATION */

#endif /* SHEETMAN_SCB_READER_H */
