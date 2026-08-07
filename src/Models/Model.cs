using SheetMan.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace SheetMan.Models;

/// <summary>
/// Everything the sheets declared, after the raw cells have been interpreted.
///
/// This is what the exporters and code generators consume. Cross-table references
/// are resolved before it is handed over, so a field knows the table and field it
/// points at rather than just their names.
/// </summary>
public class Model
{
    /// <summary>Tables, in the order their markers were found.</summary>
    public List<Table> Tables { get; set; } = new List<Table>();

    /// <summary>Enum declarations. Parsed before tables, which may refer to them.</summary>
    public List<Enum> Enums { get; set; } = new List<Enum>();

    /// <summary>Constant sets. Parsed before tables, for the same reason.</summary>
    public List<ConstantSet> ConstantSets { get; set; } = new List<ConstantSet>();

    /// <summary>
    /// The model being worked on, for the few places that cannot reach one directly.
    ///
    /// Ambient state, and not a good pattern: Field.EnumOrNull resolves an enum
    /// through it because a Field holds only its type name. Worth replacing with an
    /// explicit reference, which is why ProjectTo takes care to leave this pointing
    /// at the complete model rather than a filtered view of it.
    /// </summary>
    public static Model Current { get; set; }

    /// <summary>
    /// Publishes the new instance as <see cref="Current"/>.
    /// </summary>
    public Model()
    {
        SetToCurrent();
    }

    /// <summary>Makes this the ambient model.</summary>
    public void SetToCurrent()
    {
        Current = this;
    }

    /// <summary>Empties every entity list, keeping the instance.</summary>
    public void Reset()
    {
        Tables.Clear();
        Enums.Clear();
        ConstantSets.Clear();
    }


    #region Target side projection

    /// <summary>
    /// Returns the view of this model that belongs in output built for
    /// <paramref name="side"/>: entities and fields marked for the other side are
    /// left out.
    ///
    /// Exporters and generators are handed the projection instead of being taught
    /// to filter, so every one of them applies the rule identically and none of
    /// their traversal code changes.
    ///
    /// The projection is shallow on purpose. Tables are new instances with a
    /// narrowed field list, but they share the original Field objects and the
    /// original Data rows, so <see cref="Field.Index"/> still addresses the right
    /// column. Consumers must therefore read cells through a field's Index rather
    /// than by walking a row positionally.
    /// </summary>
    public Model ProjectTo(TargetSide side)
    {
        // Both is the default and means "everything", so hand back the model
        // itself: no copying, and output is bit-for-bit what it was before target
        // sides existed.
        if (side == TargetSide.Both)
            return this;

        // `new Model()` publishes itself as Model.Current, which Field.EnumOrNull
        // resolves against. That must keep pointing at the complete model: a field
        // surviving the projection may be typed with an enum that does not, and
        // resolution should still succeed - it is emission that is being filtered,
        // not the type system.
        var previousCurrent = Current;

        var projected = new Model();

        foreach (var table in Tables)
        {
            if (!TargetSides.Includes(side, table.TargetSide))
                continue;

            var narrowed = new Table
            {
                Location = table.Location,
                TargetSide = table.TargetSide,
                RawName = table.RawName,
                Name = table.Name,
                Comment = table.Comment,
                Data = table.Data,

                // Carried, not defaulted: the projection recomputes SerialFields from its
                // narrowed field list, and a table that must not fold must not start
                // folding because a target side was asked for.
                FoldSerialFields = table.FoldSerialFields,
            };

            foreach (var field in table.Fields)
            {
                // The primary index is what every row is addressed by, so it stays
                // regardless of side. ModelCooker already refuses to let it be
                // marked for one side only.
                if (field.Index != 0 && !TargetSides.Includes(side, field.TargetSide))
                    continue;

                narrowed.Fields.Add(field);
            }

            projected.Tables.Add(narrowed);
        }

        foreach (var enumm in Enums)
        {
            if (TargetSides.Includes(side, enumm.TargetSide))
                projected.Enums.Add(enumm);
        }

        foreach (var constantSet in ConstantSets)
        {
            if (TargetSides.Includes(side, constantSet.TargetSide))
                projected.ConstantSets.Add(constantSet);
        }

        Current = previousCurrent;

        return projected;
    }

    #endregion


    #region Tables

    /// <summary>Whether a table of this name exists.</summary>
    public bool ContainsTable(string name) => FindTable(name) != null;

    /// <summary>
    /// Finds a table, or throws naming the cell that asked for it.
    /// </summary>
    public Table GetTable(string name, Location callerLocation)
    {
        var found = FindTable(name);
        if (found == null)
            throw new SheetManException(callerLocation, $"No found table '{name}'");

        return found;
    }

    /// <summary>Finds a table by name, or null.</summary>
    public Table FindTable(string name) => Tables.Find(x => x.Name == name);

    #endregion


    #region Enums

    /// <summary>
    /// Whether an enum of this name exists.
    ///
    /// Also how a type name in a sheet is recognized as an enum rather than rejected.
    /// </summary>
    public bool ContainsEnum(string name) => FindEnum(name) != null;

