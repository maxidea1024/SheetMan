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

namespace SheetMan.Exporters;

[SheetManTarget("binary", TargetKind.Export, Section = "Exports.Binary", Order = 10)]
public class BinaryExporter : Target<RecipeModel.ExportRecipeGroup.BinaryRecipe>
{
    private Manifest _manifest;


    protected override void Run(TargetContext context, RecipeModel.ExportRecipeGroup.BinaryRecipe binaryRecipe)
    {
        // An entry left in the recipe with a blank path is treated as switched off.
        if (string.IsNullOrEmpty(binaryRecipe.Path))
            return;

        // Before anything is written: a schema change that would break a reader already
        // out there stops the run here, with nothing exported and the reason named.
        if (!string.IsNullOrEmpty(binaryRecipe.SchemaBaseline))
        {
            SchemaBaseline.Check(
                binaryRecipe.SchemaBaseline, context.Model, binaryRecipe.AcceptSchemaChanges);
        }

        string manifestFilename = Path.Combine(binaryRecipe.Path, "manifest-binary.json");

        _manifest = Manifest.Load(manifestFilename);

        // context.Model is already narrowed to this entry's target side.
        foreach (var table in context.Model.Tables)
            ExportTable(binaryRecipe, table);

        _manifest.BuildAndWriteToFile(manifestFilename);
    }

    private void ExportTable(RecipeModel.ExportRecipeGroup.BinaryRecipe recipe, Table table)
    {
        ScbWriter writer = new ScbWriter();
        var serials = table.SerialFields;

        writer.Write(ScbFormat.Version);
        writer.Write((byte)0);                      // Reserved (compression/encryption)
        writer.WriteCounter32(table.Data.Count);

        // The descriptors: one per logical column, so the file says what it holds. A
        // reader matches columns by tag rather than position, skips a tag it does not
        // know by the block's byte length, and refuses a wire it cannot read by name -
        // which between them is the whole of what makes schema changes survivable.
        writer.WriteCounter32(serials.Count);

        var lengthSlots = new int[serials.Count];

        for (int at = 0; at < serials.Count; at++)
        {
            var sf = serials[at];

            writer.WriteCounter32(sf.FirstField.Tag.Value);
            writer.Write(ScbFormat.Wire(ScbFormat.ElementFor(sf), ScbFormat.KindFor(sf)));
            writer.WriteCounter32(ScbFormat.CountFor(sf));

            // Patched after the block is written; a varint cannot be.
            lengthSlots[at] = writer.ReserveUInt32Slot();
        }

        // Column-oriented: each column's rows are one contiguous block. That is what
        // lets an unknown column be skipped in a single advance, with no per-type skip
        // logic for thirteen readers to each get subtly wrong.
        for (int at = 0; at < serials.Count; at++)
        {
            var sf = serials[at];
            int blockStart = writer.Length;

            foreach (var row in table.Data)
            {
                if (sf.IsVariableLengthArray)
                {
                    ExportArrayValue(writer, row[sf.FirstField.Index].Value, sf.FirstField);
                    continue;
                }

                foreach (var field in sf.Fields)
                    ExportValue(writer, row[field.Index].Value, field);
            }

            writer.PatchUInt32(lengthSlots[at], (uint)(writer.Length - blockStart));
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
    private void ExportArrayValue(ScbWriter writer, object value, Field field)
    {
        var elements = (System.Array)value;
        int length = elements?.Length ?? 0;

        writer.WriteCounter32(length);

        for (int i = 0; i < length; i++)
            ExportValue(writer, elements.GetValue(i), field);
    }

    private void ExportValue(ScbWriter writer, object value, Field field)
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
