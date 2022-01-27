using SheetMan.Models;
using SheetMan.Extensions;
using System.Linq;
using System.IO;
using SheetMan.Recipe;
using Serilog;
using SheetMan.Helpers;
using System.Text;

namespace SheetMan.CodeGeneration
{
    public partial class CsCodeGenerator
    {
        private Options _options;
        private Model _model;

        private RecipeModel.CodeGenerationRecipeGroup.CSharpRecipe _csharpReceipe;
        private string _csFilename;

        public void Generate(Options options, RecipeModel recipeModel, Model model)
        {
            _options = options;
            _model = model;

            foreach (var csharpRecipe in recipeModel.CodeGenerations.CSharp)
            {
                _csharpReceipe = csharpRecipe;
                GenerateModel();
            }
        }

        private void GenerateModel()
        {
            _csFilename = Path.Combine(_csharpReceipe.Path, _csharpReceipe.AccessorName + ".cs");
            _csFilename = Path.GetFullPath(_csFilename);

            Printer cs = new Printer();

            GenerateCommonHeadLines(cs);
            GenerateCodeSnippet(cs, CS_SNIPPER_USING);

            BeginNamespace(cs);

            Log.Information($"Generating codes for CSharp...");

            int count = 0;

            cs.PrintLine("#region Static tables");
            cs.ScopeIn("public partial class Tables\n{");

            GenerateCodeSnippet(cs, CS_SNIPPER_READ_BYTES_DELEGATES);

            count = 0;
            foreach (var table in _model.Tables)
            {
                if (++count != 1) cs.PrintLine();

                var vars = new string[] {
                    "table_name", table.Name.ToPascalCase(),
                    "table_type", table.Name.ToPascalCase() + "Table"
                };
                cs.PushScopedVars(vars);
                cs.PrintLine(
                    "/// <summary>\n" +
                    "/// Property for $table_name$ table.\n" +
                    "/// </summary>\n" +
                    "public static $table_type$ $table_name$ { get; private set; }"
                );
                cs.PopScopedVars();
            }


            // ReadAllAsync

            cs.PrintLine();
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Read all tables.");
            cs.PrintLine("/// </summary>");
            cs.ScopeIn("public static async Task ReadAllAsync(string basePath, string fileExtension = \".table\")\n{");
            if (_model.Tables.Count > 0)
            {
                cs.PrintLine("var tasks = new List<Task>();");
                foreach (var table in _model.Tables)
                {
                    var vars = new string[] {
                        "table_name", table.Name.ToPascalCase(),
                        "table_filename", table.Name + ".table",
                        "table_type", table.Name.ToPascalCase() + "Table",
                        "table_prop", table.Name.ToPascalCase()
                    };
                    cs.PushScopedVars(vars);
                    cs.PrintLine();
                    cs.PrintLine("$table_prop$ = new $table_type$();");
                    cs.PrintLine($"tasks.Add($table_prop$.ReadAsync(System.IO.Path.Combine(basePath, $$\"{table.Name}{{fileExtension}}\")));");
                    cs.PopScopedVars();
                }

                cs.PrintLine();
                cs.PrintLine("await Task.WhenAll(tasks);");

                cs.PrintLine();
                cs.PrintLine("SolveCrossReferences();");
            }
            else
            {
                cs.PrintLine("return await Task.CompletedTask;");
            }
            cs.ScopeOut("}");


            // SolveCrossReferences

            cs.PrintLine();
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Solve cross references.");
            cs.PrintLine("/// </summary>");
            cs.ScopeIn("private static void SolveCrossReferences()\n{");
            count = 0;
            foreach (var table in _model.Tables)
            {
                cs.PushScopedVars(new string[] { "table_name", table.Name.ToPascalCase() });

                bool hasReferenceFileds = false;
                foreach (var sf in table.SerialFields)
                {
                    if (sf.IsRef)
                    {
                        hasReferenceFileds = true;
                        break;
                    }
                }

                if (hasReferenceFileds)
                {
                    if (++count != 1) cs.PrintLine();
                    cs.ScopeIn("foreach (var record in $table_name$.Records)\n{");
                }

                foreach (var sf in table.SerialFields)
                {
                    if (!sf.IsRef)
                        continue;

                    var vars = new string[] {
                        "field_name", "_" + sf.Name.ToCamelCase(),
                        "prop_name", sf.Name.ToPascalCase(),
                        "ref_table", sf.FirstField.RefTableName.ToPascalCase(),
                        "ref_field", sf.FirstField.RefFieldName.ToPascalCase(),
                        "N", sf.Fields.Count.ToString(),
                        "default_value", GetDefaultValue(sf.FirstField)
                    };
                    cs.PushScopedVars(vars);

                    if (sf.IsArray)
                    {
                        cs.ScopeIn("for (int i = 0; i < $N$; i++)\n{");
                        if (string.IsNullOrEmpty(sf.FirstField.RefFieldName))
                        {
                            cs.PrintLine("if (record._$prop_name$_$ref_table$_index[i] > 0)\n{");
                            cs.PrintLine("    record.SetReference_$prop_name$_INTERNAL(i, $ref_table$.GetByIndex(record.$field_name$_$ref_table$_index[i]));");
                            cs.PrintLine("    record.$field_name$_F[i] = true;");
                            cs.PrintLine("}");
                            //cs.PrintLine("else\n{");
                            //cs.PrintLine("    record.SetReference_$prop_name$_INTERNAL(i, $default_value$); // Don't care it?");
                            //cs.PrintLine("}");
                        }
                        else
                        {
                            cs.PrintLine("if (record.$field_name$_$ref_table$_index[i] > 0)\n{");
                            cs.PrintLine("    record.SetReference_$prop_name$_INTERNAL(i, $ref_table$.GetByIndex(record._$prop_name$_$ref_table$_index[i]).$ref_field$);");
                            cs.PrintLine("    record.$field_name$_F[i] = true;");
                            cs.PrintLine("}");
                            //cs.PrintLine("else\n{");
                            //cs.PrintLine("    record.SetReference_$prop_name$_INTERNAL(i, $default_value$); // Don't care it?");
                            //cs.PrintLine("}");
                        }
                        cs.ScopeOut("}");
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(sf.FirstField.RefFieldName))
                        {
                            cs.PrintLine("if (record.$field_name$_$ref_table$_index > 0)\n{");
                            cs.PrintLine("    record.SetReference_$prop_name$_INTERNAL($ref_table$.GetByIndex(record.$field_name$_$ref_table$_index));");
                            cs.PrintLine("    record.$field_name$_F = true;");
                            cs.PrintLine("}");
                        }
                        else
                        {
                            cs.PrintLine("if (record.$field_name$_$ref_table$_index > 0)\n{");
                            cs.PrintLine("    record.SetReference_$prop_name$_INTERNAL($ref_table$.GetByIndex(record.$field_name$_$ref_table$_index).$ref_field$);");
                            cs.PrintLine("    record.$field_name$_F = true;");
                            cs.PrintLine("}");
                        }
                    }
                }

                if (hasReferenceFileds)
                    cs.ScopeOut("}");

                cs.PopScopedVars();
            }
            cs.ScopeOut("}"); // SolveCrossReferences

