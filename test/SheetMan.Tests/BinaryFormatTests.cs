using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SheetMan.Tests;

/// <summary>
/// The binary format, pinned byte for byte.
///
/// The golden trees already compare every exported .scb byte for byte, but they are
/// recorded from the converter's own output: a change to the layout re-records them and
/// the thirteen readers are regenerated to match, so the whole gate can move together
/// and still agree with itself. The expectation below is written out here instead, from
/// the specification rather than from the output, and moving it means editing this file
/// on purpose.
///
/// What that protects is not this repository. It is every .scb file already written by
/// a build that shipped: they are read by the layout this test spells out, and a silent
/// change to it is a silent change to what those files mean.
/// </summary>
public class BinaryFormatTests
{
    /// <summary>
    /// The smallest table in the corpus: three scalar columns, one row.
    ///
    /// `layout-edge` is a workbook whose sheets start away from A1, which is beside the
    /// point here - it is used because SecondTable is small enough to be accounted for
    /// one byte at a time.
    /// </summary>
    private const string Scenario = "layout-edge";

    /// <summary>
    /// Every byte of a whole table file, assembled from the specification.
    ///
    /// Written as segments rather than one hex blob so that a mismatch names the part of
    /// the format that moved, and so that reading the test is a way of reading the
    /// format. The row is index 1, label "gamma", amount 30.
    /// </summary>
    [Fact]
    public void A_table_file_is_byte_for_byte_what_the_format_specifies()
    {
        var conversion = SheetManRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var expected = new Segments();

        // ---------------------------------------------------------------- header
        expected.Add("version", 0x66, 0x00, 0x00, 0x00);     // 102, fixed32
        expected.Add("flags", 0x00);                         // no compression, no encryption
        expected.Add("row count", 0x02);                     // counter32: zig-zag of 1
        expected.Add("column count", 0x06);                  // counter32: zig-zag of 3

        // ----------------------------------------------------------- descriptors
        //
        // Five fields each: the tag, the wire byte (element in the low nibble, kind in
        // bits 4-5), the encoding byte, the elements per row, and the block's length in
        // bytes. The length is a plain fixed32 rather than a counter because the writer
        // states it before the block, when a varint's size could not be known yet.
        // The encodings show the writer's measure-and-keep-the-smallest selection at
        // work even on one row: an i32 whose value fits a byte travels as a varint
        // (1 byte beats raw's fixed 4), while a one-row string column stays raw - a
        // dictionary of one entry would cost more than the string it deduplicates.
        expected.Add("index: tag", 0x02);                    // counter32: zig-zag of 1
        expected.Add("index: wire", 0x02);                   // element i32, kind scalar
        expected.Add("index: encoding", 0x01);               // varint
        expected.Add("index: count", 0x02);                  // counter32: zig-zag of 1
        expected.Add("index: block length", 0x01, 0x00, 0x00, 0x00);

        expected.Add("label: tag", 0x04);                    // zig-zag of 2
        expected.Add("label: wire", 0x06);                   // element string, kind scalar
        expected.Add("label: encoding", 0x00);               // raw
        expected.Add("label: count", 0x02);
        expected.Add("label: block length", 0x06, 0x00, 0x00, 0x00);

        expected.Add("amount: tag", 0x06);                   // zig-zag of 3
        expected.Add("amount: wire", 0x02);                  // element i32, kind scalar
        expected.Add("amount: encoding", 0x01);              // varint
        expected.Add("amount: count", 0x02);
        expected.Add("amount: block length", 0x01, 0x00, 0x00, 0x00);

        // ---------------------------------------------------------------- blocks
        //
        // One contiguous block per column, in descriptor order. This is the whole of
        // what makes an unknown column skippable in a single advance.
        expected.Add("index block: row 1", 0x02);            // counter32: zig-zag of 1

        expected.Add("label block: row 1 length", 0x0a);     // counter32: zig-zag of 5
        expected.Add("label block: row 1 bytes",
            Encoding.UTF8.GetBytes("gamma"));

        expected.Add("amount block: row 1", 0x3c);           // counter32: zig-zag of 30

        byte[] produced = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir(Scenario), "binary", "SecondTable.scb"));

        expected.AssertMatches(produced);
    }

    /// <summary>
    /// Which encoding the writer picks for each conformance column, pinned by name.
    ///
    /// The corpus data is shaped so that every encoding of the spec wins somewhere -
    /// that is what makes the thirteen conformance harnesses cover every decode path,
    /// not just the ones their data happened to trigger. This test is the other half
    /// of that arrangement: if the writer's selection drifts (a tweak to a candidate,
    /// a change in the data), the coverage does not silently narrow - this fails,
    /// naming the column that moved.
    /// </summary>
    [Fact]
    public void The_conformance_corpus_exercises_every_encoding()
    {
        var conversion = SheetManRunner.Convert("conformance");
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string binaryDir = Path.Combine(RepoLayout.OutputDir("conformance"), "binary");

        // (tag, encoding) per column, in descriptor order. Encodings by number:
        // 0 raw, 1 varint, 2 delta, 3 rle, 4 delta-rle, 5 dict, 6 dict-rle.
        AssertEncodings(Path.Combine(binaryDir, "Vectors.scb"), new byte[]
        {
            4,      // index:     ascending by one    -> delta-rle
            2,      // intVal:    varying small steps -> delta
            0,      // bigVal:    i64 stays raw by spec
            0,      // floatVal
            0,      // doubleVal
            6,      // text:      few words, long runs -> dict-rle
            0,      // flag
            0,      // when
            0,      // span
            0,      // uid
            3,      // label:     two long runs       -> rle
            0, 0, 0, 0,  // ints, strs, labels, uids: arrays stay raw by spec
            1,      // owner:     small, irregular    -> varint
            1,      // tier
        });

        AssertEncodings(Path.Combine(binaryDir, "Owners.scb"), new byte[]
        {
            4,      // index:     ascending by one    -> delta-rle
            5,      // name:      two words alternating, no runs -> dict
            4,      // rank:      ascending by ten    -> delta-rle
        });
    }

    private static void AssertEncodings(string path, byte[] expected)
    {
        var reader = new FormatWalker(File.ReadAllBytes(path));

        reader.ReadFixed32();                            // version
        reader.ReadByte();                               // flags
        reader.ReadCounter32();                          // row count
        int columnCount = reader.ReadCounter32();

        Assert.Equal(expected.Length, columnCount);

        for (int at = 0; at < columnCount; at++)
        {
            reader.ReadCounter32();                      // tag
            reader.ReadByte();                           // wire
            byte encoding = reader.ReadByte();
            reader.ReadCounter32();                      // elements per row
            reader.ReadFixed32();                        // block length

            Assert.True(expected[at] == encoding,
                $"{Path.GetFileName(path)}: column {at} uses encoding {encoding}, " +
                $"expected {expected[at]}.");
        }
    }

    /// <summary>
    /// The invariant every reader checks before it allocates: the blocks are all that
    /// follows the header, so their declared lengths add up to the bytes left, and no
    /// row costs less than one byte in any raw block. An encoded block has no such
    /// floor - one run can cover any number of rows - so the floor applies to raw only.
    ///
    /// Asserted over every table in every scenario's golden tree, because a writer that
    /// gets this wrong writes a file no reader will take - and there is no reason to
    /// discover that one target at a time.
    /// </summary>
    [Fact]
    public void Every_committed_table_declares_lengths_that_add_up()
    {
        var tables = Directory
            .EnumerateFiles(Path.Combine(RepoLayout.Root, "test", "fixtures", "golden"),
                "*.scb", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(tables);

        var failures = new List<string>();

        foreach (string path in tables)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string relative = Path.GetRelativePath(RepoLayout.Root, path);

            var reader = new FormatWalker(bytes);

            Assert.Equal(102u, reader.ReadFixed32());
            Assert.Equal(0, reader.ReadByte());

            int rowCount = reader.ReadCounter32();
            int columnCount = reader.ReadCounter32();

            int declared = 0;

            for (int at = 0; at < columnCount; at++)
            {
                reader.ReadCounter32();                      // tag
                byte wire = reader.ReadByte();
                byte encoding = reader.ReadByte();
                reader.ReadCounter32();                      // elements per row
                int byteLength = (int)reader.ReadFixed32();

                int kind = (wire >> 4) & 0x03;

                if (kind > 2)
                    failures.Add($"{relative}: column {at} declares kind {kind}");

                if (encoding > 6)
                    failures.Add($"{relative}: column {at} declares encoding {encoding}");

                if (encoding == 0 && rowCount > byteLength)
                {
                    failures.Add(
                        $"{relative}: column {at} holds {byteLength} bytes for {rowCount} rows");
                }

                declared += byteLength;
            }

            if (declared != bytes.Length - reader.Position)
            {
                failures.Add($"{relative}: columns declare {declared} bytes but " +
                             $"{bytes.Length - reader.Position} follow the header");
            }
        }

        Assert.True(failures.Count == 0,
            "Committed table files disagree with their own descriptors:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Just enough of a reader to walk a header: the four primitives it is made of.
    /// </summary>
    private sealed class FormatWalker
    {
        private readonly byte[] _bytes;

        public FormatWalker(byte[] bytes) => _bytes = bytes;

        public int Position { get; private set; }

        public byte ReadByte() => _bytes[Position++];

        public uint ReadFixed32()
        {
            uint value = (uint)(_bytes[Position]
                | _bytes[Position + 1] << 8
                | _bytes[Position + 2] << 16
                | _bytes[Position + 3] << 24);

            Position += 4;
            return value;
        }

        /// <summary>A zig-zag folded varint, which is how every count travels.</summary>
        public int ReadCounter32()
        {
            uint value = 0;

            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = ReadByte();
                value |= (uint)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                    break;
            }

            return (int)(value >> 1) ^ -(int)(value & 1);
        }
    }

    /// <summary>
    /// A byte sequence built out of named pieces, so a mismatch reports which piece.
    /// </summary>
    private sealed class Segments
    {
        private readonly List<(string Name, byte[] Bytes)> _segments =
            new List<(string, byte[])>();

        public void Add(string name, params byte[] bytes) => _segments.Add((name, bytes));

        public void AssertMatches(byte[] produced)
        {
            int at = 0;

            foreach (var (name, bytes) in _segments)
            {
                Assert.True(at + bytes.Length <= produced.Length,
                    $"The file ends before `{name}`: {produced.Length} bytes in all, " +
                    $"{at + bytes.Length} needed by this point.");

                var slice = produced.Skip(at).Take(bytes.Length).ToArray();

                Assert.True(slice.SequenceEqual(bytes),
                    $"`{name}` at offset {at} is {Hex(slice)}, expected {Hex(bytes)}.");

                at += bytes.Length;
            }

            Assert.True(at == produced.Length,
                $"The file is {produced.Length} bytes and the format accounts for {at}. " +
                $"Trailing bytes: {Hex(produced.Skip(at).ToArray())}.");
        }

        private static string Hex(byte[] bytes)
            => bytes.Length == 0 ? "<nothing>" : string.Join(" ", bytes.Select(b => b.ToString("x2")));
    }
}
