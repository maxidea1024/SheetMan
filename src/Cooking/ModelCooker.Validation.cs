using System.Collections.Generic;
using System.Linq;
using SheetMan.Models;
using SheetMan.Recipe;
using SheetMan.Targets;

namespace SheetMan.Cooking
{
    public partial class ModelCooker
    {
        /// <summary>
        /// Checks a cooked model and reports everything wrong with it in one go.
        ///
        /// This ran nowhere at all until now - the method existed but nothing called
        /// it, and its uniqueness loop skipped precisely the fields it was meant to
        /// check. Catching this class of mistake statically is the point of the tool,
        /// so the checks are back, corrected, and wired into Cook.
        /// </summary>
        private void ValidateModel(Model model, RecipeModel recipeModel, TargetSide requested, Diagnostics diagnostics)
        {
            foreach (var table in model.Tables)
            {
                ValidateIndexUniqueness(table, diagnostics);
                ValidateReferences(model, table, diagnostics);
            }

            ValidateTargetSideReachability(model, recipeModel, requested, diagnostics);
        }

        /// <summary>
        /// Every field acting as an index must hold distinct values.
        ///
        /// The first column is always an index; further ones opt in with a `*` prefix
        /// on the field name. The previous version skipped a field when
        /// `field.Indexing` was set, the exact inverse of what it wanted, so it only
        /// ever examined the columns where duplicates are perfectly legal.
        /// </summary>
        private void ValidateIndexUniqueness(Table table, Diagnostics diagnostics)
        {
            foreach (var field in table.Fields)
            {
                if (!field.Indexing)
                    continue;

                if (!CanBeIndexKey(field, out string why))
                {
                    diagnostics.Error(field.TypeLocation,
                        $"Index field `{table.Name}.{field.Name}` is `{field.ElementType}`, {why} " +
                        $"Use a whole-number, string, uuid or enum column as an index.");
                    continue;
                }

                // Keyed lookup rather than comparing every row against every other.
                // The original shape was quadratic, which on a table of any size is
                // the slowest thing the converter does.
                var seen = new Dictionary<object, Location>();

                foreach (var row in table.Data)
                {
                    var cell = row[field.Index];

                    // Values are boxed, so equality has to go through Equals. A
                    // reference comparison reports every boxed int as distinct and
                    // therefore never finds a duplicate at all.
                    if (seen.TryGetValue(cell.Value, out var firstLocation))
                    {
                        diagnostics.Error(cell.RawCell.Location,
                            $"Index field `{table.Name}.{field.Name}` repeats the value `{cell.Value}`, " +
                            $"first used at {firstLocation}. Values in an index field must be unique.");
                        continue;
                    }

                    seen.Add(cell.Value, cell.RawCell.Location);
                }
            }
        }

        /// <summary>
        /// Whether a column's type can carry a lookup key.
        /// </summary>
        /// <remarks>
        /// Asked here rather than left to the generators, because the answer only shows up
        /// there as somebody else's compiler: an index becomes a hash map in most targets
        /// and a sorted array in C, and a float key hashes and compares by a bit pattern
        /// nobody wrote down. `1.1` from the sheet and `1.1` from a caller's arithmetic are
        /// then two different keys, and the lookup misses without failing.
        ///
        /// An array cannot be one either, but that is not reachable from here: a folded
        /// serial field is never an indexer.
        /// </remarks>
        private static bool CanBeIndexKey(Field field, out string why)
        {
            switch (field.ElementType)
            {
                case Models.ValueType.Float:
                case Models.ValueType.Double:
                    why = "and a lookup keyed by a floating point value misses on values " +
                          "that look equal but are not.";
                    return false;

                default:
                    why = null;
                    return true;
            }
        }