            cs.ScopeOut("}"); // class Tables

            cs.PrintLine("#endregion"); // region Static tables


            // Table classes
            foreach (var table in _model.Tables)
            {
                cs.PrintLine();
                GenerateTableClass(cs, table);
            }

            // Enums
            if (_model.Enums.Count > 0)
            {
                cs.PrintLine();
                cs.PrintLine("#region Enums");
                GenerateEnums(cs);
                cs.PrintLine("#endregion");
            }

            // ConstantSets
            if (_model.ConstantSets.Count > 0)
            {
                cs.PrintLine();
                cs.PrintLine("#region Constants");
                GenerateConstantSets(cs);
                cs.PrintLine("#endregion");
            }

            // Helpers
            {
                cs.PrintLine();
                cs.PrintLine("#region Helpers");

                GenerateCodeSnippet(cs, CS_SNIPPET_EXCEPTION);
                GenerateCodeSnippet(cs, CS_SNIPPET_COLLECTION_HELPER);
                GenerateCodeSnippet(cs, CS_SNIPPET_TOSTRING_HELPER);

                cs.PrintLine("#endregion");
            }

            EndNamespace(cs);

            StagingFiles.WriteAllTextToFile(_csFilename, cs.ToString());
        }

        private void GenerateTableClass(Printer cs, Table table)
        {
            string tableClassName = table.Name.ToPascalCase();

            Log.Information($"Generate csharp code for accessing table `{table.Name}` into `{_csFilename}`");

            int count = 0;

            // Table
            cs.PrintLine($"#region {tableClassName}Table");
            GenerateComment(cs, table.Comment);
            cs.PrintLine("[System.Serializable]");
            cs.ScopeIn($"public partial class {tableClassName}Table\n{{");

            cs.PrintLine("#region Record");
            cs.PrintLine("[System.Serializable]");
            cs.ScopeIn("public partial class Record\n{");

            // Fields
            cs.PrintLine("#region Fields");

            count = 0;
            foreach (var sf in table.SerialFields)
            {
                string[] vars = new string[] {
                    "field_type", ToCSharpTypeName(sf.FirstField),
                    "prop_name", sf.Name.ToPascalCase(),
                    "field_name", "_" + sf.Name.ToCamelCase(),
                    "N", sf.Fields.Count.ToString(),
                    "ref_table", sf.FirstField.RefTableName.ToPascalCase(),
                    "ref_field", sf.FirstField.RefFieldName.ToPascalCase()
                };
                cs.PushScopedVars(vars);

                if (++count != 1) cs.PrintLine();

                GenerateComment(cs, sf.FirstField.Comment);

                if (sf.IsArray)
                {
                    if (sf.IsRef)
                    {
                        cs.PrintLine("public $field_type$[] $prop_name$ => $field_name$;");
                        cs.PrintLine("public const int $prop_name$_N = $N$;");
                        cs.PrintLine("private $field_type$[] $field_name$ = new $field_type$[$field_name$_N];");
                        if (sf.FirstField.Type == Models.ValueType.ForeignRecord)
                            cs.PrintLine("public void SetReference_$prop_name$_INTERNAL(int index, $ref_table$Table.Record value) => $field_name$[index] = value;");
                        else
                            cs.PrintLine("public void SetReference_$prop_name$_INTERNAL(int index, $field_type$ value) => $field_name$[index] = value;");

                        cs.PrintLine("public int[] $field_name$_$ref_table$_index = new int[$prop_name$_N];");
                        cs.PrintLine("public bool[] $field_name$_F = new bool[$field_name$_N];");
                    }
                    else
                    {
                        cs.PrintLine("public $field_type$[] $prop_name$ => $field_name$;");
                        cs.PrintLine("public const int $prop_name$_N = $N$;");
                        cs.PrintLine("private $field_type$[] $field_name$ = new $field_type$[$prop_name$_N];");
                    }
                }
                else
                {
                    cs.PrintLine("public $field_type$ $prop_name$ => $field_name$;");
                    cs.PrintLine("private $field_type$ $field_name$;");

                    if (sf.IsRef)
                    {
                        if (sf.FirstField.Type == Models.ValueType.ForeignRecord)
                            cs.PrintLine("public void SetReference_$prop_name$_INTERNAL($ref_table$Table.Record value) => $field_name$ = value;");
                        else
                            cs.PrintLine("public void SetReference_$prop_name$_INTERNAL($field_type$ value) => $field_name$ = value;");

                        cs.PrintLine("public int $field_name$_$ref_table$_index;");
                        cs.PrintLine("public bool $field_name$_F = false;");
                    }
                }

                cs.PopScopedVars();
            }

            cs.PrintLine("#endregion"); // region Fields


            // Read

            cs.PrintLine();
            cs.PrintLine("#region Read record");
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Reads a table record.");
            cs.PrintLine("/// </summary>");
            cs.ScopeIn("public Task ReadAsync(LiteBinaryReader reader)\n{");

            // field 중에 enum 타입이 하나라도 있으면, 캐스팅이 필요하므로, 임시로 변수 선언해주어야함.
            bool needTempIntergerVarForEnumCasting = table.SerialFields.Where(x => x.Type == Models.ValueType.Enum).Count() > 0;
            if (needTempIntergerVarForEnumCasting)
                cs.PrintLine("int tempEnumInt = 0;");

            count = 0;
            foreach (var sf in table.SerialFields)
            {
                if (++count != 1) cs.PrintLine();

                var vars = new string[] {
                    "field_name", "_" + sf.Name.ToCamelCase(),
                    "prop_name", sf.Name.ToPascalCase(),
                    "field_type", ToCSharpTypeName(sf.FirstField),
                    "ref_table", sf.FirstField.RefTableName.ToPascalCase(),
                    "ref_field", sf.FirstField.RefFieldName.ToPascalCase(),
                    "N", sf.Fields.Count.ToString()
                };
                cs.PushScopedVars(vars);

                if (sf.IsArray)
                {
                    cs.ScopeIn("for (int i = 0; i < $prop_name$_N; ++i)\n{");
                    if (sf.Type == Models.ValueType.Enum)
                    {
                        cs.PrintLine("reader.ReadOptimalInt32(out tempEnumInt);");
                        cs.PrintLine("$field_name$[i] = ($field_type$)tempEnumInt;");
                    }
                    else
                    {
                        if (sf.IsRef)
                        {
                            cs.PrintLine("reader.Read(out $field_name$_$ref_table$_index[i]);");
                            cs.PrintLine("$field_name$[i] = default($field_type$); // will be assigned.");
                            cs.PrintLine("$field_name$_F[i] = false;");
                        }
                        else
                        {
                            cs.PrintLine("reader.Read(out $field_name$[i]);");
                        }
                    }
                    cs.ScopeOut("}");
                }
                else
                {
                    if (sf.Type == Models.ValueType.Enum)
                    {
                        cs.PrintLine("reader.ReadOptimalInt32(out tempEnumInt);");
                        cs.PrintLine("$field_name$ = ($field_type$)tempEnumInt;");
                    }
                    else
                    {
                        if (sf.IsRef)
                        {
                            cs.PrintLine("reader.Read(out $field_name$_$ref_table$_index);");
                            cs.PrintLine("$field_name$ = default($field_type$); // will be assigned.");
                            cs.PrintLine("$field_name$_F = false;");
                        }
                        else
                        {
                            cs.PrintLine("reader.Read(out $field_name$);");
                        }
                    }
                }

                cs.PopScopedVars();
            }

            if (table.SerialFields.Count > 0)
                cs.PrintLine();

            cs.PrintLine("return Task.CompletedTask;");

            cs.ScopeOut("}");
            cs.PrintLine("#endregion"); // region Read


            // ToString

            cs.PrintLine();
            cs.PrintLine("#region ToString");
            cs.ScopeIn("public override string ToString()\n{");
            cs.PrintLine("var sb = new StringBuilder(\"{\");");
            count = 0;
            //todo serialfield일 경우에는 안맞을듯 싶은데???
            foreach (var sf in table.SerialFields)
            {
                if (++count != 1)
                    cs.PrintLine("sb.Append(\",\\\"$prop_name$\\\":\"); ToStringHelper.ToString($prop_name$, sb);", new string[] { "prop_name", sf.Name.ToPascalCase() });
                else
                    cs.PrintLine("sb.Append(\"\\\"$prop_name$\\\":\"); ToStringHelper.ToString($prop_name$, sb);", new string[] { "prop_name", sf.Name.ToPascalCase() });
            }
            cs.PrintLine("sb.Append(\"}\");");
            cs.PrintLine("return sb.ToString();");
            cs.ScopeOut("}");
            cs.PrintLine("#endregion"); // region ToString

            cs.ScopeOut("}");  // class Record

            cs.PrintLine("#endregion"); // region Record


            // FieldNames

            cs.PrintLine();
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Field names.");
            cs.PrintLine("/// </summary>");

            var fieldNameLiterals = table.SerialFields.Select(x => "\"" + x.Name.ToPascalCase() + "\"").ToArray(); //TODO 어레이일 경우에는 이름이 이상하다. 일단 ToPascalCase로 했...
            cs.PrintLine($"public static readonly string[] FieldNames = {{ {string.Join(", ", fieldNameLiterals)} }};");


            // BuildObjectValueMap

            cs.PrintLine();
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Build object value map.");
            cs.PrintLine("/// </summary>");
            cs.ScopeIn("public List<object[]> BuildObjectValueMap()\n{");
            cs.PrintLine($"var result = new List<object[]>();");
            cs.PrintLine("foreach (var r in _records)");
            var fieldValues = table.SerialFields.Select(x => "r." + x.Name.ToPascalCase()).ToArray(); //TODO 어레이일 경우에는 이름이 이상하다. 일단 ToPascalCase로 했...
            cs.PrintLine($"    result.Add(new object[] {{ {string.Join(", ", fieldValues)} }});");
            cs.PrintLine();
            cs.PrintLine("return result;");
            cs.ScopeOut("}");


            // Records

            cs.PrintLine();
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// All records.");
            cs.PrintLine("/// </summary>");
            cs.PrintLine("public List<Record> Records => _records;");
            cs.PrintLine("private readonly List<Record> _records = new List<Record>();");

            // Indexing
            foreach (var sf in table.SerialFields)
            {
                if (!sf.IsIndexer)
                    continue;

                string[] vars = {
                    "table_name", table.Name,
                    "field_type", ToCSharpTypeName(sf.FirstField),
                    "prop_name", sf.Name.ToPascalCase()
                };
                cs.PushScopedVars(vars);

                cs.PrintLine();

                cs.PrintLine("#region Indexing by '$prop_name$'");
                cs.PrintLine("public Dictionary<$field_type$, Record> RecordsBy$prop_name$ => _recordsBy$prop_name$;");
                cs.PrintLine("private readonly Dictionary<$field_type$, Record> _recordsBy$prop_name$ = new Dictionary<$field_type$, Record>();");

                // GetByXXX
                cs.PrintLine();
                cs.PrintLine("/// <summary>");
                cs.PrintLine("/// Gets the value associated with the specified key. throw SheetManException if not found.");
                cs.PrintLine("/// </summary>");
                cs.ScopeIn("public Record GetBy$prop_name$($field_type$ key)\n{");
                cs.PrintLine("if (!TryGetBy$prop_name$(key, out Record record))");
                cs.PrintLine("    throw new SheetManException($$\"There is no record in table `$table_name$` that corresponds to field `$prop_name$` value {key}\");");
                cs.PrintLine();
                cs.PrintLine("return record;");
                cs.ScopeOut("}");

                // bool TryGetByXXX
                cs.PrintLine();
                cs.PrintLine("/// <summary>");
                cs.PrintLine("/// Gets the value associated with the specified key.");
                cs.PrintLine("/// </summary>");
                cs.PrintLine("public bool TryGetBy$prop_name$($field_type$ key, out Record result) => _recordsBy$prop_name$.TryGetValue(key, out result);");

                // ContainsXXX
                cs.PrintLine();
                cs.PrintLine("/// <summary>");
                cs.PrintLine("/// Determines whether the table contains the specified key.");
                cs.PrintLine("/// </summary>");
                cs.PrintLine("public bool Contains$prop_name$($field_type$ key) => _recordsBy$prop_name$.ContainsKey(key);");

                cs.PrintLine("#endregion // Indexing by `$prop_name$`");

                cs.PopScopedVars();
            }


            // ReadAsync

            cs.PrintLine();
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Read a table from specified file.");
            cs.PrintLine("/// </summary>");
            cs.ScopeIn("public async Task ReadAsync(string filename)\n{");
            cs.PrintLine("var bytes = await Tables.ReadAllBytesAsync(filename);");
            cs.PrintLine("var reader = new LiteBinaryReader(bytes);");
            cs.PrintLine("await ReadAsync(reader);");
            cs.ScopeOut("}");
            cs.PrintLine();
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Read a table from specified reader.");
            cs.PrintLine("/// </summary>");
            cs.ScopeIn("public async Task ReadAsync(LiteBinaryReader reader)\n{");
            cs.PrintLine("uint version = 0;");
            cs.PrintLine("reader.Read(out version);"); //TODO version checking
            cs.PrintLine();
            cs.PrintLine("byte flags = 0;");
            cs.PrintLine("reader.Read(out flags);"); //TODO Reserved for future features(compression/encryption)
            cs.PrintLine();
            cs.PrintLine("int count = reader.ReadCounter32();");
            cs.PrintLine("for (int i = 0; i < count; i++)");
            cs.PrintLine("{");
            cs.PrintLine("    var record = new Record();");
            cs.PrintLine("    await record.ReadAsync(reader);");
            cs.PrintLine("    _records.Add(record);");
            cs.PrintLine("}");

            count = 0;
            foreach (var sf in table.SerialFields)
            {
                if (!sf.IsIndexer)
                    continue;

                string[] vars = new string[] {
                    "prop_name", sf.Name.ToPascalCase()
                };

                if (++count == 1)
                {
                    cs.PrintLine();
                    cs.PrintLine("// Index mapping");
                }

                cs.PushScopedVars(vars);
                cs.PrintLine("foreach (var record in _records)");
                cs.PrintLine("    _recordsBy$prop_name$.Add(record.$prop_name$, record);");
                cs.PopScopedVars();
            }

            cs.ScopeOut("}"); // ReadAsync


            // ToString

            cs.PrintLine();
            cs.ScopeIn("public override string ToString()\n{");
            cs.PrintLine("var sb = new StringBuilder();");
            cs.PrintLine("ToStringHelper.ToString(_records, sb);");
            cs.PrintLine("return sb.ToString();");
            cs.ScopeOut("}");


            cs.ScopeOut("}"); // class Table

            cs.PrintLine($"#endregion"); // region Table
        }

