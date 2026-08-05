using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Everything the C# template needs, worked out in advance.
    ///
    /// Same division as the C++ view: the template decides where things go, and anything
    /// that depends on the model - a type name, a read call, a rendered literal - arrives
    /// already finished.
    /// </summary>
    internal sealed class CsFileView
    {
        /// <summary>The namespace, or empty. The template wraps the file in it when set.</summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Extension the recipe told the exporter to write, which is what the accessor's read
        /// defaults to.
        /// </summary>
        /// <remarks>
        /// It was a `".table"` literal in the template until this existed - so a recipe that set
        /// the extension on both the export and this target got the right file names out of the
        /// exporter and a reader that looked for the default anyway.
        /// </remarks>
        public string FileExtension { get; set; }

        public IReadOnlyList<CsTableView> Tables { get; set; }

        public IReadOnlyList<CsEnumView> Enums { get; set; }

        public IReadOnlyList<CsConstantSetView> ConstantSets { get; set; }

        /// <summary>
        /// Only the tables that reference another.
        ///
        /// A separate list rather than a test inside the template, because the blank line
        /// separating one table's resolution block from the next has to count these and not
        /// every table - which is what the hand-written version did with its own counter.
        /// </summary>
        public IReadOnlyList<CsTableView> TablesWithReferences { get; set; }
    }

    /// <summary>
    /// One generated file, for the templates that render one thing.
    /// </summary>
    /// <remarks>
    /// The output is a file per table, per enum and per constant set, and each of those
    /// templates needs the namespace as well as its own subject. Rather than hand every
    /// template the whole model and trust it to loop over only the right part, each gets a
    /// view holding exactly what it is for - so a template cannot reach a table it is not
    /// writing.
    ///
    /// One class with one payload property rather than four near-identical ones, because the
    /// only thing they would differ in is the name of that property and Scriban addresses it
    /// by name from the template.
    /// </remarks>
    internal sealed class CsPartView
    {
        /// <summary>The namespace, or empty. The head template wraps the file in it when set.</summary>
        public string Namespace { get; set; }

        /// <summary>The table this file is for, when it is a table file.</summary>
        public CsTableView Table { get; set; }

        /// <summary>The enum this file is for, when it is an enum file.</summary>
        public CsEnumView Enumm { get; set; }

        /// <summary>The constant set this file is for, when it is a constants file.</summary>
        public CsConstantSetView Set { get; set; }
    }

    internal sealed class CsTableView
    {
        /// <summary>Table name in Pascal case; the class is this plus `Table`.</summary>
        public string Name { get; set; }

        /// <summary>Table name as the sheet spelled it, used in the data file name.</summary>
        public string RawName { get; set; }

        /// <summary>Doc-comment lines, already split. Empty when the sheet had no comment.</summary>
        public IReadOnlyList<string> Comment { get; set; }

        public IReadOnlyList<CsFieldView> Fields { get; set; }

        /// <summary>The fields a lookup dictionary is built for.</summary>
        public IReadOnlyList<CsFieldView> IndexedFields { get; set; }

        /// <summary>The fields that point at another table.</summary>
        public IReadOnlyList<CsFieldView> ReferenceFields { get; set; }

        /// <summary>
        /// Whether the read needs a scratch int for enum casting.
        ///
        /// The reader hands back an int and the field is an enum, so one temporary is
        /// declared for the whole method rather than one per field.
        /// </summary>
        public bool NeedsEnumTemp { get; set; }

        /// <summary>`"A", "B"` - the field-name array literal's contents.</summary>
        public string FieldNameLiterals { get; set; }

        /// <summary>`r.A, r.B` - the value-map row's contents.</summary>
        public string FieldValueExpressions { get; set; }
    }

    /// <summary>
    /// One serial field, in every shape the generated class distinguishes.
    /// </summary>
    internal sealed class CsFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Public property name.</summary>
        public string PropName { get; set; }

        /// <summary>Private backing field name, including its leading underscore.</summary>
        public string FieldName { get; set; }

        /// <summary>Element type name.</summary>
        public string FieldType { get; set; }

        /// <summary>
        /// What follows the member's declaration to initialize it, or nothing when C#'s own
        /// default is already an empty value.
        /// </summary>
        public string Initializer { get; set; }

        /// <summary>Element count of a serial field, which is its column count.</summary>
        public int ElementCount { get; set; }

        /// <summary>Referenced table's class name, without the `Table` suffix.</summary>
        public string RefTable { get; set; }

        /// <summary>Referenced field's property name, empty when the reference names a whole row.</summary>
        public string RefField { get; set; }

        /// <summary>
        /// Which declaration shape applies: `array_ref`, `var_array`, `array`, `scalar_ref`
        /// or `scalar`.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial` or `scalar`. Separate from
        /// <see cref="Kind"/> because a reference and a plain field declare differently but
        /// a serial field of either reads through the same loop.
        /// </summary>
        public string ReadKind { get; set; }

        /// <summary>The column's wire tag, which is how the read matches it in a file.</summary>
        public int Tag { get; set; }

        /// <summary>
        /// The rendered CheckColumn call: kind, count and the elements this member accepts -
        /// its own, plus the lossless promotions.
        /// </summary>
        public string ColumnCheck { get; set; }

        /// <summary>
        /// The lines reading one element, at whatever depth the template places them. Two
        /// or three of them for an enum or a reference, one otherwise.
        /// </summary>
        public IReadOnlyList<string> ElementRead { get; set; }

        /// <summary>Type of the setter a resolved reference is assigned through.</summary>
        public string ReferenceSetterType { get; set; }

        /// <summary>Whether the reference names a field of the target rather than the row.</summary>
        public bool ReferencesField { get; set; }
    }

    internal sealed class CsEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<CsEnumLabelView> Labels { get; set; }
    }

    internal sealed class CsEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Whether a trailing comma follows. C# allows one; the generator omits it.</summary>
        public bool IsLast { get; set; }
    }

    internal sealed class CsConstantSetView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<CsConstantView> Constants { get; set; }
    }

    internal sealed class CsConstantView
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
    }
}
