using SheetMan.Recipe;
using SheetMan.Models;
using SheetMan.Targets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using SheetMan.Helpers;
using SheetMan.Extensions;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Emits human-readable documentation of the converted data.
    ///
    /// One summary page, one page per enum, and a page each for the constant sets and the
    /// tables. Every entity links back to the cell it was declared in, which is what makes
    /// the pages useful when a designer asks why a value came out the way it did.
    ///
    /// The markup lives in templates/html-*.sbn. This file works out the cell contents,
    /// which is where the type-dependent decisions are.
    /// </summary>
    [SheetManTarget("html", TargetKind.CodeGeneration, Section = "CodeGenerations.Html", Order = 40)]
    public partial class HtmlCodeGenerator : Target<RecipeModel.CodeGenerationRecipeGroup.HtmlRecipe>
    {
        private Model _model;
        private RecipeModel.CodeGenerationRecipeGroup.HtmlRecipe _htmlRecipe;

        protected override void Run(TargetContext context, RecipeModel.CodeGenerationRecipeGroup.HtmlRecipe htmlRecipe)
        {
            // A blank Path means the entry is switched off, as it does for every other
            // target. This one was missing it, and Path.Combine("", "index.html") is
            // "index.html" - so the skeleton recipe, whose entries are all blank and are
            // meant to be inert, quietly wrote three pages into the working directory.
            if (string.IsNullOrEmpty(htmlRecipe.Path))
                return;

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
            GenerateTables();
        }

        // ------------------------------------------------------------- pages

        private void GenerateOutline()
        {
            var view = new HtmlIndexView
            {
                Title = "Static Definitions Summary",
                Enums = _model.Enums.OrderBy(x => x.Name)
                                    .Select(x => Summarize(x.Name, x.Comment, EnumHref(x.Name, "enum_" + x.Name)))
                                    .ToList(),

                Tables = _model.Tables.OrderBy(x => x.Name)
                                      .Select(x => Summarize(x.Name, x.Comment, $"tables.html#table_{x.Name}"))
                                      .ToList(),

                ConstantSets = _model.ConstantSets.OrderBy(x => x.Name)
                                     .Select(x => Summarize(
                                         x.Name, x.Comment, $"constantsets.html#constantset_{x.Name}"))
                                     .ToList(),

                // Only the sheets tables were found in. Listing every sheet the conversion
                // touched would be better and there is no route to that from here.
                //
                // Distinct, because one workbook usually holds every table and the list was
                // otherwise the same filename repeated once per table.
                SourceSheets = _model.Tables
                                     .Select(table => new HtmlSourceSheetView
                                     {
                                         Url = table.Location.SheetUrl,
                                         Filename = table.Location.Filename,
                                     })
                                     .GroupBy(sheet => (sheet.Filename, sheet.Url))
                                     .Select(group => group.First())
                                     .OrderBy(sheet => sheet.Filename, StringComparer.Ordinal)
                                     .ToList(),
            };

            Write("index.html", "html-index.sbn", view, trailingBlankLine: false);
        }

        private void GenerateEnums()
        {
            foreach (var enumm in _model.Enums)
                GenerateEnum(enumm);
        }

        private void GenerateEnum(Models.Enum enumm)
        {
            int no = 0;

            var view = new HtmlEnumPageView
            {
                Title = $"Enum {enumm.Name}",
                Name = enumm.Name,
                SourceLink = SourceSheetLink(enumm.Location, enumm.Name),
                Comment = Esc(enumm.Comment),
                Labels = enumm.Labels.Select(label => new HtmlEnumLabelView
                {
                    No = ++no,
                    Name = label.Name,
                    SourceLink = SourceSheetLink(label.Location, label.Name),
                    Value = label.Value.ToString(CultureInfo.InvariantCulture),
                    Comment = Esc(label.Comment),
                }).ToList(),
            };

            // No footer on an enum page, so it ends on a PrintLine and carries the blank
            // line the pages built with a footer do not.
            Write($"enums/{enumm.Name.ToKebabCase()}.html", "html-enum.sbn", view, trailingBlankLine: true);
        }

        private void GenerateConstantSets()
        {
            var view = new HtmlConstantSetsPageView
            {
                Title = "ConstantSets",
                Sets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
            };

            Write("constantsets.html", "html-constantsets.sbn", view, trailingBlankLine: false);
        }

        private HtmlConstantSetView BuildConstantSet(ConstantSet constantSet)
        {
            int no = 0;

            return new HtmlConstantSetView
            {
                Name = constantSet.Name,
                SourceLink = SourceSheetLink(constantSet.Location, constantSet.Name),
                Comment = Esc(constantSet.Comment),
                Constants = constantSet.Constants.Select(constant => BuildConstant(constantSet, constant, ++no)).ToList(),
            };
        }

        private HtmlConstantView BuildConstant(ConstantSet constantSet, ConstantSet.Constant constant, int no)
        {
            var view = new HtmlConstantView
            {
                No = no,
                Name = constant.Name,
                NameCell = SourceSheetLink(constant.Location, constant.Name),
                Comment = Esc(constant.Comment),
            };

            if (constant.Type == Models.ValueType.Enum)
            {
                // An enum constant shows where both its type and its label were declared,
                // because either is a place someone might want to go from here.
                var label = constant.Enum.GetLabel(constant.Value, constant.Location);

                view.TypeCell = SourceSheetLink(constant.Enum.Location, constant.Enum.Name);
                view.ValueCell = $"{SourceSheetLink(label.Location, label.Name)} ({label.Value})";
            }
            else
            {
                view.TypeCell = Esc(constant.TypeName);
                view.ValueCell = Esc(constant.Value?.ToString());
            }

            return view;
        }

        private void GenerateTables()
        {
            var view = new HtmlTablesPageView
            {
                Title = "Tables",
                Tables = _model.Tables.Select(BuildTable).ToList(),
            };

            Write("tables.html", "html-tables.sbn", view, trailingBlankLine: false);
        }

        private HtmlTableView BuildTable(Models.Table table) => new HtmlTableView
        {
            Name = table.Name,
            SourceLink = SourceSheetLink(table.Location, table.Name),
            Comment = Esc(table.Comment),
            RecordCount = table.Data.Count,

            NameCells = table.Fields.Select(field => NameCell(table, field)).ToList(),
            CommentCells = table.Fields.Select(field => $"<th>{Esc(field.Comment)}</th>").ToList(),
            TypeCells = table.Fields.Select(field => $"<th>{TypeMarkup(field)}</th>").ToList(),
            SideCells = table.Fields.Select(field => $"<th>{field.TargetSide}</th>").ToList(),

            Rows = table.Data.Select(row => new HtmlRowView
            {
                // Driven by the field list, not by walking the row. A row holds every
                // column the sheet declared, whereas the field list is what this page is
                // meant to show; pairing them positionally only worked while the two were
                // guaranteed identical.
                Cells = table.Fields
                             .Select((field, index) => DataCell(table, field, row[field.Index].Value, index == 0))
                             .ToList(),
            }).ToList(),
        };

        // ------------------------------------------------------------- cells

        /// <summary>
        /// A column-name header. A grouped column names its group in a tooltip, since the
        /// header shows the sheet's own columns rather than the folded arrays.
        /// </summary>
        private static string NameCell(Models.Table table, Models.Field field)
        {
            string caption = field.IsRef ? $"*{Esc(field.Name)}*" : Esc(field.Name);
            string group = GroupNameOf(table, field);

            return group != null
                ? $"<th title=\"exposed as {Esc(group)}\">{caption}</th>"
                : $"<th>{caption}</th>";
        }

        private static string DataCell(Models.Table table, Models.Field field, object value, bool isIndex)
        {
            string content = DataValueMarkup(field, value);

            // The index cell is the row's anchor, so a reference elsewhere on the page can
            // link straight to it.
            return isIndex
                ? $"<td id=\"table_{table.Name}.{Esc(value?.ToString())}\" align=right><code><font color=green>{content}</font></code></td>"
                : $"<td>{content}</td>";
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

        private static string TypeMarkup(Models.Field field)
        {
            if (field.IsRef)
            {
                // What a reference points at is not rendered here. Following it and showing
                // the target's type was attempted and abandoned; the table it points into
                // is one link away on the same page.
                return "<font color=red><b>ref?</b></font>";
            }

            // Element type drives the choice; the brackets are appended after, so an array
            // of enums still links to its declaration.
            string suffix = field.IsArray ? "[]" : "";

            if (field.ElementType == Models.ValueType.Enum)
            {
                return $"<a href=\"{EnumHref(field.Enum.Name, "enum_" + field.Enum.Name)}\">" +
                       $"enum.{Esc(field.Enum.Name)}</a>{suffix}";
            }

            return $"<font color=blue>{Esc(field.TypeName)}{suffix}</font>";
        }

        private static string DataValueMarkup(Models.Field field, object value)
        {
            if (field.IsRef)
            {
                // The stored index, not the value it points at.
                //
                // Following the reference and rendering the target's value was attempted
                // and abandoned: a chain that leads back on itself recursed without bound.
                // The cooker now rejects cyclic references outright, so it could be
                // revisited - but showing the key is also the honest thing for a page
                // documenting what is stored.
                return $"<code><font color=green>{Esc(value?.ToString())}</font></code> : ";
            }

            // A delimited cell holds an array, so render its elements. Falling into the
            // scalar switch below would try to cast the array to the element type.
            if (field.IsArray && value is Array elements)
            {
                var rendered = new StringBuilder();

                for (int i = 0; i < elements.Length; i++)
                {
                    if (i > 0)
                        rendered.Append("<font color=#BBBBBB>, </font>");

                    rendered.Append(ScalarValueMarkup(field, elements.GetValue(i)));
                }

                return rendered.ToString();
            }

            return ScalarValueMarkup(field, value);
        }

        /// <summary>
        /// One value of a field's element type.
        /// </summary>
        private static string ScalarValueMarkup(Models.Field field, object value)
        {
            switch (field.ElementType)
            {
                case Models.ValueType.String:
                    return Esc((string)value);

                case Models.ValueType.Bool:
                    // A tick or nothing, rather than the words: a column of them reads as a
                    // pattern, which is what someone scanning the page is looking for.
                    return (bool)value ? "&#x2714;" : "";

                case Models.ValueType.Int32:
                    return ((int)value).ToString(CultureInfo.InvariantCulture);

                case Models.ValueType.Int64:
                    return ((long)value).ToString(CultureInfo.InvariantCulture);

                case Models.ValueType.Float:
                    return ((float)value).ToString(CultureInfo.InvariantCulture);

                case Models.ValueType.Double:
                    return ((double)value).ToString(CultureInfo.InvariantCulture);

                case Models.ValueType.DateTime:
                    return ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                case Models.ValueType.TimeSpan:
                    return ((TimeSpan)value).ToString(null, CultureInfo.InvariantCulture);

                case Models.ValueType.Uuid:
                    return ((Guid)value).ToString();

                case Models.ValueType.Enum:
                {
                    var label = field.Enum.GetLabel((int)value, null);

                    return $"<a href=\"{EnumHref(field.Enum.Name, $"const_{field.Enum.Name}.{label.Name}")}\" " +
                           $"title=\"enum.{field.Enum.Name}.{label.Name}\">{label.Name}</a>";
                }

                case Models.ValueType.ForeignRecord:
                    return "TODO";

                default:
                    throw new SheetManException($"unsupported type `{field.Type}`");
            }
        }

        // ----------------------------------------------------------- helpers

        private static HtmlSummaryEntryView Summarize(string name, string comment, string href)
            => new HtmlSummaryEntryView
            {
                Name = name,
                Comment = Esc(comment),
                Href = href,
            };

        /// <summary>
        /// A link into an enum's own page.
        ///
        /// One place, because there are three callers and they used to disagree with the
        /// generator: all of them wrote `enums.html`, and this target has never produced a
        /// file by that name - it writes `enums/&lt;kebab-name&gt;.html`, one per enum. So
        /// every enum link in the generated documentation was a dead one, on the index page
        /// and in every type column and every enum-valued cell.
        ///
        /// A golden comparison cannot catch that. It checks that the markup has not changed,
        /// which it had not: the link had been wrong since it was written.
        ///
        /// Every page that links here sits at the output root, so one relative form serves
        /// all of them. An enum page linking to another enum page would need `../`, and
        /// none does.
        /// </summary>
        private static string EnumHref(string enumName, string fragment)
            => $"enums/{enumName.ToKebabCase()}.html#{fragment}";

        /// <summary>
        /// Escapes text that came from the spreadsheet before it reaches the page.
        ///
        /// Comments and string cells are written by designers, so an ampersand or an
        /// angle bracket in a perfectly ordinary description used to break the
        /// generated documentation - the text was interpolated into the markup raw.
        /// </summary>
        private static string Esc(string text)
            => string.IsNullOrEmpty(text) ? "" : WebUtility.HtmlEncode(text);

        /// <summary>
        /// The caption for something, as an anchor back to the cell it was declared in when
        /// the source has an addressable url, and as plain text when it does not.
        ///
        /// Google Sheets links open where they point. A workbook on disk does not - and this
        /// used to return the empty string in that case, which took the caption with it. So
        /// an Excel-sourced model produced enum pages whose heading read `Enumeration:` with
        /// no name after it and whose rows had an empty cell where each label's name should
        /// be. Every model in the fixtures is Excel-sourced, and the golden pages recorded
        /// the blanks as correct.
        ///
        /// The text is what matters here; the link is a convenience on top of it.
        /// </summary>
        private static string SourceSheetLink(Models.Location location, string caption = "")
        {
            string text = Esc(string.IsNullOrEmpty(caption) ? location.ToString() : caption);

            if (string.IsNullOrEmpty(location.SheetUrl))
                return text;

            return $"<a href=\"{location.SheetUrl}\" title=\"Jump to source sheet\">{text}</a>";
        }

        private void Write(string filename, string templateName, HtmlPageView view, bool trailingBlankLine)
        {
            view.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH':'mm':'ss");
            view.User = Environment.UserName;

            string fullPath = Path.Combine(_htmlRecipe.Path, filename);

            StagingFiles.WriteAllTextToFile(
                fullPath, TemplateEngine.Render(templateName, view, trailingBlankLine));
        }
    }
}
