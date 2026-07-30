using CommandLine;
using SheetMan.Recipe;
using SheetMan.Models;
using System.Net;
using System.Globalization;
using System.Linq;
using System.IO;
using SheetMan.Helpers;
using SheetMan.Extensions;
using System.Collections.Generic;
using SheetMan.Targets;

namespace SheetMan.CodeGeneration
{
    [SheetManTarget("html", TargetKind.CodeGeneration, Section = "CodeGenerations.Html", Order = 40)]
    public partial class HtmlCodeGenerator : Target<RecipeModel.CodeGenerationRecipeGroup.HtmlRecipe>
    {
        private Model _model;
        private RecipeModel.CodeGenerationRecipeGroup.HtmlRecipe _htmlRecipe;


        protected override void Run(TargetContext context, RecipeModel.CodeGenerationRecipeGroup.HtmlRecipe htmlRecipe)
        {
            _htmlRecipe = htmlRecipe;

            // Already narrowed to the side this entry is built for. Both (the default)
            // leaves the model unchanged.
            _model = context.Model;

            GenerateHtml();
        }

        private void GenerateHtml()
        {
            GenerateOutline();
            GenerateEnums();
            GenerateConstantSets();
            GenerateVariableSets();
            GenerateTables();
        }

        private void GenerateOutline()
        {
            var html = new Printer();

            PrintHeader(html, "Static Definitions Summary");

            html.PrintLine("<br>");

            html.PrintLine("<table class=\"table-bordered table-striped table-condensed\"><tr>");

            html.PrintLine("<td valign=top>");
            html.PrintLine("<h2 id=\"enums\">Enumerations</h2>");
            html.PrintLine("<ul>");
            foreach (var enumm in _model.Enums.OrderBy(x => x.Name))
            {
                if (!string.IsNullOrEmpty(enumm.Comment))
                    html.PrintLine($"<li><a href=\"enums.html#enum_{enumm.Name}\">{enumm.Name}</a> - {Esc(enumm.Comment)}</li>");
                else
                    html.PrintLine($"<li><a href=\"enums.html#enum_{enumm.Name}\">{enumm.Name}</a></li>");
            }
            html.PrintLine("</ul>");
            html.PrintLine("</td>");

            html.PrintLine("<td valign=top>");
            html.PrintLine("<h2 id=\"tables\">Tables</h2>");
            html.PrintLine("<ul>");
            foreach (var table in _model.Tables.OrderBy(x => x.Name))
            {
                if (!string.IsNullOrEmpty(table.Comment))
                    html.PrintLine($"<li><a href=\"tables.html#table_{table.Name}\">{table.Name}</a> - {Esc(table.Comment)}</li>");
                else
                    html.PrintLine($"<li><a href=\"tables.html#table_{table.Name}\">{table.Name}</a></li>");
            }
            html.PrintLine("</ul>");
            html.PrintLine("</td>");


            html.PrintLine("<td valign=top>");
            html.PrintLine("<h2 id=\"constantsets\">ConstantSets</h2>");
            html.PrintLine("<ul>");
            foreach (var constantSet in _model.ConstantSets.OrderBy(x => x.Name))
            {
                if (!string.IsNullOrEmpty(constantSet.Comment))
                    html.PrintLine($"<li><a href=\"constantsets.html#constantset_{constantSet.Name}\">{constantSet.Name}</a> - {Esc(constantSet.Comment)}</li>");
                else
                    html.PrintLine($"<li><a href=\"constantsets.html#constantset_{constantSet.Name}\">{constantSet.Name}</a></li>");
            }
            html.PrintLine("</ul>");
            html.PrintLine("</td>");


            //todo 이렇게하면 테이블이 정의된 시트만 나옴.
            //     유니티크한 모든 위치를 알아내는 방법이 필요한데...

            html.PrintLine("<td valign=top>");
            html.PrintLine("<h2 id=\"source-sheets\">Source sheets</h2>");
            html.PrintLine("<ul>");
            foreach (var table in _model.Tables)
                html.PrintLine($"<li><a href=\"{table.Location.SheetUrl}\">{table.Location.Filename}</a></li>");
            html.PrintLine("</ul>");
            html.PrintLine("</td>");

            html.PrintLine("</tr></table>");

            /*
            html.PrintLine("<br>");
            html.PrintLine("<br>");
            html.PrintLine("<div class=\"definition\">");
            html.PrintLine(
                "<h3>Rules for no value is specified</h3>" +
                "<table class=\"table-bordered table-striped table-condensed\">" +
                "<thead><th>Type</th><th>Default (fallback)</th></thead>" +
                "<tr><td>string</td><td>\"\" (empty string)</td></tr>" +
                "<tr><td>bool</td><td>false</td></tr>" +
                "<tr><td>int32</td><td>0</td></tr>" +
                "<tr><td>int64</td><td>0</td></tr>" +
                "<tr><td>float</td><td>0.0</td></tr>" +
                "<tr><td>double</td><td>0.0</td></tr>" +
                "<tr><td>datetime</td><td>1970-1-1 0:00:00</td></tr>" +
                "<tr><td>enum</td><td>0 or None</td></tr>" +
                "<tr><td>Reference</td><td>-1 (<font color=magenta>The actual value is replaced by the default value of the type.</font>)</td></tr>" +
                "</table>"
                );
            html.PrintLine("<font color=red><i>Note: If you do not specify a value, it will be replaced by the default value, but in some cases it may cause program behavior problems.</i></font>");
            html.PrintLine("</div>");
            */

            PrintFooter(html);

            WriteAllTextToFile("index.html", html.ToString());
        }

