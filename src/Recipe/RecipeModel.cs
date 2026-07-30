using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SheetMan.Targets;

namespace SheetMan.Recipe
{
    public class RecipeModel
    {
        #region Source group

        /// <summary>
        /// Where the sheets are read from.
        ///
        /// Several sources combine into one model, so a project can split its data across
        /// workbooks and Google Sheets documents however suits the people editing it.
        /// </summary>
        public class SourceRecipeGroup
        {
            /// <summary>
            /// A directory of Excel workbooks.
            /// </summary>
            public class XlsxRecipe
            {
                /// <summary>
                /// Directory to search, including subdirectories.
                ///
                /// Any file or directory whose name begins with `#` is skipped, which is
                /// how work in progress is kept out of a build.
                /// </summary>
                public string Path { get; set; } = "";

                /// <summary>
                /// Semicolon-separated extensions to pick up. Everything else in the
                /// directory is ignored.
                /// </summary>
                public string FileExtensionPatterns { get; set; } = ".xls;.xlsx";
            }

            /// <summary>
            /// A Google Sheets document, fetched over the API.
            /// </summary>
            public class GoogleSheetsRecipe
            {
                /// <summary>
                /// Path to the OAuth client secret downloaded from the Google Cloud
                /// console.
                ///
                /// Do not commit this file. The first run opens a browser for consent and
                /// caches the resulting token under the user's profile, so only the first
                /// run is interactive.
                /// </summary>
                public string ClientSecretFilename { get; set; } = "";

                /// <summary>
                /// The document id, which is the long identifier in its URL.
                /// </summary>
                public string SheetsId { get; set; } = "";
            }

            /// <summary>Excel sources.</summary>
            public List<XlsxRecipe> Xlsx { get; set; } = new List<XlsxRecipe>();

            /// <summary>Google Sheets sources.</summary>
            public List<GoogleSheetsRecipe> GoogleSheets { get; set; } = new List<GoogleSheetsRecipe>();
        }

        /// <summary>Where the sheets are read from.</summary>
        public SourceRecipeGroup Sources { get; set; } = new SourceRecipeGroup();

        /// <summary>
        /// Inserts a `None = 0` label into any enum that declares neither the name `None`
        /// nor the value zero.
        ///
        /// On by default, because a field of an enum type has to hold something before it
        /// is assigned and a nameless zero is worse than a named one. Turn it off for a
        /// project that would rather its enums contain exactly what the sheets say - at
        /// the cost of a default-constructed field holding a value with no label.
        /// </summary>
        public bool AutoInsertEnumNoneLabel { get; set; } = true;

        /// <summary>
        /// Character separating the elements of an array cell, for fields typed
        /// `int[]`, `string[]` and so on.
        ///
        /// Semicolon by default rather than comma, because comma appears constantly in
        /// ordinary prose and in numbers formatted for humans. Whitespace around each
        /// element is trimmed, so `1; 2 ;3` reads the same as `1;2;3`.
        /// </summary>
        public string ArrayDelimiter { get; set; } = ";";
        #endregion


        #region Export group

        /// <summary>
        /// Where the converted data is written.
        ///
        /// File targets stage their output and commit it at the end of a successful run;
        /// database targets load into shadow storage and swap it in. Either way a failed
        /// run leaves the previous output untouched.
        /// </summary>
        public class ExportRecipeGroup
        {
            /// <summary>
            /// One binary file per table, in SheetMan's own LiteBinary format.
            ///
            /// This is what the generated C# and C++ readers consume.
            /// </summary>
            public class BinaryRecipe : IOutputRecipe
            {
                /// <summary>Output directory. Created if it does not exist.</summary>
                public string Path { get; set; } = "";

                /// <summary>
                /// Extension of each table file. Must match the extension the code
                /// generators are told to expect.
                /// </summary>
                public string FileExtension { get; set; } = ".table";

                /// <summary>
                /// Reserved. Not implemented: the format writes a reserved byte where a
                /// compression flag would go, but nothing sets or reads it, and the
                /// generated readers reject a non-zero value.
                /// </summary>
                public bool Compress { get; set; } = false;