    /// <summary>
    /// Finds an enum, or throws naming the cell that asked for it.
    /// </summary>
    public Enum GetEnum(string name, Location callerLocation)
    {
        var found = FindEnum(name);
        if (found == null)
            throw new SheetManException(callerLocation, $"No found enum '{name}'");

        return found;
    }

    /// <summary>Finds an enum by name, or null.</summary>
    public Enum FindEnum(string name) => Enums.Find(x => x.Name == name);
    #endregion


    #region Constants

    /// <summary>Whether a constant set of this name exists.</summary>
    private bool ContainsConstantSet(string name) => FindConstantSet(name) != null;

    /// <summary>Finds a constant set by name, or null.</summary>
    private ConstantSet FindConstantSet(string name) => ConstantSets.Find(x => x.Name == name);

    #endregion


    #region Referencing

    /// <summary>
    /// One hop of a reference chain: the table arrived at, and the field followed
    /// into it.
    /// </summary>
    public class Reference
    {
        /// <summary>Table this hop lands in.</summary>
        public Table Table { get; set; }

        /// <summary>Field followed, or null when the reference names the whole row.</summary>
        public Field Field { get; set; }
    }

    /// <summary>
    /// Resolves every foreign reference in the model, recording what it cannot
    /// resolve instead of throwing.
    ///
    /// Reporting rather than throwing is what lets a broken workbook come back
    /// with all of its problems at once. Resolution failures used to abort the run
    /// on the first one, so they could never join the report that validation
    /// produces a moment later.
    ///
    /// A field whose reference does not resolve is left unresolved. That is safe
    /// because the recorded diagnostics stop the run before anything is generated.
    /// </summary>
    public void SolveTableCrossReferencings(Diagnostics diagnostics)
    {
        foreach (var table in Tables)
        {
            foreach (var field in table.Fields)
            {
                if (!field.IsRef)
                    continue;

                if (!TryResolveReference(table, field, diagnostics, out var referenceChain))
                    continue;

                if (field.ResolvedRefField == null)
                {
                    field.Type = Models.ValueType.ForeignRecord; // the value is a row of the referenced table, not its key
                    field.TypeName = $"{field.ResolvedRefTable.Name}.Record";
                }
                else
                {
                    field.Type = field.ResolvedRefField.Type;
                    field.TypeName = field.ResolvedRefField.TypeName;
                }

                field.RefChainPath = string.Join("_", referenceChain.Select(x => x.Table.Name.ToPascalCase()));
            }
        }
    }

    /// <summary>
    /// Walks a reference to whatever it ultimately points at, following further
    /// references along the way.
    ///
    /// Resolving the target and describing the chain used to be two methods that
    /// walked the same links with the same rules, which meant every fix had to be
    /// made twice. They are one walk now: the chain falls out of the traversal
    /// that resolves the target.
    /// </summary>
    /// <returns>False when the reference could not be resolved, having recorded why.</returns>
    private bool TryResolveReference(Table table, Field refererField, Diagnostics diagnostics, out List<Reference> referenceChain)
    {
        referenceChain = new List<Reference>();

        var fieldNode = refererField;

        // Tracks the fields already walked. The only cycle check used to be "does
        // this land back on the table we started from", so a cycle that excludes
        // the starting table - B.g points at C.h, C.h back at B.g - spun forever
        // and the tool hung with no output at all.
        var visited = new HashSet<Field> { refererField };

        for (; ; )
        {
            var refTable = FindTable(fieldNode.RefTableName);
            if (refTable == null)
            {
                diagnostics.Error(fieldNode.DetailTypeLocation,
                    $"Field `{table.Name}.{refererField.Name}` references table `{fieldNode.RefTableName}`, which does not exist.");
                return false;
            }

            if (refTable == table)
            {
                diagnostics.Error(fieldNode.DetailTypeLocation,
                    $"Field `{table.Name}.{refererField.Name}` references its own table `{table.Name}`.");
                return false;
            }

            refererField.ResolvedRefTable = refTable;

            if (string.IsNullOrEmpty(fieldNode.RefFieldName))
            {
                refererField.ResolvedRefField = null;
                referenceChain.Add(new Reference { Table = refTable, Field = null });
                return true;
            }

            var refField = refTable.FindField(fieldNode.RefFieldName);
            if (refField == null)
            {
                diagnostics.Error(fieldNode.DetailTypeLocation,
                    $"Field `{table.Name}.{refererField.Name}` references `{fieldNode.RefTableName}.{fieldNode.RefFieldName}`, " +
                    $"but table `{refTable.Name}` has no field named `{fieldNode.RefFieldName}`.");
                return false;
            }

            referenceChain.Add(new Reference { Table = refTable, Field = refField });

            if (!refField.IsRef)
            {
                refererField.ResolvedRefField = refField;
                return true;
            }

            if (!visited.Add(refField))
            {
                diagnostics.Error(fieldNode.DetailTypeLocation,
                    $"A cyclic reference has been detected while resolving `{table.Name}.{refererField.Name}`. " +
                    $"The chain returns to `{refField.OwnerTable?.Name}.{refField.Name}`.");
                return false;
            }

            fieldNode = refField; // Chain
        }
    }

    #endregion
}
