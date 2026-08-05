using Newtonsoft.Json;
using System.Linq;

namespace SheetMan.Models
{
    /// <summary>
    /// One column of a table.
    ///
    /// A field keeps four separate cell locations because the four header rows that
    /// describe it can each be wrong on their own - a bad type and a bad target side are
    /// different mistakes in different cells, and a diagnostic should point at the one
    /// that is actually at fault.
    /// </summary>
    public class Field
    {
        /// <summary>Cell holding the field's name.</summary>
        [JsonIgnore]
        public Location NameLocation { get; set; }

        /// <summary>Cell holding the field's type.</summary>
        [JsonIgnore]
        public Location TypeLocation { get; set; }

        /// <summary>
        /// Cell holding the detail type - the enum name, or the reference target. Blank
        /// for a plain scalar field.
        /// </summary>
        [JsonIgnore]
        public Location DetailTypeLocation { get; set; }

        /// <summary>Cell holding the field's target side.</summary>
        [JsonIgnore]
        public Location TargetSideLocation { get; set; }

        /// <summary>
        /// Table this field belongs to. Used by diagnostics that need to name the field
        /// in full.
        /// </summary>
        [JsonIgnore]
        public Table OwnerTable { get; set; }

        /// <summary>Name exactly as written in the sheet, `*` prefix included.</summary>
        public string RawName { get; set; }

        /// <summary>
        /// Name normalized to Pascal case with any `*` prefix removed. This is what
        /// generated code uses.
        /// </summary>
        public string Name { get; set; }

        /// <summary>Target side filtering option</summary>
        public TargetSide TargetSide { get; set; }

        /// <summary>
        /// Type as written in the sheet. For an enum field this is the enum's name, and
        /// for a resolved reference it becomes the referenced field's type name.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Position of this field's column within the table.
        ///
        /// How every value is addressed: a row is a flat list of cells and this indexes
        /// into it. Target-side filtering narrows the field list without renumbering, so
        /// an index always refers to the same column of the original sheet.
        /// </summary>
        public int Index { get; set; }

        /// <summary>Resolved type.</summary>
        public ValueType Type { get; set; }

        /// <summary>Description from the sheet, emitted as a doc comment.</summary>
        public string Comment { get; set; }

        /// <summary>
        /// Whether this field is an index, so its values must be unique.
        ///
        /// True for the first column always, and for any field whose name carries a
        /// leading `*`.
        /// </summary>
        [JsonIgnore]
        public bool Indexing { get; set; }

        /// <summary>
        /// Table this field references, as written in the detail-type cell. Empty when
        /// the field is not a reference.
        /// </summary>
        public string RefTableName { get; set; }

        /// <summary>
        /// Field within the referenced table, for the `RefTable.RefFieldName` form.
        /// Null or empty when the reference names the whole row.
        /// </summary>
        public string RefFieldName { get; set; }

        /// <summary>
        /// The table actually pointed at, filled in once references are resolved. Null
        /// when resolution failed, which the diagnostics will have reported.
        /// </summary>
        [JsonIgnore]
        public Table ResolvedRefTable { get; set; }

        /// <summary>
        /// The field actually pointed at, or null for a whole-row reference.
        /// </summary>
        [JsonIgnore]
        public Field ResolvedRefField { get; set; }

        /// <summary>
        /// The chain a reference walks, joined with underscores: a field pointing through
        /// A to B to C gives `A_B_C`.
        ///
        /// Used to name generated members so two references that end up at the same type
        /// through different paths do not collide.
        /// </summary>
        [JsonIgnore]
        public string RefChainPath { get; set; }

        /// <summary>Whether this field references another table.</summary>
        [JsonIgnore]
        public bool IsRef => !string.IsNullOrEmpty(RefTableName);

        /// <summary>
        /// The column's wire tag: what identifies it in a binary file, instead of its position.
        /// </summary>
        /// <remarks>
        /// Comes from an `@N` suffix on the sheet's field name (`Price@3`), or is assigned by
        /// ordinal after the table is parsed when no field in the table carries one. By the time
        /// anything downstream reads it, it is never null - the cooker's AssignTags fills it.
        ///
        /// For a serial field, the tag lives on the first column and identifies the whole
        /// logical column; the other members must not carry one.
        /// </remarks>
        public int? Tag { get; set; }

        /// <summary>
        /// Whether this field's cells hold a delimited list.
        ///
        /// Only true of the `T[]` types. A serial field is also an array to its
        /// consumers, but that is a property of the group rather than of one column -
        /// see <see cref="SerialField.IsArray"/>.
        /// </summary>
        [JsonIgnore]
        public bool IsArray => ValueTypes.IsArray(Type);

        /// <summary>
        /// Element type for an array field; the field's own type when it is scalar.
        /// </summary>
        [JsonIgnore]
        public ValueType ElementType => ValueTypes.ElementOf(Type);

        /// <summary>
        /// The enum this field's type refers to, or throws if it has none.
        ///
        /// Prefer <see cref="EnumOrNull"/> where the field's type is not already known to
        /// be an enum; this overload exists for the code paths that have just tested it
        /// and would rather not test again.
        /// </summary>
        [JsonIgnore]
        public Enum Enum
        {
            get
            {
                // Element type, so an `enum[]` field resolves against the same
                // declaration a scalar `enum` field would.
                if (ElementType != ValueType.Enum)
                {
                    throw new SheetManException(NameLocation,
                        $"Field `{OwnerTable?.Name}.{Name}` has type `{TypeName}`, which is not an enum.");
                }

                return Model.Current.GetEnum(TypeName, null);
            }
        }

        /// <summary>
        /// The enum this field's type refers to, or null if it has none.
        /// </summary>
        [JsonIgnore]
        public Enum EnumOrNull
        {
            get
            {
                // Accepts EnumArray as well: an array of enum labels resolves against
                // the same declaration as a scalar one.
                if (ElementType != ValueType.Enum)
                    return null;

                return Model.Current.GetEnum(TypeName, null);
            }
        }

        // Reserved for describing database column constraints - nullability, length,
        // uniqueness - from the sheet. Nothing sets either of these: the database
        // exporters derive their column types from ValueType and make every column NOT
        // NULL, since a sheet cell always has a value even when that value is empty.
        // Declaring constraints would need somewhere in the sheet to say so.

        /// <summary>Reserved. Not populated.</summary>
        [JsonIgnore]
        public bool IsNullable { get; set; }

        /// <summary>Reserved. Not populated.</summary>
        [JsonIgnore]
        public int Length { get; set; }
    }
}