        private void GenerateEnums()
        {
            var html = new Printer();

            //PrintHeader(html, "Enumerations");
            //html.PrintLine("<br>");

            foreach (var enumm in _model.Enums)
                GenerateEnum(enumm);

            //PrintFooter(html);

            //WriteAllTextToFile("enums.html", html.ToString());
        }

        private void GenerateEnum(Models.Enum enumm)
        {
            string htmlFilename = $"enums/{enumm.Name.ToKebabCase()}.html";

            var html = new Printer();

            PrintHeader(html, $"Enum {enumm.Name}");

            html.PrintLine("<div class=\"definition\">");

            html.PrintLine($"<h3 id=\"enum_{enumm.Name}\"><i>Enumeration: {GetSourceSheetLink(enumm.Location, enumm.Name)}</i></h3>");

            if (!string.IsNullOrEmpty(enumm.Comment))
                html.PrintLine($"<div class=\"comment\">{Esc(enumm.Comment)}</div>");

            html.PrintLine("<table class=\"table-bordered table-striped table-condensed\">");
            html.PrintLine("<thead><th>No</th><th>Name</th><th>Value</th><th>Description</th></thead>");

            int no = 1;
            foreach (var label in enumm.Labels)
            {
                html.PrintLine("<tr>");
                html.PrintLine($"<td>{no}</td>");
                html.PrintLine($"<td id=\"const_{enumm.Name}.{label.Name}\">{GetSourceSheetLink(label.Location, label.Name)}</td>");
                html.PrintLine($"<td><code>{label.Value}</code></td>");
                html.PrintLine($"<td>{Esc(label.Comment)}</td>");
                html.PrintLine("</tr>");
                no++;
            }
            html.PrintLine("</table>");

            html.PrintLine("</div>");

            html.PrintLine("<br>");
            
            WriteAllTextToFile(htmlFilename, html.ToString());
        }

        private void GenerateConstantSets()
        {
            var html = new Printer();

            PrintHeader(html, "ConstantSets");
            html.PrintLine("<br>");

            foreach (var constantSet in _model.ConstantSets)
                GenerateConstantSet(html, constantSet);

            PrintFooter(html);

            WriteAllTextToFile("constantsets.html", html.ToString());
        }

