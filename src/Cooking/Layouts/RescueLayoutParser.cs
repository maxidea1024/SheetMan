using System.Collections.Generic;
using System.Linq;
using Serilog;
using SheetMan.Extensions;
using SheetMan.Models;
using SheetMan.Models.Raw;

namespace SheetMan.Cooking.Layouts
{
    /// <summary>
    /// The layout an existing project already had: one table per sheet, named by the sheet tab,
    /// with three header rows above the data.
    /// </summary>
    /// <remarks>
    /// <code>
    ///     row 1   field descriptions   &lt;- a `#` prefix drops the column
    ///     row 2   field names          &lt;- the first is the index
    ///     row 3   field types          &lt;- `int`, `string`, `intArray`, `enum:Name`, ...
    ///     row 4   data rows...
    ///
    ///     a sheet named `*TableEnums*` instead holds every enum, one per column:
    ///
    ///     row 1   enum description     &lt;- a `#` column describes the enum to its left
    ///     row 2   enum name
    ///     row 3   labels...            &lt;- names only; values are the order they appear in
    /// </code>
    ///
    /// This exists so a project can be converted without first rewriting every workbook, and it
    /// is not the layout to start a new project in. Nothing here says which sheets are tables
    /// and which are working notes, no column can be marked for one target side, and the
    /// numbering that would make `Reward1`..`Reward5` an array in the sheetman layout means
    /// nothing at all - so those three things are recovered by convention, by the recipe naming
    /// its sheets, and by not folding anything.
    ///
    /// Where the two layouts can agree they do: type names, `#` as the mark for a commented-out
    /// column, `;` between the elements of an array cell, and every rule about what a value
    /// means all come from <see cref="CookingContext"/> unchanged.
    /// </remarks>
    [SheetManLayout("rescue",
        Summary = "One table per sheet, named by the sheet tab, with three header rows.")]
    public sealed class RescueLayoutParser : ILayoutParser
    {
        /// <summary>Row holding each column's description. A `#` here drops the column.</summary>
        private const int CommentRow = 0;

        /// <summary>Row holding each column's name.</summary>
        private const int NameRow = 1;

        /// <summary>Row holding each column's type.</summary>
        private const int TypeRow = 2;

        /// <summary>First row of data.</summary>
        private const int FirstDataRow = 3;

        /// <summary>
        /// What a sheet's name must contain for it to hold enum declarations rather than a
        /// table.
        /// </summary>
        /// <remarks>
        /// A convention, and one this layout has no better alternative to: there is no marker
        /// in the cells to go by, and the sheet is shaped nothing like a table - reading it as
        /// one would take the first enum's name as the index column.
        /// </remarks>
        private const string EnumSheetMarker = "TableEnums";

        /// <summary>
        /// The name given to the index column synthesized for a table whose own first column
        /// cannot be one.
        /// </summary>
        private const string SynthesizedIndexName = "Index";

        private CookingContext _context;

        private Model Model => _context.Model;

        public void ParseDeclarations(CookingContext context, IReadOnlyList<RawSheet> sheets)
        {
            _context = context;

            foreach (var sheet in sheets)
            {
                if (!IsEnumSheet(sheet))
                    continue;

                foreach (var enumm in ParseEnumSheet(sheet))
                {
                    if (Model.ContainsEnum(enumm.Name))
                    {
                        throw new SheetManException(enumm.Location,
                            $"Enum `{enumm.Name}` is already defined.");
                    }

                    Model.Enums.Add(enumm);
                }
            }
        }

        public void ParseTables(CookingContext context, IReadOnlyList<RawSheet> sheets)
        {
            _context = context;

            foreach (var sheet in sheets)
            {
                if (IsEnumSheet(sheet))
                    continue;

                var table = ParseTableSheet(sheet);
                if (table == null)
                    continue;

                if (Model.ContainsTable(table.Name))
                {
                    throw new SheetManException(table.Location,
                        $"Table `{table.Name}` is already defined. In this layout a table is named by its " +
                        "sheet tab, so two sheets of the same name in one source collide.");
                }

                Model.Tables.Add(table);
            }
        }

        private static bool IsEnumSheet(RawSheet sheet)
        {
            return (sheet.Location?.Sheet ?? "").Contains(EnumSheetMarker);
        }


        #region Enums