        private void GenerateEnums(Printer cs)
        {
            //cs.ScopeIn("namespace Enums\n{");
            int count = 0;
            foreach (var enumm in _model.Enums)
            {
                if (++count != 1) cs.PrintLine("");
                GenerateEnum(cs, enumm);
            }
            //cs.ScopeOut("} // end of namespace Enums");
        }

        private void GenerateEnum(Printer cs, Models.Enum enumm)
        {
            Log.Information($"Generate csharp code for accessing enum `{enumm.Name}` into `{_csFilename}`");

            string typeName = enumm.Name.ToPascalCase();

            cs.PrintLine($"// Generated from {enumm.Location}");
            GenerateComment(cs, enumm.Comment);

            cs.ScopeIn($"public enum {typeName}\n{{");
            for (int i = 0; i < enumm.Labels.Count; i++)
            {
                var label = enumm.Labels[i];

                GenerateComment(cs, label.Comment);
                if (i == enumm.Labels.Count - 1)
                    cs.PrintLine($"{label.Name.ToPascalCase()} = {label.Value}");
                else
                    cs.PrintLine($"{label.Name.ToPascalCase()} = {label.Value},");
            }
            cs.ScopeOut("}");

            // IEqulityComparer<T>
            cs.PrintLine();
            //cs.PrintLine("#if !NO_UNITY");
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Helper class for avoiding boxing as dictionary key.");
            cs.PrintLine("/// </summary>");
            cs.ScopeIn($"public struct {typeName}Comparer : IEqualityComparer<{typeName}>\n{{");
            cs.ScopeIn($"public bool Equals({typeName} x, {typeName} y)\n{{");
            cs.PrintLine("return x == y;");
            cs.ScopeOut("}");
            cs.PrintLine();
            cs.ScopeIn($"public int GetHashCode({typeName} obj)\n{{");
            cs.PrintLine("return (int)obj;");
            cs.ScopeOut("}");
            cs.ScopeOut("}");
            //cs.PrintLine("#endif");
        }

