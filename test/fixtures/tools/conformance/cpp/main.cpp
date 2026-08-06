// Conformance harness for the generated C++ reader.
//
// Reads Vectors.table through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

#include <cstdint>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>

#include SHEETMAN_ACCESSOR_HEADER

namespace {

// Round-trip precision, so the printed value is the one that was read. Nine and
// seventeen significant digits are what it takes for float and double respectively.
std::string number(float value) {
  std::ostringstream out;
  out << std::setprecision(9) << value;
  return out.str();
}

std::string number(double value) {
  std::ostringstream out;
  out << std::setprecision(17) << value;
  return out.str();
}

std::string quote(const std::string& value) {
  std::ostringstream out;
  out << '"';

  for (unsigned char c : value) {
    if (c == '"') {
      out << "\\\"";
    } else if (c == '\\') {
      out << "\\\\";
    } else if (c == '\n') {
      out << "\\n";
    } else if (c == '\r') {
      out << "\\r";
    } else if (c == '\t') {
      out << "\\t";
    } else if (c < 0x20) {
      out << "\\u" << std::hex << std::setw(4) << std::setfill('0')
          << static_cast<int>(c) << std::dec;
    } else {
      // Bytes above 0x7f pass through: the source was UTF-8 and so is the output.
      out << static_cast<char>(c);
    }
  }

  out << '"';
  return out.str();
}

}  // namespace

int main(int argc, char** argv) {
  if (argc < 2) {
    std::cerr << "usage: conformance-cpp <binary-directory>\n";
    return 1;
  }

  sheetman_conformance::Tables tables;
  tables.read_all(argv[1]);

  std::ostringstream json;
  json << '[';

  const auto& records = tables.vectors().records();

  for (std::size_t i = 0; i < records.size(); ++i) {
    const auto& r = records[i];

    if (i > 0) json << ',';

    json << '{';
    json << "\"index\":" << r.index << ',';
    json << "\"int_val\":" << r.int_val << ',';
    json << "\"big_val\":\"" << r.big_val << "\",";
    json << "\"float_val\":" << number(r.float_val) << ',';
    json << "\"double_val\":" << number(r.double_val) << ',';
    json << "\"text\":" << quote(r.text) << ',';
    json << "\"flag\":" << (r.flag ? "true" : "false") << ',';
    json << "\"when\":\"" << sheetman::to_net_ticks(r.when) << "\",";
    json << "\"span\":\"" << r.span.count() << "\",";
    json << "\"uid\":\"" << r.uid.to_string() << "\",";
    json << "\"label\":" << static_cast<std::int32_t>(r.label) << ',';

    json << "\"ints\":[";
    for (std::size_t k = 0; k < r.ints.size(); ++k)
      json << (k > 0 ? "," : "") << r.ints[k];
    json << "],";

    json << "\"strs\":[";
    for (std::size_t k = 0; k < r.strs.size(); ++k)
      json << (k > 0 ? "," : "") << quote(r.strs[k]);
    json << "],";

    // The two array forms whose element read is not the scalar one in a loop.
    json << "\"labels\":[";
    for (std::size_t k = 0; k < r.labels.size(); ++k)
      json << (k > 0 ? "," : "") << static_cast<std::int32_t>(r.labels[k]);
    json << "],";

    json << "\"uids\":[";
    for (std::size_t k = 0; k < r.uids.size(); ++k)
      json << (k > 0 ? "," : "") << '"' << r.uids[k].to_string() << '"';
    json << ']';

    // The reference indices, which is what the exporter writes for a foreign field.
    json << ",\"owner\":" << r.owner_index;
    json << ",\"tier\":" << r.tier_index;

    json << '}';
  }

  json << ']';

  std::cout << json.str();
  return 0;
}
