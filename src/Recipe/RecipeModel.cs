using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SheetMan.Recipe
{
    public class RecipeModel
    {
        #region Source group
        /// <summary>
        ///
        /// </summary>
        public class SourceRecipeGroup
        {
            /// <summary>
            ///
            /// </summary>
            public class XlsxRecipe
            {
                /// <summary></summary>
                public string Path { get; set; } = "";

                /// <summary></summary>
                public string FileExtensionPatterns { get; set; } = ".xls;.xlsx";
            }

            /// <summary>
            ///
            /// </summary>
            public class GoogleSheetsRecipe
            {
                /// <summary></summary>
                public string ClientSecretFilename { get; set; } = "";

                /// <summary></summary>
                public string SheetsId { get; set; } = "";
            }

            /// <summary></summary>
            public List<XlsxRecipe> Xlsx { get; set; } = new List<XlsxRecipe>();

            /// <summary></summary>
            public List<GoogleSheetsRecipe> GoogleSheets { get; set; } = new List<GoogleSheetsRecipe>();
        }

        /// <summary></summary>
        public SourceRecipeGroup Sources { get; set; } = new SourceRecipeGroup();
        #endregion


        #region Export group
        /// <summary>
        ///
        /// </summary>
        public class ExportRecipeGroup
        {
            /// <summary></summary>
            public class BinaryRecipe
            {
                /// <summary></summary>
                public string Path { get; set; } = "";

                /// <summary></summary>
                public string FileExtension { get; set; } = ".table";

                /// <summary></summary>
                public bool Compress { get; set; } = false;
            }

            /// <summary>
            ///
            /// </summary>
            public class JsonRecipe
            {
                /// <summary></summary>
                public string Path { get; set; } = "";
                /// <summary></summary>
                public bool UseCompactRowFormat { get; set; } = false;
                /// <summary></summary>
                public bool Indented { get; set; } = false;
            }

            /// <summary>
            ///
            /// </summary>
            public class MongoDbRecipe
            {

            }

            /// <summary>
            ///
            /// </summary>
            public class MySqlRecipe
            {

            }

            /// <summary></summary>
            public List<BinaryRecipe> Binary { get; set; } = new List<BinaryRecipe>();

            /// <summary></summary>
            public List<JsonRecipe> Json { get; set; } = new List<JsonRecipe>();

            /// <summary></summary>
            public List<MongoDbRecipe> MongoDb { get; set; } = new List<MongoDbRecipe>();

            /// <summary></summary>
            public List<MySqlRecipe> MySql { get; set; } = new List<MySqlRecipe>();
        }

        /// <summary></summary>
        public ExportRecipeGroup Exports { get; set; } = new ExportRecipeGroup();
        #endregion


        #region Code generation group
        /// <summary>
        ///
        /// </summary>
        public class CodeGenerationRecipeGroup
        {
            /// <summary>
            ///
            /// </summary>
            public class CppRecipe
            {
                /// <summary></summary>
                public string Path { get; set; } = "";

                /// <summary></summary>
                public string AccessorName { get; set; } = "SheetManAccessor";

                /// <summary></summary>
                public string Namespace { get; set; } = "";

                /// <summary></summary>
                public string BinaryTableFileExtension { get; set; } = ".table";
            }

            /// <summary>
            ///
            /// </summary>
            public class CSharpRecipe
            {
                /// <summary></summary>
                public string Path { get; set; } = "";

                /// <summary></summary>
                public string AccessorName { get; set; } = "SheetManAccessor";

                /// <summary></summary>
                public string Namespace { get; set; } = "";

                /// <summary></summary>
                public string BinaryTableFileExtension { get; set; } = ".table";
            }

            /// <summary>
            ///
            /// </summary>
            public class TypescriptRecipe
            {
                /// <summary></summary>
                public string Path { get; set; } = "";

                /// <summary></summary>
                public string AccessorName { get; set; } = "SheetManAccessor";

                /// <summary></summary>
                public string Namespace { get; set; } = "";

                /// <summary></summary>
                public string BinaryTableFileExtension { get; set; } = ".table";
                
                /// <summary></summary>
                public bool UseStringEnum { get; set; }
            }

            /// <summary>
            ///
            /// </summary>
            public class HtmlRecipe
            {
                /// <summary></summary>
                public string Path { get; set; } = "";
            }

            /// <summary></summary>
            public List<CppRecipe> Cpp { get; set; } = new List<CppRecipe>();

            /// <summary></summary>
            public List<CSharpRecipe> CSharp { get; set; } = new List<CSharpRecipe>();

            /// <summary></summary>
            public List<TypescriptRecipe> Typescript { get; set; } = new List<TypescriptRecipe>();

            /// <summary></summary>
            public List<HtmlRecipe> Html { get; set; } = new List<HtmlRecipe>();
        }

        /// <summary></summary>
        public CodeGenerationRecipeGroup CodeGenerations = new CodeGenerationRecipeGroup();
        #endregion


        /// <summary>
        ///
        /// </summary>
        public void WriteToFile(string filename)
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filename, json);
        }

        /// <summary>
        ///
        /// </summary>
        public static RecipeModel LoadFromFile(string filename)
        {
            string json = File.ReadAllText(filename);
            return JsonConvert.DeserializeObject<RecipeModel>(json);
        }

        /// <summary>
        ///
        /// </summary>
        public static RecipeModel Current { get; private set; }

        /// <summary>
        ///
        /// </summary>
        public RecipeModel()
        {
            Current = this;
        }
    }
}
