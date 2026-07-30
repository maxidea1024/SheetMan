# Conformance harnesses

One per output language. Each reads `Vectors.table` through that language's generated
accessor and prints what it found; the suite compares the result against what the JSON
exporter wrote from the same cells.

This is what makes adding a language cost a harness rather than a gate of its own. A
harness is expected to be about fifty lines and to contain no parsing: the generated
reader does that, and the harness only prints.

## Output contract

A JSON array, one object per row, keys named as the exporter names them. Values in the
canonical form below, so that a harness formats as little as possible and the comparison
is not at the mercy of how a language renders a date.

| Sheet type | Printed as |
|--|--|
| `int`, `float`, `double` | JSON number |
| `bigint` | JSON string, decimal digits - past 2^53 a JSON number is not exact |
| `string` | JSON string |
| `bool` | JSON boolean |
| `datetime`, `timespan` | JSON string, .NET ticks in decimal - exact, and no formatting to disagree about |
| `uuid` | JSON string, lower case with hyphens |
| `enum` | JSON number |
| any array | JSON array of the above |

Print to standard output and nothing else. Exit non-zero on failure with a message on
standard error.