        /// <summary>
        /// Reads the enum-collection sheet: each enum is a column of label names, with the name
        /// of the enum in row 2 and a description in row 1.
        /// </summary>
        /// <remarks>
        /// A column whose description starts with `#` belongs to the enum on its left and holds
        /// per-label comments, which is how the workbook this layout was taken from annotates
        /// its labels.
        /// </remarks>
        private List<Models.Enum> ParseEnumSheet(RawSheet sheet)
        {
            Log.Information($"Parsing rescue enum sheet `{sheet.Location}`");

            var result = new List<Models.Enum>();

            if (sheet.Rows.Count <= NameRow)
                return result;

            var commentRow = sheet.Rows[CommentRow];
            var nameRow = sheet.Rows[NameRow];

            for (int colIdx = 0; colIdx < sheet.ColumnCount; colIdx++)
            {
                // A description column annotates whatever enum it follows, so it is consumed
                // by that enum rather than read on its own.
                if (_context.IsIgnorantName(commentRow[colIdx].Value))
                    continue;

                var nameCell = nameRow[colIdx];
                if (nameCell.Value.Length == 0)
                    continue;

                string rawName = nameCell.Value;
                string name = rawName.ToPascalCase();

                _context.RequiresIdentifier(name, nameCell.Location);

                int commentColumn = DescriptionColumnFor(sheet, colIdx);

                result.Add(ParseEnumColumn(sheet, colIdx, commentColumn, rawName, name, commentRow[colIdx].Value));
            }

            return result;
        }

        /// <summary>
        /// The column carrying per-label comments for the enum in <paramref name="colIdx"/>, or
        /// -1 when it has none.
        /// </summary>
        private int DescriptionColumnFor(RawSheet sheet, int colIdx)
        {
            int next = colIdx + 1;

            if (next >= sheet.ColumnCount)
                return -1;

            return _context.IsIgnorantName(sheet.Rows[CommentRow][next].Value) ? next : -1;
        }

        private Models.Enum ParseEnumColumn(
            RawSheet sheet, int colIdx, int commentColumn, string rawName, string name, string comment)
        {
            var result = new Models.Enum
            {
                Location = sheet.Rows[NameRow][colIdx].Location,
                TargetSide = TargetSide.Both,
                RawName = rawName,
                Name = name,
                Comment = comment,
                Labels = new List<Models.Enum.Label>(),
            };

            // Values are the order the labels appear in, because this layout does not write
            // them down. That is safe as long as it stays internally consistent - every data
            // cell names a label rather than a number - and it is why a label must never be
            // reordered or removed once data has been exported from it.
            int nextValue = 1;

            for (int rowIdx = TypeRow; rowIdx < sheet.Rows.Count; rowIdx++)
            {
                var labelCell = sheet.Rows[rowIdx][colIdx];

                // A blank ends the enum: the columns are independent and each runs as far as
                // it has labels.
                if (labelCell.Value.Length == 0)
                    break;

                string labelRawName = labelCell.Value;
                string labelName = labelRawName.ToPascalCase();

                if (_context.IsIgnorantName(labelName))
                    continue;

                _context.RequiresIdentifier(labelName, labelCell.Location);

                if (result.Contains(labelName))
                {
                    throw new SheetManException(labelCell.Location,
                        $"Label '{labelName}' is already defined in enum '{result.Name}'.");
                }

                // `None` takes zero wherever it sits, so a default-constructed field of this
                // type reads as the label the sheet already uses for "nothing".
                int value = labelName == "None" ? 0 : nextValue++;

                result.Labels.Add(new Models.Enum.Label
                {
                    Location = labelCell.Location,
                    RawName = labelRawName,
                    Name = labelName,
                    Value = value,
                    Comment = commentColumn >= 0 ? sheet.Rows[rowIdx][commentColumn].Value : "",
                });
            }

            _context.ApplyAutoNoneLabel(result, result.Location);

            return result;
        }

        #endregion


        #region Tables

