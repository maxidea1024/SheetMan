# Conformance harness for the generated Ruby reader.
#
# Reads Vectors.table through the generated accessor and prints each row in the canonical
# form described in ../README.md. No parsing here: the generated reader does that.

require_relative 'conformance_data'

def quote(value)
  quoted = +'"'

  value.each_char do |c|
    case c
    when '"' then quoted << '\\"'
    when '\\' then quoted << '\\\\'
    when "\n" then quoted << '\\n'
    when "\r" then quoted << '\\r'
    when "\t" then quoted << '\\t'
    else
      quoted << (c.ord < 0x20 ? format('\\u%04x', c.ord) : c)
    end
  end

  quoted << '"'
end

if ARGV.empty?
  warn 'usage: harness.rb <binary-directory>'
  exit 1
end

tables = Conformance::Tables.new
tables.read_all(ARGV[0])

json = +'['

tables.vectors.records.each_with_index do |r, position|
  json << ',' if position.positive?

  json << '{'
  json << '"index":' << r.index.to_s << ','
  json << '"intVal":' << r.int_val.to_s << ','

  # A string, because JSON's single numeric type would round anything past 2^53.
  json << '"bigVal":"' << r.big_val.to_s << '",'

  json << '"floatVal":' << r.float_val.to_s << ','
  json << '"doubleVal":' << r.double_val.to_s << ','
  json << '"text":' << quote(r.text) << ','
  json << '"flag":' << r.flag.to_s << ','

  # Ticks, which is what the generated fields hold.
  json << '"when":"' << r.when_.to_s << '",'
  json << '"span":"' << r.span.to_s << '",'

  json << '"uid":"' << r.uid.to_s << '",'
  json << '"label":' << r.label.to_s << ','

  json << '"ints":[' << r.ints.map(&:to_s).join(',') << '],'
  json << '"strs":[' << r.strs.map { |value| quote(value) }.join(',') << ']'
  json << '}'
end

json << ']'

# Written as bytes: Ruby would otherwise transcode to the default external encoding, which
# on Windows is a legacy codepage and would mangle every non-ASCII value in the corpus.
$stdout.binmode
$stdout.write(json.encode(Encoding::UTF_8))
$stdout.flush
