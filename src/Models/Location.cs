namespace SheetMan.Models;

/// <summary>
/// Declared Cell Location
/// </summary>
public class Location
{
    /// <summary>.xlsx file name or google-sheet id</summary>
    public string Filename { get; set; }

    /// <summary>Set only for Google Sheets, where a cell has a URL to link to.</summary>
    public string SheetUrl { get; set; } = ""; // built per cell rather than cached, so moving an entity updates its link

    /// <summary>Sheet Name</summary>
    public string Sheet { get; set; }

    /// <summary>Column</summary>
    public int Column { get; set; }

    /// <summary>Row</summary>
    public int Row { get; set; }

    public Location CloneWithXY(int column, int row)
    {
        return new Location {
            Filename = this.Filename,
            SheetUrl = this.SheetUrl,
            Sheet = this.Sheet,
            Column = column,
            Row = row,
        };
    }

    public override string ToString()
    {
        //return $"{Filename} : {Sheet} : {CellRange}";
        //return $"{Filename}/{Sheet}:{CellRange}";
        if (!string.IsNullOrEmpty(SheetUrl))
            return SheetUrl;

        return $"{Filename} : {Sheet} : {CellRange}";
    }

    public string CellRange => $"{ColumnName(Column)}{Row + 1}";

    /// <summary>
    /// Spreadsheet column label for a zero-based column index:
    /// 0 -> A, 25 -> Z, 26 -> AA, 701 -> ZZ, 702 -> AAA.
    ///
    /// This is bijective base-26, not plain base-26: there is no zero digit, so
    /// each carry subtracts one. Getting it wrong is what made every reference
    /// past column X point at the wrong cell, including the `&range=` fragment in
    /// the Google Sheets deep links.
    /// </summary>
    public static string ColumnName(int column)
    {
        if (column < 0)
            return "?";

        var name = new System.Text.StringBuilder();

        for (int n = column; n >= 0; n = n / 26 - 1)
            name.Insert(0, (char)('A' + n % 26));

        return name.ToString();
    }
}