        /// <summary>
        /// Reads one sheet as a table, or returns null when it does not look like one.
        /// </summary>
        /// <remarks>
        /// A workbook in this layout holds reference tabs and working notes next to its data,
        /// and nothing in a sheet says which it is. The shape of the header is the only
        /// evidence there is, so a sheet whose second row does not open with a name and whose
        /// third does not open with a type is left alone and reported.
        ///
        /// Naming the sheets in the recipe's `IncludeSheets` is the way to not rely on this:
        /// a sheet that was asked for and does not parse is a mistake worth hearing about, and
        /// that is what <see cref="Sources.SheetFilter"/> already reports.
        /// </remarks>
        private Models.Table ParseTableSheet(RawSheet sheet)
        {
            string sheetName = sheet.Location?.Sheet ?? "";

            if (sheet.Rows.Count <= FirstDataRow || sheet.ColumnCount == 0)
            {
                Log.Warning($"Skipping sheet `{sheetName}`: it has no data rows under a three-row header. ({sheet.Location})");
                return null;
            }

            string firstName = sheet.Rows[NameRow][0].Value;
            string firstType = sheet.Rows[TypeRow][0].Value;

            // The type has to be one this layout recognizes, not merely present. A reference
            // tab in these workbooks reads `CharacterType` over `PC` in its first column -
            // a perfectly good identifier over a cell that is simply data - so a check that
            // only asked whether the type cell was filled took the tab for a table and failed
            // on the unknown type `pc` instead of leaving it alone.
            if (!firstName.ToPascalCase().IsValidIdentifier() || !IsTypeSpelling(firstType))
            {
                Log.Warning(
                    $"Skipping sheet `{sheetName}`: row {NameRow + 1} should open with a field name and " +
                    $"row {TypeRow + 1} with its type, and they hold `{firstName}` and `{firstType}`. " +
                    $"({sheet.Location})");
                return null;
            }

            string rawName = sheetName;
            string name = rawName.ToPascalCase();

            _context.RequiresIdentifier(name, sheet.Location);

            Log.Information($"Parsing rescue table `{name}`. ({sheet.Location})");

            var table = new Models.Table
            {
                Location = sheet.Location,
                TargetSide = TargetSide.Both,
                RawName = rawName,
                Name = name,

                // The sheet has no room for a table description: row 1 describes the index
                // column, which is where that text belongs and where it goes.
                Comment = "",

                // Nothing in this layout means what a trailing number means in the other one.
                FoldSerialFields = false,
            };

            var columns = ParseFields(table, sheet);

            ParseData(table, sheet, columns);

            _context.AssignTags(table);

            return table;
        }

        /// <summary>
        /// One column of the sheet, and where the field that came from it reads its value.
        /// </summary>
        private sealed class DataColumn
        {
            /// <summary>Column of the sheet, or -1 for the synthesized index.</summary>
            public int SheetColumn;

            public Field Field;
        }

        private List<DataColumn> ParseFields(Models.Table table, RawSheet sheet)
        {
            var commentRow = sheet.Rows[CommentRow];
            var nameRow = sheet.Rows[NameRow];
            var typeRow = sheet.Rows[TypeRow];

            var columns = new List<DataColumn>();

            for (int colIdx = 0; colIdx < sheet.ColumnCount; colIdx++)
            {
                var commentCell = commentRow[colIdx];
                var nameCell = nameRow[colIdx];
                var typeCell = typeRow[colIdx];

                // The three ways this layout switches a column off, all of them in use in the
                // workbooks it was taken from: a `#` on the description, an empty name or type,
                // and a leading underscore on the name.
                if (_context.IsIgnorantName(commentCell.Value))
                    continue;

                if (nameCell.Value.Length == 0 || typeCell.Value.Length == 0)
                    continue;

                if (nameCell.Value.StartsWith("_"))
                    continue;

                string fieldRawName = nameCell.Value;
                string fieldName = fieldRawName.ToPascalCase();

                _context.RequiresIdentifier(fieldName, nameCell.Location);

                var clash = table.FindField(fieldName);
                if (clash != null)
                {
                    // Named in full because the two columns usually do not look alike in the
                    // sheet: `IconPath` and `Icon_Path` are different headings to whoever
                    // typed them, and only become the same name here. Saying just the
                    // normalized name leaves the author hunting for a duplicate that, as far
                    // as the sheet is concerned, is not there.
                    string how = clash.RawName == fieldRawName
                        ? "the same name twice"
                        : $"`{clash.RawName}` and `{fieldRawName}`, which differ only in punctuation or case";

                    throw new SheetManException(nameCell.Location,
                        $"Table `{table.Name}` has two columns called `{fieldName}`: it uses {how}. " +
                        $"Names are normalized so that every language gets the same member out of them, " +
                        $"and two columns cannot share one. Rename one of them, or put a `#` on the " +
                        $"description of the column that is no longer used. " +
                        $"(the other is at {clash.NameLocation})");
                }

                var field = new Field
                {
                    OwnerTable = table,
                    NameLocation = nameCell.Location,
                    TypeLocation = typeCell.Location,
                    DetailTypeLocation = typeCell.Location,

                    // No column of this layout can be marked for one side. Pointed at the type
                    // cell so that if the shared index check ever does complain about a side,
                    // it names a cell that exists.
                    TargetSideLocation = typeCell.Location,
                    TargetSide = TargetSide.Both,

                    Index = table.Fields.Count,
                    Comment = commentCell.Value,
                    RawName = fieldRawName,
                    Name = fieldName,

                    // The first column of the sheet is the index, the way the first column of
                    // an entity is in the other layout. Whether it can actually be one is
                    // settled below.
                    Indexing = colIdx == 0,
                };

                ApplyType(field, typeCell);

                table.Fields.Add(field);
                columns.Add(new DataColumn { SheetColumn = colIdx, Field = field });
            }

            if (table.Fields.Count == 0)
                throw new SheetManException(sheet.Location, $"Table `{table.Name}` has no usable columns.");

            EnsureIntegerPrimaryIndex(table, sheet, columns);

            _context.CheckPrimaryIndexValidity(table.Fields[0]);

            return columns;
        }

