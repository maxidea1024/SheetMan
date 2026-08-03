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

        /// <summary>
        /// The commit this conversion is of.
        ///
        /// What the history files a snapshot under, and what a range query names. Left out,
        /// it is read from the working copy the sheets are in - so a developer converting
        /// locally needs nothing, while a CI job that checked out a detached HEAD can say
        /// exactly which commit it built.
        ///
        /// Not required to be a git hash. A project keeping its sheets somewhere without
        /// commits can pass any stable identifier and the history treats it as opaque.
        /// </summary>
        [Option("commit", HelpText = "Commit this conversion is of. Read from git when left out.")]
        public string Commit { get; set; }

        /// <summary>
        /// Branch this snapshot belongs to.
        ///
        /// Snapshots are chained per branch, so this decides which history the conversion
        /// extends. Read from the working copy when left out - but a detached HEAD, which
        /// is what most CI checkouts produce, is not a branch and yields nothing.
        /// </summary>
        [Option("branch", HelpText = "Branch this snapshot belongs to. Read from git when left out.")]
        public string Branch { get; set; }

        /// <summary>
        /// Who made the change, as `Name &lt;email&gt;`.
        ///
        /// For the build systems that know the author without a git checkout to read it
        /// from. Overrides what the commit says.
        /// </summary>
        [Option("commit-author", HelpText = "Author of the change, as `Name <email>`. Overrides git.")]
        public string CommitAuthor { get; set; }

        /// <summary>When the change was made, as an ISO 8601 timestamp. Overrides git.</summary>
        [Option("commit-date", HelpText = "When the change was made, ISO 8601. Overrides git.")]
        public string CommitDate { get; set; }

        /// <summary>
        /// Working copy to read commit information from.
        ///
        /// Left out, the sheets' own source directories are tried and then the working
        /// directory. Given, it is the only place looked at: falling through to somewhere
        /// else would record another repository's commits against this data.
        /// </summary>
        [Option("repository", HelpText = "Working copy to read commit information from.")]
        public string Repository { get; set; }

        [Option("verbose", HelpText = "Sets whether to output debugging log messages.")]
        public bool Verbose { get; set; }

        [Option("silent", HelpText = "Suppress all logging message except ERROR/FATAL.")]
        public bool Silent { get; set; }
        
        [Option("debug", HelpText = "Enables or disables internal debugging.")]
        public bool Debugging { get; set; }
    }
}
