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
//   counter32   zig-zag encoded i32 written as a varint32
//   string      counter32 byte length, then that many UTF-8 bytes
//
// One of several readers of one format the exporter defines. The conformance corpus
// is what keeps them agreeing.
//
// No dependencies: everything here is core and std, so the generated crate needs no
// Cargo registry access to build.

use std::fmt;
use std::fs;
use std::io;
use std::path::Path;

/// Stamped at the head of every table file by the exporter.
/// The format is column-oriented and self-describing: the header names every column
/// and how long its block is, and a reader that meets a version it does not know stops
/// rather than guessing. 102 replaced 101 outright - a descriptor gained its encoding
/// byte - before any 101 file had shipped.
pub const FORMAT_VERSION: u32 = 102;

// The wire element types and kinds, as a column descriptor spells them.
pub const ELEMENT_VARINT: u8 = 0;
pub const ELEMENT_BOOL: u8 = 1;
pub const ELEMENT_I32: u8 = 2;
pub const ELEMENT_I64: u8 = 3;
pub const ELEMENT_F32: u8 = 4;
pub const ELEMENT_F64: u8 = 5;
pub const ELEMENT_STRING: u8 = 6;
pub const ELEMENT_UUID: u8 = 7;

pub const KIND_SCALAR: u8 = 0;
pub const KIND_FIXED_ARRAY: u8 = 1;
pub const KIND_VAR_ARRAY: u8 = 2;

// How a block's values are laid out. Raw is the layout 101 had; the others compress
// a column that repeats itself. spec/scb-v102-column-encoding.md is the contract.
pub const ENCODING_RAW: u8 = 0;
pub const ENCODING_VARINT: u8 = 1;
pub const ENCODING_DELTA: u8 = 2;
pub const ENCODING_RLE: u8 = 3;
pub const ENCODING_DELTA_RLE: u8 = 4;
pub const ENCODING_DICT: u8 = 5;
pub const ENCODING_DICT_RLE: u8 = 6;

/// One column as the file describes it.
#[derive(Clone, Copy, Debug)]
pub struct Column {
    /// What identifies the column, instead of its position.
    pub tag: i32,
    pub element: u8,
    pub kind: u8,
    /// How the block's values are laid out: one of the ENCODING_* constants.
    pub encoding: u8,
    /// Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one.
    pub count: i32,
    /// Total bytes of the column block - what a skip advances by.
    pub byte_length: i32,
}

/// A parsed header: the row count and the column descriptors that follow it.
pub struct Header {
    pub row_count: i32,
    pub columns: Vec<Column>,
}