        /// <summary>
        /// Reads a rescue type name onto a field.
        /// </summary>
        /// <remarks>
        /// The spellings differ from SheetMan's in three ways and no more: case is not
        /// consistent in the sheets (`Int`, `int`, `IntArray`, `Intarray`), an array is a
        /// `...Array` suffix rather than `[]`, and an enum is `enum:Name` in the one cell
        /// rather than `enum` plus a detail-type cell. They are rewritten here and handed to
        /// the shared type reader, so the two layouts cannot come to different conclusions
        /// about what a type is.
        ///
        /// `foreign` has no spelling because the layout has no way to write one. A converted
        /// project gets its references back by adding them to the sheets, which is a change to
        /// the data rather than to this.
        /// </remarks>
        private void ApplyType(Field field, RawCell typeCell)
        {
            string declared = typeCell.Value.Trim();
            var spelling = ReadTypeSpelling(declared);

            if (spelling.EnumName != null)
            {
                string enumName = spelling.EnumName.ToPascalCase();

                if (enumName.Length == 0)
                {
                    throw new SheetManException(typeCell.Location,
                        "In case of enum type, the enum name must follow `enum:`.");
                }

                if (!Model.ContainsEnum(enumName))
                {
                    throw new SheetManException(typeCell.Location,
                        $"Column `{field.Name}` is typed `{declared}`, but no enum named `{enumName}` was " +
                        $"declared. Enums come from the sheet whose name contains `{EnumSheetMarker}`.");
                }

                field.TypeName = enumName;
                field.Type = spelling.IsArray ? Models.ValueType.EnumArray : Models.ValueType.Enum;
                return;
            }

            _context.RequiresValidTypeName(spelling.Name, typeCell.Location);

            var elementType = _context.ParseValueType(spelling.Name, typeCell.Location);

            field.TypeName = spelling.Name;

            if (!spelling.IsArray)
            {
                field.Type = elementType;
                return;
            }

            var arrayType = Models.ValueTypes.ArrayOf(elementType);
            if (arrayType == Models.ValueType.None)
                throw new SheetManException(typeCell.Location, $"type `{spelling.Name}` cannot be used as an array element.");

            field.Type = arrayType;
        }