        private void GenerateConstantSets(Printer cs)
        {
            int count = 0;
            foreach (var constantSet in _model.ConstantSets)
            {
                if (++count != 1) cs.PrintLine();
                GenerateConstantSet(cs, constantSet);
            }
        }

        private void GenerateConstantSet(Printer cs, ConstantSet constantSet)
        {
            Log.Information($"Generate csharp code for accessing constant-set `{constantSet.Name}` into `{_csFilename}`");

            cs.PrintLine($"// Generated from {constantSet.Location}");
            GenerateComment(cs, constantSet.Comment);

            cs.ScopeIn($"public static class {constantSet.Name.ToPascalCase()}\n{{");

            foreach (var constant in constantSet.Constants)
            {
                GenerateComment(cs, constant.Comment);

                string csharpType = ToCSharpTypeName(constant.Type, constant.Enum, null, false);
                cs.PrintLine($"public static {csharpType} {constant.Name.ToPascalCase()} {{ get; }}");
            }

            cs.PrintLine();
            cs.PrintLine("/// <summary>");
            cs.PrintLine("/// Static constructor for initialize static variables.");
            cs.PrintLine("/// </summary>");
            cs.ScopeIn($"static {constantSet.Name.ToPascalCase()}()\n{{");
            foreach (var constant in constantSet.Constants)
            {
                string value = RenderConstantValue(cs, constant.Name, constant.Type, constant.Enum, constant.Value, constant.Location);
                cs.PrintLine($"{constant.Name.ToPascalCase()} = {value};");
            }
            cs.ScopeOut("}");

            cs.ScopeOut("}");
        }