                /// <summary>
                /// Which side this output is built for: "c", "s", or "cs"/blank for
                /// both. Entities and fields marked for the other side are left out.
                ///
                /// Declare the same side on the exporter and on the code generator
                /// that reads its files: the two must agree on the column set or the
                /// generated reader will not match the data.
                /// </summary>
                public string TargetSide { get; set; } = "cs";
            }

            /// <summary>
            /// One .json file per table.
            ///
            /// This is what the generated TypeScript reads.
            /// </summary>
            public class JsonRecipe : IOutputRecipe
            {
                /// <summary>Output directory. Created if it does not exist.</summary>
                public string Path { get; set; } = "";

                /// <summary>
                /// Writes each row as a bare array of values instead of an object with
                /// field names.
                ///
                /// Smaller, at the cost of being unreadable on its own. The generated
                /// readers handle both, deciding from the shape of the first row.
                /// </summary>
                public bool UseCompactRowFormat { get; set; } = false;

                /// <summary>
                /// Pretty-prints the output. Worth it while inspecting data by hand, not
                /// for something a program will read.
                /// </summary>
                public bool Indented { get; set; } = false;

                /// <summary>
                /// Which side this output is built for: "c", "s", or "cs"/blank for
                /// both. Entities and fields marked for the other side are left out.
                ///
                /// Declare the same side on the exporter and on the code generator
                /// that reads its files: the two must agree on the column set or the
                /// generated reader will not match the data.
                /// </summary>
                public string TargetSide { get; set; } = "cs";
            }

            /// <summary>
            /// Shared settings for the database export targets.
            ///
            /// Each target loads into shadow tables and then swaps them in, so a run
            /// that fails partway leaves the live data untouched. Atomicity is per
            /// store: files and four databases cannot be committed as one transaction
            /// without a distributed coordinator, so each is made atomic on its own
            /// rather than pretending otherwise.
            /// </summary>
            public abstract class DatabaseRecipe : IOutputRecipe
            {
                /// <summary>
                /// Connection string. Supports `${NAME}` placeholders filled from the
                /// environment, so a recipe holding no secrets can be committed:
                ///
                ///     "Server=db;Database=game;Uid=sheetman;Pwd=${DB_PASSWORD}"
                /// </summary>
                public string ConnectionString { get; set; } = "";

                /// <summary>
                /// Prefix applied to every table, collection or key name written.
                /// Lets one database hold several independent sets of exported data.
                /// </summary>
                public string NamePrefix { get; set; } = "";

                /// <summary>
                /// Which side this output is built for: "c", "s", or "cs"/blank for
                /// both. Entities and fields marked for the other side are left out.
                /// </summary>
                public string TargetSide { get; set; } = "cs";
            }

            /// <summary>
            /// MongoDB target. One collection per table, one document per row.
            /// </summary>
            public class MongoDbRecipe : DatabaseRecipe
            {
            }

            /// <summary>
            /// MySQL target. One table per table, recreated on each run.
            /// </summary>
            public class MySqlRecipe : DatabaseRecipe
            {
            }

            /// <summary>
            /// PostgreSQL target. One table per table, recreated on each run.
            /// </summary>
            public class PostgreSqlRecipe : DatabaseRecipe
            {
                /// <summary>Schema the tables are created in.</summary>
                public string Schema { get; set; } = "public";
            }

            /// <summary>
            /// Redis target. One hash per row, plus an index set per table.
            /// </summary>
            public class RedisRecipe : DatabaseRecipe
            {
                /// <summary>Database number to select on the server.</summary>
                public int Database { get; set; } = 0;
            }

            /// <summary>Binary file targets.</summary>
            public List<BinaryRecipe> Binary { get; set; } = new List<BinaryRecipe>();

            /// <summary>JSON file targets.</summary>
            public List<JsonRecipe> Json { get; set; } = new List<JsonRecipe>();

            /// <summary>MongoDB targets.</summary>
            public List<MongoDbRecipe> MongoDb { get; set; } = new List<MongoDbRecipe>();

            /// <summary>MySQL targets.</summary>
            public List<MySqlRecipe> MySql { get; set; } = new List<MySqlRecipe>();

