using CommandLine;
using SheetMan.Recipe;
using SheetMan.Models;
using System.IO;
using Serilog;
using SheetMan.Extensions;
using SheetMan.Helpers;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Globalization;
using System.Text;

// `using System` brings System.ValueType into scope, which collides with the
// model's own ValueType that this file refers to unqualified throughout.
using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.CodeGeneration
{
    public partial class TsCodeGenerator
    {
        private Options _options;
        private Model _model;
        private RecipeModel.CodeGenerationRecipeGroup.TypescriptRecipe _typescriptRecipe;

        public void Generate(Options options, RecipeModel recipeModel, Model model)
        {
            _options = options;
            _model = model;

            foreach (var typescriptRecipe in recipeModel.CodeGenerations.Typescript)
            {
                _typescriptRecipe = typescriptRecipe;

                // Narrowed to the side this entry is built for. Both (the default)
                // returns the model unchanged.
                _model = model.ProjectTo(RecipeTargetSide.Of(typescriptRecipe.TargetSide, "CodeGenerations.Typescript"));

                GenerateModel();
            }
        }

        private string GetTsFilename(string name)
        {
            return Path.Combine(_typescriptRecipe.Path, name);
        }

        private void GenerateModel()
        {
            GenerateIndexTs();

            // Enums
            if (_model.Enums.Count > 0)
                GenerateEnums();

            // Tables
            if (_model.Tables.Count > 0)
                GenerateTables();

            // Constant sets
            if (_model.ConstantSets.Count > 0)
                GenerateConstantSets();

            // The runtime the generated tables import.
            WriteBinaryReaderRuntime();
        }


        //파일을 쪼개게 되면 의존성에 따라서 임포트를 해줘야하는 불편함이 있다..
        //감수하자.

        private void GenerateIndexTs()
        {
            string tsFilename = GetTsFilename("index.ts");
            Printer ts = new Printer();

            GenerateCommonHeadLines(ts);

            BeginNamespace(ts);

            if (_model.Enums.Count > 0)
            {
                ts.PrintLine();
                ts.PrintLine("// Enums");
                foreach (var enumm in _model.Enums)
                    ts.PrintLine($"export {{ {enumm.Name} }} from './enums/{enumm.Name}'");
            }

            if (_model.Tables.Count > 0)
            {
                ts.PrintLine();
                ts.PrintLine("// Tables");
                foreach (var table in _model.Tables)
                {
                    ts.PrintLine($"export {{ {table.Name}Record }} from './tables/{table.Name}'");
                    ts.PrintLine($"export {{ {table.Name}Table }} from './tables/{table.Name}'");
                }
            }

            if (_model.ConstantSets.Count > 0)
            {
                ts.PrintLine();
                ts.PrintLine("// Constants");
                foreach (var constantSet in _model.ConstantSets)
                {
                    // `{{` / `}}` because this is an interpolated string: the literal
                    // braces of the named export used to be read as an interpolation
                    // hole, emitting `export GameConfig from ...`, which is a syntax
                    // error. The enum and table lines above always had it right.
                    ts.PrintLine($"export {{ {constantSet.Name} }} from './constants/{constantSet.Name}'");
                }
            }

            ts.PrintLine();
            ts.PrintLine("export { Tables } from './Tables'");
            ts.PrintLine("export { Updater } from './Updater'");

            EndNamespace(ts);


            StagingFiles.WriteAllTextToFile(tsFilename, ts.ToString());
        }

        private void GenerateTables()
        {
            foreach (var table in _model.Tables)
                GenerateTable(table);

            GenerateTableSet();
            GenerateUpdater();
        }

        private void GenerateTableSet()
        {
            string tsFilename = GetTsFilename($"Tables.ts");
            Printer ts = new Printer();
            int count = 0;

            GenerateCommonHeadLines(ts);

            //ts.PrintLine("import * as axios from 'axios'");
            ts.PrintLine("import * as path from 'path'");

            ts.PrintLine();
            foreach (var table in _model.Tables)
            {
                //ts.PrintLine($"import {{ {table.Name}Record }} from './tables/{table.Name}'");
                ts.PrintLine($"import {{ {table.Name}Table }} from './tables/{table.Name}'");
            }


            ts.PrintLine();
            ts.PrintLine("/** Tables */");
            ts.ScopeIn("export class Tables {");

            count = 0;
            foreach (var table in _model.Tables)
            {
                if (++count != 1) ts.PrintLine();

                ts.PrintLine($"/** Peroperty for table {table.Name} */");
                ts.PrintLine($"public get {table.Name.ToCamelCase()}(): {table.Name}Table {{ return this._{table.Name.ToCamelCase()} }}");
                ts.PrintLine($"private _{table.Name.ToCamelCase()}: {table.Name}Table = new {table.Name}Table()");
            }


            // public readAll(basePath: string): Promise<void>

            ts.PrintLine();
            ts.PrintLine("/** Read all tables asynchronously. */");
            ts.ScopeIn("public async readAll(basePath: string): Promise<void> {");
            foreach (var table in _model.Tables)
                ts.PrintLine($"await this._{table.Name.ToCamelCase()}.read(path.join(basePath, '{table.Name}.json'))");
            ts.ScopeOut("}");


            // public readAllSync(basePath: string): void

            ts.PrintLine();
            ts.PrintLine("/** Read all tables synchronously. */");
            ts.ScopeIn("public readAllSync(basePath: string): void {");
            foreach (var table in _model.Tables)
                ts.PrintLine($"this._{table.Name.ToCamelCase()}.readSync(path.join(basePath, '{table.Name}.json'))");
            ts.PrintLine();
            ts.PrintLine("this.solveCrossReferences()");
            ts.ScopeOut("}");


            //todo solveCrossReferences()
            ts.PrintLine();
            ts.ScopeIn("private solveCrossReferences(): void {");
            ts.ScopeOut("}"); // end of solveCrossReferences


            ts.ScopeOut("}"); // end of class Tables



            // Write to file.
            StagingFiles.WriteAllTextToFile(tsFilename, ts.ToString());
        }

        private void GenerateUpdater()
        {
            string tsFilename = GetTsFilename($"Updater.ts");
            Printer ts = new Printer();

            GenerateCommonHeadLines(ts);

            ts.PrintLine("import * as axios from 'axios'");

            ts.PrintLine();
            ts.ScopeIn("export class Updater {");
            ts.PrintLine("//TODO");
            ts.ScopeOut("}");


            // Write to file.
            StagingFiles.WriteAllTextToFile(tsFilename, ts.ToString());
        }

        private void GenerateTable(Models.Table table)
        {
            string tsFilename = GetTsFilename($"tables/{table.Name}.ts");
            Printer ts = new Printer();

            //TODO import (의존하는 요소들을 찾아서 임포트 해주면됨)

            string tableClassName = $"{table.Name}Table";
            string recordClassName = $"{table.Name}Record";

            GenerateCommonHeadLines(ts);

            //ts.PrintLine("import { promises as fs } from 'fs'");
            ts.PrintLine("import * as fs from 'fs'");

            // The binary read path needs the LiteBinary reader. Imported by a relative
            // path from the generated tables/ directory, so a consumer only has to place
            // lib/ts/sheetman alongside the generated output.
            ts.PrintLine("import * as sheetman from '../sheetman/lite_binary_reader'");

            GenerateImportsForTable(ts, table);

            // interface IDataRow
            ts.PrintLine();
            ts.PrintLine("/** A type for handling rows when parsing .json. */");
            ts.ScopeIn("interface IDataRow {");

            foreach (var sf in table.SerialFields)
            {
                string[] vars = new string[] {
                    "field_type", ToTypescriptTypename(sf.FirstField),
                    "prop_name", sf.Name.ToCamelCase(),
                    "field_name", $"_{sf.Name.ToCamelCase()}",
                    "N", sf.Fields.Count.ToString(),
                    "ref_table", sf.FirstField.RefTableName.ToPascalCase(),
                    "ref_field", sf.FirstField.RefFieldName.ToPascalCase()
                };
                ts.PushScopedVars(vars);

                // The JSON wire type, which is not always the member type: a 64-bit
                // integer is exported as a string so JSON's single numeric type cannot
                // round it. A reference carries the target's index, which is a number
                // either way.
                string wireType = JsonWireTypeOf(sf);

                if (sf.IsArray)
                    ts.PrintLine($"$prop_name$: {wireType}[]");
                else
                    ts.PrintLine($"$prop_name$: {wireType}");

                ts.PopScopedVars();
            }

            ts.ScopeOut("}"); // end of IDataRow


            ts.PrintLine();
            ts.PrintLine($"// Generated from {table.Location}");
            GenerateComment(ts, table.Comment);
            ts.ScopeIn($"export class {recordClassName} {{");

            // Constructor
            ts.PrintLine("/** Default constructor */");
            ts.PrintLine("constructor() {");
            ts.PrintLine("}");

            // Field and properties
            foreach (var sf in table.SerialFields)
            {
                string[] vars = new string[] {
                    "field_type", ToTypescriptTypename(sf.FirstField),
                    "prop_name", sf.Name.ToCamelCase(),
                    "field_name", $"_{sf.Name.ToCamelCase()}",
                    "N", sf.Fields.Count.ToString(),
                    "ref_table", sf.FirstField.RefTableName.ToPascalCase(),
                    "ref_field", sf.FirstField.RefFieldName.ToPascalCase()
                };
                ts.PushScopedVars(vars);

                ts.PrintLine();

                GenerateComment(ts, sf.FirstField.Comment);

                if (sf.IsArray)
                {
                    if (sf.IsRef)
                    {
                        ts.PrintLine("public get $prop_name$(): $field_type$[] { return this.$field_value$ }");
                        ts.PrintLine("public static readonly $prop_name$_N: number = $N$");
                        //어짜피 값으로 어싸인될것이므로 객체를 할당할 필요없음.
                        //ts.PrintLine($"private $field_name$: $field_type$[] = new Array<$field_type$>({recordClassName}.$prop_name$_N)");
                        ts.PrintLine($"private $field_name$: $field_type$[]");
                        if (sf.ElementType == Models.ValueType.ForeignRecord)
                            ts.PrintLine("public setReference_$prop_name$_INTERNAL(index: number, value: $ref_table$Record): void { this.$field_name$[index] = value; }");
                        else
                            ts.PrintLine("public setReference_$prop_name$_INTERNAL(index: number, value: $field_type$): void { this.$field_name$[index] = value; }");

                        //TODO 나중에 하자.
                        //ts.PrintLine("public get $prop_name$(): $field_type$[] { return this.$field_name$; }");
                        //ts.PrintLine("public static readonly $prop_name$_N: number = $N$;");
                        //ts.PrintLine("private $field_name$: $field_type$[] = new Array<$field_type$>(
                    }
                    else if (sf.IsVariableLengthArray)
                    {
                        // No _N: a delimited cell's length differs from row to row.
                        ts.PrintLine($"public get $prop_name$(): $field_type$[] {{ return this.$field_name$ }}");
                        ts.PrintLine($"private $field_name$: $field_type$[] = []");
                    }
                    else
                    {
                        ts.PrintLine($"public get $prop_name$(): $field_type$[] {{ return this.$field_name$ }}");
                        ts.PrintLine($"public static readonly $prop_name$_N = $N$");
                        //어짜피 값으로 어싸인될것이므로 객체를 할당할 필요없음.
                        //ts.PrintLine($"private $field_name$: $field_type$[] = new Array<$field_type$>({recordClassName}.$prop_name$_N)");
                        ts.PrintLine($"private $field_name$: $field_type$[]");
                    }
                }
                else
                {
                    ts.PrintLine("public get $prop_name$(): $field_type$ { return this.$field_name$ }");
                    ts.PrintLine("private $field_name$: $field_type$");

                    if (sf.IsRef)
                    {
                        if (sf.ElementType == Models.ValueType.ForeignRecord)
                            ts.PrintLine("public setReference_$prop_name$_INTERNAL(value: $ref_table$Record) { this.$field_name$ = value; }");
                        else
                            ts.PrintLine("public setReference_$prop_name$_INTERNAL(value: $field_type$) { this.$field_name$ = value }");

                        ts.PrintLine("public $field_name$_$ref_table$_index: number");
                        ts.PrintLine("public $field_name$_F: boolean = false");
                    }
                }

                ts.PopScopedVars();
            }


            // populateFieldValues

            ts.PrintLine();
            ts.PrintLine("/** Populate field values. */");
            ts.ScopeIn("public populateFieldValues(dataRow: IDataRow): void {");

            foreach (var sf in table.SerialFields)
            {
                string[] vars = new string[] {
                    "field_type", ToTypescriptTypename(sf.FirstField),
                    "prop_name", sf.Name.ToCamelCase(),
                    "field_name", $"_{sf.Name.ToCamelCase()}",
                    "N", sf.Fields.Count.ToString(),
                    "ref_table", sf.FirstField.RefTableName.ToPascalCase(),
                    "ref_field", sf.FirstField.RefFieldName.ToPascalCase()
                };
                ts.PushScopedVars(vars);

                if (!NeedsJsonConversion(sf))
                {
                    // Array or scalar alike: a value the JSON carries as-is is assigned
                    // straight through.
                    ts.PrintLine("this.$field_name$ = dataRow.$prop_name$");
                }
                else if (sf.IsArray)
                {
                    ts.PrintLine($"this.$field_name$ = dataRow.$prop_name$.map(v => {FromJsonExpression(sf, "v")})");
                }
                else
                {
                    ts.PrintLine($"this.$field_name$ = {FromJsonExpression(sf, "dataRow.$prop_name$")}");
                }
            }

            ts.ScopeOut("}"); // end of populateFieldValues



            ts.PrintLine();
            ts.PrintLine("/** Populate field values. */");
            ts.ScopeIn("public populateFieldValuesCompact(dataRow: any[]): void {");
            ts.PrintLine("let offset = 0");

            foreach (var sf in table.SerialFields)
            {
                string[] vars = new string[] {
                    "field_type", ToTypescriptTypename(sf.FirstField),
                    "prop_name", sf.Name.ToCamelCase(),
                    "field_name", $"_{sf.Name.ToCamelCase()}",
                    "N", sf.Fields.Count.ToString(),
                    "ref_table", sf.FirstField.RefTableName.ToPascalCase(),
                    "ref_field", sf.FirstField.RefFieldName.ToPascalCase()
                };
                ts.PushScopedVars(vars);

                // The compact row is flat: a serial field contributes one entry per
                // column, matching how the binary exporter writes them. Reading a
                // single entry for the whole group took only its first column and
                // left every later field reading someone else's value.
                string convert = NeedsJsonConversion(sf) ? $".map(v => {FromJsonExpression(sf, "v")})" : "";

                if (sf.IsVariableLengthArray)
                {
                    // One entry that already is an array, so it is taken whole. A
                    // serial field is flattened across N entries and sliced below.
                    ts.PrintLine($"this.$field_name$ = dataRow[offset++]{convert}");
                }
                else if (sf.IsArray)
                {
                    ts.PrintLine($"this.$field_name$ = dataRow.slice(offset, offset + $N$){convert}");
                    ts.PrintLine("offset += $N$");
                }
                else if (NeedsJsonConversion(sf))
                {
                    ts.PrintLine($"this.$field_name$ = {FromJsonExpression(sf, "dataRow[offset++]")}");
                }
                else
                {
                    ts.PrintLine("this.$field_name$ = dataRow[offset++]");
                }
            }

            ts.ScopeOut("}"); // end of populateFieldValues

            GenerateRecordBinaryRead(ts, table);

            ts.ScopeOut("}"); // end of class Record


            // Table

            ts.PrintLine();
            ts.PrintLine($"// Generated from {table.Location}");
            GenerateComment(ts, table.Comment);
            ts.ScopeIn($"export class {table.Name}Table {{");

            ts.PrintLine("/** Default constructor. */");
            ts.PrintLine("constructor() {");
            ts.PrintLine("}");

            ts.PrintLine();
            ts.PrintLine("/** All records. */");
            ts.PrintLine($"public get records(): {table.Name}Record[] {{ return this._records }}");
            ts.PrintLine($"private _records: {table.Name}Record[] = []");


            // Indexing

            foreach (var sf in table.SerialFields)
            {
                if (!sf.IsIndexer)
                    continue;

                string[] vars = {
                    "table_name", table.Name,
                    "record_type", recordClassName,
                    "field_type", ToTypescriptTypename(sf.FirstField),
                    "prop_name", sf.Name.ToCamelCase(),
                    "pascal_name", sf.Name.ToPascalCase()
                };
                ts.PushScopedVars(vars);


                ts.PrintLine();
                ts.PrintLine($"// Indexing by '$prop_name$'");
                ts.PrintLine("public get recordsBy$pascal_name$(): Map<$field_type$, $record_type$> { return this._recordsBy$pascal_name$ }");
                ts.PrintLine("private _recordsBy$pascal_name$: Map<$field_type$, $record_type$> = new Map<$field_type$, $record_type$>()");


                // getByXXX

                ts.PrintLine();
                ts.PrintLine("/** Gets the value associated with the specified key. throw Error if not found. */");
                ts.PrintLine("public getBy$pascal_name$(key: $field_type$): $record_type$ {");
                ts.PrintLine("    const found = this._recordsBy$pascal_name$.get(key)");
                ts.PrintLine("    if (!found)");
                ts.PrintLine("        throw new Error(`There is no record in table \"$table_name$\" that corresponds to field \"$prop_name$\" value $${key}`)");
                ts.PrintLine();
                ts.PrintLine("    return found");
                ts.PrintLine("}");


                // tryGetByXXX

                ts.PrintLine();
                ts.PrintLine("/** Gets the value associated with the specified key. */");
                ts.PrintLine("public tryGetBy$pascal_name$(key: $field_type$): $record_type$ | undefined {");
                ts.PrintLine("    return this._recordsBy$pascal_name$.get(key)");
                ts.PrintLine("}");


                // constainsXXX

                ts.PrintLine();
                ts.PrintLine("/** Determines whether the table contains the specified key. */");
                ts.PrintLine("public contains$pascal_name$(key: $field_type$): boolean {");
                ts.PrintLine("    return !!this._recordsBy$pascal_name$.has(key)");
                ts.PrintLine("}");

                ts.PopScopedVars();
            }


            // read(filename: string): Promise<void>

            ts.PrintLine();
            ts.PrintLine("/** Read a table from specified file. */");
            ts.PrintLine("public async read(filename: string): Promise<void> {");
            ts.PrintLine("    const json = await fs.promises.readFile(filename, \"utf8\")");
            ts.PrintLine("    this.readFromJson(json)");
            ts.PrintLine("}");


            // readSync(filename: string): void

            ts.PrintLine();
            ts.PrintLine("/** Read a table from specified file synchronously. */");
            ts.PrintLine("public readSync(filename: string): void {");
            ts.PrintLine("    const json = fs.readFileSync(filename, \"utf8\")");
            ts.PrintLine("    this.readFromJson(json)");
            ts.PrintLine("}");


            // readFromJson(json: string): void
            {
                string[] vars = new string[] {
                    "record_type", recordClassName
                };
                ts.PushScopedVars(vars);

                ts.PrintLine();
                ts.PrintLine("private readFromJson(json: string): void {");
                ts.PrintLine("    const dataRows: any[] = JSON.parse(json)");
                ts.PrintLine("    if (this.isCompactRowFormatted(dataRows)) {");
                ts.PrintLine("        for (const dataRow of dataRows) {");
                ts.PrintLine("            const record = new $record_type$()");
                ts.PrintLine("            record.populateFieldValuesCompact(dataRow)");
                ts.PrintLine("            this._records.push(record)");
                ts.PrintLine("        }");
                ts.PrintLine("    } else {");
                ts.PrintLine("        for (const dataRow of dataRows as IDataRow[]) {");
                ts.PrintLine("            const record = new $record_type$()");
                ts.PrintLine("            record.populateFieldValues(dataRow)");
                ts.PrintLine("            this._records.push(record)");
                ts.PrintLine("        }");
                ts.PrintLine("    }");
                ts.PrintLine("");
                ts.PrintLine("    this.mapping()");
                ts.PrintLine("}");

                ts.PopScopedVars();
            }

            ts.PrintLine();
            ts.PrintLine("private isCompactRowFormatted(rows: any[]): boolean {");
            ts.PrintLine("    return rows.length > 0 && Array.isArray(rows[0])");
            ts.PrintLine("}");


            // Binary read path, beside the JSON one.
            //
            // Both are generated so a consumer picks per deployment rather than per build:
            // JSON where the data is inspected by hand or served as text, binary where
            // size and parse time matter. The two produce identical values, which is why
            // the reader formats dates and durations as the strings the JSON export writes
            // rather than surfacing raw ticks.

            GenerateBinaryRead(ts, table, recordClassName);


            // mapping(): void

            ts.PrintLine();
            ts.PrintLine("/** Index mapping. */");
            ts.ScopeIn("private mapping(): void {");

            var indexers = table.SerialFields.Where(sf => sf.IsIndexer).ToList();

            if (indexers.Count > 0)
            {
                // One pass over the records filling every index, rather than a pass per
                // index. A table with a primary and two secondary indexes walked its rows
                // three times where once will do.
                ts.ScopeIn("for (const record of this._records)\n{");

                foreach (var sf in indexers)
                {
                    ts.PushScopedVars(new string[] {
                        "prop_name_pascal", sf.Name.ToPascalCase(),
                        "prop_name", sf.Name.ToCamelCase()
                    });
                    ts.PrintLine("this._recordsBy$prop_name_pascal$.set(record.$prop_name$, record)");
                    ts.PopScopedVars();
                }

                ts.ScopeOut("}");
            }

            ts.ScopeOut("}");

            ts.ScopeOut("}"); // end of class Table


            // Write to file.
            StagingFiles.WriteAllTextToFile(tsFilename, ts.ToString());
        }

        /// <summary>
        /// Emits the binary read path: a reader over the .table file the binary exporter
        /// writes, in the exact field order it writes them.
        /// </summary>
        private void GenerateBinaryRead(Printer ts, Models.Table table, string recordClassName)
        {
            ts.PrintLine();
            ts.PrintLine("/** Read a table from a binary .table file. */");
            ts.ScopeIn("public readBinarySync(filename: string): void\n{");
            ts.PrintLine("this.readBinaryFrom(sheetman.readAllBytes(filename))");
            ts.ScopeOut("}");

            ts.PrintLine();
            ts.PrintLine("/** Read a table from binary data already in memory. */");
            ts.ScopeIn("public readBinaryFrom(data: Uint8Array): void\n{");
            ts.PrintLine("const reader = new sheetman.LiteBinaryReader(data)");
            ts.PrintLine("const rowCount = sheetman.readTableHeader(reader)");
            ts.PrintLine();
            ts.ScopeIn("for (let i = 0; i < rowCount; ++i)\n{");
            ts.PrintLine($"const record = new {recordClassName}()");
            ts.PrintLine("record.readBinary(reader)");
            ts.PrintLine("this._records.push(record)");
            ts.ScopeOut("}");
            ts.PrintLine();
            ts.PrintLine("this.mapping()");
            ts.ScopeOut("}");
        }

        /// <summary>
        /// Emits a record's binary read, matching the exporter's field order.
        /// </summary>
        private void GenerateRecordBinaryRead(Printer ts, Models.Table table)
        {
            ts.PrintLine();
            ts.PrintLine("/** Read one record. Field order must match the exporter's. */");
            ts.ScopeIn("public readBinary(reader: sheetman.LiteBinaryReader): void\n{");

            foreach (var sf in table.SerialFields)
            {
                string field = "_" + sf.Name.ToCamelCase();

                if (sf.IsVariableLengthArray)
                {
                    // Length varies per row, so it precedes the elements on the wire.
                    ts.ScopeIn("{");
                    ts.PrintLine("const count = reader.readCounter32()");
                    ts.PrintLine($"this.{field} = []");
                    ts.ScopeIn("for (let i = 0; i < count; ++i)\n{");
                    ts.PrintLine($"this.{field}.push({BinaryReadExpression(sf)})");
                    ts.ScopeOut("}");
                    ts.ScopeOut("}");
                    continue;
                }

                if (sf.IsArray)
                {
                    // A serial field has one element per column, a count this generator
                    // knows, so nothing precedes the elements on the wire.
                    ts.PrintLine($"this.{field} = []");
                    ts.ScopeIn($"for (let i = 0; i < {sf.Fields.Count}; ++i)\n{{");

                    if (sf.IsRef)
                        ts.PrintLine($"this.{field}_{sf.FirstField.RefTableName.ToPascalCase()}_index.push(reader.readInt32())");
                    else
                        ts.PrintLine($"this.{field}.push({BinaryReadExpression(sf)})");

                    ts.ScopeOut("}");
                    continue;
                }

                if (sf.IsRef)
                {
                    // A reference stores the target's index; the value itself is filled in
                    // once every table is loaded.
                    ts.PrintLine($"this.{field}_{sf.FirstField.RefTableName.ToPascalCase()}_index = reader.readInt32()");
                    continue;
                }

                ts.PrintLine($"this.{field} = {BinaryReadExpression(sf)}");
            }

            if (table.SerialFields.Count == 0)
                ts.PrintLine("// No fields to read.");

            ts.ScopeOut("}");
        }

        /// <summary>
        /// The call reading one value of a column's element type.
        /// </summary>
        private string BinaryReadExpression(SerialField sf)
        {
            switch (sf.ElementType)
            {
                case ValueType.String: return "reader.readString()";
                case ValueType.Bool: return "reader.readBool()";
                case ValueType.Int32: return "reader.readInt32()";
                case ValueType.Int64: return "reader.readInt64()";
                case ValueType.Float: return "reader.readFloat()";
                case ValueType.Double: return "reader.readDouble()";
                case ValueType.DateTime: return "reader.readDateTime()";
                case ValueType.TimeSpan: return "reader.readTimeSpan()";
                case ValueType.Uuid: return "reader.readUuid()";

                // Enum values travel zig-zag encoded rather than fixed width.
                case ValueType.Enum: return $"reader.readEnum() as {ToTypescriptTypename(sf.FirstField)}";

                case ValueType.ForeignRecord: return "reader.readInt32()";

                default:
                    throw new SheetManException(
                        $"TypeScript generator cannot read type `{sf.Type}` from binary.");
            }
        }

        /// <summary>
        /// The type a value has in the JSON export, which is not always the type the
        /// generated member exposes.
        /// </summary>
        private string JsonWireTypeOf(SerialField sf)
        {
            // A 64-bit integer is exported as a string, because JSON's single numeric
            // type is a double and would round it.
            if (sf.ElementType == ValueType.Int64)
                return "string";

            return ToTypescriptTypename(sf.FirstField);
        }

        /// <summary>
        /// Wraps a value read from JSON so it becomes the member's type.
        ///
        /// Two types need it. A 64-bit integer arrives as a string and is reconstructed
        /// exactly. A float arrives as the shortest decimal that round-trips it, which in
        /// JavaScript widens to a double a hair away from the stored 32-bit value - so it
        /// is rounded back to float precision, and both read paths then agree.
        /// </summary>
        private string FromJsonExpression(SerialField sf, string source)
        {
            switch (sf.ElementType)
            {
                case ValueType.Int64: return $"BigInt({source})";
                case ValueType.Float: return $"Math.fround({source})";
                default: return source;
            }
        }

        /// <summary>
        /// Whether values of this column need converting on the way in from JSON.
        /// </summary>
        private bool NeedsJsonConversion(SerialField sf)
            => sf.ElementType == ValueType.Int64 || sf.ElementType == ValueType.Float;

        private void GenerateImportsForTable(Printer ts, Models.Table table)
        {
            var imports = new List<string>();

            foreach (var sf in table.SerialFields)
            {
                if (sf.ElementType == ValueType.Enum)
                {
                    string importStatement = $"import {{ {sf.FirstField.Enum.Name} }} from '../enums/{sf.FirstField.Enum.Name}'";
                    if (!imports.Contains(importStatement))
                        imports.Add(importStatement);
                }

                // A record-level reference names the target's Record class in this
                // file's signatures, so that class has to be imported. Without this
                // the emitted module referred to a type it never pulled in and did
                // not compile - the enum branch above had always been the only one.
                //
                // Resolved rather than declared table name: the declared one is the
                // raw detail-type text, while resolution has already followed the
                // reference chain to the table actually being pointed at.
                if (sf.ElementType == ValueType.ForeignRecord)
                {
                    var refTable = sf.FirstField.ResolvedRefTable;
                    if (refTable != null && refTable.Name != table.Name)
                    {
                        string recordName = refTable.Name.ToPascalCase() + "Record";
                        string importStatement = $"import {{ {recordName} }} from './{refTable.Name}'";
                        if (!imports.Contains(importStatement))
                            imports.Add(importStatement);
                    }
                }
            }

            if (imports.Count > 0)
            {
                ts.PrintLine();
                ts.PrintLine("// Automatically import to handle external type references.");
                foreach (var import in imports)
                    ts.PrintLine(import);
            }
        }

        private void GenerateEnums()
        {
            foreach (var enumm in _model.Enums)
                GenerateEnum(enumm);
        }

        private void GenerateEnum(Models.Enum enumm)
        {
            string tsFilename = GetTsFilename($"enums/{enumm.Name}.ts");
            Printer ts = new Printer();

            //Log.Information($"Generate typescript code for accessing enum `{enumm.Name}` into `{_tsFilename}`");

            string typeName = enumm.Name;

            GenerateCommonHeadLines(ts);

            ts.PrintLine($"// Generated from {enumm.Location}");
            GenerateComment(ts, enumm.Comment);

            ts.ScopeIn($"export enum {typeName} {{");
            for (int i = 0; i < enumm.Labels.Count; i++)
            {
                var label = enumm.Labels[i];

                GenerateComment(ts, label.Comment);

                if (_typescriptRecipe.UseStringEnum)
                {
                    if (i == enumm.Labels.Count - 1)
                        ts.PrintLine($"{label.Name} = '{label.Name}'");
                    else
                        ts.PrintLine($"{label.Name} = '{label.Name}',");
                }
                else
                {
                    if (i == enumm.Labels.Count - 1)
                        ts.PrintLine($"{label.Name} = {label.Value}");
                    else
                        ts.PrintLine($"{label.Name} = {label.Value},");
                }
            }
            ts.ScopeOut("}");

            StagingFiles.WriteAllTextToFile(tsFilename, ts.ToString());
        }

        /// <summary>
        /// Writes the LiteBinary reader into the output, beside the generated modules.
        ///
        /// Emitted rather than left for the consumer to copy: the generated tables import
        /// it by a relative path, and TypeScript has no include-path setting that would
        /// let a project point somewhere else. Shipping it makes the output directory
        /// self-contained.
        ///
        /// The source is an embedded resource taken from lib/ts, so there is one copy to
        /// maintain and it cannot drift from what is shipped.
        /// </summary>
        private void WriteBinaryReaderRuntime()
        {
            const string resourceName = "SheetMan.Runtime.Ts.lite_binary_reader.ts";

            using var stream = typeof(TsCodeGenerator).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new SheetManException($"Embedded resource `{resourceName}` is missing from the build.");

            using var reader = new StreamReader(stream);

            StagingFiles.WriteAllTextToFile(
                GetTsFilename("sheetman/lite_binary_reader.ts"), reader.ReadToEnd());
        }

        private void GenerateConstantSets()
        {
            foreach (var constantSet in _model.ConstantSets)
                GenerateConstantSet(constantSet);
        }

        /// <summary>
        /// Emits one module per constant set, mirroring what the C# generator produces
        /// as a static class.
        ///
        /// index.ts has always re-exported these modules, but nothing ever wrote them,
        /// so any sheet defining a `const` entity produced TypeScript that could not
        /// resolve its own imports.
        ///
        /// Members are camelCase to match the rest of the generated TypeScript, where
        /// table fields use the same convention.
        /// </summary>
        private void GenerateConstantSet(ConstantSet constantSet)
        {
            string tsFilename = GetTsFilename($"constants/{constantSet.Name}.ts");
            Printer ts = new Printer();

            Log.Information($"Generate typescript code for constant-set `{constantSet.Name}` into `{tsFilename}`");

            GenerateCommonHeadLines(ts);

            GenerateImportsForConstantSet(ts, constantSet);

            ts.PrintLine();
            ts.PrintLine($"// Generated from {constantSet.Location}");
            GenerateComment(ts, constantSet.Comment);

            ts.ScopeIn($"export class {constantSet.Name.ToPascalCase()} {{");

            int count = 0;
            foreach (var constant in constantSet.Constants)
            {
                if (++count != 1) ts.PrintLine();

                GenerateComment(ts, constant.Comment);

                string typeName = ToTypescriptTypename(constant.Type, constant.Enum, null);
                string value = RenderConstantValue(constant);

                ts.PrintLine($"public static readonly {constant.Name.ToCamelCase()}: {typeName} = {value}");
            }

            ts.ScopeOut("}");

            StagingFiles.WriteAllTextToFile(tsFilename, ts.ToString());
        }

        private void GenerateImportsForConstantSet(Printer ts, ConstantSet constantSet)
        {
            var imports = new List<string>();

            foreach (var constant in constantSet.Constants)
            {
                if (constant.Type != ValueType.Enum)
                    continue;

                string importStatement = $"import {{ {constant.Enum.Name} }} from '../enums/{constant.Enum.Name}'";
                if (!imports.Contains(importStatement))
                    imports.Add(importStatement);
            }

            if (imports.Count > 0)
            {
                ts.PrintLine();
                ts.PrintLine("// Automatically import to handle external type references.");
                foreach (var import in imports)
                    ts.PrintLine(import);
            }
        }

        /// <summary>
        /// Renders a cooked constant value as a TypeScript literal.
        ///
        /// Types that TypeScript has no native equivalent for - datetime, timespan and
        /// uuid - are surfaced as strings, matching ToTypescriptTypename.
        /// </summary>
        private string RenderConstantValue(ConstantSet.Constant constant)
        {
            switch (constant.Type)
            {
                case ValueType.String:
                    return $"'{EscapeTypescriptString((string)constant.Value)}'";

                case ValueType.Bool:
                    return (bool)constant.Value ? "true" : "false";

                case ValueType.Int32:
                    return ((int)constant.Value).ToString(CultureInfo.InvariantCulture);

                case ValueType.Int64:
                    // `n` suffix: a bigint-typed member cannot be initialized from a
                    // number literal, and TypeScript rejects it outright.
                    return ((long)constant.Value).ToString(CultureInfo.InvariantCulture) + "n";

                case ValueType.Float:
                    return ((float)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                case ValueType.Double:
                    return ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture);

                case ValueType.DateTime:
                    return $"'{((DateTime)constant.Value).ToString("o", CultureInfo.InvariantCulture)}'";

                case ValueType.TimeSpan:
                    return $"'{((TimeSpan)constant.Value).ToString(null, CultureInfo.InvariantCulture)}'";

                case ValueType.Uuid:
                    return $"'{(Guid)constant.Value}'";

                case ValueType.Enum:
                {
                    var label = constant.Enum.GetLabel((int)constant.Value, constant.Location);
                    return $"{constant.Enum.Name.ToPascalCase()}.{label.Name}";
                }

                default:
                    throw new SheetManException(constant.Location,
                        $"Constant `{constant.Name}` has type `{constant.Type}`, which the TypeScript generator cannot render.");
            }
        }

        /// <summary>
        /// Escapes a string for a single-quoted TypeScript literal.
        ///
        /// Non-ASCII characters are left alone: the generated files are UTF-8, and
        /// localized text is far more readable unescaped.
        /// </summary>
        private string EscapeTypescriptString(string input)
        {
            var literal = new StringBuilder(input.Length + 2);

            foreach (var c in input)
            {
                switch (c)
                {
                    case '\'': literal.Append("\\'"); break;
                    case '\\': literal.Append(@"\\"); break;
                    case '\0': literal.Append(@"\0"); break;
                    case '\b': literal.Append(@"\b"); break;
                    case '\f': literal.Append(@"\f"); break;
                    case '\n': literal.Append(@"\n"); break;
                    case '\r': literal.Append(@"\r"); break;
                    case '\t': literal.Append(@"\t"); break;
                    case '\v': literal.Append(@"\v"); break;
                    default:
                        if (c < 0x20)
                            literal.Append(@"\u").Append(((int)c).ToString("x4"));
                        else
                            literal.Append(c);
                        break;
                }
            }

            return literal.ToString();
        }

        private void GenerateComment(Printer ts, string comment)
        {
            if (string.IsNullOrEmpty(comment))
                return;

            bool singleLineComment = comment.Where(c => c == '\n').Count() <= 1;
            GenerateDocStringComment(ts, "/** ", "", comment, " */\n", singleLineComment); // single line comment
        }

        /// <summary>
        /// Emits a doc comment, wrapping the sheet's description text in the target
        /// language's comment syntax.
        ///
        /// Each generator carries its own copy of this. Worth sharing one day - the shape
        /// is identical and only the delimiters differ - but the three have diverged
        /// slightly in how they treat blank lines, so unifying them would change output
        /// rather than merely deduplicate code.
        /// </summary>
        private void GenerateDocStringComment(Printer ts, string commentStart, string linePrefix, string contents, string commentEnd, bool singleLineComment = false)
        {
            if (!string.IsNullOrEmpty(commentStart))
                ts.Print(commentStart);

            if (singleLineComment)
            {
                ts.Print(contents.Replace("\n", "")); // omit new-line
            }
            else
            {
                var lines = contents.Split("\n");
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];

                    if (line.Length == 0 && string.IsNullOrEmpty(linePrefix) && i != (lines.Length - 1))
                    {
                        ts.Print("\n");
                    }
                    else if (line.Length > 0 || i != (lines.Length - 1)) // skip the empty last line
                    {
                        ts.Print($"{linePrefix}{line}");
                    }
                }
            }

            if (!string.IsNullOrEmpty(commentEnd))
                ts.Print(commentEnd);
        }

        private string ToTypescriptTypename(Field field, bool asArray = false)
        {
            // ElementType, not Type: an array field is rendered by naming its element
            // and letting the caller add the brackets, exactly as a serial field is.
            return ToTypescriptTypename(field.ElementType, field.EnumOrNull, field.RefTableName, asArray); //TODO field.RefTableName으로 하면 안되고 resolve된 이름으로 해야할텐데...
        }

        private string ToTypescriptTypename(Models.ValueType type, Models.Enum enumm, string refTableName, bool asArray = false)
        {
            string result;
            switch (type)
            {
                case Models.ValueType.String:
                    result = "string";
                    break;
                case Models.ValueType.Bool:
                    result = "boolean";
                    break;
                case Models.ValueType.Int32:
                    result = "number";
                    break;
                case Models.ValueType.Int64:
                    // BigInt, not number. A double carries 53 bits of mantissa, so a
                    // 64-bit value past 2^53 comes back quietly wrong - the same class of
                    // corruption the binary writer itself once had, and just as invisible.
                    result = "bigint";
                    break;
                case Models.ValueType.Float:
                    result = "number";
                    break;
                case Models.ValueType.Double:
                    result = "number";
                    break;
                case Models.ValueType.TimeSpan:
                // These three surface as strings rather than richer types.
                //
                // TypeScript reads the JSON export, and JSON has no date, duration or
                // uuid: each arrives as text. Declaring `Date` would oblige the generated
                // reader to parse on load - work a consumer may not want, on a value it
                // may only pass through - and there is nothing to parse a duration or a
                // uuid into at all. The text is exactly what was exported, so a consumer
                // that needs a richer type can construct one where it needs it.
                    result = "string";
                    break;
                case Models.ValueType.DateTime:
                    result = "string";
                    break;
                case Models.ValueType.Uuid:
                    result = "string";
                    break;
                case Models.ValueType.Enum:
                    result = QualifiedNamespacePrefix + enumm.Name.ToPascalCase();
                    break;
                case Models.ValueType.ForeignRecord:
                    result = $"{refTableName.ToPascalCase()}Record";
                    break;
                default:
                    throw new SheetManException($"unsupported type: {type}");
            }

            return asArray ? (result + "[]") : result;
        }


        //TODO typescript namespace에서 '.'를 지원하는지?
        //typescript namespace spec 에 대해서 한번더 확인해보자.
        private void BeginNamespace(Printer ts)
        {
            if (string.IsNullOrEmpty(_typescriptRecipe.Namespace))
                return;

            ts.ScopeIn($"namespace {_typescriptRecipe.Namespace}\n{{");
        }

        private void EndNamespace(Printer ts)
        {
            if (string.IsNullOrEmpty(_typescriptRecipe.Namespace))
                return;

            ts.PrintLine();
            ts.ScopeOut($"}} // namespace {_typescriptRecipe.Namespace}");
        }

        private string QualifiedNamespacePrefix
        {
            get
            {
                if (string.IsNullOrEmpty(_typescriptRecipe.Namespace))
                    return "";

                return _typescriptRecipe.Namespace + ".";
            }
        }

        private void GenerateCommonHeadLines(Printer ts)
        {
            ts.PrintLine("// ------------------------------------------------------------------------------");
            ts.PrintLine("// <auto-generated>");
            ts.PrintLine("//     THIS CODE WAS GENERATED BY SheetMan.");
            ts.PrintLine("//");
            ts.PrintLine("//     CHANGES TO THIS FILE MAY CAUSE INCORRECT BEHAVIOR AND WILL BE LOST IF");
            ts.PrintLine("//     THE CODE IS REGENERATED.");
            ts.PrintLine("// </auto-generated>");
            ts.PrintLine("// ------------------------------------------------------------------------------");
            ts.PrintLine();
        }
    }
}
