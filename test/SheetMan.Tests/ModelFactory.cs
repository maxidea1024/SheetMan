using System.Collections.Generic;
using SheetMan.Models;

using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.Tests
{
    /// <summary>
    /// Builds small models in memory, for the tests that ask a question about the model
    /// itself rather than about a conversion.
    ///
    /// The end-to-end gates convert a committed workbook, which is the right shape for
    /// "does this recipe produce that output". It is the wrong shape for "does changing one
    /// cell change exactly one row's hash": expressing that as a workbook means a second
    /// .xlsx differing in one cell, and reviewing the test then means opening Excel.
    /// </summary>
    internal static class ModelFactory
    {
        /// <summary>A model holding the given tables and nothing else.</summary>
        public static Model Of(params Table[] tables)
        {
            var model = new Model();

            foreach (var table in tables)
                model.Tables.Add(table);

            return model;
        }

        /// <summary>
        /// A table whose first column is its primary index.
        /// </summary>
        /// <param name="rows">One array per row, one value per column, in column order.</param>
        public static Table Table(
            string name,
            IReadOnlyList<(string Name, ValueType Type)> columns,
            params object[][] rows)
        {
            var table = new Models.Table
            {
                Name = name,
                RawName = name,
                TargetSide = TargetSide.Both,
                Location = At(name, 0, 0),
            };

            for (int column = 0; column < columns.Count; column++)
            {
                table.Fields.Add(new Field
                {
                    Name = columns[column].Name,
                    RawName = columns[column].Name,
                    Type = columns[column].Type,
                    TypeName = columns[column].Type.ToString().ToLowerInvariant(),
                    TargetSide = TargetSide.Both,
                    Index = column,
                    Indexing = column == 0,
                    OwnerTable = table,
                    NameLocation = At(name, column, 1),
                });
            }

            for (int row = 0; row < rows.Length; row++)
            {
                var cells = new List<Cell>(columns.Count);

                for (int column = 0; column < columns.Count; column++)
                {
                    cells.Add(new Cell
                    {
                        Value = rows[row][column],
                        RawCell = new Models.Raw.RawCell { Location = At(name, column, row + 2) },
                    });
                }

                table.Data.Add(cells);
            }

            return table;
        }

        private static Location At(string sheet, int column, int row)
            => new Location { Filename = "memory.xlsx", Sheet = sheet, Column = column, Row = row };
    }
}
