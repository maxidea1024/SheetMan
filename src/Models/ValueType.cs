namespace SheetMan.Models
{
    /// <summary>
    /// Field Value Type.
    /// </summary>
    public enum ValueType
    {
        /// <summary></summary>
        None = 0,

        /// <summary></summary>
        String = 1,
        /// <summary></summary>
        Bool = 2,
        /// <summary></summary>
        Int32 = 3,
        /// <summary></summary>
        Int64 = 4,
        /// <summary></summary>
        Float = 5,
        /// <summary></summary>
        Double = 6,
        /// <summary></summary>
        TimeSpan = 7,
        /// <summary></summary>
        DateTime = 8,
        /// <summary></summary>
        Uuid = 9,
        /// <summary></summary>
        Enum = 10,
        /// <summary></summary>
        ForeignRecord = 11,
        /// <summary></summary>
        Unresolved = 12,

        /// <summary></summary>
        StringArray = 32,
        /// <summary></summary>
        BoolArray = 33,
        /// <summary></summary>
        Int32Array = 34,
        /// <summary></summary>
        Int64Array = 35,
        /// <summary></summary>
        FloatArray = 36,
        /// <summary></summary>
        DoubleArray = 37,
        /// <summary></summary>
        TimeSpanArray = 38,
        /// <summary></summary>
        DateTimeArray = 39,
        /// <summary></summary>
        UuidArray = 40,
        /// <summary></summary>
        EnumArray = 41,
        /// <summary></summary>
        ForeignRecordArray = 42,
    }
}