/// What went wrong while reading a table.
#[derive(Debug)]
pub enum Error {
    /// The file ended before a value did.
    Truncated { position: usize, length: usize, wanted: usize },
    /// A varint ran past the five bytes an i32 can need.
    VarintTooLong,
    /// A length prefix was negative.
    NegativeLength,
    /// The file was written by a version this build does not read.
    UnsupportedVersion { found: u32, expected: u32 },
    /// The reserved byte was not zero, so the file uses a feature this build lacks.
    UnsupportedFeatures,
    /// The bytes of a string were not valid UTF-8.
    InvalidUtf8,
    /// A column and the generated member disagree about shape or type.
    ///
    /// Refusal is by name and both wires, never by reading anyway: a value that might
    /// not survive the conversion is a value this format does not read.
    ColumnMismatch { field: &'static str, detail: &'static str },
    /// A column block's declared length and the bytes the read consumed disagree.
    BlockLengthMismatch { tag: i32 },
    /// A column declares more bytes than the file has left to give it.
    ColumnLengthImplausible { tag: i32, byte_length: i32 },
    /// The row count is larger than a column block could hold that many rows in.
    RowCountImplausible { rows: i32, tag: i32, byte_length: i32 },
    /// The blocks the columns declare and the bytes after the header do not add up.
    HeaderLengthMismatch { declared: i32, available: i32 },
    /// A lookup for a key no row carries.
    ///
    /// Raised by the generated `get_by_*_or_error` lookups, which is where a caller has
    /// said the key has to be there. `find_by_*` answers the same question with `None`.
    RecordNotFound { table: &'static str, field: &'static str, key: String },
    /// The file could not be opened or read.
    Io(io::Error),
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Error::Truncated { position, length, wanted } => write!(
                f,
                "table data ended after {} of {} bytes while {} more were expected",
                position, length, wanted
            ),
            Error::VarintTooLong => write!(f, "varint32 is longer than five bytes"),
            Error::NegativeLength => write!(f, "length is negative"),
            Error::UnsupportedVersion { found, expected } => write!(
                f,
                "table format version {} is not supported (expected {})",
                found, expected
            ),
            Error::UnsupportedFeatures => write!(f, "table declares unsupported features"),
            Error::InvalidUtf8 => write!(f, "string bytes are not valid UTF-8"),
            Error::ColumnMismatch { field, detail } => write!(f, "{}: {}", field, detail),
            Error::ColumnLengthImplausible { tag, byte_length } => write!(
                f,
                "column tag {} declares {} bytes, which the file cannot hold",
                tag, byte_length
            ),
            Error::RowCountImplausible { rows, tag, byte_length } => write!(
                f,
                "the row count {} is larger than column tag {} can hold in its {} bytes",
                rows, tag, byte_length
            ),
            Error::HeaderLengthMismatch { declared, available } => write!(
                f,
                "the columns declare {} bytes but {} follow the header",
                declared, available
            ),
            Error::BlockLengthMismatch { tag } => write!(
                f,
                "column tag {}: the block's declared length and the bytes consumed disagree",
                tag
            ),
            Error::RecordNotFound { table, field, key } => write!(
                f,
                "there is no record in table `{}` that corresponds to field `{}` value {}",
                table, field, key
            ),
            Error::Io(inner) => write!(f, "{}", inner),
        }
    }
}

impl std::error::Error for Error {}

impl From<io::Error> for Error {
    fn from(inner: io::Error) -> Self {
        Error::Io(inner)
    }
}

pub type Result<T> = std::result::Result<T, Error>;

/// Sequential reader over a table file's bytes.
///
/// Borrows rather than owns, so loading a table costs no copy of the file.
pub struct Reader<'a> {
    data: &'a [u8],
    position: usize,
}

impl<'a> Reader<'a> {
    pub fn new(data: &'a [u8]) -> Self {
        Reader { data, position: 0 }
    }

    /// Bytes consumed so far.
    /// Advances past bytes without interpreting them: an unknown column whole block.
    /// The column-oriented layout is what makes this one call the entirety of skipping.
    pub fn skip(&mut self, byte_count: i32) -> Result<()> {
        if byte_count < 0 {
            return Err(Error::NegativeLength);
        }

        // take() is the bounds check.
        self.take(byte_count as usize)?;
        Ok(())
    }

    // Promotions: a member reading a file element narrower than itself. Only the
    // mathematically lossless directions exist; check_column already refused the rest.

    /// An i32 member from i32 or varint.
    pub fn read_i32_as(&mut self, element: u8) -> Result<i32> {
        if element == ELEMENT_I32 {
            self.read_i32()
        } else {
            self.read_counter32()
        }
    }

    /// An i64 member from i64, i32 or varint.
    pub fn read_i64_as(&mut self, element: u8) -> Result<i64> {
        match element {
            ELEMENT_I64 => self.read_i64(),
            ELEMENT_I32 => Ok(self.read_i32()? as i64),
            _ => Ok(self.read_counter32()? as i64),
        }
    }

    /// An f64 member from f64, f32 or i32 - all exact in an f64.
    pub fn read_f64_as(&mut self, element: u8) -> Result<f64> {
        match element {
            ELEMENT_F64 => self.read_f64(),
            ELEMENT_F32 => Ok(self.read_f32()? as f64),
            _ => Ok(self.read_i32()? as f64),
        }
    }

    pub fn position(&self) -> usize {
        self.position
    }

    /// Bytes left to read.
    pub fn remaining(&self) -> usize {
        self.data.len() - self.position
    }

    fn take(&mut self, count: usize) -> Result<&'a [u8]> {
        if self.remaining() < count {
            return Err(Error::Truncated {
                position: self.position,
                length: self.data.len(),
                wanted: count,
            });
        }

