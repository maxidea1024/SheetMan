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
| `foreign` | JSON number - the **stored index**, which is what the exporter writes |
| any array | JSON array of the above |

The corpus carries two references into a second table, `owner` pointing at a whole row and
`tier` at one of that row's fields. They are compared as the index each came in as, because
that is what the exporter has to compare against - it writes the stored index, not the value
a reference resolves to.

What they are really for is the loading. Splitting each target's output into a file per table
gave every language a question it did not have before - how does one table's file reach
another's - and a harness that loads through the accessor runs the reference resolution
whether or not the answer is compared. A language whose split output cannot see the other
table's file does not compile, or does not load, and never reaches the comparison at all.

Print to standard output and nothing else. Exit non-zero on failure with a message on
standard error.

## The constant set

The corpus also carries a constant set, `Limits`. No harness prints it and the comparison
never looks at it - constants are not rows, so there is nothing in the exporter's JSON to
compare against.

It is there so that every language's constants file is generated and then compiled, or
required, or imported. Nothing did that before: neither this corpus nor `reserved-words` -
the only other scenario generating for all twelve - had a constant set, so splitting each
target's output into a file per table produced a constants file in twelve languages that
nothing ever built. Rust proved the cost of that: a constant typed with an enum names that
enum, the dependency graph did not say so, and the crate did not compile.

`DefaultFlag` and `BuildId` are the two that earn their place. An enum-typed constant makes
the file depend on an enum declared elsewhere, and a uuid makes it depend on the reader.
Every other type is self-contained and is here only so the set is not misleadingly narrow.
