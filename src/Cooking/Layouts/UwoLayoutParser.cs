using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Serilog;
using SheetMan.Extensions;
using SheetMan.Helpers;
using SheetMan.Models;
using SheetMan.Models.Raw;

namespace SheetMan.Cooking.Layouts;

/// <summary>
/// The layout of a live game's workbooks, where a table is a workbook's defined name.
/// </summary>
/// <remarks>
/// Neither markers nor sheet tabs: the boundary of a table is a rectangle the workbook has
/// a name for, so the tab's name means nothing and one sheet can carry several tables side
/// by side. Inside the rectangle:
///
///   row 1     property names; a blank one excludes the column
///   row 2     types
///   row 3..   rows whose first cell begins with `:` - `:required`, `:min`, `:links` and
///             the rest - which declare constraints rather than data. Their count differs
///             per table, so they are recognized by that `:` and not by position.
///   then      the data
///
/// The full survey, and what each piece of it is for, is in
/// doc/uwo-레이아웃-분석-20260808.md.
/// </remarks>
[SheetManLayout("uwo",
    Summary = "A table is a workbook's defined name; two header rows and ':'-keyed constraint rows.",
    UsesNamedRanges = true)]
public sealed class UwoLayoutParser : ILayoutParser
{
    /// <summary>Rows of the rectangle, counted from its top.</summary>
    private const int NameRow = 0;
    private const int TypeRow = 1;

    private CookingContext _context;

    /// <summary>
    /// Nothing to do: this layout declares no enums and no constant sets.
    /// </summary>
    /// <remarks>
    /// It has no way to. A column whose values come from a fixed set is a `number` with a
    /// `:enum` row listing them, which is a constraint on the data rather than a type -
    /// see §4.2 of the analysis. Turning those into real enums is a separate decision,
    /// because the labels are Korean display text rather than identifiers.
    /// </remarks>
    public void ParseDeclarations(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;
    }

    public void ParseTables(CookingContext context, IReadOnlyList<RawSheet> sheets)
    {
        _context = context;

        foreach (var sheet in sheets)
        {
            if (sheet.NamedRanges.Count == 0)
            {
                // Ordinary: a workbook in this layout holds working sheets beside its data,
                // and the way it says a sheet is data is by having a name for it.
                Log.Information(
                    $"Skipping sheet `{sheet.Location?.Sheet}`: no defined name covers it. ({sheet.Location})");
                continue;
            }

            foreach (var named in sheet.NamedRanges)
            {
                var table = ParseTable(sheet, named);
                if (table is not null)
                    context.Model.Tables.Add(table);
            }
        }
    }

    private Models.Table ParseTable(RawSheet sheet, RawNamedRange named)
    {
        // `_BCGL`, `_BCCN`: the same table built for one region. The suffix decides which
        // file the original exporter writes, and the table inside keeps the base name.
        // Kept whole here - two tables of one name would collide - and left as a thing to
        // decide, which §6 of the analysis records.
        string rawName = named.Name;

        if (named.Height <= TypeRow + 1)
        {
            Log.Warning(
                $"Skipping `{rawName}`: the range covers {named.Height} row(s), and a table needs "
                + $"a name row, a type row and data. ({sheet.Location})");
            return null;
        }

        var marker = CellAt(sheet, named, NameRow, 0);

        var table = new Models.Table
        {
            Location = marker.Location,
            TargetSide = TargetSide.Both,
            RawName = rawName,
            Name = rawName.ToPascalCase(),
            Comment = "",

            // No serial-number convention here. A number in a name is part of the name -
            // `OceanNpcLocal01` is one table, not element 1 of something - and folding on
            // it would invent arrays. Records come from the `[...]` notation instead.
            FoldSerialFields = false,
        };

        Log.Information($"Parsing table `{table.Name}`. ({marker.Location})");

        var columns = ParseFields(table, sheet, named);
        if (columns is null)
            return null;

        ParseData(table, sheet, named, columns);

        _context.AssignTags(table);

        return table;
    }

