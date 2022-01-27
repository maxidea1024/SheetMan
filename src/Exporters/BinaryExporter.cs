using CommandLine;
using SheetMan.Recipe;
using SheetMan.Models;
using SheetMan.Runtime;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SheetMan.Helpers;

namespace SheetMan.Exporters
{
    public class BinaryExporter
    {
        const uint BinaryFileFormatVersion = 100;

        private Options _options;
        private RecipeModel _recipeModel;
        private Model _model;
        private Manifest _manifest;

        public void Export(Options options, RecipeModel recipeModel, Model model)
        {
            _options = options;
            _recipeModel = recipeModel;
            _model = model;

            //TODO 수정

            foreach (var binaryRecipe in _recipeModel.Exports.Binary)
            {
                if (string.IsNullOrEmpty(binaryRecipe.Path))
                    continue;
                
                string manifestFilename = Path.Combine(binaryRecipe.Path, "manifest-binary.json");

                _manifest = Manifest.Load(manifestFilename);

                foreach (var table in _model.Tables)
                    ExportTable(binaryRecipe, table);

                _manifest.BuildAndWriteToFile(manifestFilename);
            }
        }

        private void ExportTable(RecipeModel.ExportRecipeGroup.BinaryRecipe recipe, Table table)
        {
            LiteBinaryWriter writer = new LiteBinaryWriter();

            writer.Write(BinaryFileFormatVersion);      // version
            writer.Write((byte)0);                      // Reserved for future features(compression/encryption)
            writer.WriteCounter32(table.Data.Count);    // number of row

            foreach (var row in table.Data)
            {
                foreach (var sf in table.SerialFields)
                {
                    foreach (var field in sf.Fields)
                        ExportValue(writer, row[field.Index].Value, field);
                }
            }

            var filename = Path.Combine(recipe.Path, table.Name + recipe.FileExtension);
            filename = Path.GetFullPath(filename);

            var data = writer.ToArray();

            Log.Information($"Exporting binary file '{filename}' ({data.Length} bytes)");
            string stagingFilename = StagingFiles.WriteAllBytesToFile(filename, data);

            _manifest.Add(table.Name + recipe.FileExtension, stagingFilename);
        }

        private void ExportValue(LiteBinaryWriter writer, object value, Field field)
        {
            Models.ValueType valueType = field.Type;

            //TODO 만약 index 타입이 int32가 아니라면 대략 낭패. 이부분은 좀 정리가 필요해보임.
            //첫번째 컬럼 정보를 넘겨 받아서 타입 대응을 해주는게 좋을듯!
            //윗단에서 정리하는게 바람직해보이는데..
            if (field.IsRef/* || cell.IsIndividualRef*/)
                valueType = Models.ValueType.Int32;

            //TODO 개별 셀 참조를 구현할때 필요함. 일단은 보류하자.
            //TODO 임포트된 데이터를 사용해서 기록하도록 하자.
            //var resolvedValue = ResolveValue(value, field);
            var resolvedValue = value;
            switch (valueType)
            {
                case Models.ValueType.String:
                    writer.Write((string)value);
                    break;
                case Models.ValueType.Bool:
                    writer.Write((bool)value);
                    break;
                case Models.ValueType.Int32:
                    writer.Write((int)value);
                    break;
                case Models.ValueType.Int64:
                    writer.Write((long)value);
                    break;
                case Models.ValueType.Float:
                    writer.Write((float)value);
                    break;
                case Models.ValueType.Double:
                    writer.Write((double)value);
                    break;
                case Models.ValueType.DateTime:
                    writer.Write((DateTime)value);
                    break;
                case Models.ValueType.TimeSpan:
                    writer.Write((TimeSpan)value);
                    break;
                case Models.ValueType.Uuid:
                    writer.Write((Guid)value);
                    break;
                case Models.ValueType.Enum:
                    writer.WriteOptimalInt32((int)value);
                    break;
                case Models.ValueType.ForeignRecord:
                    writer.Write((int)value);
                    break;
                default:
                    throw new SheetManException($"unsupported type  `{valueType}`");
            }
        }


        //TODO 셀 참조를 구현할때 필요함. 일단은 보류.
        /*
        private static string ResolveCellValue(Models.Cell cell, Models.Column column)
        {
            string cellValue = cell?.value;

            if (cell != null && cell.IsIndividualRef)
            {
                // 참조일 경우에는 가리키는 것을 찾아서 저장해야함.  링크로 간주하면 될듯??
                var refTable = GetTable(cell.refTable, column.location);
                var refColumn = refTable.GetColumn(cell.refColumn);
                if (refColumn.type != column.type)
                {
                    //TODO
                    throw new SheetManException("");
                }

                //var refCell = refTable.rows[cell.location.
                Models.Cell refCell = null;
                foreach (var row in refTable.rows)
                {
                    if (cell.refIndex.ToString() == row.cells[0].value)
                    {
                        refCell = row.cells[refColumn.index];
                        break;
                    }
                }

                if (refCell == null)
                {
                    //TODO
                    throw new SheetManException("");
                }

                cellValue = refCell.value;

                //TODO recursive chain...
            }

            return cellValue;
        }
        */
    }
}
