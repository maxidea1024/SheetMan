# Conformance harness for the generated Python reader.
#
# Reads Vectors.scb through the generated accessor and prints each row in the canonical
# form described in ../README.md. No parsing here: the generated reader does that.

import json
import struct
import sys

from conformance import Tables


def single(value):
    """A float32 value as the shortest decimal that round-trips it.

    Python has no single-precision float, so the reader hands back a double holding the
    stored value. Printing that shows digits the original 32 bits never carried, so it
    is narrowed back before printing - which is the same step the TypeScript reader
    makes with Math.fround for the same reason.
    """
    return struct.unpack("<f", struct.pack("<f", value))[0]


def main():
    if len(sys.argv) < 2:
        sys.stderr.write("usage: harness.py <binary-directory>\n")
        return 1

    tables = Tables()
    tables.read_all(sys.argv[1])

    rows = []

    for record in tables.vectors.records:
        rows.append({
            "index": record.index,
            "int_val": record.int_val,

            # A string, because JSON's single numeric type would round anything past 2^53
            # - which two of the corpus rows are.
            "big_val": str(record.big_val),

            "float_val": single(record.float_val),
            "double_val": record.double_val,
            "text": record.text,
            "flag": record.flag,

            # Ticks, which is what the generated fields hold: datetime cannot express a
            # tick and timedelta cannot express TimeSpan.MaxValue.
            "when": str(record.when),
            "span": str(record.span),

            "uid": str(record.uid),
            "label": int(record.label),
            "ints": list(record.ints),
            "strs": list(record.strs),

            # The two array forms whose element read is not the scalar one in a loop.
            "labels": [int(value) for value in record.labels],
            "uids": [str(value) for value in record.uids],

            # The reference indices, which is what the exporter writes for a foreign field.
            "owner": record.owner_index,
            "tier": record.tier_index,
        })

    sys.stdout.write(json.dumps(rows, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