        private void GenerateConstantSet(Printer html, ConstantSet constantSet)
        {
            html.PrintLine("<div class=\"definition\">");

            html.PrintLine($"<h3 id=\"constantset_{constantSet.Name}\"><i>ConstantSet: {GetSourceSheetLink(constantSet.Location, constantSet.Name)}</i></h3>");

            if (!string.IsNullOrEmpty(constantSet.Comment))
                html.PrintLine($"<div class=\"comment\">{Esc(constantSet.Comment)}</div>");

            html.PrintLine("<table class=\"table-bordered table-striped table-condensed\">");
            html.PrintLine("<thead><th>No</th><th>Name</th><th>Type</th><th>Value</th><th>Description</th></thead>");

            int no = 1;
            foreach (var constant in constantSet.Constants)
            {
                html.PrintLine("<tr>");
                html.PrintLine($"<td>{no}</td>");
                html.PrintLine($"<td id=\"const_{constantSet.Name}.{constant.Name}\">{GetSourceSheetLink(constant.Location, constant.Name)}</td>");
                if (constant.Type == Models.ValueType.Enum)
                {
                    var label = constant.Enum.GetLabel(constant.Value, constant.Location);
                    html.PrintLine($"<td>{GetSourceSheetLink(constant.Enum.Location, constant.Enum.Name)}</td>");
                    //html.PrintLine($"<td><code>{label.Name}</code></td>");
                    html.PrintLine($"<td>{GetSourceSheetLink(label.Location, label.Name)} ({label.Value})</td>");
                }
                else
                {
                    html.PrintLine($"<td>{Esc(constant.TypeName)}</td>");
                    //html.PrintLine($"<td><code>{constant.Value}</code></td>");
                    html.PrintLine($"<td>{Esc(constant.Value?.ToString())}</td>");
                }
                html.PrintLine($"<td>{Esc(constant.Comment)}</td>");
                html.PrintLine("</tr>");
                no++;
            }
            html.PrintLine("</table>");

            html.PrintLine("</div>");

            html.PrintLine("<br>");
        }

        private void GenerateVariableSets()
        {
            //TODO
        }

        private void GenerateVariableSet(Printer html, ConstantSet constantSet)
        {
            //TODO
        }

        private void GenerateTables()
        {
            var html = new Printer();

            PrintHeader(html, "Tables");
            html.PrintLine("<br>");

            foreach (var table in _model.Tables)
                GenerateTable(html, table);

            PrintFooter(html);

            WriteAllTextToFile("tables.html", html.ToString());
        }


