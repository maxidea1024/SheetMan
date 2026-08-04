using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// Renders the embedded code-generation templates.
    ///
    /// The generators used to build their output by calling into a printer line by line,
    /// which put the shape of a C++ header - the part someone reviewing the output cares
    /// about - inside string literals scattered through several hundred lines of C#. A
    /// template puts the shape in one readable place and leaves the C# to work out the
    /// values, which is the division that makes a new output language tractable.
    ///
    /// Templates are embedded resources, as the readers are, so what ships cannot drift
    /// from what is committed.
    /// </summary>
    internal static class TemplateEngine
    {
        /// <summary>
        /// Renders a template against a model.
        ///
        /// Member names are addressed in the template the way Scriban addresses them by
        /// default - `record_name` for RecordName - which happens to read well here because
        /// the languages being generated are themselves snake_case or camelCase.
        /// </summary>
        /// <param name="templateName">File name under templates/, such as `cpp.sbn`.</param>
        /// <param name="model">The view the template reads.</param>
        public static string Render(string templateName, object model)
        {
            var template = Template.Parse(Load(templateName), templateName);

            if (template.HasErrors)
            {
                throw new SheetManException(
                    $"Template `{templateName}` failed to parse:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, template.Messages));
            }

            var context = new TemplateContext
            {
                // A typo in a template is a bug in this repository, not something to paper
                // over with an empty string in somebody's generated header.
                StrictVariables = true,

                // Templates are written for a fixed model, so there is no reason to allow
                // the recursion that a runaway `include` would need.
                LoopLimit = 100_000,

                // So a template can `include` the pieces several pages share - a page head,
                // a footer - instead of each carrying its own copy.
                TemplateLoader = new EmbeddedTemplateLoader(),
            };

            var globals = new ScriptObject();
            globals.Import(model, renamer: member => StandardMemberRenamer.Default(member));
            context.PushGlobal(globals);

            return Normalize(template.Render(context));
        }

        /// <summary>
        /// Resolves `include` against the embedded templates, by file name.
        /// </summary>
        private sealed class EmbeddedTemplateLoader : ITemplateLoader
        {
            public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
                => templateName;

            public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
                => TemplateEngine.Load(templatePath);

            public ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
                => new ValueTask<string>(Load(context, callerSpan, templatePath));
        }

        /// <summary>
        /// Puts the rendered text into the form a text file is supposed to take.
        ///
        /// Three things:
        ///
        ///   - line endings are LF. The printer this replaced declared CRLF and then
        ///     normalized it away again on the way out, so every generated file has been LF
        ///     all along.
        ///
        ///   - every line is right-trimmed, which matters for templates: an indented line
        ///     whose content turns out to be empty would otherwise leave trailing spaces.
        ///
        ///   - the file ends with exactly one newline. Not two.
        ///
        /// That last one used to be two, and the note here said so: the printer split on the
        /// final newline, which yields one empty segment, then appended a newline to every
        /// segment including that one. An accident, kept while the generators were moved onto
        /// templates so that the golden trees could prove the bytes had not moved.
        ///
        /// That move is long done, and the accident outlived its reason. One trailing newline
        /// is what every tool expects - it is what makes a file's last line a line at all -
        /// and two is a blank line at the end of every generated file that a formatter, a
        /// linter or a reviewer will want to remove.
        /// </summary>
        private static string Normalize(string text)
        {
            var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));

            for (int i = 0; i < lines.Count; i++)
                lines[i] = lines[i].TrimEnd();

            // Whatever the template file happened to end with is discarded, so the ending is
            // decided here rather than by an editor's trailing-newline habit.
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            var result = new StringBuilder(text.Length + 16);

            foreach (var line in lines)
            {
                result.Append(line);
                result.Append('\n');
            }

            return result.ToString();
        }

        private static string Load(string templateName)
        {
            string resourceName = "SheetMan.Templates." + templateName;

            using var stream = typeof(TemplateEngine).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new SheetManException($"Embedded template `{resourceName}` is missing from the build.");

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
