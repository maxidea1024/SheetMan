namespace SheetMan.Models
{
    /// <summary>
    /// Declared Cell Location
    /// </summary>
    public class Location
    {
        /// <summary>.xlsx file name or google-sheet id</summary>
        public string Filename { get; set; }

        // 구글 스프레드 시트일 경우에 한해서 유효함.
        public string SheetUrl { get; set; } = ""; //캐싱된 값을 사용하면 문제가 있음.(위치를 임의로 변경했을 경우 갱신이 안됨

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

        public string CellRange
        {
            get
            {
                string c = "";
                if (Column >= 24)
                {
                    c += "A";
                    c += char.ToString((char)('A' + (Column % 24)));
                }
                else
                {
                    c = char.ToString((char)('A' + Column));
                }

                return $"{c}{Row + 1}";
            }
        }
    }
}