    /// <summary>One column of the rectangle that survived into the model.</summary>
    private sealed class DataColumn
    {
        public required int RangeColumn { get; init; }
        public required Field Field { get; init; }

        /// <summary>Base-2 text, which is what a `bit` column holds.</summary>
        public required bool IsBinaryText { get; init; }
    }

    private List<DataColumn> ParseFields(Models.Table table, RawSheet sheet, RawNamedRange named)
    {
        var columns = new List<DataColumn>();

        for (int col = 0; col < named.Width; col++)
        {
            var nameCell = CellAt(sheet, named, NameRow, col);
            var typeCell = CellAt(sheet, named, TypeRow, col);

            string rawFieldName = nameCell.Value.Trim();
            string rawType = typeCell.Value.Trim();

            // A blank name is how this layout parks a column, and it uses it constantly:
            // the Korean label beside each data column is one of these. `-` in either cell
            // says the same thing explicitly.
            if (rawFieldName.Length == 0 || rawFieldName == "-" || rawType == "-")
                continue;

            if (_context.IsIgnorantName(rawFieldName))
                continue;

            var field = BuildField(table, sheet, named, col, nameCell, typeCell, rawFieldName, rawType,
                                   out bool isBinaryText);
            if (field is null)
                continue;

            if (table.ContainsField(field.Name))
            {
                throw new SheetManException(nameCell.Location,
                    $"Table `{table.Name}` has two columns named `{field.Name}`.");
            }

            field.Index = table.Fields.Count;
            table.Fields.Add(field);
            columns.Add(new DataColumn { RangeColumn = col, Field = field, IsBinaryText = isBinaryText });
        }

        if (table.Fields.Count == 0)
        {
            Log.Warning($"Skipping `{table.RawName}`: no column of it has both a name and a type.");
            return null;
        }

        if (!table.Fields[0].Indexing)
        {
            throw new SheetManException(table.Location,
                $"Table `{table.Name}` has no `key` column. The first column of a table in this "
                + $"layout is typed `key`, which is what every row is addressed by.");
        }

        _context.CheckPrimaryIndexValidity(table.Fields[0]);

        return columns;
    }