        private void GenerateComment(Printer cs, string comment)
        {
            if (string.IsNullOrEmpty(comment))
                return;

            cs.PrintLine("/// <summary>\n/// " + comment.Replace("\n", "\n/// ") + "\n/// </summary>");
        }

        private string ToCSharpTypeName(Field field, bool asArray = false)
        {
            return ToCSharpTypeName(field.Type, field.EnumOrNull, field.RefTableName, asArray); //TODO field.RefTableName으로 하면 안되고 resolve된 이름으로 해야할텐데...
        }

        private string ToCSharpTypeName(Models.ValueType type, Models.Enum enumm, string refTableName, bool asArray = false)
        {
            string result;
            switch (type)
            {
                case Models.ValueType.String:
                    result = "string";
                    break;
                case Models.ValueType.Bool:
                    result = "bool";
                    break;
                case Models.ValueType.Int32:
                    result = "int";
                    break;
                case Models.ValueType.Int64:
                    result = "long";
                    break;
                case Models.ValueType.Float:
                    result = "float";
                    break;
                case Models.ValueType.Double:
                    result = "double";
                    break;
                case Models.ValueType.TimeSpan:
                    result = "System.TimeSpan";
                    break;
                case Models.ValueType.DateTime:
                    result = "System.DateTime";
                    break;
                case Models.ValueType.Uuid:
                    result = "System.Guid";
                    break;
                case Models.ValueType.Enum:
                    result = QualifiedNamespacePrefix + enumm.Name.ToPascalCase();
                    break;
                case Models.ValueType.ForeignRecord:
                    result = $"{refTableName.ToPascalCase()}Table.Record";
                    break;
                default:
                    throw new SheetManException($"unsupported type: {type}");
            }

            return asArray ? (result + "[]") : result;
        }