        let slice = &self.data[self.position..self.position + count];
        self.position += count;

        Ok(slice)
    }

    pub fn read_u8(&mut self) -> Result<u8> {
        Ok(self.take(1)?[0])
    }

    pub fn read_bool(&mut self) -> Result<bool> {
        Ok(self.read_u8()? != 0)
    }

    pub fn read_i32(&mut self) -> Result<i32> {
        let bytes = self.take(4)?;
        Ok(i32::from_le_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
    }

    pub fn read_u32(&mut self) -> Result<u32> {
        let bytes = self.take(4)?;
        Ok(u32::from_le_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
    }

    pub fn read_i64(&mut self) -> Result<i64> {
        let bytes = self.take(8)?;
        Ok(i64::from_le_bytes([
            bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
        ]))
    }

    /// A single-precision value as its stored bit pattern, so it survives exactly rather
    /// than through a decimal rendering.
    pub fn read_f32(&mut self) -> Result<f32> {
        Ok(f32::from_bits(self.read_u32()?))
    }

    pub fn read_f64(&mut self) -> Result<f64> {
        let bytes = self.take(8)?;
        Ok(f64::from_bits(u64::from_le_bytes([
            bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
        ])))
    }

    /// A length-prefixed UTF-8 string.
    pub fn read_string(&mut self) -> Result<String> {
        let length = self.read_counter32()?;

        if length < 0 {
            return Err(Error::NegativeLength);
        }

        if length == 0 {
            return Ok(String::new());
        }

        let bytes = self.take(length as usize)?;

        std::str::from_utf8(bytes)
            .map(|text| text.to_owned())
            .map_err(|_| Error::InvalidUtf8)
    }

    /// A timestamp as .NET ticks: 100 ns units since 0001-01-01.
    ///
    /// Ticks rather than a date type, because std has none and the values a sheet can
    /// hold reach both 0001-01-01 and 9999-12-31, which most of them cannot express.
    pub fn read_datetime_ticks(&mut self) -> Result<i64> {
        self.read_i64()
    }

    /// A duration as .NET ticks.
    pub fn read_duration_ticks(&mut self) -> Result<i64> {
        self.read_i64()
    }

    /// The sixteen bytes of a uuid's .NET layout.
    pub fn read_uuid(&mut self) -> Result<Uuid> {
        let bytes = self.take(16)?;

        let mut value = [0u8; 16];
        value.copy_from_slice(bytes);

        Ok(Uuid(value))
    }

    /// An i32 written in as few bytes as its magnitude needed, either sign.
    pub fn read_optimal_i32(&mut self) -> Result<i32> {
        let encoded = self.read_varint32()?;

        // Undoes the zig-zag fold: the low bit carried the sign. The shift is on the
        // unsigned value, so the sign has to come back from that bit rather than from
        // the shift.
        Ok(((encoded >> 1) as i32) ^ -((encoded & 1) as i32))
    }

    /// A count, in the same encoding as `read_optimal_i32`.
    pub fn read_counter32(&mut self) -> Result<i32> {
        self.read_optimal_i32()
    }

    /// An enum value, which travels zig-zag encoded rather than fixed width.
    pub fn read_enum(&mut self) -> Result<i32> {
        self.read_optimal_i32()
    }

    fn read_varint32(&mut self) -> Result<u32> {
        let mut value: u32 = 0;

        let mut shift = 0;
        while shift < 35 {
            let byte = self.read_u8()?;
            value |= ((byte & 0x7F) as u32) << shift;

            if byte & 0x80 == 0 {
                return Ok(value);
            }

            shift += 7;
        }

        Err(Error::VarintTooLong)
    }
}

/// A 128 bit identifier, stored in .NET Guid byte order.
///
/// That order is not plain big-endian: the first three components are little endian and
/// the trailing eight bytes are not, which is what Display has to account for.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub struct Uuid(pub [u8; 16]);

impl fmt::Display for Uuid {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        // Component order matching .NET's Guid.ToString("D").
        const ORDER: [usize; 16] = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15];

        for (position, index) in ORDER.iter().enumerate() {
            if position == 4 || position == 6 || position == 8 || position == 10 {
                write!(f, "-")?;
            }

            write!(f, "{:02x}", self.0[*index])?;
        }

        Ok(())
    }
}