    private Field BuildField(
        Models.Table table, RawSheet sheet, RawNamedRange named, int col,
        RawCell nameCell, RawCell typeCell, string rawFieldName, string rawType,
        out bool isBinaryText)
    {
        isBinaryText = false;

        // `number:sc`, `string:c`, `key:2000~3999`: the type cell carries the side, or the
        // key's permitted range.
        string[] typeParts = rawType.Split(':');
        string typeName = typeParts[0].Trim().ToLowerInvariant();
        string qualifier = typeParts.Length > 1 ? typeParts[1].Trim() : "";

        var field = new Field
        {
            OwnerTable = table,
            NameLocation = nameCell.Location,
            TypeLocation = typeCell.Location,
            DetailTypeLocation = typeCell.Location,
            TargetSideLocation = typeCell.Location,
            Comment = "",
            RawName = rawFieldName,
            TargetSide = TargetSide.Both,
        };

        // `character[0]["Id"]`: the column name is a path into the row's JSON. Translated
        // into the same record model SheetMan's own `Group.Member` notation produces, so
        // there is one model behind two notations rather than two models.
        if (!UwoColumnPath.TrySplit(rawFieldName, out var path, out string problem))
        {
            throw new SheetManException(nameCell.Location,
                $"Column `{rawFieldName}` of `{table.RawName}` {problem}");
        }

        if (path.IsNested)
        {
            field.GroupName = path.Group.ToPascalCase();
            field.MemberName = path.Member.ToPascalCase();
            field.GroupOrdinal = path.Ordinal;
            field.Name = path.Group.ToPascalCase() + path.Ordinal.ToString(CultureInfo.InvariantCulture)
                       + path.Member.ToPascalCase();
        }
        else
        {
            field.Name = rawFieldName.ToPascalCase();
        }

        _context.RequiresIdentifier(field.Name, nameCell.Location);

        switch (typeName)
        {
            case "key":
                field.Indexing = true;
                field.TypeName = "int";
                field.Type = Models.ValueType.Int32;

                // `key:0~200` is the range of ids this table may use. Recorded in the
                // original exporter's output and checked there; there is nowhere in this
                // model to put it, so it is dropped with a note rather than silently.
                if (qualifier.Contains('~'))
                    Log.Debug($"`{table.RawName}.{field.Name}` declares the key range `{qualifier}`, which is not carried into the model.");

                return field;

            case "number":
                field.TypeName = "int";
                field.Type = Models.ValueType.Int32;
                ApplySide(field, qualifier);
                return field;

            case "float":
                field.TypeName = "double";
                field.Type = Models.ValueType.Double;
                ApplySide(field, qualifier);
                return field;

            case "string":
            case "text":
                // `text` is a localized string. What separates it from `string` is that the
                // original exporter also collects it into a per-table list for the engine's
                // string table; the value in the data file is the same.
                field.TypeName = "string";
                field.Type = Models.ValueType.String;
                ApplySide(field, qualifier);
                return field;

            case "bool":
                field.TypeName = "bool";
                field.Type = Models.ValueType.Bool;
                ApplySide(field, qualifier);
                return field;

            case "bit":
                // A flag set written as base 2 - `1111111` is 127. Carried as a 64-bit
                // integer, and the digits are converted when the cell is read.
                field.TypeName = "bigint";
                field.Type = Models.ValueType.Int64;
                isBinaryText = true;
                ApplySide(field, qualifier);
                return field;

            case "strkey":
                throw new SheetManException(typeCell.Location,
                    $"Column `{rawFieldName}` of `{table.RawName}` is typed `strkey`. A string "
                    + $"primary index is not supported in this layout yet.");

            default:
                if (typeName.StartsWith('[') && typeName.EndsWith(']'))
                {
                    throw new SheetManException(typeCell.Location,
                        $"Column `{rawFieldName}` of `{table.RawName}` is typed `{rawType}`. Delimited "
                        + $"array columns are not supported in this layout yet.");
                }

                throw new SheetManException(typeCell.Location,
                    $"Column `{rawFieldName}` of `{table.RawName}` is typed `{rawType}`, which this "
                    + $"layout does not recognize.");
        }
    }

    /// <summary>
    /// Applies the side qualifier, where the type cell carries one.
    /// </summary>
    /// <remarks>
    /// Membership rather than equality, which is what the original exporter does: `sc` is
    /// both, `c` is the client, `s` is the server. Anything else leaves the column in both,
    /// because a qualifier this layout uses for something else - a key range - must not be
    /// read as "neither side wants this".
    /// </remarks>
    private static void ApplySide(Field field, string qualifier)
    {
        if (qualifier.Length == 0)
            return;

        bool server = qualifier.Contains('s');
        bool client = qualifier.Contains('c');

        if (server && !client)
            field.TargetSide = TargetSide.ServerOnly;
        else if (client && !server)
            field.TargetSide = TargetSide.ClientOnly;
    }

    private void ParseData(
        Models.Table table, RawSheet sheet, RawNamedRange named, List<DataColumn> columns)
    {
        for (int row = TypeRow + 1; row < named.Height; row++)
        {
            var keyCell = CellAt(sheet, named, row, 0);
            string key = keyCell.Value.Trim();

            // A row whose first cell begins with `:` declares constraints - `:required`,
            // `:min`, `:links` - rather than holding data. There is no fixed number of
            // them, which is why they are recognized here and not counted as header rows.
            if (key.StartsWith(':'))
                continue;

            // The end of the table. The range is often drawn over blank rows below the
            // data, and the original exporter stops the same way.
            if (key.Length == 0)
                break;

            // A commented-out row, the same convention the column names use.
            if (key.StartsWith('#'))
                continue;

            var cells = new List<Cell>(table.Fields.Count);

            foreach (var column in columns)
            {
                var rawCell = CellAt(sheet, named, row, column.RangeColumn);
                cells.Add(ReadCell(column, rawCell));
            }

            table.Data.Add(cells);
        }
    }

