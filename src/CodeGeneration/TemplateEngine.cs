using System;
using System.IO;
using System.Text;
using Scriban;
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
            };

            var globals = new ScriptObject();
            globals.Import(model, renamer: member => StandardMemberRenamer.Default(member));
            context.PushGlobal(globals);

            return Normalize(template.Render(context));
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
        private static string Normalize(string text)
        {
            var result = new StringBuilder(text.Length + 16);

            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                result.Append(line.TrimEnd());
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
