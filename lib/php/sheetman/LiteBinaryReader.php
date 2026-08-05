<?php

/*
 * SheetMan LiteBinary reader for PHP 8.1 and above.
 *
 * Reads the .table files produced by SheetMan's binary exporter. The format is
 * defined by the C# writer in src/Exporters/LiteBinaryWriter.cs, and this is a
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
 * No dependencies beyond the standard library, and nothing that needs an
 * extension: unpack() is part of core.
 */

declare(strict_types=1);

namespace SheetMan;

/** Thrown when a table file is truncated, malformed, or not a table file. */
final class LiteBinaryException extends \RuntimeException
{
}

/**
 * A 128 bit identifier, held as the sixteen bytes in .NET Guid order.
 *
 * That order is not plain big-endian: the first three components are little
 * endian and the trailing eight bytes are not, which is what __toString has to
 * account for.
 */
final class Uuid implements \Stringable
{
    public function __construct(public readonly string $bytes)
    {
        if (\strlen($bytes) !== 16) {
            throw new LiteBinaryException('A uuid is sixteen bytes, not ' . \strlen($bytes) . '.');
        }
    }

    public static function empty(): self
    {
        return new self(\str_repeat("\0", 16));
    }

    /** The 8-4-4-4-12 form, matching .NET's Guid.ToString("D"). */
    public function __toString(): string
    {
        static $order = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15];

        $text = '';

        foreach ($order as $i => $index) {
            if ($i === 4 || $i === 6 || $i === 8 || $i === 10) {
                $text .= '-';
            }

            $text .= \sprintf('%02x', \ord($this->bytes[$index]));
        }

        return $text;
    }

    public function equals(self $other): bool
    {
        return $this->bytes === $other->bytes;
    }
}

/**
 * Sequential reader over a table file's bytes.
 *
 * Every read either advances the cursor or throws, so a caller never has to
 * check a return value - which is what PHP callers expect, and unlike the C and
 * Unreal readers there is no build here that turns exceptions off.
 */
final class LiteBinaryReader
{
    /** Version stamped at the head of every table file by the exporter. */
    /**
     * 101 is column-oriented and self-describing; it replaced 100 outright, before the
     * tool fed anything live, so nothing reads or writes 100 any more.
     */
    public const FILE_FORMAT_VERSION = 101;

    // The wire element types and kinds, as the v101 column descriptors spell them.
    public const ELEMENT_VARINT = 0;
    public const ELEMENT_BOOL = 1;
    public const ELEMENT_I32 = 2;
    public const ELEMENT_I64 = 3;
    public const ELEMENT_F32 = 4;
    public const ELEMENT_F64 = 5;
    public const ELEMENT_STRING = 6;
    public const ELEMENT_UUID = 7;

    public const KIND_SCALAR = 0;
    public const KIND_FIXED_ARRAY = 1;
    public const KIND_VAR_ARRAY = 2;

    private int $position = 0;
    private readonly int $length;

    public function __construct(private readonly string $data)
    {
        $this->length = \strlen($data);
    }

    public static function fromFile(string $filename): self
    {
        $data = @\file_get_contents($filename);

        if ($data === false) {
            throw new LiteBinaryException("Cannot read `{$filename}`.");
        }

        return new self($data);
    }

    /**
     * Advances past bytes without interpreting them: an unknown column whole block.
     * The column-oriented layout is what makes this one call the entirety of skipping.
     */
    public function skip(int $byteCount): void
    {
        $remaining = \strlen($this->bytes) - $this->position;

        if ($byteCount < 0 || $byteCount > $remaining) {
            throw new LiteBinaryException("Cannot skip {$byteCount} bytes with {$remaining} remaining.");
        }

        $this->position += $byteCount;
    }

    // Promotions: a member reading a file element narrower than itself. Only the
    // mathematically lossless directions exist; checkColumn already refused the rest.

    /** An int member from i32 or varint. */
    public function readI32As(int $element): int
    {
        return $element === self::ELEMENT_I32 ? $this->readInt32() : $this->readCounter32();
    }

    /** A 64-bit member from i64, i32 or varint. */
    public function readI64As(int $element): int
    {
        if ($element === self::ELEMENT_I64) {
            return $this->readInt64();
        }

        return $element === self::ELEMENT_I32 ? $this->readInt32() : $this->readCounter32();
    }