            /// <summary>PostgreSQL targets.</summary>
            public List<PostgreSqlRecipe> PostgreSql { get; set; } = new List<PostgreSqlRecipe>();

            /// <summary>Redis targets.</summary>
            public List<RedisRecipe> Redis { get; set; } = new List<RedisRecipe>();
        }

        /// <summary>Where the converted data is written.</summary>
        public ExportRecipeGroup Exports { get; set; } = new ExportRecipeGroup();
        #endregion


        #region Code generation group

        /// <summary>
        /// What source code to emit for reading the exported data.
        ///
        /// The point of the tool: a project uses the declared entities without writing
        /// any loading code of its own.
        /// </summary>
        public class CodeGenerationRecipeGroup
        {
            /// <summary>
            /// C++17 header. Reads the binary export.
            /// </summary>
            public class CppRecipe : IOutputRecipe
            {
                /// <summary>Output directory. Created if it does not exist.</summary>
                public string Path { get; set; } = "";

                /// <summary>
                /// Name of the generated accessor, which is also the generated file's
                /// name where the target emits a single file.
                /// </summary>
                public string AccessorName { get; set; } = "SheetManAccessor";

                /// <summary>
                /// Namespace to wrap the generated code in. Omitting it puts everything
                /// in the global namespace, where the names may collide with something.
                /// </summary>
                public string Namespace { get; set; } = "";

                /// <summary>
                /// Extension the generated reader expects on table files. Must match the
                /// binary export's FileExtension.
                /// </summary>
                public string BinaryTableFileExtension { get; set; } = ".table";

                /// <summary>
                /// Which side this output is built for: "c", "s", or "cs"/blank for
                /// both. Entities and fields marked for the other side are left out.
                ///
                /// Declare the same side on the exporter and on the code generator
                /// that reads its files: the two must agree on the column set or the
                /// generated reader will not match the data.
                /// </summary>
                public string TargetSide { get; set; } = "cs";
            }

            /// <summary>
            /// C# source. Reads the binary export, and is Unity-compatible.
            /// </summary>
            public class CSharpRecipe : IOutputRecipe
            {
                /// <summary>Output directory. Created if it does not exist.</summary>
                public string Path { get; set; } = "";

                /// <summary>
                /// Name of the generated accessor, which is also the generated file's
                /// name where the target emits a single file.
                /// </summary>
                public string AccessorName { get; set; } = "SheetManAccessor";

                /// <summary>
                /// Namespace to wrap the generated code in. Omitting it puts everything
                /// in the global namespace, where the names may collide with something.
                /// </summary>
                public string Namespace { get; set; } = "";

                /// <summary>
                /// Extension the generated reader expects on table files. Must match the
                /// binary export's FileExtension.
                /// </summary>
                public string BinaryTableFileExtension { get; set; } = ".table";

                /// <summary>
                /// Which side this output is built for: "c", "s", or "cs"/blank for
                /// both. Entities and fields marked for the other side are left out.
                ///
                /// Declare the same side on the exporter and on the code generator
                /// that reads its files: the two must agree on the column set or the
                /// generated reader will not match the data.
                /// </summary>
                public string TargetSide { get; set; } = "cs";
            }

            /// <summary>
            /// TypeScript modules. Read the JSON export.
            /// </summary>
            public class TypescriptRecipe : IOutputRecipe
            {
                /// <summary>Output directory. Created if it does not exist.</summary>
                public string Path { get; set; } = "";

                /// <summary>
                /// Name of the generated accessor, which is also the generated file's
                /// name where the target emits a single file.
                /// </summary>
                public string AccessorName { get; set; } = "SheetManAccessor";

                /// <summary>
                /// Namespace to wrap the generated code in. Omitting it puts everything
                /// in the global namespace, where the names may collide with something.
                /// </summary>
                public string Namespace { get; set; } = "";

                /// <summary>
                /// Extension the generated reader expects on table files. Must match the
                /// binary export's FileExtension.
                /// </summary>
                public string BinaryTableFileExtension { get; set; } = ".table";

