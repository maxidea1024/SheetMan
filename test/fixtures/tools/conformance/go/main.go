// Conformance harness for the generated Go reader.
//
// Reads Vectors.scb through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

package main

import (
	"encoding/json"
	"fmt"
	"os"
	"strconv"

	"conformance"
	"conformance/sheetman"
)

// The two array forms whose element read is not the scalar one in a loop, rendered as the
// contract asks: an enum as its number, a uuid as its text.
func labels(values []conformance.Flag) []int32 {
	out := make([]int32, 0, len(values))
	for _, value := range values {
		out = append(out, int32(value))
	}
	return out
}

func uuids(values []sheetman.UUID) []string {
	out := make([]string, 0, len(values))
	for _, value := range values {
		out = append(out, value.String())
	}
	return out
}

func main() {
	if len(os.Args) < 2 {
		fmt.Fprintln(os.Stderr, "usage: harness <binary-directory>")
		os.Exit(1)
	}

	var tables conformance.Tables
	if err := tables.ReadAll(os.Args[1]); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	rows := make([]map[string]any, 0, len(tables.Vectors.Records()))

	for _, r := range tables.Vectors.Records() {
		rows = append(rows, map[string]any{
			"index":     r.Index,
			"intVal":    r.IntVal,
			"bigVal":    strconv.FormatInt(r.BigVal, 10),
			"floatVal":  r.FloatVal,
			"doubleVal": r.DoubleVal,
			"text":      r.Text,
			"flag":      r.Flag,

			// Ticks, which is what the generated fields hold: a time.Time cannot express
			// year 1 and a time.Duration cannot express TimeSpan.MaxValue, both of which
			// the corpus contains.
			"when": strconv.FormatInt(r.When, 10),
			"span": strconv.FormatInt(r.Span, 10),

			"uid":   r.Uid.String(),
			"label": int32(r.Label),
			"ints":  r.Ints,
			"strs":  r.Strs,

			// The two array forms whose element read is not the scalar one in a loop.
			"labels": labels(r.Labels),
			"uids":   uuids(r.Uids),

			// The reference indices, which is what the exporter writes for a foreign field.
			"owner": r.OwnerIndex,
			"tier":  r.TierIndex,
		})
	}

	encoded, err := json.Marshal(rows)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	os.Stdout.Write(encoded)
}
