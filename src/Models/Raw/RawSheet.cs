using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;

namespace SheetMan.Models.Raw
{
    /// <summary>
    ///
    /// </summary>
    public class RawSheet
    {
        /// <summary></summary>
        [JsonIgnore]
        public Location Location { get; set; }

        /// <summary></summary>
        public int ColumnCount { get; set; }

        /// <summary></summary>
        public List<List<RawCell>> Rows { get; set; } = new List<List<RawCell>>();

        /// <summary>
        ///
        /// </summary>
        public bool Optimize()
        {
            // Remove top empty rows
            int topEmptyRowCount = 0;
            foreach (var row in this.Rows)
            {
                if (!IsWholeEmptyRow(row))
                    break;

                topEmptyRowCount++;
            }
            if (topEmptyRowCount > 0)
                this.Rows.RemoveRange(0, topEmptyRowCount);

            // Remove bottom empty rows
            int bottomEmptyRowCount = 0;
            for (int i = this.Rows.Count - 1; i >= 0; --i)
            {
                if (IsWholeEmptyRow(this.Rows[i]))
                    bottomEmptyRowCount++;
                else
                    break;
            }
            if (bottomEmptyRowCount > 0)
                this.Rows.RemoveRange(this.Rows.Count - bottomEmptyRowCount, bottomEmptyRowCount);

            // Expand max columns.
            int maxColumnCount = 0;
            foreach (var row in this.Rows)
            {
                if (row.Count > maxColumnCount)
                    maxColumnCount = row.Count;
            }

            int leftEmptyColumnCount = 0;
            for (int i = 0; i < maxColumnCount; i++)
            {
                if (!IsWholeEmptyColumn(this.Rows, i))
                    break;

                leftEmptyColumnCount++;
            }
            if (leftEmptyColumnCount > 0)
            {
                maxColumnCount -= leftEmptyColumnCount;
                foreach (var row in this.Rows)
                {
                    if (row.Count >= leftEmptyColumnCount)
                        row.RemoveRange(0, leftEmptyColumnCount);
                }
            }

            int rowIndex = this.Location.Row + topEmptyRowCount;
            foreach (var row in this.Rows)
            {
                int fill = maxColumnCount - row.Count;
                if (fill == 0)
                    continue;

                int colIndex = row.Count > 0 ? row[^1].Location.Column + 1 : this.Location.Column;

                for (int i = 0; i < fill; i++)
                {
                    RawCell rawCell = new RawCell
                    {
                        Location = new Location
                        {
                            Filename = this.Location.Filename,
                            Sheet = this.Location.Sheet,
                            Column = colIndex + i + leftEmptyColumnCount,
                            Row = rowIndex
                        },
                        Value = "",
                        Note = ""
                    };
                    row.Add(rawCell);
                }
                rowIndex++;
            }

            int rightEmptyColumnCount = 0;
            for (int i = maxColumnCount-1; i >= 0; i--)
            {
                if (!IsWholeEmptyColumn(this.Rows, i))
                    break;

                rightEmptyColumnCount++;
            }
            if (rightEmptyColumnCount > 0)
            {
                foreach (var row in this.Rows)
                    row.RemoveRange(row.Count - rightEmptyColumnCount, rightEmptyColumnCount);
                maxColumnCount -= rightEmptyColumnCount;
            }

            this.ColumnCount = maxColumnCount;


            // googlesheets에서 최적화를 하는건지 중간에 빈 로우를 건너뛰는 이슈가 있음.
            // 채워주는 기능을 만들자.

            var rows2 = new List<List<RawCell>>();

            for (int rowIdx = 0; rowIdx < Rows.Count; rowIdx++)
            {
                var currentRow = Rows[rowIdx];

                rows2.Add(currentRow);

                if (rowIdx < Rows.Count - 1)
                {
                    var nextRow = Rows[rowIdx+1];

                    int distance = nextRow[0].Location.Row - currentRow[0].Location.Row;
                    if (distance > 1)
                    {
                        //Log.Information($"건너뛴 로우 채워주기: {distance-1}");

                        for (int insertion = 0; insertion < distance - 1; insertion++)
                        {
                            var row = new List<RawCell>(maxColumnCount);

                            for (int colIdx = 0; colIdx < maxColumnCount; colIdx++)
                            {
                                var cell = new RawCell {
                                    Location = currentRow[0].Location.CloneWithXY(currentRow[0].Location.Column + colIdx, currentRow[0].Location.Row + rowIdx + 1),
                                    Value = "",
                                    Note = ""
                                };
                                row.Add(cell);
                            }

                            rows2.Add(row);
                        }
                    }
                }
            }

            Rows = rows2;


            Log.Information($"Optimize sheet: {this.Location}");
            Log.Information($"  => {this.ColumnCount}x{this.Rows.Count} (Shrink => T:{topEmptyRowCount}, B:{bottomEmptyRowCount}, L:{leftEmptyColumnCount}, R:{rightEmptyColumnCount})");

            return this.Rows.Count > 0 && maxColumnCount > 0;
        }

        private bool IsWholeEmptyRow(List<RawCell> row)
        {
            foreach (var cell in row)
            {
                if (cell.Value.Length > 0)
                    return false;
            }

            return true;
        }

        private bool IsWholeEmptyColumn(List<List<RawCell>> rows, int column)
        {
            foreach (var row in rows)
            {
                if (row[column].Value.Length > 0)
                    return false;
            }

            return true;
        }
    }
}