        /// <summary>
        /// Checks that every foreign reference points at something that exists: the
        /// table, the field within it, and a row carrying the referenced key.
        /// </summary>
        private void ValidateReferences(Model model, Table table, Diagnostics diagnostics)
        {
            foreach (var field in table.Fields)
            {
                if (!field.IsRef)
                    continue;

                // A reference that failed to resolve has already been reported by
                // SolveTableCrossReferencings, which knows exactly which link in the
                // chain broke. Repeating it here would just say the same thing twice.
                if (field.ResolvedRefTable == null)
                    continue;

                ValidateReferencedKeysExist(table, field, field.ResolvedRefTable, diagnostics);
            }
        }

        /// <summary>
        /// Checks the referencing cells themselves: whatever a reference column holds
        /// has to match a row in the target table.
        /// </summary>
        private void ValidateReferencedKeysExist(Table table, Field field, Table foreignTable, Diagnostics diagnostics)
        {
            if (foreignTable.Fields.Count == 0)
                return;

            // Whichever form a reference takes, the cell stores the target's primary
            // index, so the keys to match against all live in its first column.
            var foreignKeys = new HashSet<object>();
            foreach (var foreignRow in foreignTable.Data)
                foreignKeys.Add(foreignRow[foreignTable.Fields[0].Index].Value);

            foreach (var row in table.Data)
            {
                var cell = row[field.Index];

                // Zero is the conventional "points at nothing". Index values start at
                // one, so it can never collide with a real row.
                if (cell.Value is int key && key == 0)
                    continue;

                if (foreignKeys.Contains(cell.Value))
                    continue;

                diagnostics.Error(cell.RawCell.Location,
                    $"Field `{table.Name}.{field.Name}` references `{foreignTable.Name}` row `{cell.Value}`, " +
                    $"which does not exist.");
            }
        }

        /// <summary>
        /// Checks that no build leaves a reference dangling.
        ///
        /// Target-side filtering removes whole entities from an output. If a table that
        /// survives references one that does not, the generated code names a type that
        /// was never emitted, and the failure surfaces in the consuming project's
        /// compiler instead of here.
        ///
        /// Only the sides the recipe actually asks for are checked, so a workbook is
        /// never rejected over a combination nobody builds.
        /// </summary>
        private void ValidateTargetSideReachability(
            Model model, RecipeModel recipeModel, TargetSide requested, Diagnostics diagnostics)
        {
            foreach (var side in RequestedTargetSides(recipeModel, requested))
            {
                if (side == TargetSide.Both)
                    continue;

                var visibleTables = new HashSet<string>(
                    model.Tables.Where(t => TargetSides.Includes(side, t.TargetSide)).Select(t => t.Name));

                foreach (var table in model.Tables)
                {
                    if (!TargetSides.Includes(side, table.TargetSide))
                        continue;

                    foreach (var field in table.Fields)
                    {
                        if (!field.IsRef)
                            continue;

                        // Already reported as unresolvable; whether it would also be
                        // filtered out is beside the point.
                        if (field.ResolvedRefTable == null)
                            continue;

                        if (!TargetSides.Includes(side, field.TargetSide))
                            continue;

                        if (visibleTables.Contains(field.RefTableName))
                            continue;

                        diagnostics.Error(field.DetailTypeLocation,
                            $"In a `{TargetSides.Describe(side)}` build, field `{table.Name}.{field.Name}` references table " +
                            $"`{field.RefTableName}`, which that build excludes by target side.");
                    }
                }
            }
        }

        /// <summary>
        /// The distinct target sides any output entry in the recipe asks for.
        ///
        /// Taken from the target registry, which is the same list the run itself works
        /// through, so every target that will produce output is covered.
        ///
        /// It was previously a hand-written enumeration of six recipe sections, and the
        /// four database sections were missing from it - added later, to the run but not
        /// here. A recipe whose only server-side output was a database export therefore
        /// had its server side left unvalidated, and a table referencing a client-only
        /// table would reach the exporter unreported.
        /// </summary>
        private static IEnumerable<TargetSide> RequestedTargetSides(RecipeModel recipeModel, TargetSide requested)
        {
            var sides = new HashSet<TargetSide>();

            foreach (var planned in TargetRegistry.Plan(recipeModel, requested))
                sides.Add(planned.Side);

            return sides;
        }

    }
}