        //테이블 헤더 sticky로 만들기
        //https://www.geeksforgeeks.org/how-to-make-bootstrap-table-with-sticky-table-head/
        private void GenerateTable(Printer html, Models.Table table)
        {
            html.PrintLine("<div class=\"definition\">");

            html.PrintLine($"<h3 id=\"table_{table.Name}\"><i>Table: {GetSourceSheetLink(table.Location, table.Name)} - {table.Data.Count} Record(s)</i></h3>");

            if (!string.IsNullOrEmpty(table.Comment))
                html.PrintLine($"<div class=\"comment\">{Esc(table.Comment)}</div>");

            html.PrintLine("<table class=\"table-bordered table-striped table-condensed header-fixed  table-hover\">");

            // Header shows the sheet's own columns rather than the folded groups, so the
            // page reads like the workbook it documents. Which columns folded together is
            // still worth knowing, so a grouped column names its group in a tooltip.
            html.PrintLine("<thead>");
            foreach (var field in table.Fields)
            {
                string caption = field.IsRef ? $"*{Esc(field.Name)}*" : Esc(field.Name);
                string group = GroupNameOf(table, field);

                if (group != null)
                    html.PrintLine($"<th title=\"exposed as {Esc(group)}\">{caption}</th>");
                else
                    html.PrintLine($"<th>{caption}</th>");
            }
            html.PrintLine("</thead>");

            //html.Print($"<tr height=1><td style='background-color: #CCCCCC; padding: 0 0 0 0;' colspan={table.Fields.Count}></td></tr>");

            html.PrintLine("<thead>");
            foreach (var field in table.Fields)
            {
                html.PrintLine($"<th>{Esc(field.Comment)}</th>");
            }
            html.PrintLine("</thead>");

            //html.Print($"<tr height=1><td style='background-color: #CCCCCC; padding: 0 0 0 0;' colspan={table.Fields.Count}></td></tr>");

            // Field types.
            html.PrintLine("<thead>");
            foreach (var field in table.Fields)
            {
                html.Print("<th>");

                if (field.IsRef)
                {
                    //TODO
                    /*
                    if (_model.GetRefField(field, null, out Models.Table refTable, out Models.Field refField, out string refValue))
                    {
                        if (refField != null)
                        {
                            html.Print($"<a href=\"tables.html#table_{refTable.Name}\">ref:{refTable.Name}.{refField.Name}</a>");
                            html.Print(" ");
                            PrintType(html, refField);
                        }
                        else
                        {
                            //TODO
                            //이미 검증이 되었을 경우에는 이 경우는 절대 나오면 안된다!
                        }
                    }
                    else*/
                    {
                        html.Print("<font color=red><b>ref?</b></font>");
                    }
                }
                else
                {
                    PrintType(html, field);
                }

                html.Print("</th>");
            }
            html.PrintLine("</thead>");


            //html.Print($"<tr height=1><td style='background-color: #CCCCCC; padding: 0 0 0 0;' colspan={table.Fields.Count}></td></tr>");

            html.PrintLine("<thead>");
            foreach (var field in table.Fields)
            {
                html.PrintLine($"<th>{field.TargetSide}</th>");
            }
            html.PrintLine("</thead>");


            //html.Print(string.Format("<tr height=1><td style='background-color: #CCCCCC; padding: 0 0 0 0;' colspan={0}></td></tr>", table.Fields.Count));
            //
            //// field C# types.
            //html.PrintLine("<thead>");
            //foreach (var field in table.Fields)
            //{
            //	html.Print(string.Format("<th><code><font color=blue>{0}</font></code>", field.GetCSharpValueType()));
            //}
            //html.PrintLine("</thead>");
            //
            //
            //html.Print(string.Format("<tr height=1><td style='background-color: #CCCCCC; padding: 0 0 0 0;' colspan={0}></td></tr>", table.Fields.Count));
            //
            //// field C/C++ types.
            //html.PrintLine("<thead>");
            //foreach (var field in table.Fields)
            //{
            //	html.Print(string.Format("<th><code><font color=blue>{0}</font></code>", field.GetCppValueType()));
            //}
            //html.PrintLine("</thead>");

            //html.Print($"<tr height=2><td style='background-color: #CCCCCC; padding: 0 0 0 0;' colspan={table.Fields.Count}></td></tr>");

            // Rows
            foreach (var row in table.Data)
            {
                html.PrintLine("<tr>");

                // Driven by the field list, not by walking the row. A row holds every
                // column the sheet declared, whereas the field list is what this page
                // is meant to show; pairing them positionally only worked while the
                // two were guaranteed identical.
                for (int fieldIndex = 0; fieldIndex < table.Fields.Count; fieldIndex++)
                {
                    var field = table.Fields[fieldIndex];
                    var cell = row[field.Index];

                    string bgcolor = "";
                    //if (cell.Value == "")
                    //    bgcolor = " style='background-color: #FFEFEF;'";

                    if (fieldIndex == 0)
                    {
                        html.Print($"<td id=\"table_{table.Name}.{Esc(cell.Value?.ToString())}\" align=right{bgcolor}><code><font color=green>");
                        PrintDataValue(html, field, cell.Value);
                        html.Print("</font></code></td>");
                    }
                    else
                    {
                        html.Print($"<td{bgcolor}>");
                        PrintDataValue(html, field, cell.Value);
                        html.Print("</td>");
                    }
                }

                html.PrintLine("</tr>");
            }

            html.PrintLine("</table>");

            html.PrintLine("</div>");

            html.PrintLine("<br>");
        }

