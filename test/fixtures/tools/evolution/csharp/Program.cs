// Skew harness for the generated C# reader.
//
// Reads one table out of a directory of .table files and prints what came back. The point
// is that the directory need not have been written by the schema this was generated from:
// a column added since is skipped, a column removed since keeps its default, a widened
// type is promoted, and an incompatible one is refused by name.
//
//     evolution-csharp <binary-directory> <table-name>
//
// Prints {"rows":[...]} on a successful read and {"error":"..."} on a refused one, and
// exits 0 either way - a refusal is an outcome to assert, not a harness failure.

using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SheetMan.Evolution;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: evolution-csharp <binary-directory> <table-name>");
            return 1;
        }

        string filename = Path.Combine(args[0], args[1] + ".table");

        try
        {
            IEnumerable rows = await Read(args[1], filename);

            var json = new StringBuilder("{\"rows\":[");
            bool first = true;

            foreach (object row in rows)
            {
                if (!first)
                    json.Append(',');

                // The generated record's own ToString is JSON of every field it has, which
                // is what lets one harness print two generations of the same table without
                // knowing what either of them looks like.
                json.Append(row.ToString());
                first = false;
            }

            json.Append("]}");

            Console.WriteLine(json.ToString());
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("{\"error\":" + Quote(e.Message) + "}");
            return 0;
        }
    }

    /// <summary>
    /// Reads one table by name. The three table types are what both generations have in
    /// common; their fields are not, and nothing here looks at those.
    /// </summary>
    private static async Task<IEnumerable> Read(string table, string filename)
    {
        switch (table)
        {
            case "Evolution":
            {
                var t = new EvolutionTable();
                await t.ReadAsync(filename);
                return t.Records;
            }

            case "Promoted":
            {
                var t = new PromotedTable();
                await t.ReadAsync(filename);
                return t.Records;
            }

            case "Refused":
            {
                var t = new RefusedTable();
                await t.ReadAsync(filename);
                return t.Records;
            }

            default:
                throw new ArgumentException($"No table called `{table}` in this generation.");
        }
    }

    private static string Quote(string value)
    {
        var sb = new StringBuilder("\"");

        foreach (char c in value ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}
