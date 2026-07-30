namespace SheetMan.Models
{
    /// <summary>
    /// What a field holds, as written in a table's type row.
    ///
    /// The array members start at 32 rather than following on from the scalars, leaving
    /// room to add scalar types without renumbering - and letting
    /// <see cref="ValueTypes.IsArray"/> be a single comparison.
    /// </summary>
    public enum ValueType
    {
        /// <summary>Not set.</summary>
        None = 0,

        /// <summary>`string` - UTF-8 text.</summary>
        String = 1,
        /// <summary>`bool` - Y/N, YES/NO, TRUE/FALSE, 1/0, or blank for false.</summary>
        Bool = 2,
        /// <summary>`int` - 32-bit signed integer.</summary>
        Int32 = 3,
        /// <summary>`bigint` - 64-bit signed integer.</summary>
        Int64 = 4,
        /// <summary>`float` - single precision.</summary>
        Float = 5,
        /// <summary>`double` - double precision.</summary>
        Double = 6,
        /// <summary>`timespan` - a duration, carried as 100 ns ticks.</summary>
        TimeSpan = 7,
        /// <summary>`datetime` - a point in time, carried as 100 ns ticks.</summary>
        DateTime = 8,
        /// <summary>`uuid` - a 128-bit identifier.</summary>
        Uuid = 9,
        /// <summary>A label of an `enum` entity declared in the sheets.</summary>
        Enum = 10,

        /// <summary>
        /// A `foreign` reference to a whole row of another table. Stored as that row's
        /// primary index and turned into a reference by the generated code once every
        /// table is loaded.
        /// </summary>
        ForeignRecord = 11,

        /// <summary>
        /// Placeholder for a reference whose target is not known yet.
        ///
        /// Table data is parsed before references are resolved, so nothing should still
        /// hold this by the time a value is read - a field that does means resolution
        /// never ran for it.
        /// </summary>
        Unresolved = 12,

        /// <summary>`string[]`</summary>
        StringArray = 32,
        /// <summary>`bool[]`</summary>
        BoolArray = 33,
        /// <summary>`int[]`</summary>
        Int32Array = 34,
        /// <summary>`bigint[]`</summary>
        Int64Array = 35,
        /// <summary>`float[]`</summary>
        FloatArray = 36,
        /// <summary>`double[]`</summary>
        DoubleArray = 37,
        /// <summary>`timespan[]`</summary>
        TimeSpanArray = 38,
        /// <summary>`datetime[]`</summary>
        DateTimeArray = 39,
        /// <summary>`uuid[]`</summary>
        UuidArray = 40,
        /// <summary>`enum[]`</summary>
        EnumArray = 41,

        /// <summary>
        /// Reserved. `foreign[]` is rejected by the cooker: resolving a varying number of
        /// references per row is a shape the generated readers do not have.
        /// </summary>
        ForeignRecordArray = 42,
    }

    /// <summary>
    /// Relates each scalar value type to its array counterpart.
    ///
    /// SheetMan has two separate notions of "array":
    ///
    ///   * a serial field, where consecutively numbered columns (Text1, Text2, ...)
    ///     are folded into one array. Every row has the same number of elements,
    ///     because the count is the number of columns.
    ///
    ///   * an array type, written `int[]` in the type row, where one cell holds
    ///     several delimited values. Length varies from row to row.
    ///
    /// The array ValueType members below the scalars have existed since the start but
    /// nothing ever produced one; they describe the second kind.
    /// </summary>
    public static class ValueTypes
    {
        /// <summary>Whether this type describes a delimited array cell.</summary>
        public static bool IsArray(ValueType type) => type >= ValueType.StringArray;

        /// <summary>
        /// The element type of an array type; the type itself when it is already
        /// scalar, so callers can normalize without testing first.
        /// </summary>
        public static ValueType ElementOf(ValueType type)
        {
            return type switch
            {
                ValueType.StringArray => ValueType.String,
                ValueType.BoolArray => ValueType.Bool,
                ValueType.Int32Array => ValueType.Int32,
                ValueType.Int64Array => ValueType.Int64,
                ValueType.FloatArray => ValueType.Float,
                ValueType.DoubleArray => ValueType.Double,
                ValueType.TimeSpanArray => ValueType.TimeSpan,
                ValueType.DateTimeArray => ValueType.DateTime,
                ValueType.UuidArray => ValueType.Uuid,
                ValueType.EnumArray => ValueType.Enum,
                ValueType.ForeignRecordArray => ValueType.ForeignRecord,
                _ => type,
            };
        }

        /// <summary>
        /// The array type holding <paramref name="element"/>, or None when there is no
        /// array form of it.
        /// </summary>
        public static ValueType ArrayOf(ValueType element)
        {
            return element switch
            {
                ValueType.String => ValueType.StringArray,
                ValueType.Bool => ValueType.BoolArray,
                ValueType.Int32 => ValueType.Int32Array,
                ValueType.Int64 => ValueType.Int64Array,
                ValueType.Float => ValueType.FloatArray,
                ValueType.Double => ValueType.DoubleArray,
                ValueType.TimeSpan => ValueType.TimeSpanArray,
                ValueType.DateTime => ValueType.DateTimeArray,
                ValueType.Uuid => ValueType.UuidArray,
                ValueType.Enum => ValueType.EnumArray,
                ValueType.ForeignRecord => ValueType.ForeignRecordArray,
                _ => ValueType.None,
            };
        }
    }
}