    /// <summary>
    /// Reads one cell into the value its column's type calls for.
    /// </summary>
    /// <remarks>
    /// `-` is this layout's "no value", and a blank cell is a mistake - the reverse of
    /// SheetMan's own layout, where a blank cell is an empty value. The original exporter
    /// reports a blank and tells the author to write `-`, so the two agree about which is
    /// which; here a `-` becomes the type's empty value, which is what the exporter's
    /// output holds by omitting the property.
    /// </remarks>
    private Cell ReadCell(DataColumn column, RawCell rawCell)
    {
        string text = rawCell.Value.Trim();

        // `-` is this layout's "no value", and the original exporter answers it by leaving
        // the property out of the row altogether. There is no absent here - every field of
        // every row has a value - so it becomes the type's empty one, which is what a
        // consumer reading that JSON gets for a missing property anyway.
        if (text == "-")
            return EmptyCell(column, rawCell);

        if (text.Length == 0)
        {
            // Blank is a mistake in this layout, and the original exporter says so: it
            // reports the cell and tells the author to write `-`. Reported rather than
            // refused, because refusing would stop a conversion of six hundred tables over
            // a cell whose intent is not in doubt.
            Log.Warning(
                $"`{column.Field.OwnerTable.Name}.{column.Field.Name}` is blank. This layout "
                + $"writes `-` for no value; read as the type's empty value.\n    at {rawCell.Location}");

            return EmptyCell(column, rawCell);
        }

        if (column.IsBinaryText)
        {
            try
            {
                text = Convert.ToInt64(text, 2).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                throw new SheetManException(rawCell.Location,
                    $"`{rawCell.Value}` is not a base-2 number, and column "
                    + $"`{column.Field.OwnerTable.Name}.{column.Field.Name}` is typed `bit`.");
            }
        }
        else if (IsInteger(column.Field.Type) && LooksHexadecimal(text))
        {
            // `0x5f0300` in a `number` column, which the data really does contain - colour
            // values are written that way. The original exporter puts the text straight
            // into its JSON and the JSON library it parses with accepts `0x`, so the number
            // that reaches the game is the hexadecimal one. Read the same value here rather
            // than a different one, or the same sheet would mean two things.
            try
            {
                text = Convert.ToInt64(text.Substring(2), 16).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                throw new SheetManException(rawCell.Location,
                    $"`{rawCell.Value}` begins `0x` but is not a hexadecimal number, and column "
                    + $"`{column.Field.OwnerTable.Name}.{column.Field.Name}` holds numbers.");
            }
        }

        return new Cell
        {
            RawCell = rawCell,
            Value = _context.ParseValue(
                column.Field.Type, column.Field.EnumOrNull, text, rawCell.Location),
        };
    }

    private static bool IsInteger(Models.ValueType type)
        => type == Models.ValueType.Int32 || type == Models.ValueType.Int64;

    private static bool LooksHexadecimal(string text)
        => text.Length > 2 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X');

    /// <summary>
    /// The type's empty value, for a cell that says it holds none.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than parsed from `""`: the value parser rejects an empty string
    /// for every numeric type, correctly - a blank where a number belongs is exactly what
    /// it exists to catch - and what is happening here is a column saying it has no value,
    /// which is a different thing.
    /// </remarks>
    private static Cell EmptyCell(DataColumn column, RawCell rawCell)
    {
        object value = column.Field.Type switch
        {
            Models.ValueType.Int32 => 0,
            Models.ValueType.Int64 => 0L,
            Models.ValueType.Double => 0.0,
            Models.ValueType.Float => 0.0f,
            Models.ValueType.Bool => false,
            _ => "",
        };

        return new Cell { RawCell = rawCell, Value = value };
    }

    /// <summary>A cell of the rectangle, addressed from its top-left.</summary>
    private static RawCell CellAt(RawSheet sheet, RawNamedRange named, int row, int column)
        => sheet.Rows[named.Row + row][named.Column + column];
}