    /** A float member from f64, f32 or i32 - all exact in a PHP float. */
    public function readF64As(int $element): float
    {
        if ($element === self::ELEMENT_F64) {
            return $this->readDouble();
        }

        return $element === self::ELEMENT_F32 ? $this->readFloat() : (float)$this->readInt32();
    }

    public function position(): int
    {
        return $this->position;
    }

    public function remaining(): int
    {
        return $this->length - $this->position;
    }

    // ------------------------------------------------------------ primitives

    public function readBool(): bool
    {
        return $this->readFixed8() !== 0;
    }

    public function readInt32(): int
    {
        $value = $this->readFixed32();

        // unpack('V') is unsigned, and PHP's int is signed, so anything with the
        // top bit set arrives as a large positive number rather than a negative
        // one. Folding it back is the whole of the difference.
        return $value >= 0x80000000 ? $value - 0x100000000 : $value;
    }

    public function readUint32(): int
    {
        return $this->readFixed32();
    }

    /**
     * A 64 bit value, assembled from two 32 bit halves.
     *
     * Not unpack('P'), which hands back an unsigned interpretation that PHP
     * cannot hold past 2^63 and silently turns into a float - losing exactly the
     * precision this format exists to preserve. Not unpack('q') either: that one
     * is machine byte order and the format is little endian regardless of where
     * it is read.
     *
     * The shift is arithmetic on PHP's signed 64 bit int, so a high half with its
     * top bit set produces the right negative value without any special case.
     */
    public function readInt64(): int
    {
        $low = $this->readFixed32();
        $high = $this->readFixed32();

        if ($high >= 0x80000000) {
            $high -= 0x100000000;
        }

        return ($high << 32) | $low;
    }

    public function readFloat(): float
    {
        $bytes = $this->take(4);

        /** @var array{1: float} $parsed */
        $parsed = \unpack('g', $bytes);

        return $parsed[1];
    }

    public function readDouble(): float
    {
        $bytes = $this->take(8);

        /** @var array{1: float} $parsed */
        $parsed = \unpack('e', $bytes);

        return $parsed[1];
    }

    /** UTF-8 bytes. A PHP string is a byte string, so nothing is decoded here. */
    public function readString(): string
    {
        $length = $this->readCounter32();

        if ($length < 0) {
            throw new LiteBinaryException("A string length of {$length} is negative.");
        }

        return $this->take($length);
    }

    /**
     * Ticks, for both datetime and timespan.
     *
     * Not DateTimeImmutable: a sheet reaches 0001-01-01 and TimeSpan's full
     * range, PHP's date types carry microseconds rather than ticks, and a value
     * a caller only passes through should not be rounded on the way past.
     */
    public function readDateTimeTicks(): int
    {
        return $this->readInt64();
    }

    public function readTimespanTicks(): int
    {
        return $this->readInt64();
    }

    public function readUuid(): Uuid
    {
        return new Uuid($this->take(16));
    }

    /** An enum's underlying value, which travels zig-zag encoded. */
    public function readEnum(): int
    {
        return $this->readCounter32();
    }

    /** The element count in front of a variable length array. */
    public function readCounter32(): int
    {
        $encoded = $this->readVarint32();

        // Zig-zag: the sign is in the low bit. The mask keeps the shift inside
        // 32 bits, since PHP's int is wider than the value being decoded.
        $value = (($encoded >> 1) ^ -($encoded & 1)) & 0xFFFFFFFF;

        return $value >= 0x80000000 ? $value - 0x100000000 : $value;
    }

    // --------------------------------------------------------------- header