        /// <summary>
        /// Splits a rescue type spelling into the pieces the shared type reader takes.
        /// </summary>
        /// <returns>
        /// The SheetMan type name, whether the column holds a delimited list, and the enum's
        /// name when the spelling was `enum:Name` (null otherwise).
        /// </returns>
        private static (string Name, bool IsArray, string EnumName) ReadTypeSpelling(string declared)
        {
            string text = (declared ?? "").Trim();
            string normalized = text.ToLowerInvariant();

            // The `Array` suffix comes off first so that the name behind it - including an
            // enum's - is read exactly as a scalar's would be. Taking the enum name off the
            // undivided text instead would look up `GradeTypeArray`.
            bool isArray = normalized.EndsWith("array");
            if (isArray)
            {
                text = text.Substring(0, text.Length - "array".Length).Trim();
                normalized = text.ToLowerInvariant();
            }

            if (normalized.StartsWith("enum:"))
            {
                // The enum's name is kept as written rather than lower-cased: only the
                // `enum:` in front of it is a keyword.
                return ("enum", isArray, text.Substring(text.IndexOf(':') + 1).Trim());
            }

            // `long` is what these sheets call a 64-bit integer; SheetMan calls it `bigint`.
            if (normalized == "long")
                normalized = "bigint";

            return (normalized, isArray, null);
        }

        /// <summary>
        /// Whether a cell holds something this layout would accept as a type at all.
        /// </summary>
        /// <remarks>
        /// Asked of a sheet's first type cell to decide whether the sheet is a table, so it
        /// has to answer without throwing - which is the whole difference between it and
        /// <see cref="CookingContext.RequiresValidTypeName"/>, whose list it defers to.
        /// </remarks>
        private bool IsTypeSpelling(string declared)
        {
            if (string.IsNullOrWhiteSpace(declared))
                return false;

            var spelling = ReadTypeSpelling(declared);

            if (spelling.EnumName != null)
                return spelling.EnumName.Length > 0;

            return _context.IsValidTypeName(spelling.Name);
        }

        /// <summary>
        /// Gives the table an integer primary index, making one up when its own first column
        /// cannot be one.
        /// </summary>
        /// <remarks>
        /// Every row is addressed by an integer key downstream - the binary format, the
        /// generated readers and reference resolution all assume it - and one real table in
        /// this layout keys itself by a string instead. Rather than refuse the table or quietly
        /// change what its key means, an ordinal column is put in front of it and the original
        /// column is kept as a secondary index, which is already a supported thing for a string
        /// column to be. Callers therefore still look the row up by the key the sheet uses.
        /// </remarks>
        private void EnsureIntegerPrimaryIndex(Models.Table table, RawSheet sheet, List<DataColumn> columns)
        {
            var first = table.Fields[0];

            if (first.Type == Models.ValueType.Int32)
                return;

            if (table.ContainsField(SynthesizedIndexName))
            {
                throw new SheetManException(first.NameLocation,
                    $"Table `{table.Name}` is keyed by `{first.Name}`, which is `{first.Type}` rather than " +
                    $"`int`, so an integer index column has to be added - but the table already has a " +
                    $"column called `{SynthesizedIndexName}`. Rename it, or make the first column an int.");
            }

            Log.Warning(
                $"Table `{table.Name}` is keyed by `{first.Name}`, which is `{first.Type}` rather than `int`. " +
                $"An ordinal `{SynthesizedIndexName}` column has been added as the primary index and " +
                $"`{first.Name}` kept as a secondary one, so rows are still looked up by it. ({first.NameLocation})");

            var synthesized = new Field
            {
                OwnerTable = table,
                NameLocation = sheet.Location,
                TypeLocation = sheet.Location,
                DetailTypeLocation = sheet.Location,
                TargetSideLocation = sheet.Location,
                TargetSide = TargetSide.Both,
                Index = 0,
                Comment = $"Row number, added by SheetMan because `{first.Name}` cannot be a primary index.",
                RawName = SynthesizedIndexName,
                Name = SynthesizedIndexName,
                TypeName = "int",
                Type = Models.ValueType.Int32,
                Indexing = true,
            };

            // The original stays an index - a secondary one, which may be a string - so the
            // key the sheet is written around is still what callers look a row up by.
            first.Indexing = true;

            table.Fields.Insert(0, synthesized);
            columns.Insert(0, new DataColumn { SheetColumn = -1, Field = synthesized });

            // Field.Index addresses a cell of a row, so every column after the inserted one
            // moves along by one.
            for (int i = 0; i < table.Fields.Count; i++)
                table.Fields[i].Index = i;
        }

