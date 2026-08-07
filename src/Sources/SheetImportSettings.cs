using System;
using SheetMan.Models.Raw;
using SheetMan.Recipe;

namespace SheetMan.Sources
{
    /// <summary>
    /// The settings every sheet-reading source shares, read out of one recipe entry.
    /// </summary>
    /// <remarks>
    /// Read once per entry rather than per sheet, so a malformed setting is reported before any
    /// sheet is read instead of on whichever one happened to reach it first.
    /// </remarks>
    public sealed class SheetImportSettings
    {
        private SheetImportSettings(SheetFilter filter, SheetLayout layout)
        {
            Filter = filter;
            Layout = layout;
        }

        /// <summary>Which sheets of the source to read.</summary>
        public SheetFilter Filter { get; }

        /// <summary>How to read them, stamped onto every sheet this entry produces.</summary>
        public SheetLayout Layout { get; }

        /// <summary>
        /// Reads one entry's settings, rejecting values that are not spellings of anything.
        /// </summary>
        /// <param name="section">Recipe path of the entry, for messages.</param>
        public static SheetImportSettings From(SheetSourceRecipe recipe, string section)
        {
            if (recipe == null)
                return new SheetImportSettings(SheetFilter.All, SheetLayout.Default);

            string layoutId = (recipe.Layout ?? "").Trim();
            if (layoutId.Length == 0)
                layoutId = SheetLayout.Default.Id;

            return new SheetImportSettings(
                SheetFilter.From(recipe),
                new SheetLayout(layoutId.ToLowerInvariant(), ParseDuplicateIndexPolicy(recipe.OnDuplicateIndex, section)));
        }

        private static DuplicateIndexPolicy ParseDuplicateIndexPolicy(string value, string section)
        {
            // Blank is the default rather than an error: it is what an entry written before
            // the setting existed holds, and what deleting the line leaves behind.
            string text = (value ?? "").Trim();
            if (text.Length == 0)
                return DuplicateIndexPolicy.Error;

            switch (text.ToLowerInvariant().Replace("_", "-"))
            {
                case "error": return DuplicateIndexPolicy.Error;
                case "keep-first": return DuplicateIndexPolicy.KeepFirst;
                case "keep-last": return DuplicateIndexPolicy.KeepLast;
            }

            throw new SheetManException(
                $"Recipe `{section}` sets `OnDuplicateIndex` to `{text}`. " +
                "It takes `error`, `keep-first` or `keep-last`.");
        }
    }
}
