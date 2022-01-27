using System.Collections.Generic;
using Newtonsoft.Json;

namespace SheetMan.Models
{
    /// <summary>
    ///
    /// </summary>
    public class Row
    {
        /// <summary>로우가 정의된 위치</summary>
        [JsonIgnore]
        public Location Location { get; set; }

        /// <summary>로우안에 위치한 셀 목록</summary>
        public List<Cell> Cells { get; set; }
    }
}
