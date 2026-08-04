using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>Everything the C templates need, worked out in advance.</summary>
    internal sealed class CFileView
    {
        /// <summary>
        /// What every generated identifier starts with.
        ///
        /// C has no namespaces, so the prefix is the whole of the collision avoidance.
        /// Taken from the accessor name, lower_snake_case.
        /// </summary>
        public string Prefix { get; set; }

        /// <summary>The prefix in upper case, for the include guard and the enum constants.</summary>
        public string UpperPrefix { get; set; }

        /// <summary>Name of the header, so the .c can include it.</summary>
        public string HeaderName { get; set; }

        public IReadOnlyList<CEnumView> Enums { get; set; }
        public IReadOnlyList<CConstantView> Constants { get; set; }
        public IReadOnlyList<CTableView> Tables { get; set; }
        public CAccessorView Accessor { get; set; }
    }

    internal sealed class CEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<CEnumLabelView> Labels { get; set; }
    }

    internal sealed class CEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }

    /// <summary>
    /// One constant, already flattened out of its set.
    ///
    /// C has nothing to nest them in, so the set's name becomes part of each constant's
    /// name rather than a scope around them.
    /// </summary>
    internal sealed class CConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>
        /// Whether the value can be an initializer in a header.
        ///
        /// A uuid cannot: it is a struct, and one defined in a header would be a separate
        /// object in every translation unit that included it. Those go in the .c and are
        /// declared `extern` instead.
        /// </summary>
        public bool IsExtern { get; set; }
    }

    internal sealed class CTableView
    {
        public string RawName { get; set; }

        /// <summary>The record struct's name, already prefixed.</summary>
        public string RecordName { get; set; }

        /// <summary>The table struct's name, already prefixed.</summary>
        public string TableName { get; set; }

        /// <summary>What the table's functions are called, minus the verb.</summary>
        public string FunctionPrefix { get; set; }

        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Name of the primary index member.</summary>
        public string IndexField { get; set; }

        /// <summary>
        /// What one row costs at its very smallest.
        ///
        /// Every field encodes to at least one byte - a varint's shortest form, a fixed8,
        /// an empty string's length prefix - so this is the field count. It is what lets
        /// the generated code reject a row count larger than the file before allocating
        /// for it, rather than asking the allocator for four billion rows and finding out
        /// that way.
        /// </summary>
        public int MinRowBytes { get; set; }

        public IReadOnlyList<CFieldView> Fields { get; set; }
    }

    internal sealed class CFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The member declarations.
        ///
        /// A list, because a variable length array contributes a pointer and a count, and a
        /// reference contributes an index as well as the resolved pointer.
        /// </summary>
        public IReadOnlyList<string> Declarations { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        public int ElementCount { get; set; }

        /// <summary>The element's C type, for the array allocations.</summary>
        public string ElementType { get; set; }

        /// <summary>The complete reader call for a scalar member.</summary>
        public string ReadScalar { get; set; }

        /// <summary>The complete reader call for one element of an array member.</summary>
        public string ReadElement { get; set; }

        /// <summary>
        /// Whether reading needs a scratch int32 first, which only an enum does.
        ///
        /// The reader hands back the underlying value and the member is the enum type, so
        /// there is a cast in between and nowhere to put it in a single call.
        /// </summary>
        public bool NeedsScratch { get; set; }

        /// <summary>The enum's type name, when this field is one.</summary>
        public string EnumType { get; set; }
    }

    internal sealed class CAccessorView
    {
        /// <summary>The prefix its functions carry: `SheetManData`, giving `SheetManData_Free`.</summary>
        public string Name { get; set; }

        /// <summary>Its struct's name, which is the prefix with the type suffix.</summary>
        public string TypeName { get; set; }

        public string FileExtension { get; set; }
        public IReadOnlyList<CTableSlotView> Tables { get; set; }
        public IReadOnlyList<CCrossReferenceView> CrossReferences { get; set; }
    }

    internal sealed class CTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string FunctionPrefix { get; set; }
        public string DataFileName { get; set; }
    }

    internal sealed class CCrossReferenceView
    {
        public string Table { get; set; }
        public string FunctionPrefix { get; set; }

        /// <summary>The record struct being walked, which the loop declares a pointer to.</summary>
        public string RecordName { get; set; }

        public IReadOnlyList<CReferenceFieldView> Fields { get; set; }
    }

    internal sealed class CReferenceFieldView
    {
        public string Name { get; set; }
        public string RefTable { get; set; }
        public string RefFunctionPrefix { get; set; }

        /// <summary>The referenced record's struct, which the resolved member points at.</summary>
        public string RefRecordName { get; set; }

        /// <summary>What the resolved member is assigned, with `target` naming the row.</summary>
        public string Value { get; set; }

        public bool IsArray { get; set; }

        /// <summary>
        /// How many elements the resolution loop runs over.
        ///
        /// A literal for a serial field, whose length is fixed at generation, and the
        /// record's own count member for a variable length one.
        /// </summary>
        public string CountExpression { get; set; }
    }
}
