using System;
using System.Collections.Generic;
using System.IO;
using SheetMan.Helpers;
using SheetMan.Targets;

namespace SheetMan.CodeGeneration
{
    /// <summary>
    /// What every code-generation target does the same way.
    ///
    /// Two methods, and both used to be copied into each generator. Neither is interesting,
    /// which is the point: thirteen copies of an uninteresting method are thirteen places for
    /// one of them to drift, and nothing reports it. The Unreal generator's copy of the reader
    /// writer pointed at the C++ reader for months, which is how it came to ship an Unreal
    /// module full of std::string.
    ///
    /// What is genuinely per-language stays in the generator: the file layout, the type names,
    /// the escaping, the reader calls. This is only the plumbing they share.
    ///
    /// Three generators keep their own <c>CommentLines</c> because theirs is not this one, and
    /// they are worth naming so nobody folds them in later on the strength of the signature:
    ///
    ///   TypeScript wraps the whole comment in `/** ... */` and runs its lines together.
    ///
    ///   Python maps each line through its own doc escaping.
    ///
    ///   C# tests <c>IsNullOrEmpty</c> rather than <c>IsNullOrWhiteSpace</c>, so a comment of
    ///   nothing but spaces reaches its template as one blank line instead of none.
    ///
    /// The last of those is a difference of two words and shows up as one blank line in one
    /// generated file. Which is the reason this list exists rather than a note saying the
    /// methods are all the same.
    /// </summary>
    public abstract class CodeGenerator<TRecipe> : Target<TRecipe>
        where TRecipe : class, IOutputRecipe
    {
        /// <summary>
        /// A sheet comment split into the lines a comment block needs.
        ///
        /// Line endings are normalized because a comment typed into Excel on Windows carries
        /// CRLF, and a template emitting it verbatim after a `//` leaves a stray blank line in
        /// the generated file.
        /// </summary>
        protected static IReadOnlyList<string> CommentLines(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return Array.Empty<string>();

            return comment.Replace("\r\n", "\n").Split('\n');
        }

        /// <summary>
        /// Writes the language's binary reader beside the generated code.
        /// </summary>
        /// <remarks>
        /// From an embedded resource rather than from `lib/` on disk, so what a published
        /// build writes cannot differ from what is committed - and so a generated output tree
        /// is self-contained: nothing to install, no include path to set, and no chance of a
        /// consumer pairing generated code with a reader of a different vintage.
        /// </remarks>
        /// <param name="resourceName">Logical name, as SheetMan.csproj declares it.</param>
        /// <param name="path">Where to write it. Made absolute here.</param>
        protected void WriteBinaryReaderRuntime(string resourceName, string path)
        {
            using var stream = GetType().Assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
                throw new SheetManException($"Embedded resource `{resourceName}` is missing from the build.");

            using var reader = new StreamReader(stream);

            StagingFiles.WriteAllTextToFile(Path.GetFullPath(path), reader.ReadToEnd());
        }
    }
}
