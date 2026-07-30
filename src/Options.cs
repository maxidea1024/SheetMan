using System.Collections.Generic;
using CommandLine;

namespace SheetMan
{
    public class Options
    {
        [Option('r', "recipe", HelpText = "Recipe file.")]
        public string RecipeFilename { get; set; }

        /// <summary>
        /// Writes a starting recipe and exits.
        ///
        /// Every list comes out holding one entry with its defaults filled in, so the file
        /// shows what each target takes rather than only that the section exists.
        /// </summary>
        [Option("new-recipe", HelpText = "Write a starting recipe file and exit.")]
        public string NewRecipeFilename { get; set; }

        /// <summary>
        /// Narrows the whole run to one side of the data.
        ///
        /// Two things follow from it. Output entries built for the other side are skipped,
        /// and the entries that do run see only the tables, columns and rows that belong to
        /// the requested side - so `--target-side server` on a recipe whose entries are
        /// marked `cs` produces the server cut of that output rather than everything.
        ///
        /// Left out, the run is not narrowed at all and each entry is built for whatever
        /// side it declares, which is what happened before this option existed.
        /// </summary>
        [Option("target-side",
            HelpText = "Narrow the run to one side: `client`, `server`, or `both` (the default).")]
        public string TargetSide { get; set; }

        [Option("verbose", HelpText = "Sets whether to output debugging log messages.")]
        public bool Verbose { get; set; }

        [Option("silent", HelpText = "Suppress all logging message except ERROR/FATAL.")]
        public bool Silent { get; set; }
        
        [Option("debug", HelpText = "Enables or disables internal debugging.")]
        public bool Debugging { get; set; }
    }
}