        //TODO 배열도 지원하면 좋으려나?
        private string RenderConstantValue(Printer output, string name, ValueType valueType, Models.Enum enumm, object value, Location location)
        {
            StringBuilder render = new StringBuilder();

            switch (valueType)
            {
                case Models.ValueType.String:
                    render.Append($"\"{EscapeString((string)value)}\"");
                    break;
                case Models.ValueType.Bool:
                    render.Append((bool)value ? "true" : "false");
                    break;
                case Models.ValueType.Int32:
                    render.Append((int)value);
                    break;
                case Models.ValueType.Int64:
                    render.Append((long)value);
                    break;
                case Models.ValueType.Float:
                    render.Append((float)value + "f"); // "f" suffix
                    break;
                case Models.ValueType.Double:
                    render.Append((double)value);
                    break;
                case Models.ValueType.TimeSpan:
                    render.Append((System.TimeSpan)value); //TODO 어떻게 넣어주어야하나?
                    break;
                case Models.ValueType.DateTime:
                    render.Append((System.DateTime)value);
                    break;
                case Models.ValueType.Uuid:
                    render.Append((System.Guid)value);
                    break;
                case Models.ValueType.Enum:
                    {
                        var label = enumm.GetLabel(value, location);
                        render.Append($"{QualifiedNamespacePrefix}{enumm.Name.ToPascalCase()}.{label.Name.ToPascalCase()}");
                    }
                    break;
                default:
                    throw new SheetManException(location, $"unsupported constant type `{valueType}`");
            }

            return render.ToString();
        }

