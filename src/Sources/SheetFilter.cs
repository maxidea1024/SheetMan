using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SheetMan.Recipe;

namespace SheetMan.Sources;

/// <summary>
/// Decides which sheets of a source a recipe entry wants.
/// </summary>
/// <remarks>
/// Two lists, both optional: an include list narrows the workbook to what is named, and an
/// exclude list drops from whatever is left. Neither is a layout question - a sheet that is
/// not input is not input whichever way the ones that are get read - so this sits in front
/// of every source rather than inside a parser.
///
/// An include list also gets the sheets it named checked off: see
/// <see cref="ReportUnmatchedIncludes"/>. A name that matched nothing is almost always a
/// typo or a renamed tab, and the failure it causes otherwise is a table missing from the
/// output with nothing in the run saying so.
/// </remarks>
public sealed class SheetFilter
{
    /// <summary>Takes every sheet. What a recipe entry that names neither list gets.</summary>
    public static readonly SheetFilter All = new SheetFilter(new List<Pattern>(), new List<Pattern>());

    private sealed class Pattern
    {
        public string Text;
        public Regex Regex;
        public bool Matched;
    }

    private readonly List<Pattern> _includes;
    private readonly List<Pattern> _excludes;

    private SheetFilter(List<Pattern> includes, List<Pattern> excludes)
    {
        _includes = includes;
        _excludes = excludes;
    }

    /// <summary>Builds a filter from a recipe entry's two lists.</summary>
    public static SheetFilter From(SheetSourceRecipe recipe)
    {
        if (recipe is null)
            return All;

        return new SheetFilter(Compile(recipe.IncludeSheets), Compile(recipe.ExcludeSheets));
    }

    /// <summary>
    /// Whether a sheet of this name should be read.
    /// </summary>
    public bool Includes(string sheetName)
    {
        string name = (sheetName ?? "").Trim();

        // Recorded even when the sheet is excluded further down, because the question
        // this answers is "did the recipe name something that is not there" - and a
        // sheet named by both lists is there.
        bool included = _includes.Count == 0;

        foreach (var pattern in _includes)
        {
            if (!pattern.Regex.IsMatch(name))
                continue;

            pattern.Matched = true;
            included = true;
        }

        if (!included)
            return false;

        return !_excludes.Any(pattern => pattern.Regex.IsMatch(name));
    }

    /// <summary>
    /// Throws naming the entries of <see cref="SheetSourceRecipe.IncludeSheets"/> that no
    /// sheet ever matched. Call once the source has offered every sheet it has.
    /// </summary>
    /// <param name="section">Recipe path of the entry, for the message.</param>
    /// <param name="available">Every sheet name the source saw, to suggest from.</param>
    public void ReportUnmatchedIncludes(string section, IEnumerable<string> available)
    {
        var missing = _includes.Where(pattern => !pattern.Matched).Select(pattern => pattern.Text).ToList();
        if (missing.Count == 0)
            return;

        var names = available?.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
                    ?? new List<string>();

        throw new SheetManException(
            $"Recipe `{section}` asks for {missing.Count} sheet(s) that the source does not have: " +
            $"{string.Join(", ", missing)}.\n" +
            $"Sheets that are there: {(names.Count > 0 ? string.Join(", ", names) : "(none)")}");
    }

    private static List<Pattern> Compile(IEnumerable<string> patterns)
    {
        var result = new List<Pattern>();

        if (patterns is null)
            return result;

        foreach (var raw in patterns)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0)
                continue;

            result.Add(new Pattern { Text = text, Regex = ToRegex(text) });
        }

        return result;
    }

    /// <summary>
    /// Turns a glob into a whole-string regex, with every other character taken literally.
    /// </summary>
    private static Regex ToRegex(string glob)
    {
        var expression = new StringBuilder("^");

        foreach (char c in glob)
        {
            switch (c)
            {
                case '*': expression.Append(".*"); break;
                case '?': expression.Append('.'); break;
                default: expression.Append(Regex.Escape(c.ToString())); break;
            }
        }

        expression.Append('$');

        // Case-insensitive because a sheet tab is typed by hand in two places - the tab
        // and the recipe - and a project that renames `ItemTable` to `Itemtable` has not
        // changed which sheet it means.
        return new Regex(expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