                /// <summary>
                /// Which side this output is built for: "c", "s", or "cs"/blank for
                /// both. Entities and fields marked for the other side are left out.
                ///
                /// Declare the same side on the exporter and on the code generator
                /// that reads its files: the two must agree on the column set or the
                /// generated reader will not match the data.
                /// </summary>
                public string TargetSide { get; set; } = "cs";
                
                /// <summary>
                /// Emits enums as string unions rather than numeric enums.
                ///
                /// Readable in a debugger and in logs, at the cost of not matching the
                /// integers the exported data actually carries.
                /// </summary>
                public bool UseStringEnum { get; set; }
            }

            /// <summary>
            /// Browsable documentation of the converted data.
            ///
            /// Not consumed by any program: it exists so the data that reached a build can
            /// be checked by eye, with links back to the cell each value came from.
            /// </summary>
            public class HtmlRecipe : IOutputRecipe
            {
                /// <summary>Output directory. Created if it does not exist.</summary>
                public string Path { get; set; } = "";

                /// <summary>
                /// Which side this output is built for: "c", "s", or "cs"/blank for
                /// both. Entities and fields marked for the other side are left out.
                ///
                /// Declare the same side on the exporter and on the code generator
                /// that reads its files: the two must agree on the column set or the
                /// generated reader will not match the data.
                /// </summary>
                public string TargetSide { get; set; } = "cs";
            }

            /// <summary>C++ targets.</summary>
            public List<CppRecipe> Cpp { get; set; } = new List<CppRecipe>();

            /// <summary>C# targets.</summary>
            public List<CSharpRecipe> CSharp { get; set; } = new List<CSharpRecipe>();

            /// <summary>TypeScript targets.</summary>
            public List<TypescriptRecipe> Typescript { get; set; } = new List<TypescriptRecipe>();

            /// <summary>HTML documentation targets.</summary>
            public List<HtmlRecipe> Html { get; set; } = new List<HtmlRecipe>();
        }

        /// <summary>What source code to emit for reading the exported data.</summary>
        public CodeGenerationRecipeGroup CodeGenerations { get; set; } = new CodeGenerationRecipeGroup();
        #endregion


        #region Target group

        /// <summary>
        /// Output entries named by target id rather than by recipe section.
        ///
        /// <code>
        /// "Targets": [
        ///   { "Type": "python", "Path": "./out/py", "PackageName": "gamedata" },
        ///   { "Type": "binary", "Path": "./out/data" }
        /// ]
        /// </code>
        ///
        /// `Type` picks the target; everything beside it is that target's own settings, the
        /// same fields its dedicated section would take. Any registered target can be used
        /// here, including the ones that have a section of their own, so a recipe may use
        /// either form or both.
        ///
        /// This exists so that adding a target does not mean extending this class. The
        /// sections above are the targets that predate it and stay for the recipes that
        /// already use them; a target added since is reached only through here.
        ///
        /// Held as raw JSON because the entry type is not known until `Type` is read. The
        /// registry deserializes each one into its target's entry type, rejecting an
        /// unrecognized `Type` and any field the target does not have - a misspelled
        /// setting is a mistake worth reporting, not a default worth taking silently.
        /// </summary>
        public List<JObject> Targets { get; set; } = new List<JObject>();
        #endregion


        /// <summary>
        /// Reads a recipe. Comments are permitted, which is why recipes can explain
        /// themselves in place.
        /// </summary>
        public static RecipeModel LoadFromFile(string filename)
        {
            string json = File.ReadAllText(filename);
            return JsonConvert.DeserializeObject<RecipeModel>(json);
        }

        /// <summary>
        /// The most recently constructed recipe.
        ///
        /// Ambient state, and dubious: deserialization and `--new-recipe` both construct
        /// one, so this points at whichever happened last rather than at the recipe being
        /// run. Nothing reads it today; prefer passing the recipe explicitly, as the
        /// exporters and generators do.
        /// </summary>
        public static RecipeModel Current { get; private set; }

        /// <summary>
        /// Publishes the new instance as <see cref="Current"/>.
        /// </summary>
        public RecipeModel()
        {
            Current = this;
        }
    }
}