        private string GetDefaultValue(Field field)
        {
            var valueType = field.Type;
            switch (valueType)
            {
                case Models.ValueType.String:
                    return "\"\"";
                case Models.ValueType.Bool:
                    return "false";
                case Models.ValueType.Int32:
                    return "0";
                case Models.ValueType.Int64:
                    return "0";
                case Models.ValueType.Float:
                    return "0f";
                case Models.ValueType.Double:
                    return "0.0";
                case Models.ValueType.TimeSpan:
                    return "System.TimeSpan.MinValue";
                case Models.ValueType.DateTime:
                    return "System.DateTime.MinValue";
                case Models.ValueType.Uuid:
                    return "new System.Guid()";
                case Models.ValueType.Enum:
                    return ToCSharpTypeName(field) + "." + field.Enum.Labels[0].Name.ToPascalCase();

                // 그냥 null을 넣어주어도 될듯한데??
                case Models.ValueType.ForeignRecord:
                    //return string.Format("new {0}{1}Table.Record()", CSharpNamespacePrefix, column.ExportedRefTable);
                    return "null";

                default:
                    throw new SheetManException("unsupported type: " + valueType);
            }
        }

        private string EscapeString(string input)
        {
            var literal = new StringBuilder(input.Length + 2);
            foreach (var c in input)
            {
                switch (c)
                {
                    case '\'': literal.Append("\\\'"); break;
                    case '\\': literal.Append(@"\\"); break;
                    case '\0': literal.Append(@"\0"); break;
                    case '\a': literal.Append(@"\a"); break;
                    case '\b': literal.Append(@"\b"); break;
                    case '\f': literal.Append(@"\f"); break;
                    case '\n': literal.Append(@"\n"); break;
                    case '\r': literal.Append(@"\r"); break;
                    case '\t': literal.Append(@"\t"); break;
                    case '\v': literal.Append(@"\v"); break;
                    default:
                        // ASCII printable character
                        if (c >= 0x20 && c <= 0x7e)
                        {
                            literal.Append(c);
                            // As UTF16 escaped character
                        }
                        else
                        {
                            literal.Append(@"\u");
                            literal.Append(((int)c).ToString("x4"));
                        }
                        break;
                }
            }
            return literal.ToString();
        }

