using SheetMan.Models.Raw;

namespace SheetMan.Models
{
    /// <summary>Cell</summary>
    public class Cell
    {
        /// <summary>Raw cell</summary>
        public RawCell RawCell { get; set; }

        /// <summary>Imported value</summary>
        public object Value { get; set; }
    }
}
