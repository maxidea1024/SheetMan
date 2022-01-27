using SheetMan.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace SheetMan.Models
{
    /// <summary>
    /// Model
    /// </summary>
    public class Model
    {
        /// <summary></summary>
        public List<Table> Tables { get; set; } = new List<Table>();

        /// <summary></summary>
        public List<Enum> Enums { get; set; } = new List<Enum>();

        /// <summary></summary>
        public List<ConstantSet> ConstantSets { get; set; } = new List<ConstantSet>();

        /// <summary></summary>
        public List<VariableSet> VariableSets { get; set; } = new List<VariableSet>();

        /// <summary></summary>
        public List<FormulaSet> FormulaSets { get; set; } = new List<FormulaSet>();

        /// <summary></summary>
        public static Model Current { get; set; }

        /// <summary>
        ///
        /// </summary>
        public Model()
        {
            SetToCurrent();
        }

        /// <summary>
        ///
        /// </summary>
        public void SetToCurrent()
        {
            Current = this;
        }

        /// <summary>
        ///
        /// </summary>
        public void Reset()
        {
            Tables.Clear();
            Enums.Clear();
            ConstantSets.Clear();
            VariableSets.Clear();
            FormulaSets.Clear();
        }


        #region Tables

        /// <summary>
        ///
        /// </summary>
        public bool ContainsTable(string name) => FindTable(name) != null;

        /// <summary>
        ///
        /// </summary>
        public Table GetTable(string name, Location callerLocation)
        {
            var found = FindTable(name);
            if (found == null)
                throw new SheetManException(callerLocation, $"No found table '{name}'");

            return found;
        }

        /// <summary>
        ///
        /// </summary>
        public Table FindTable(string name) => Tables.Find(x => x.Name == name);

        #endregion


        #region Enums

        /// <summary>
        ///
        /// </summary>
        public bool ContainsEnum(string name) => FindEnum(name) != null;

        /// <summary>
        ///
        /// </summary>
        public Enum GetEnum(string name, Location callerLocation)
        {
            var found = FindEnum(name);
            if (found == null)
                throw new SheetManException(callerLocation, $"No found enum '{name}'");

            return found;
        }

        /// <summary>
        ///
        /// </summary>
        public Enum FindEnum(string name) => Enums.Find(x => x.Name == name);
        #endregion


        #region Constants

        /// <summary>
        ///
        /// </summary>
        private bool ContainsConstantSet(string name) => FindConstantSet(name) != null;

        /// <summary>
        ///
        /// </summary>
        private ConstantSet FindConstantSet(string name) => ConstantSets.Find(x => x.Name == name);

        #endregion


        #region Referencing

        public class Reference
        {
            public Table Table { get; set; }
            public Field Field { get; set; }
        }

        public void GetReferenceChain(Table table, Field field, List<Reference> referenceChain, bool includeSelfReference = false)
        {
            var refererField = field;
            var fieldNode = field;

            if (includeSelfReference)
                referenceChain.Add(new Reference { Table = table, Field = field });

            for (; ; )
            {
                var refTable = GetTable(fieldNode.RefTableName, fieldNode.TypeLocation);

                // Check cyclic referencing.
                if (refTable == table)
                    throw new SheetManException(fieldNode.TypeLocation, $"A self reference has been detected.");

                refererField.ResolvedRefTable = refTable;

                if (string.IsNullOrEmpty(fieldNode.RefFieldName))
                {
                    referenceChain.Add(new Reference { Table = refTable, Field = null });
                    return;
                }

                var refField = refTable.GetField(fieldNode.RefFieldName, fieldNode.TypeLocation);

                referenceChain.Add(new Reference { Table = refTable, Field = refField });

                if (!refField.IsRef)
                    return;
                else
                    fieldNode = refField; // Chain
            }
        }

        private void ResolveReference(Table table, Field refererField)
        {
            if (!refererField.IsRef)
                return;

            var fieldNode = refererField;

            for (; ; )
            {
                var refTable = GetTable(fieldNode.RefTableName, fieldNode.TypeLocation);

                // Check cyclic referencing.
                if (refTable == table)
                    throw new SheetManException(fieldNode.TypeLocation, $"A self reference has been detected.");

                refererField.ResolvedRefTable = refTable;

                if (string.IsNullOrEmpty(fieldNode.RefFieldName))
                {
                    refererField.ResolvedRefField = null;
                    return;
                }

                var refField = refTable.GetField(fieldNode.RefFieldName, fieldNode.TypeLocation);
                if (!refField.IsRef)
                {
                    refererField.ResolvedRefField = refField;
                    return;
                }
                else
                {
                    // Chain
                    fieldNode = refField;
                }
            }
        }

        /// <summary>
        /// Handles the case where there is a reference between tables.
        /// </summary>
        public void SolveTableCrossReferencings()
        {
            foreach (var table in Tables)
            {
                foreach (var field in table.Fields)
                {
                    if (!field.IsRef)
                        continue;

                    ResolveReference(table, field);

                    // 아래 코드는 ResolveReference 함수안으로 넣어도 될듯 한데..
                    if (field.ResolvedRefField == null)
                    {
                        field.Type = Models.ValueType.ForeignRecord; // 참조되는 다른 테이블의 레코드여야함.
                        field.TypeName = $"{field.ResolvedRefTable.Name}.Record";
                    }
                    else
                    {
                        field.Type = field.ResolvedRefField.Type;
                        field.TypeName = field.ResolvedRefField.TypeName;
                    }

                    var referenceChain = new List<Reference>();
                    GetReferenceChain(table, field, referenceChain, false);

                    field.RefChainPath = string.Join("_", referenceChain.Select(x => x.Table.Name.ToPascalCase()));

                    //Log.Information($"  => {table.Name} :: RefChainPath={field.RefChainPath}");
                }
            }
        }
        #endregion
    }
}
