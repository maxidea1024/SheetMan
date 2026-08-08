using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SheetMan.Exporters;

/// <summary>
/// Writes the Scb format that <see cref="BinaryExporter"/> produces.
///
/// Self-contained on purpose. The exporter used to borrow the writer from the
/// runtime shared with the Unity plugin - 3,600 lines of read and write machinery,
/// of which it called four members - and that coupling forced the converter to stay
/// within the C# level Unity accepts, for no benefit to either side.
///
/// The format is small enough to state in full:
///
///   fixed8      one byte
///   fixed32     four bytes, little endian
///   fixed64     eight bytes, little endian
///   varint32    seven bits per byte, high bit set while more bytes follow,
///               at most five bytes
///   counter32   zig-zag encoded int32 written as a varint32, so a small value of
///               either sign costs one byte
///   string      counter32 byte length, then that many UTF-8 bytes
///
/// Nothing here allocates per value. Encoding and formatting go straight into the
/// buffer through spans, which matters because a localization table is hundreds of
/// thousands of cells and the naive shape - encode to a temporary array, copy, drop
/// it - makes one garbage array per cell.
///
/// Every reader is a separate implementation of the same description: the emitted
/// C# one, lib/cpp for C++, and lib/ts for TypeScript. A change here has to be made
/// in all three, and the round-trip tests are what catch it when it is not.
/// </summary>
public sealed class ScbWriter
{
    private byte[] _buffer;
    private int _length;

    public ScbWriter(int initialCapacity = 64 * 1024)
    {
        _buffer = new byte[Math.Max(initialCapacity, 16)];
        _length = 0;
    }

    /// <summary>Bytes written so far.</summary>
    public int Length => _length;

    /// <summary>
    /// Everything written, as a view over the internal buffer.
    ///
    /// A view rather than a copy: a table's bytes are handed straight to the file
    /// write, and copying them first would double the largest allocation the export
    /// makes. Valid only until the next write.
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    // ------------------------------------------------------------ scalars

    /// <summary>A byte, written as-is. Used for the format's reserved flags byte.</summary>
    public void Write(byte value) => Reserve(1)[0] = value;

    public void Write(bool value) => Reserve(1)[0] = value ? (byte)1 : (byte)0;

    public void Write(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);

    public void Write(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);

    /// <summary>
    /// Writes a fixed32 placeholder and hands back its offset for <see cref="PatchUInt32"/>.
    /// </summary>
    /// <remarks>
    /// For the column byte lengths: a length is only known after its block is written, and a
    /// varint cannot be patched in place because its own size depends on the value. A fixed
    /// four bytes per column is the cost, which on any real table is noise.
    /// </remarks>
    public int ReserveUInt32Slot()
    {
        int offset = _length;
        Write(0u);
        return offset;
    }

    /// <summary>Overwrites a slot from <see cref="ReserveUInt32Slot"/>.</summary>
    public void PatchUInt32(int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(offset, 4), value);

    /// <summary>
    /// A 64-bit integer.
    ///
    /// Written as a full eight bytes. The original cast through uint, truncating every
    /// value to its low 32 bits before widening again, so anything outside
    /// [0, uint.MaxValue] was silently corrupted - negatives came back as large
    /// positives and large positives lost their high half. The reader always read a
    /// full eight bytes, so only the writer was wrong, which is why nothing that
    /// round-tripped within C# ever noticed.
    /// </summary>
    public void Write(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);

    /// <summary>
    /// A float, as its IEEE-754 bit pattern, so the value survives exactly rather
    /// than through a decimal rendering.
    /// </summary>
    public void Write(float value)
        => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), BitConverter.SingleToInt32Bits(value));

    public void Write(double value)
        => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), BitConverter.DoubleToInt64Bits(value));

    /// <summary>
    /// A string: its UTF-8 byte length, then the bytes.
    ///
    /// Encoded straight into the buffer. The byte count is measured first, which walks
    /// the string twice, and that is the cheaper half of the trade - the alternative
    /// allocates an array per string and throws it away.
    ///
    /// Length in bytes rather than characters, because that is what a reader needs to
    /// know how far to advance.
    /// </summary>
    public void Write(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteCounter32(0);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);

        WriteCounter32(byteCount);
        Encoding.UTF8.GetBytes(value, Reserve(byteCount));
    }

    /// <summary>
    /// A date, as .NET ticks: 100 ns units since 0001-01-01.
    ///
    /// Ticks rather than a rendered timestamp, so the value is exact and needs no
    /// parsing on the way back in.
    /// </summary>
    public void Write(DateTime value)
        => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value.Ticks);

    /// <summary>A duration, as .NET ticks.</summary>
    public void Write(TimeSpan value)
        => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value.Ticks);

    /// <summary>
    /// A uuid, as the sixteen bytes of its .NET layout.
    ///
    /// Written in place; ToByteArray would allocate one array per value. That layout
    /// is not plain big-endian - the first three components are little endian - and
    /// every reader accounts for it when formatting the text form.
    /// </summary>
    public void Write(Guid value)
    {
        if (!value.TryWriteBytes(Reserve(16)))
            throw new SheetManException($"Could not write uuid `{value}`.");
    }

    // ------------------------------------------------- variable length ints

    /// <summary>
    /// An int32 in as few bytes as its magnitude needs, either sign.
    ///
    /// Zig-zag first so a small negative costs one byte rather than five: the sign
    /// bit is folded into the low bit instead of sitting at the top, where a plain
    /// varint would have to carry every intervening one.
    /// </summary>
    public void WriteOptimalInt32(int value)
    {
        uint encoded = ZigZagEncode32(value);

        // Reserved at the maximum and trimmed after, so the length is known without
        // measuring the encoding twice.
        var span = Reserve(MaxVarint32Length);

        int written = 0;
        while (encoded >= 0x80)
        {
            span[written++] = (byte)(encoded | 0x80);
            encoded >>= 7;
        }

        span[written++] = (byte)encoded;

        _length -= MaxVarint32Length - written;
    }

    /// <summary>
    /// A count. The same encoding as <see cref="WriteOptimalInt32"/>, named for what
    /// it means at the call site.
    /// </summary>
    public void WriteCounter32(int count) => WriteOptimalInt32(count);

    // --------------------------------------------------------- primitives

    private const int MaxVarint32Length = 5;

    /// <summary>
    /// Advances the cursor by <paramref name="count"/> and returns the room to fill.
    ///
    /// The single place the buffer grows and the position moves, so no write can
    /// forget to do either.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<byte> Reserve(int count)
    {
        if (_length + count > _buffer.Length)
            Grow(count);

        var span = _buffer.AsSpan(_length, count);
        _length += count;

        return span;
    }

    private void Grow(int additional)
    {
        int required = _length + additional;

        int capacity = _buffer.Length;
        while (capacity < required)
            capacity *= 2;

        Array.Resize(ref _buffer, capacity);
    }

    /// <summary>
    /// Folds a signed value's sign into its low bit, so small magnitudes of either
    /// sign encode short.
    /// </summary>
    private static uint ZigZagEncode32(int value)
    {
        // The right shift must be arithmetic, which it is for a signed int in C#:
        // it produces all ones for a negative value and all zeros otherwise.
        return unchecked((uint)((value << 1) ^ (value >> 31)));
    }
}
