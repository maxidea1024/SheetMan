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
// Deliberately small. This replaces a 3,600 line runtime that had to be installed
// into the consuming project as a plugin, of which the generated code called four
// members. The C++ and TypeScript outputs each carry an equivalent of this file, and
// all three are implementations of one format the exporter defines.
//
// Reads go through Span, so loading a table costs no allocations beyond the strings
// and arrays the records actually hold - which matters because this runs at game
// startup, over every row of every table.
//
// Targets C# 8 and netstandard2.1 so Unity 2020.3 accepts it unchanged. The
// SheetMan repository compiles this file against exactly that to keep it so.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SheetMan.Binary
{
    /// <summary>Thrown when a table file is truncated, malformed, or not a table file.</summary>
    public class ScbException : Exception
    {
        public ScbException(string message) : base(message) { }
    }

    /// <summary>
    /// Sequential reader over a table file's bytes.
    ///
    /// Holds a <see cref="ReadOnlyMemory{T}"/> rather than a span, because a span cannot
    /// live in a field: the generated code reads a record at a time through many calls,
    /// so the cursor has to survive between them. Each read slices a span off it, which
    /// costs nothing.
    ///
    /// Every read either advances the cursor or throws, so callers need not check a
    /// return value - except <see cref="TryReadCounter32"/>, kept for generated code
    /// that reads a length it wants to handle itself.
    /// </summary>
    public sealed class ScbReader
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _position;

        public ScbReader(byte[] data)
            : this(new ReadOnlyMemory<byte>(data ?? throw new ArgumentNullException(nameof(data))))
        {
        }

        /// <summary>
        /// Reads from memory the caller already has, without copying it - a slice of a
        /// pooled buffer, or bytes that arrived over the network.
        /// </summary>
        public ScbReader(ReadOnlyMemory<byte> data)
        {
            _data = data;
            _position = 0;
        }

        /// <summary>Bytes consumed so far.</summary>
        public int Position => _position;

        /// <summary>Bytes left to read.</summary>
        public int Remaining => _data.Length - _position;

        // ------------------------------------------------------------ scalars

        public void Read(out byte value)
        {
            value = Take(1)[0];
        }

        public void Read(out bool value)
        {
            value = Take(1)[0] != 0;
        }

        public void Read(out int value)
        {
            value = BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        }

        public void Read(out uint value)
        {
            value = BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        }

        public void Read(out long value)
        {
            value = BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        }

        public void Read(out float value)
        {
            // The stored bits reinterpreted, not a decimal conversion, so the value is
            // exactly what was written.
            value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(Take(4)));
        }

        public void Read(out double value)
        {
            value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));
        }

        /// <summary>
        /// A string: its UTF-8 byte length, then the bytes.
        ///
        /// Decoded straight from the span, so the only allocation is the string itself.
        /// </summary>
        public void Read(out string value)
        {
            int length = ReadCounter32();
            if (length < 0)
                throw new ScbException("string length is negative");

            if (length == 0)
            {
                value = string.Empty;
                return;
            }

            value = Encoding.UTF8.GetString(Take(length));
        }

        /// <summary>A date, carried as .NET ticks: 100 ns units since 0001-01-01.</summary>
        public void Read(out DateTime value)
        {
            value = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));
        }

        /// <summary>A duration, carried as .NET ticks.</summary>
        public void Read(out TimeSpan value)
        {
            value = new TimeSpan(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));
        }

        /// <summary>
        /// A uuid, carried as the sixteen bytes of its .NET layout.
        ///
        /// Constructed from the span directly; the obvious version copies into a
        /// temporary array, which on a table with a uuid column is one allocation per row.
        /// </summary>
        public void Read(out Guid value)
        {
            value = new Guid(Take(16));
        }

        // ------------------------------------------------- variable length ints

        /// <summary>
        /// An int32 written in as few bytes as its magnitude needed, either sign.
        /// </summary>
        public int ReadOptimalInt32()
        {
            uint encoded = ReadVarint32();

            // Undoes the zig-zag fold: the low bit carried the sign.
            return unchecked((int)(encoded >> 1) ^ -(int)(encoded & 1));
        }

        public void ReadOptimalInt32(out int value) => value = ReadOptimalInt32();

        /// <summary>A count, in the same encoding as <see cref="ReadOptimalInt32"/>.</summary>
        public int ReadCounter32() => ReadOptimalInt32();

        /// <summary>
        /// Advances past bytes without interpreting them: an unknown column's whole block.
        /// </summary>
        /// <remarks>
        /// This one call is the entirety of "skip a column the generated code does not know".
        /// The column-oriented layout is what makes that possible - a row-oriented file would
        /// need a skip routine per wire type, in every reader, each a chance to misalign.
        /// </remarks>
        public void Skip(int byteCount)
        {
            if (byteCount < 0 || byteCount > Remaining)
                throw new ScbException($"cannot skip {byteCount} bytes with {Remaining} remaining");

            _position += byteCount;
        }

        // -------------------------------------------------------- promotions
        //
        // A value whose file element is narrower than the member reads through these, so a
        // reader built after a type was widened still reads data written before it. Only the
        // mathematically lossless directions exist; anything else was already refused by the
        // column check, by name.

        /// <summary>An int32 member from i32 or varint.</summary>
        public int ReadI32As(byte element)
        {
            if (element == ScbTable.ElementI32)
            {
                Read(out int exact);
                return exact;
            }

            return ReadOptimalInt32();
        }

        /// <summary>An int64 member from i64, i32 or varint.</summary>
        public long ReadI64As(byte element)
        {
            switch (element)
            {
                case ScbTable.ElementI64:
                {
                    Read(out long exact);
                    return exact;
                }

                case ScbTable.ElementI32:
                {
                    Read(out int narrower);
                    return narrower;
                }

                default:
                    return ReadOptimalInt32();
            }
        }

        /// <summary>A double member from f64, f32 or i32 - all exact in a double.</summary>
        public double ReadF64As(byte element)
        {
            switch (element)
            {
                case ScbTable.ElementF64:
                {
                    Read(out double exact);
                    return exact;
                }

                case ScbTable.ElementF32:
                {
                    Read(out float single);
                    return single;
                }

                default:
                {
                    Read(out int integer);
                    return integer;
                }
            }
        }

        /// <summary>
        /// A count, without throwing when the data has run out.
        /// </summary>
        /// <returns>False when there was nothing left to read.</returns>
        public bool TryReadCounter32(out int value)
        {
            if (Remaining <= 0)
            {
                value = 0;
                return false;
            }

            value = ReadCounter32();
            return true;
        }

        // --------------------------------------------------------- primitives

        /// <summary>
        /// Advances the cursor by <paramref name="count"/> and returns what was skipped.
        ///
        /// The single place bounds are checked and the position moves, so no read can
        /// forget to do either.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<byte> Take(int count)
        {
            if (Remaining < count)
            {
                throw new ScbException(
                    $"table data ended after {_position} of {_data.Length} bytes " +
                    $"while {count} more were expected");
            }

            var span = _data.Span.Slice(_position, count);
            _position += count;

            return span;
        }

        private uint ReadVarint32()
        {
            uint value = 0;

            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = Take(1)[0];
                value |= (uint)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                    return value;
            }

            throw new ScbException("varint32 is longer than five bytes");
        }
    }

    /// <summary>
    /// One column as the file describes it: the descriptor the header carries per column.
    /// </summary>
    public struct ScbColumn
    {
        /// <summary>What identifies the column, instead of its position.</summary>
        public int Tag;

        /// <summary>Element type: one of the Element* constants.</summary>
        public byte Element;

        /// <summary>Kind: scalar, fixed array or variable array.</summary>
        public byte Kind;

        /// <summary>Elements per row: 1 for a scalar, N for a fixed array, 0 for a variable one.</summary>
        public int Count;

        /// <summary>Total bytes of the column's data block - what a skip advances by.</summary>
        public int ByteLength;
    }

    /// <summary>
    /// The file-level parts of the format, shared by every generated table.
    /// </summary>
    public static class ScbTable
    {
        /// <summary>Version stamped at the head of every table file by the exporter.</summary>
        /// <remarks>
        /// The format is column-oriented and self-describing: the header names every column
        /// and how long its block is, and a reader that meets a version it does not know
        /// stops rather than guessing.
        /// </remarks>
        public const uint FormatVersion = 101;

        public const byte ElementVarint = 0;
        public const byte ElementBool = 1;
        public const byte ElementI32 = 2;
        public const byte ElementI64 = 3;
        public const byte ElementF32 = 4;
        public const byte ElementF64 = 5;
        public const byte ElementString = 6;
        public const byte ElementUuid = 7;

        public const byte KindScalar = 0;
        public const byte KindFixedArray = 1;
        public const byte KindVarArray = 2;

        /// <summary>
        /// Reads and checks the file header, returning the row count and the column
        /// descriptors the data blocks follow.
        /// </summary>
        public static ScbColumn[] ReadHeader(ScbReader reader, out int rowCount)
        {
            reader.Read(out uint version);
            if (version != FormatVersion)
            {
                throw new ScbException(
                    $"table format version {version} is not supported (expected {FormatVersion})");
            }

            reader.Read(out byte reserved);
            if (reserved != 0)
                throw new ScbException("table declares unsupported features");

            rowCount = reader.ReadCounter32();
            if (rowCount < 0)
                throw new ScbException("table row count is negative");

            int columnCount = reader.ReadCounter32();
            if (columnCount < 0)
                throw new ScbException("table column count is negative");

            var columns = new ScbColumn[columnCount];

            for (int at = 0; at < columnCount; at++)
            {
                columns[at].Tag = reader.ReadCounter32();

                reader.Read(out byte wire);
                columns[at].Element = (byte)(wire & 0x0F);
                columns[at].Kind = (byte)((wire >> 4) & 0x03);

                columns[at].Count = reader.ReadCounter32();

                reader.Read(out uint byteLength);
                columns[at].ByteLength = (int)byteLength;
            }

            // What the descriptors say about the file, checked before anybody allocates for the
            // row count. The blocks are all that follows the header, so their declared lengths have
            // to add up to the bytes left, and every row costs at least one byte in every block - a
            // varint's shortest form, an empty string's length prefix, a variable array's counter.
            // A row count larger than that is one the exporter could not have written.

            int available = reader.Remaining;
            int declared = 0;

            foreach (var column in columns)
            {
                if (column.ByteLength < 0 || column.ByteLength > available - declared)
                {
                    throw new ScbException(
                        $"column tag {column.Tag} declares {column.ByteLength} bytes, which the " +
                        "file cannot hold");
                }

                declared += column.ByteLength;

                if (rowCount > column.ByteLength)
                {
                    throw new ScbException(
                        $"the row count {rowCount} is larger than column tag {column.Tag} can " +
                        $"hold in its {column.ByteLength} bytes");
                }
            }

            if (declared != available)
            {
                throw new ScbException(
                    $"the columns declare {declared} bytes but {available} follow the header");
            }

            return columns;
        }

        /// <summary>
        /// That a column is what the generated member expects, or a lossless promotion of it.
        /// </summary>
        /// <remarks>
        /// Refusal is by name and by both types, never by reading anyway: a value that might
        /// not survive the conversion is a value this format does not read. What each member
        /// accepts is decided at generation time - the accepted list is in the generated call.
        /// </remarks>
        /// <summary>
        /// The one, two and three element forms the generated code actually emits.
        /// </summary>
        /// <remarks>
        /// Rather than one `params` overload, which allocates an array per column per load
        /// for a list that is never longer than three and is known at generation time.
        /// </remarks>
        public static void CheckColumn(
            in ScbColumn column, string fieldName, byte kind, int count, byte accepted)
        {
            CheckShape(column, fieldName, kind, count);

            if (column.Element != accepted)
                throw ElementMismatch(column, fieldName);
        }

        public static void CheckColumn(
            in ScbColumn column, string fieldName, byte kind, int count,
            byte accepted, byte alsoAccepted)
        {
            CheckShape(column, fieldName, kind, count);

            if (column.Element != accepted && column.Element != alsoAccepted)
                throw ElementMismatch(column, fieldName);
        }

        public static void CheckColumn(
            in ScbColumn column, string fieldName, byte kind, int count,
            byte accepted, byte alsoAccepted, byte andAccepted)
        {
            CheckShape(column, fieldName, kind, count);

            if (column.Element != accepted && column.Element != alsoAccepted
                && column.Element != andAccepted)
            {
                throw ElementMismatch(column, fieldName);
            }
        }

        private static void CheckShape(
            in ScbColumn column, string fieldName, byte kind, int count)
        {
            if (column.Kind != kind || (kind != KindVarArray && column.Count != count))
            {
                throw new ScbException(
                    $"{fieldName}: the file's column (kind {column.Kind}, count {column.Count}) does not " +
                    $"match the generated member (kind {kind}, count {count}). The schema changed shape; " +
                    "regenerate the code or rebuild the data.");
            }
        }

        private static ScbException ElementMismatch(
            in ScbColumn column, string fieldName)
            => new ScbException(
                $"{fieldName}: the file carries element type {column.Element}, which this member " +
                "cannot read. The column changed type incompatibly; regenerate the code or " +
                "rebuild the data.");

        /// <summary>
        /// The general form, for an accepted list longer than three. Nothing emits one today.
        /// </summary>
        public static void CheckColumn(
            in ScbColumn column, string fieldName, byte kind, int count, params byte[] acceptedElements)
        {
            if (column.Kind != kind || (kind != KindVarArray && column.Count != count))
            {
                throw new ScbException(
                    $"{fieldName}: the file's column (kind {column.Kind}, count {column.Count}) does not " +
                    $"match the generated member (kind {kind}, count {count}). The schema changed shape; " +
                    "regenerate the code or rebuild the data.");
            }

            for (int at = 0; at < acceptedElements.Length; at++)
            {
                if (column.Element == acceptedElements[at])
                    return;
            }

            throw new ScbException(
                $"{fieldName}: the file carries element type {column.Element}, which this member " +
                $"cannot read (accepts: {string.Join(", ", acceptedElements)}). The column changed " +
                "type incompatibly; regenerate the code or rebuild the data.");
        }

        /// <summary>
        /// That a block was consumed exactly. A mismatch means the reader and writer disagree
        /// about the format, and stopping here names the column instead of corrupting the next.
        /// </summary>
        public static void CheckBlockEnd(ScbReader reader, in ScbColumn column, int expectedEnd)
        {
            if (reader.Position != expectedEnd)
            {
                throw new ScbException(
                    $"column tag {column.Tag}: its block declared {column.ByteLength} bytes but the " +
                    $"read ended {expectedEnd - reader.Position} bytes short of its boundary");
            }
        }

        /// <summary>Reads a whole file into memory.</summary>
        public static byte[] ReadAllBytes(string filename) => File.ReadAllBytes(filename);
    }
}
