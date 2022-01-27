using Newtonsoft.Json;
using System.Collections.Generic;

namespace SheetMan.Models.Raw
{
    /// <summary></summary>
    public class RawCell
    {
        /// <summary></summary>
        [JsonIgnore]
        public Location Location { get; set; }

        /// <summary></summary>
        public string Value { get; set; }

        /// <summary></summary>
        public string Note { get; set; }
    }
}