        private void BeginNamespace(Printer cs)
        {
            if (string.IsNullOrEmpty(_csharpReceipe.Namespace))
                return;

            cs.ScopeIn($"namespace {_csharpReceipe.Namespace}\n{{");
        }

        private void EndNamespace(Printer cs)
        {
            if (string.IsNullOrEmpty(_csharpReceipe.Namespace))
                return;

            cs.PrintLine();
            cs.ScopeOut($"}} // namespace {_csharpReceipe.Namespace}");
        }

        private string QualifiedNamespacePrefix
        {
            get
            {
                if (string.IsNullOrEmpty(_csharpReceipe.Namespace))
                    return "";

                return "global::" + _csharpReceipe.Namespace + ".";
            }
        }

        static void GenerateCodeSnippet(Printer cs, string code)
        {
            // Get rid of unwanted first line feeds.
            if (!string.IsNullOrEmpty(code) && code.Length > 0)
            {
                while (true)
                {
                    if (code[0] == '\r' && code[1] == '\n')
                        code = code[2..];
                    else if (code[0] == '\n')
                        code = code[1..];
                    else
                        break;
                }
            }

            cs.PrintLine(code);
        }

        private void GenerateCommonHeadLines(Printer cs)
        {
            cs.PrintLine("// ------------------------------------------------------------------------------");
            cs.PrintLine("// <auto-generated>");
            cs.PrintLine("//     THIS CODE WAS GENERATED BY SheetMan.");
            cs.PrintLine("//");
            cs.PrintLine("//     CHANGES TO THIS FILE MAY CAUSE INCORRECT BEHAVIOR AND WILL BE LOST IF");
            cs.PrintLine("//     THE CODE IS REGENERATED.");
            cs.PrintLine("// </auto-generated>");
            cs.PrintLine("// ------------------------------------------------------------------------------");
            cs.PrintLine();
        }
    }
}
