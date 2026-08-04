// Conformance harness for the generated Rust reader.
//
// Reads Vectors.table through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

use std::env;
use std::path::Path;
use std::process;

use conformance::{sheetman, Tables};

fn main() {
    let args: Vec<String> = env::args().collect();

    if args.len() < 2 {
        eprintln!("usage: harness <binary-directory>");
        process::exit(1);
    }

    let mut tables = Tables::default();

    if let Err(error) = tables.read_all(Path::new(&args[1])) {
        eprintln!("{}", error);
        process::exit(1);
    }

    let mut json = String::from("[");

    for (position, record) in tables.vectors.records().iter().enumerate() {
        if position > 0 {
            json.push(',');
        }

        json.push('{');
        json.push_str(&format!("\"index\":{},", record.index));
        json.push_str(&format!("\"int_val\":{},", record.int_val));
        json.push_str(&format!("\"big_val\":\"{}\",", record.big_val));

        // Exponent form: Rust's Display for a float never uses one, so a denormal comes
        // out as three hundred digits. Both are valid JSON; this one is readable.
        json.push_str(&format!("\"float_val\":{:e},", record.float_val));
        json.push_str(&format!("\"double_val\":{:e},", record.double_val));

        json.push_str(&format!("\"text\":{},", quote(&record.text)));
        json.push_str(&format!("\"flag\":{},", record.flag));

        // Ticks, which is what the generated fields hold: std has no date type, and the
        // corpus reaches 0001-01-01 and TimeSpan.MaxValue either way.
        json.push_str(&format!("\"when\":\"{}\",", record.when));
        json.push_str(&format!("\"span\":\"{}\",", record.span));

        json.push_str(&format!("\"uid\":\"{}\",", record.uid));
        json.push_str(&format!("\"label\":{},", record.label.value()));

        json.push_str("\"ints\":[");
        for (i, value) in record.ints.iter().enumerate() {
            if i > 0 {
                json.push(',');
            }
            json.push_str(&value.to_string());
        }
        json.push_str("],");

        json.push_str("\"strs\":[");
        for (i, value) in record.strs.iter().enumerate() {
            if i > 0 {
                json.push(',');
            }
            json.push_str(&quote(value));
        }
        json.push(']');

        // The reference indices, which is what the exporter writes for a foreign field.
        json.push_str(&format!(",\"owner\":{}", record.owner_index));
        json.push_str(&format!(",\"tier\":{}", record.tier_index));

        json.push('}');
    }

    json.push(']');

    print!("{}", json);

    // Referenced so the import is not flagged as unused when the corpus has no uuid.
    let _ = sheetman::FORMAT_VERSION;
}

fn quote(value: &str) -> String {
    let mut quoted = String::from("\"");

    for c in value.chars() {
        match c {
            '"' => quoted.push_str("\\\""),
            '\\' => quoted.push_str("\\\\"),
            '\n' => quoted.push_str("\\n"),
            '\r' => quoted.push_str("\\r"),
            '\t' => quoted.push_str("\\t"),
            c if (c as u32) < 0x20 => quoted.push_str(&format!("\\u{:04x}", c as u32)),
            c => quoted.push(c),
        }
    }

    quoted.push('"');
    quoted
}