        private void PrintHeader(Printer html, string title)
        {
            html.Print("<html><head>");

            html.PrintLine("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\">");

            //html.Print("<style type=\"text/css\"/><!--");
            //html.Print(BOOTSTRAP_CSS);
            //html.Print("--></style>");
            
            
            //html.PrintLine("<script src=\"https://code.jquery.com/jquery-3.3.1.slim.min.js\" integrity=\"sha384-q8i/X+965DzO0rT7abK41JStQIAqVgRVzpbzo5smXKp4YfRvH+8abtTE1Pi6jizo\" crossorigin=\"anonymous\"></script>");
            //html.PrintLine("<script src=\"https://cdnjs.cloudflare.com/ajax/libs/popper.js/1.14.7/umd/popper.min.js\" integrity=\"sha384-UO2eT0CpHqdSJQ6hJty5KVphtPhzWj9WO1clHTMGa3JDZwrnQq4sF86dIHNDz0W1\" crossorigin=\"anonymous\"></script>");
            html.PrintLine("<link rel=\"stylesheet\" href=\"https://stackpath.bootstrapcdn.com/bootstrap/4.3.1/css/bootstrap.min.css\" integrity=\"sha384-ggOyR0iXCbMQv3Xipma34MD+dH/1fQ784/j6cY/iJTQUOhcWr7x9JvoRxT2MZw1T\" crossorigin=\"anonymous\">");
            html.PrintLine("<script src=\"https://stackpath.bootstrapcdn.com/bootstrap/4.3.1/js/bootstrap.min.js\" integrity=\"sha384-JjSmVgyd0p3pXB1rRibZUAYoIIy6OrQ6VrjIEaFf/nJGzIxFDsf4x0xIM+B07jRM\" crossorigin=\"anonymous\"></script>");
            

            html.Print($"<title>{title}</title></head><body>");

            html.PrintLine("<div class=\"container-fluid\">");
            html.PrintLine($"<h1>{title}</h1>");
        }

        private void PrintFooter(Printer html)
        {
            string date = System.DateTime.Now.ToString("yyyy-MM-dd HH':'mm':'ss");
            string user = System.Environment.UserName;

            html.PrintLine("</div>");

            html.Print("<br>");
            html.Print($"<h4>This file was created at {date} by {user}</h4>");

            html.Print("</body>");
            html.Print("</html>");
        }

        /// <summary>
        /// The array this column is exposed as, when it folded into a group with others.
        /// Null for a column that stands alone.
        /// </summary>
        private static string GroupNameOf(Models.Table table, Models.Field field)
        {
            foreach (var sf in table.SerialFields)
            {
                if (sf.Fields.Count > 1 && sf.Fields.Contains(field))
                    return sf.Name;
            }

            return null;
        }

        static void PrintType(Printer html, Models.Field field)
        {
            //html.Print("<code>");

            // Element type drives the choice; the brackets are appended after, so an
            // array of enums still links to its declaration.
            string suffix = field.IsArray ? "[]" : "";

            switch (field.ElementType)
            {
                case Models.ValueType.Enum:
                    html.Print($"<a href=\"enums.html#enum_{field.Enum.Name}\">enum.{Esc(field.Enum.Name)}</a>{suffix}");
                    break;

                default:
                    html.Print($"<font color=blue>{Esc(field.TypeName)}{suffix}</font>");
                    break;
            }

            //html.Print("</code>");
        }

        private void PrintDataValue(Printer html, Models.Field field, object value,
                    Models.Table redirectedTable = null, Models.Field originalField = null, string originalIndex = null)
        {
            //if (value == "")
            //{
            //    html.Print("<code>Unspecified</code>");
            //    return;
            //}

            if (field.IsRef)
            {
                // The stored index, not the value it points at.
                //
                // Following the reference and rendering the target's value was attempted
                // and abandoned: a chain that leads back on itself recursed without
                // bound. The cooker now rejects cyclic references outright, so it could
                // be revisited - but showing the key is also the honest thing for a page
                // documenting what is stored, and the table it points into is one link
                // away on the same page.
                html.Print($"<code><font color=green>{Esc(value?.ToString())}</font></code> : ");
                return;
            }

            if (redirectedTable != null)
            {
                html.Print(string.Format("<a href=\"tables.html#table_{0}.{1}\" title=\"{2}\">", redirectedTable.Name, originalIndex, string.Format("ref:{0}.{1}#{2}", redirectedTable.Name, field.Name, originalIndex)));
            }

            // A delimited cell holds an array, so print its elements. Falling into the
            // scalar switch below would try to cast the array to the element type.
            if (field.IsArray && value is System.Array elements)
            {
                for (int i = 0; i < elements.Length; i++)
                {
                    if (i > 0)
                        html.Print("<font color=#BBBBBB>, </font>");

                    PrintScalarValue(html, field, elements.GetValue(i));
                }

                if (redirectedTable != null)
                    html.Print("</a>");

                return;
            }

            PrintScalarValue(html, field, value);

            if (redirectedTable != null)
                html.Print("</a>");
        }