    /**
     * Reads and checks the file header, returning the row count that follows it.
     *
     * The reserved byte is written as zero and is where compression or
     * encryption flags would go; a non-zero value means the file needs handling
     * this build does not have.
     */
    /**
     * Reads and checks a table file header.
     *
     * Returns [rowCount, columns]: the column descriptors the data blocks follow, each
     * an array with tag, element, kind, count and byteLength keys.
     */
    public function readTableHeader(): array
    {
        $version = $this->readFixed32();

        if ($version !== self::FILE_FORMAT_VERSION) {
            throw new LiteBinaryException(
                "Table format version {$version} is not supported (expected "
                . self::FILE_FORMAT_VERSION . ').'
            );
        }

        if ($this->readFixed8() !== 0) {
            throw new LiteBinaryException('The table declares unsupported features.');
        }

        $rowCount = $this->readCounter32();

        if ($rowCount < 0) {
            throw new LiteBinaryException("The table row count {$rowCount} is negative.");
        }

        $columnCount = $this->readCounter32();

        if ($columnCount < 0) {
            throw new LiteBinaryException("The table column count {$columnCount} is negative.");
        }

        $columns = [];

        for ($at = 0; $at < $columnCount; $at++) {
            $tag = $this->readCounter32();
            $wire = $this->readFixed8();
            $count = $this->readCounter32();
            $byteLength = $this->readFixed32();

            $columns[] = [
                'tag' => $tag,
                'element' => $wire & 0x0F,
                'kind' => ($wire >> 4) & 0x03,
                'count' => $count,
                'byteLength' => $byteLength,
            ];
        }

        // What the descriptors say about the file, checked before anybody allocates for the
        // row count. The blocks are all that follows the header, so their declared lengths have
        // to add up to the bytes left, and every row costs at least one byte in every block - a
        // varint's shortest form, an empty string's length prefix, a variable array's counter.
        // A row count larger than that is one the exporter could not have written.

        $available = $this->remaining();
        $declared = 0;

        foreach ($columns as $column) {
            if ($column['byteLength'] < 0 || $column['byteLength'] > $available - $declared) {
                throw new LiteBinaryException(
                    "Column tag {$column['tag']} declares {$column['byteLength']} bytes, which " .
                    'the file cannot hold.');
            }

            $declared += $column['byteLength'];

            if ($rowCount > $column['byteLength']) {
                throw new LiteBinaryException(
                    "The row count {$rowCount} is larger than column tag {$column['tag']} can " .
                    "hold in its {$column['byteLength']} bytes.");
            }
        }

        if ($declared !== $available) {
            throw new LiteBinaryException(
                "The columns declare {$declared} bytes but {$available} follow the header.");
        }

        return [$rowCount, $columns];
    }

    /**
     * That a column is what the generated member expects, or a lossless promotion of it.
     * Refusal is by name and both types, never by reading anyway.
     */
    public static function checkColumn(array $column, string $fieldName, int $kind, int $count, array $accepted): void
    {
        if ($column['kind'] !== $kind || ($kind !== self::KIND_VAR_ARRAY && $column['count'] !== $count)) {
            throw new LiteBinaryException(
                "{$fieldName}: the file column (kind {$column['kind']}, count {$column['count']}) "
                . "does not match the generated member (kind {$kind}, count {$count}). The schema "
                . 'changed shape; regenerate the code or rebuild the data.'
            );
        }

        if (!\in_array($column['element'], $accepted, true)) {
            throw new LiteBinaryException(
                "{$fieldName}: the file carries element type {$column['element']}, which this "
                . 'member cannot read. The column changed type incompatibly; regenerate the code '
                . 'or rebuild the data.'
            );
        }
    }

    /**
     * That a block was consumed exactly: a mismatch is a format disagreement, and stopping
     * here names the column instead of corrupting the next.
     */
    public function checkBlockEnd(array $column, int $expectedEnd): void
    {
        if ($this->position !== $expectedEnd) {
            $short = $expectedEnd - $this->position;
            throw new LiteBinaryException(
                "Column tag {$column['tag']}: its block declared {$column['byteLength']} bytes "
                . "but the read ended {$short} bytes short of its boundary."
            );
        }
    }

    // ---------------------------------------------------------------- inner

    private function take(int $count): string
    {
        if ($count < 0 || $this->remaining() < $count) {
            throw new LiteBinaryException(
                "Table data ended after {$this->position} of {$this->length} bytes "
                . "while {$count} more were expected."
            );
        }

        $slice = \substr($this->data, $this->position, $count);

        $this->position += $count;

        return $slice;
    }

    private function readFixed8(): int
    {
        return \ord($this->take(1));
    }

    private function readFixed32(): int
    {
        /** @var array{1: int} $parsed */
        $parsed = \unpack('V', $this->take(4));

        return $parsed[1];
    }

    private function readVarint32(): int
    {
        $value = 0;

        for ($shift = 0; $shift < 35; $shift += 7) {
            $byte = $this->readFixed8();

            $value |= ($byte & 0x7F) << $shift;

            if (($byte & 0x80) === 0) {
                return $value & 0xFFFFFFFF;
            }
        }

        throw new LiteBinaryException('A varint32 is longer than five bytes.');
    }
}
