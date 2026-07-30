using System.Text.RegularExpressions;

namespace SheetMan.Tests
{
    /// <summary>
    /// Masks the parts of SheetMan's output that legitimately change between runs, so
    /// golden comparison reacts to behaviour changes rather than to the clock.
    ///
    /// Only two things are non-deterministic today:
    ///
    ///   * manifest files stamp DateTime.Now on the manifest and on every item
    ///   * the HTML footer embeds the wall clock and the machine's user name
    ///
    /// Binary tables and the generated C#/TypeScript are already byte-stable.
    ///
    /// The HTML footer is masked rather than tolerated: baking the build machine's
    /// user name into a generated artifact is itself worth removing later, and the
    /// mask makes the golden files reviewable in the meantime.
    /// </summary>
    internal static class OutputNormalizer
    {
        private static readonly Regex IsoTimestamp = new Regex(
            @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?([+-]\d{2}:\d{2}|Z)",
            RegexOptions.Compiled);

        private static readonly Regex HtmlFooter = new Regex(
            @"This file was created at [^<]*",
            RegexOptions.Compiled);

        public static string Normalize(string relativePath, string content)
        {
            content = content.Replace("\r\n", "\n");

            if (relativePath.Replace('\\', '/').Contains("manifest"))
                content = IsoTimestamp.Replace(content, "<TIMESTAMP>");

            if (relativePath.EndsWith(".html"))
                content = HtmlFooter.Replace(content, "This file was created at <TIMESTAMP> by <USER>");

            return content;
        }

        /// <summary>
        /// Files compared byte for byte rather than as normalized text.
        /// </summary>
        public static bool IsBinary(string relativePath)
            => relativePath.EndsWith(".table");
    }
}