/// Reads and checks a table file's header, returning the row count that follows it.
///
/// The reserved byte is written as zero and is where compression or encryption flags
/// would go; a non-zero value means the file needs handling this build does not have.
pub fn read_table_header(reader: &mut Reader<'_>) -> Result<Header> {
    let version = reader.read_u32()?;

    if version != FORMAT_VERSION {
        return Err(Error::UnsupportedVersion { found: version, expected: FORMAT_VERSION });
    }

    if reader.read_u8()? != 0 {
        return Err(Error::UnsupportedFeatures);
    }

    let count = reader.read_counter32()?;
    if count < 0 {
        return Err(Error::NegativeLength);
    }

    let column_count = reader.read_counter32()?;
    if column_count < 0 {
        return Err(Error::NegativeLength);
    }

    let mut columns = Vec::with_capacity(column_count as usize);

    for _ in 0..column_count {
        let tag = reader.read_counter32()?;
        let wire = reader.read_u8()?;
        let encoding = reader.read_u8()?;
        let element_count = reader.read_counter32()?;
        let byte_length = reader.read_u32()? as i32;

        columns.push(Column {
            tag,
            element: wire & 0x0f,
            kind: (wire >> 4) & 0x03,
            encoding,
            count: element_count,
            byte_length,
        });
    }

    // What the descriptors say about the file, checked before anybody allocates for the
    // row count. The blocks are all that follows the header, so their declared lengths have
    // to add up to the bytes left. A raw block also costs at least one byte per row - a
    // varint's shortest form, an empty string's length prefix, a variable array's counter -
    // so a larger row count is one the exporter could not have written. An encoded block
    // has no such floor; its decode checks run sums and dictionary bounds instead.

    let available = reader.remaining() as i32;
    let mut declared: i32 = 0;

    for column in &columns {
        if column.byte_length < 0 || column.byte_length > available - declared {
            return Err(Error::ColumnLengthImplausible {
                tag: column.tag,
                byte_length: column.byte_length,
            });
        }

        declared += column.byte_length;

        if column.encoding == ENCODING_RAW && count > column.byte_length {
            return Err(Error::RowCountImplausible {
                rows: count,
                tag: column.tag,
                byte_length: column.byte_length,
            });
        }
    }

    if declared != available {
        return Err(Error::HeaderLengthMismatch { declared, available });
    }

    Ok(Header { row_count: count, columns })
}

/// That a column is what the generated member expects, or a lossless promotion of it.
pub fn check_column(
    column: &Column, field: &'static str, kind: u8, count: i32, accepted: &[u8],
) -> Result<()> {
    if column.kind != kind || (kind != KIND_VAR_ARRAY && column.count != count) {
        return Err(Error::ColumnMismatch {
            field,
            detail: "the column's shape does not match the generated member; the schema \
                     changed shape, regenerate the code or rebuild the data",
        });
    }

    // An encoding this build cannot decode is refused by name, exactly like an element
    // it cannot read. An unknown column's encoding never gets here - a skip is a skip
    // whatever the block's layout.
    if column.encoding != ENCODING_RAW {
        return Err(Error::ColumnMismatch {
            field,
            detail: "the column uses an encoding this reader does not support;                      regenerate the code or rebuild the data",
        });
    }

    if !accepted.contains(&column.element) {
        return Err(Error::ColumnMismatch {
            field,
            detail: "the column's element type is one this member cannot read; the column \
                     changed type incompatibly, regenerate the code or rebuild the data",
        });
    }

    Ok(())
}

/// That a block was consumed exactly: a mismatch is a format disagreement, and stopping
/// here names the column instead of corrupting the next.
pub fn check_block_end(reader: &Reader<'_>, column: &Column, expected_end: usize) -> Result<()> {
    if reader.position() != expected_end {
        return Err(Error::BlockLengthMismatch { tag: column.tag });
    }

    Ok(())
}

/// Reads a whole file into memory.
pub fn read_all_bytes(filename: &Path) -> Result<Vec<u8>> {
    Ok(fs::read(filename)?)
}