        private void ParseData(Models.Table table, RawSheet sheet, List<DataColumn> columns)
        {
            var indexField = table.Fields[0];
            int indexColumn = columns[0].SheetColumn;

            // Where each index value was first seen, so a duplicate can name the row it
            // collides with and `keep-last` can go back and replace it.
            var rowsByIndex = new Dictionary<object, int>();

            var policy = (sheet.Layout ?? SheetLayout.Default).OnDuplicateIndex;

            for (int rowIdx = FirstDataRow; rowIdx < sheet.Rows.Count; rowIdx++)
            {
                var rawRow = sheet.Rows[rowIdx];

                if (IsBlankRow(rawRow, columns))
                    continue;

                // The index cell decides whether the row is data at all. A `#` in front of it
                // is how these sheets comment a row out, and a blank one is a row somebody
                // started and did not finish - both are dropped, but only the second is worth
                // a word, because the first was meant.
                string indexText = indexColumn >= 0 ? rawRow[indexColumn].Value : "";

                if (indexColumn >= 0)
                {
                    if (_context.IsIgnorantName(indexText))
                        continue;

                    if (indexText.Length == 0)
                    {
                        Log.Warning(
                            $"Skipping row {rowIdx + 1} of `{table.Name}`: it has no `{indexField.Name}` but is " +
                            $"not empty, so it is an unfinished row rather than the end of the table. " +
                            $"({rawRow[0].Location})");
                        continue;
                    }
                }

                var row = ReadRow(table, sheet, columns, rowIdx);

                object indexValue = row[indexField.Index].Value;

                if (rowsByIndex.TryGetValue(indexValue, out int firstRow))
                {
                    // `error` leaves the duplicate in place for ValidateIndexUniqueness, which
                    // reports every one of them at once against the location of each cell.
                    // Dropping it here would report one per run instead.
                    if (ApplyDuplicatePolicy(policy, table, indexField, indexValue, rowIdx, firstRow, row))
                        continue;
                }
                else
                {
                    rowsByIndex.Add(indexValue, table.Data.Count);
                }

                table.Data.Add(row);
            }
        }

        /// <summary>
        /// Acts on a repeated index value.
        /// </summary>
        /// <returns>True when the row has been dealt with and must not be appended.</returns>
        private bool ApplyDuplicatePolicy(
            DuplicateIndexPolicy policy, Models.Table table, Field indexField,
            object indexValue, int sheetRow, int firstDataRow, List<Cell> row)
        {
            switch (policy)
            {
                case DuplicateIndexPolicy.KeepFirst:
                    Log.Warning(
                        $"Dropping row {sheetRow + 1} of `{table.Name}`: `{indexField.Name}` is `{indexValue}`, " +
                        $"which an earlier row already used. ({row[indexField.Index].RawCell.Location})");
                    return true;

                case DuplicateIndexPolicy.KeepLast:
                    Log.Warning(
                        $"Replacing an earlier row of `{table.Name}` with row {sheetRow + 1}: `{indexField.Name}` " +
                        $"is `{indexValue}`, which both use. ({row[indexField.Index].RawCell.Location})");
                    table.Data[firstDataRow] = row;
                    return true;

                default:
                    return false;
            }
        }

        private List<Cell> ReadRow(Models.Table table, RawSheet sheet, List<DataColumn> columns, int rowIdx)
        {
            var rawRow = sheet.Rows[rowIdx];
            var row = new List<Cell>(columns.Count);

            foreach (var column in columns)
            {
                var field = column.Field;

                // The synthesized index has no cell of its own, so it gets one: the row's
                // ordinal, anchored on the first real cell so a diagnostic about it still
                // points at the row it is about.
                var rawCell = column.SheetColumn >= 0
                    ? rawRow[column.SheetColumn]
                    : new RawCell
                    {
                        Location = rawRow[0].Location,
                        Value = (table.Data.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Note = "",
                    };

                row.Add(new Cell
                {
                    RawCell = rawCell,
                    Value = _context.ParseValue(field.Type, field.EnumOrNull, rawCell.Value, rawCell.Location),
                });
            }

            return row;
        }

        /// <summary>
        /// Whether a row holds nothing in any column the table actually reads.
        /// </summary>
        /// <remarks>
        /// Asked of the read columns rather than the whole row, so a stray note left in a
        /// dropped `#` column does not keep an otherwise empty row alive.
        /// </remarks>
        private static bool IsBlankRow(List<RawCell> rawRow, List<DataColumn> columns)
        {
            return columns.All(column => column.SheetColumn < 0 || rawRow[column.SheetColumn].Value.Length == 0);
        }

        #endregion
    }
}