        /// <summary>
        /// Renders one value of a field's element type.
        /// </summary>
        private void PrintScalarValue(Printer html, Models.Field field, object value)
        {
            switch (field.ElementType)
            {
                case Models.ValueType.String:
                    html.Print(Esc((string)value));
                    break;

                case Models.ValueType.Bool:
                    {
                        bool booleanValue = (bool)value;

                        //if (booleanValue)
                        //    html.Print("<font color=blue><b>Yes</b></font>");
                        //else
                        //    html.Print("<font color=#BBBBBB>No</font>");

                        if (booleanValue)
                            html.Print("&#x2714;");
                        break;
                    }

                case Models.ValueType.Int32:
                    html.Print(((int)value).ToString(CultureInfo.InvariantCulture));
                    break;

                case Models.ValueType.Int64:
                    html.Print(((long)value).ToString(CultureInfo.InvariantCulture));
                    break;

                case Models.ValueType.Float:
                    html.Print(((float)value).ToString(CultureInfo.InvariantCulture));
                    break;

                case Models.ValueType.Double:
                    html.Print(((double)value).ToString(CultureInfo.InvariantCulture));
                    break;

                case Models.ValueType.DateTime:
                    html.Print(((System.DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    break;

                case Models.ValueType.TimeSpan:
                    html.Print(((System.TimeSpan)value).ToString(null, CultureInfo.InvariantCulture));
                    break;

                case Models.ValueType.Uuid:
                    html.Print(((System.Guid)value).ToString());
                    break;

                case Models.ValueType.Enum:
                    {
                        var label = field.Enum.GetLabel((int)value, null);
                        html.Print($"<a href=\"enums.html#const_{field.Enum.Name}.{label.Name}\" title=\"enum.{field.Enum.Name}.{label.Name}\">{label.Name}</a>");
                        break;
                    }

                case Models.ValueType.ForeignRecord:
                    html.Print("TODO");
                    break;

                default:
                    throw new SheetManException($"unsupported type `{field.Type}`");
            }
        }

        private string GetEnumLink(Models.Enum enumm)
        {
            return $"<a href=\"enums.html#enum_{enumm.Name}\">{enumm.Name}</a>";
        }


        /// <summary>
        /// Escapes text that came from the spreadsheet before it reaches the page.
        ///
        /// Comments and string cells are written by designers, so an ampersand or an
        /// angle bracket in a perfectly ordinary description used to break the
        /// generated documentation - the text was interpolated into the markup raw.
        /// </summary>
        private static string Esc(string text)
            => string.IsNullOrEmpty(text) ? "" : WebUtility.HtmlEncode(text);

        private string GetSourceSheetLink(Models.Location location, string caption = "")
        {
            /*
            string c = char.ToString((char)('A' + location.field));
            string cellPos = string.Format("{0}{1}", c, location.row + 1);

            //TODO 경로를 고정하지 않도록 하자.
            return string.Format("<a href=\"{0}\">{1} : {2} : {3}</a>",
                            location.filename.Replace("../StaticDefinitions/", ""),
                            location.filename.Replace("../StaticDefinitions/", ""),
                            location.sheetName,
                            cellPos);
            */

            //TODO Xlsx의 경우 링크를 해줘봐야 브라우저에서 열리지 않는다. 해주는 방법이 없을까?

            if (!string.IsNullOrEmpty(location.SheetUrl))
            {
                if (!string.IsNullOrEmpty(caption))
                    return $"<a href =\"{location.SheetUrl}\" title=\"Jump to source sheet\">{caption}</a>";
                else
                    return $"<a href =\"{location.SheetUrl}\" title=\"Jump to source sheet\">{location}</a>";
            }

            return "";
        }

        private void WriteAllTextToFile(string filename, string text)
        {
            string fullPath = Path.Combine(_htmlRecipe.Path, filename);
            StagingFiles.WriteAllTextToFile(fullPath, text);
        }
    }
}
