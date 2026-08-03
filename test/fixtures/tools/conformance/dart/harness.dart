// Conformance harness for the generated Dart reader.
//
// Reads Vectors.table through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

import 'dart:convert';
import 'dart:io';

import 'conformance_data.dart';

void main(List<String> args) {
  if (args.isEmpty) {
    stderr.writeln('usage: harness.dart <binary-directory>');
    exit(1);
  }

  final tables = Tables();
  tables.readAll(args[0]);

  final json = StringBuffer('[');

  for (var position = 0; position < tables.vectors.records.length; position++) {
    if (position > 0) json.write(',');

    final r = tables.vectors.records[position];

    json.write('{');
    json.write('"index":${r.index},');
    json.write('"intVal":${r.intVal},');

    // A string, because JSON's single numeric type would round anything past 2^53 - which
    // is the very reason this field is a BigInt rather than an int.
    json.write('"bigVal":"${r.bigVal}",');

    json.write('"floatVal":${r.floatVal},');
    json.write('"doubleVal":${r.doubleVal},');
    json.write('"text":${quote(r.text)},');
    json.write('"flag":${r.flag},');

    // Ticks, which is what the generated fields hold.
    json.write('"when":"${r.when}",');
    json.write('"span":"${r.span}",');

    json.write('"uid":"${r.uid}",');
    json.write('"label":${r.label.value},');

    json.write('"ints":[${r.ints.join(',')}],');
    json.write('"strs":[${r.strs.map(quote).join(',')}]');
    json.write('}');
  }

  json.write(']');

  // Written as UTF-8 bytes rather than through stdout's encoding, which on Windows is a
  // legacy codepage and would mangle every non-ASCII value in the corpus.
  stdout.add(utf8.encode(json.toString()));
}

String quote(String value) {
  final quoted = StringBuffer('"');

  for (final rune in value.runes) {
    if (rune == 0x22) {
      quoted.write(r'\"');
    } else if (rune == 0x5C) {
      quoted.write(r'\\');
    } else if (rune == 0x0A) {
      quoted.write(r'\n');
    } else if (rune == 0x0D) {
      quoted.write(r'\r');
    } else if (rune == 0x09) {
      quoted.write(r'\t');
    } else if (rune < 0x20) {
      quoted.write('\\u${rune.toRadixString(16).padLeft(4, '0')}');
    } else {
      quoted.writeCharCode(rune);
    }
  }

  return (quoted..write('"')).toString();
}
