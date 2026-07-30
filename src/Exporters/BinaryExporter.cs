using CommandLine;
using SheetMan.Recipe;
using SheetMan.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SheetMan.Helpers;
using System.Collections.Generic;
using SheetMan.Targets;

namespace SheetMan.Exporters
{
    [SheetManTarget("binary", TargetKind.Export, Section = "Exports.Binary", Order = 10)]
    public class BinaryExporter : Target<RecipeModel.ExportRecipeGroup.BinaryRecipe>
    {
        const uint BinaryFileFormatVersion = 100;

        private Manifest _manifest;


        protected override void Run(TargetContext context, RecipeModel.ExportRecipeGroup.BinaryRecipe binaryRecipe)
        {
            // An entry left in the recipe with a blank path is treated as switched off.
            if (string.IsNullOrEmpty(binaryRecipe.Path))
                return;

            string manifestFilename = Path.Combine(binaryRecipe.Path, "manifest-binary.json");

            _manifest = Manifest.Load(manifestFilename);

            // context.Model is already narrowed to this entry's target side.
            foreach (var table in context.Model.Tables)
                ExportTable(binaryRecipe, table);

            _manifest.BuildAndWriteToFile(manifestFilename);
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
                    // A serial field's length is its column count, which the reader
                    // already knows from the generated code, so nothing is written for
                    // it. A delimited array varies per row and has to carry its own
                    // length. Only the latter gets a counter, which keeps the format
                    // of existing tables unchanged.
                    if (sf.IsVariableLengthArray)
                    {
                        ExportArrayValue(writer, row[sf.FirstField.Index].Value, sf.FirstField);
                        continue;
                    }

                    foreach (var field in sf.Fields)
                        ExportValue(writer, row[field.Index].Value, field);
                }
            }

            var filename = Path.Combine(recipe.Path, table.Name + recipe.FileExtension);
            filename = Path.GetFullPath(filename);

            // A view over the writer's buffer, not a copy: this is the biggest allocation
            // in the export and there is no reason to make it twice.
            Log.Information($"Exporting binary file '{filename}' ({writer.Length} bytes)");
            string stagingFilename = StagingFiles.WriteAllBytesToFile(filename, writer.WrittenSpan);

            _manifest.Add(table.Name + recipe.FileExtension, stagingFilename);
        }

        /// <summary>
        /// Writes a delimited array cell: element count first, then the elements.
        /// </summary>
        private void ExportArrayValue(LiteBinaryWriter writer, object value, Field field)
        {
            var elements = (System.Array)value;
            int length = elements?.Length ?? 0;

            writer.WriteCounter32(length);

            for (int i = 0; i < length; i++)
                ExportValue(writer, elements.GetValue(i), field);
        }

        private void ExportValue(LiteBinaryWriter writer, object value, Field field)
        {
            // Element type, so the same switch serves a scalar field and one element
            // of an array field.
            Models.ValueType valueType = field.ElementType;

            // A reference is stored as the target's primary index, which is always an
            // int32: the cooker rejects a table whose index column is any other type, so
            // there is no case here for the index being something else.
            if (field.IsRef)
                valueType = Models.ValueType.Int32;

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
    }
}
