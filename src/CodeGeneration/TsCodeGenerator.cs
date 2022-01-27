using CommandLine;
using SheetMan.Recipe;
using SheetMan.Models;
using System.IO;
using Serilog;
using SheetMan.Extensions;
using SheetMan.Helpers;
using System.Linq;
using System.Collections.Generic;

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
                    ts.PrintLine($"export { constantSet.Name } from './constants/{constantSet.Name}'");
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
            int count = 0;

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
                //TODO Array type일 경우에는 어떻게??
                //TODO 참조 타입일 경우에는??
                if (sf.IsArray)
                {
                    ts.PrintLine("$prop_name$: $field_type$[]");
                }
                else
                {
                    ts.PrintLine("$prop_name$: $field_type$");
                }
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
                        if (sf.FirstField.Type == Models.ValueType.ForeignRecord)
                            ts.PrintLine("public setReference_$prop_name$_INTERNAL(index: number, value: $ref_table$Record): void { $field_name$[index] = value; }");
                        else
                            ts.PrintLine("public setReference_$prop_name$_INTERNAL(index: number, value: $field_type$): void { $field_name$[index] = value; }");

                        //TODO 나중에 하자.
                        //ts.PrintLine("public get $prop_name$(): $field_type$[] { return this.$field_name$; }");
                        //ts.PrintLine("public static readonly $prop_name$_N: number = $N$;");
                        //ts.PrintLine("private $field_name$: $field_type$[] = new Array<$field_type$>(
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
                        if (sf.FirstField.Type == Models.ValueType.ForeignRecord)
                            ts.PrintLine("public setReference_$prop_name$_INTERNAL(value: $ref_table$Record) { $field_name$ = value; }");
                        else
                            ts.PrintLine("public setReference_$prop_name$_INTERNAL(value: $field_type$) { $field_name$ = value }");

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

                // 실상 배열이던 아니던 상환 없음.
                if (sf.IsArray)
                {
                    ts.PrintLine("this.$field_name$ = dataRow.$prop_name$");
                }
                else
                {
                    ts.PrintLine("this.$field_name$ = dataRow.$prop_name$");
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

                // 실상 배열이던 아니던 상환 없음.
                if (sf.IsArray)
                {
                    ts.PrintLine("this.$field_name$ = dataRow[offset++]");
                }
                else
                {
                    ts.PrintLine("this.$field_name$ = dataRow[offset++]");
                }
            }

            ts.ScopeOut("}"); // end of populateFieldValues



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


            // mapping(): void

            ts.PrintLine();
            ts.PrintLine("/** Index mapping. */");
            ts.ScopeIn("private mapping(): void {");
            foreach (var sf in table.SerialFields)
            {
                if (!sf.IsIndexer)
                    continue;

                string[] vars = new string[] {
                    "prop_name_pascal", sf.Name.ToPascalCase(),
                    "prop_name", sf.Name.ToCamelCase()
                };

                //TODO 루프를 한번만 도는게 좋을듯..
                ts.PushScopedVars(vars);
                ts.PrintLine("for (const record of this._records)");
                ts.PrintLine("    this._recordsBy$prop_name_pascal$.set(record.$prop_name$, record);");
                ts.PopScopedVars();
            }
            ts.ScopeOut("}");

            ts.ScopeOut("}"); // end of class Table


            // Write to file.
            StagingFiles.WriteAllTextToFile(tsFilename, ts.ToString());
        }

        private void GenerateImportsForTable(Printer ts, Models.Table table)
        {
            var imports = new List<string>();

            foreach (var sf in table.SerialFields)
            {
                //TODO 참조일 경우에 외부 테이블 import추가해야함.

                if (sf.FirstField.Type == ValueType.Enum)
                {
                    string importStatement = $"import {{ {sf.FirstField.Enum.Name} }} from '../enums/{sf.FirstField.Enum.Name}'";
                    if (!imports.Contains(importStatement))
                        imports.Add(importStatement);
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

        private void GenerateComment(Printer ts, string comment)
        {
            if (string.IsNullOrEmpty(comment))
                return;

            bool singleLineComment = comment.Where(c => c == '\n').Count() <= 1;
            GenerateDocStringComment(ts, "/** ", "", comment, " */\n", singleLineComment); // single line comment
        }

        //TODO 공용으로 빼줘도 좋을듯..
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
            return ToTypescriptTypename(field.Type, field.EnumOrNull, field.RefTableName, asArray); //TODO field.RefTableName으로 하면 안되고 resolve된 이름으로 해야할텐데...
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
                    result = "number";
                    break;
                case Models.ValueType.Float:
                    result = "number";
                    break;
                case Models.ValueType.Double:
                    result = "number";
                    break;
                case Models.ValueType.TimeSpan:
                    result = "string"; //TODO 어떻게 하는게 좋을까?
                    break;
                case Models.ValueType.DateTime:
                    result = "string"; //TODO 어떻게 하는게 좋을까?
                    break;
                case Models.ValueType.Uuid:
                    result = "string"; //TODO 어떻게 하는게 좋을까?
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
