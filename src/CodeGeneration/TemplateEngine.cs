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
        /// <param name="trailingBlankLine">
        /// Whether the file ends with a blank line. Not a matter of taste: the printer this
        /// replaced left one behind wherever a generator's last call was PrintLine and none
        /// where it was Print, so the generated C++ has one and the generated HTML does not.
        /// Stated here rather than left to whether a template file happens to end with a
        /// newline, which an editor may add or remove without anyone noticing.
        /// </param>
        public static string Render(string templateName, object model, bool trailingBlankLine = true)
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

            return Normalize(template.Render(context), trailingBlankLine);
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
        /// Puts the rendered text into the form the generated files have always taken.
        ///
        /// This reproduces what Printer.ToString did, deliberately, because the golden
        /// trees record its output and moving the generators onto templates is only
        /// verifiable if the bytes do not move. Three things:
        ///
        ///   - line endings are LF. The printer declared CRLF and then normalized it away
        ///     again on the way out, so every generated file has been LF all along.
        ///
        ///   - every line is right-trimmed, which matters for templates: an indented line
        ///     whose content turns out to be empty would otherwise leave trailing spaces.
        ///
        ///   - the file ends with a blank line. The printer split on the final newline,
        ///     which yields one empty segment, and then appended a newline to every
        ///     segment including that one. An accident, but a harmless one, and preserving
        ///     it is what keeps this change reviewable.
        /// </summary>
        private static string Normalize(string text, bool trailingBlankLine)
        {
            var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));

            for (int i = 0; i < lines.Count; i++)
                lines[i] = lines[i].TrimEnd();

            // Whatever the template file happened to end with is discarded, so the ending
            // is decided by the caller rather than by an editor's trailing-newline habit.
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            var result = new StringBuilder(text.Length + 16);

            foreach (var line in lines)
            {
                result.Append(line);
                result.Append('\n');
            }

            if (trailingBlankLine)
                result.Append('\n');

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
