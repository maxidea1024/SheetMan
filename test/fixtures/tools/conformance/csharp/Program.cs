// Conformance harness for the generated C# reader.
//
// Reads Vectors.table through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using SheetMan.Conformance;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: conformance-csharp <binary-directory>");
            return 1;
        }

        await Tables.ReadAllAsync(args[0]);

        var json = new StringBuilder("[");

        for (int i = 0; i < Tables.Vectors.Records.Count; i++)
        {
            var r = Tables.Vectors.Records[i];

            if (i > 0)
                json.Append(',');

            json.Append('{');
            json.Append("\"index\":").Append(Number(r.Index)).Append(',');
            json.Append("\"intVal\":").Append(Number(r.IntVal)).Append(',');
            json.Append("\"bigVal\":\"").Append(Number(r.BigVal)).Append("\",");
            json.Append("\"floatVal\":").Append(Number(r.FloatVal)).Append(',');
            json.Append("\"doubleVal\":").Append(Number(r.DoubleVal)).Append(',');
            json.Append("\"text\":").Append(Quote(r.Text)).Append(',');
            json.Append("\"flag\":").Append(r.Flag ? "true" : "false").Append(',');
            json.Append("\"when\":\"").Append(Number(r.When.Ticks)).Append("\",");
            json.Append("\"span\":\"").Append(Number(r.Span.Ticks)).Append("\",");
            json.Append("\"uid\":\"").Append(r.Uid.ToString("D").ToLowerInvariant()).Append("\",");
            json.Append("\"label\":").Append(Number((int)r.Label)).Append(',');

            json.Append("\"ints\":[");
            for (int k = 0; k < r.Ints.Length; k++)
                json.Append(k > 0 ? "," : "").Append(Number(r.Ints[k]));
            json.Append("],");

            json.Append("\"strs\":[");
            for (int k = 0; k < r.Strs.Length; k++)
                json.Append(k > 0 ? "," : "").Append(Quote(r.Strs[k]));
            json.Append(']');

            // The reference indices, which is what the exporter writes for a foreign field.
            json.Append(",\"owner\":").Append(r._owner_Owners_index);
            json.Append(",\"tier\":").Append(r._tier_Owners_index);

            json.Append('}');
        }

        json.Append(']');

        // UTF-8 without a byte order mark: the comparison reads this back as text, and a
        // mark would land inside the first token.
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Out.Write(json.ToString());
        return 0;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Round-trip format, so the printed value is the one that was read.</summary>
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string value)
    {
        var quoted = new StringBuilder("\"");

        foreach (var c in value ?? "")
        {
            if (c == '"')
                quoted.Append("\\\"");
            else if (c == '\\')
                quoted.Append("\\\\");
            else if (c == '\n')
                quoted.Append("\\n");
            else if (c == '\r')
                quoted.Append("\\r");
            else if (c == '\t')
                quoted.Append("\\t");
            else if (c < 0x20)
                quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                quoted.Append(c);
        }

        return quoted.Append('"').ToString();
    }
}
