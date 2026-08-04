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
    public const FILE_FORMAT_VERSION = 100;

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
    public function readTableHeader(): int
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

        return $rowCount;
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
