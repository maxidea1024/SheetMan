using System;
using System.IO;
using NPOI.XSSF.UserModel;

namespace SheetMan.FixtureGen
{
    /// <summary>
    /// Generates the .xlsx fixtures used by the regression tests.
    ///
    /// The fixtures are committed to the repo; this generator exists so they can be
    /// reviewed as code and regenerated deterministically instead of being opaque
    /// binaries nobody dares to touch.
    ///
    ///     dotnet run --project test/fixtures/tools/FixtureGen
    ///
    /// Fixtures are split by intent:
    ///
    ///   core.xlsx         Everything that works today. Its generated output is the
    ///                     golden baseline that the port must not change.
    ///   excel-typed.xlsx  Cells that carry real Excel types (numeric, date) rather
    ///                     than strings. Exercises importer behaviour that string
    ///                     cells hide.
    ///   layout-edge.xlsx  Sheets with leading blank rows/columns, ragged rows and
    ///                     interior blank rows, which drive RawSheet.Optimize.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string outputDir = args.Length > 0
                ? args[0]
                : Path.Combine(FindRepoRoot(), "test", "fixtures", "xlsx");

            // One directory per scenario: XlsxImporter scans its source path
            // recursively, so fixtures sharing a directory would bleed into each
            // other's runs.
            WriteCore(Prepare(outputDir, "core", "core.xlsx"));
            WriteExcelTyped(Prepare(outputDir, "excel-typed", "excel-typed.xlsx"));
            WriteLayoutEdge(Prepare(outputDir, "layout-edge", "layout-edge.xlsx"));
            WriteForeignFieldRef(Prepare(outputDir, "foreign-field", "foreign-field.xlsx"));
            WriteInvalid(Prepare(outputDir, "invalid", "invalid.xlsx"));
            WriteSideDangling(Prepare(outputDir, "side-dangling", "side-dangling.xlsx"));
            WriteArrayForeign(Prepare(outputDir, "array-foreign", "array-foreign.xlsx"));
            WriteStrictValues(Prepare(outputDir, "strict-values", "strict-values.xlsx"));
            WriteDoubleStar(Prepare(outputDir, "double-star", "double-star.xlsx"));
            WriteFormulaError(Prepare(outputDir, "formula-error", "formula-error.xlsx"));
            WriteEnumByValue(Prepare(outputDir, "enum-by-value", "enum-by-value.xlsx"));
            WriteReservedWords(Prepare(outputDir, "reserved-words", "reserved-words.xlsx"));
            WriteConformance(Prepare(outputDir, "conformance", "conformance.xlsx"));

            Console.WriteLine($"Fixtures written to {outputDir}");
            return 0;
        }

        private static string Prepare(string outputDir, string scenario, string filename)
        {
            string dir = Path.Combine(outputDir, scenario);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, filename);
        }

        // ---------------------------------------------------------------- core

        private static void WriteCore(string path)
        {
            var workbook = new XSSFWorkbook();

            // --- Enums -------------------------------------------------------

            var enums = new SheetBuilder(workbook.CreateSheet("Enums"));

            // Declares its own 0 entry, so SheetMan leaves the label list alone.
            enums.Enum(1, 1, new EnumSpec { Name = "ValueType", Comment = "Value types used by the test tables." }
                .Label("None", "0", "no value")
                .Label("Int32", "1", "32 bit integer")
                .Label("Int64", "2", "64 bit integer")
                .Label("Float", "3", "single precision float"));

            // No `None` and no 0 entry, so SheetMan auto-inserts one. Covers
            // ModelCooker.ParseEnum's implicit-label path.
            enums.Enum(6, 1, new EnumSpec { Name = "Grade", Comment = "Item grade. Deliberately omits a zero entry." }
                .Label("Common", "1", "common grade")
                .Label("Rare", "2", "rare grade")
                .Label("Epic", "3", "epic grade"));

            // Labels declared in snake_case. They are stored Pascal-cased, so a data
            // cell repeating the declared spelling has to resolve back to them.
            enums.Enum(11, 1, new EnumSpec { Name = "SkillType", Comment = "Declared in snake_case on purpose." }
                .Label("none", "0", "no skill")
                .Label("fire_ball", "1", "throws a fireball")
                .Label("ice_shard", "2", "throws an ice shard"));

            // --- All primitive field types -----------------------------------

            var types = new SheetBuilder(workbook.CreateSheet("Types"));

            var testFieldTypes = new TableSpec
            {
                Name = "TestFieldTypes",
                Comment = "One column per supported primitive type.",
            };
            testFieldTypes
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("StringField", "string", "utf8 text"))
                .Field(FieldSpec.Of("BoolField", "bool", "logical flag", targetSide: "c"))
                .Field(FieldSpec.Of("IntField", "int", "32 bit integer", targetSide: "s"))
                .Field(FieldSpec.Of("BigIntField", "bigint", "64 bit integer"))
                .Field(FieldSpec.Of("FloatField", "float", "single precision"))
                .Field(FieldSpec.Of("DoubleField", "double", "double precision"))
                .Field(FieldSpec.Of("DatetimeField", "datetime", "date and time"))
                .Field(FieldSpec.Of("TimespanField", "timespan", "time interval"))
                .Field(FieldSpec.Of("UuidField", "uuid", "globally unique id"))
                .Field(FieldSpec.Of("ValueTypeField", "enum", "enum reference", detailType: "ValueType"))
                // A field commented out with `#` keeps its column but is dropped from
                // the model, so the data cells below it are never parsed.
                .Field(FieldSpec.Of("#IgnoredField", "string", "should not appear in output"));

            testFieldTypes
                .Row("1", "first", "Y", "1,024", "9007199254740993", "1.5", "2.25", "2022-01-24 10:30:00", "1.02:03:04", "7b7d9f6a-1e2c-4c1a-9a5f-2b6d0c3e4f51", "Int32", "junk")
                .Row("2", "second", "N", "-20", "-9007199254740993", "-0.5", "1e-8", "1999-12-31 23:59:59", "00:00:01", "0f8fad5b-d9cb-469f-a165-70867728950e", "Float", "junk")
                // Empty string and empty bool are both legal: bool treats blank as false.
                .Row("3", "", "", "0", "0", "0", "0", "2000-01-01 00:00:00", "00:00:00", "00000000-0000-0000-0000-000000000000", "None", "junk");

            types.Table(1, 1, testFieldTypes);

            // --- Cross-table references --------------------------------------

            var refs = new SheetBuilder(workbook.CreateSheet("Refs"));

            var itemCategory = new TableSpec
            {
                Name = "ItemCategory",
                Comment = "Referenced by Item.CategoryId.",
            };
            itemCategory
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Name", "string", "category name"))
                .Field(FieldSpec.Of("Description", "string", "human readable description"));
            itemCategory
                .Row("1", "Weapon", "things that hit")
                .Row("2", "Armor", "things that absorb")
                .Row("3", "Potion", "things that heal");

            refs.Table(1, 1, itemCategory);

            var item = new TableSpec
            {
                Name = "Item",
                Comment = "References ItemCategory by record.",
            };
            item
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Name", "string", "item name"))
                // `foreign` with a bare table name resolves to that table's record.
                .Field(FieldSpec.Of("CategoryId", "foreign", "owning category", detailType: "ItemCategory"))
                .Field(FieldSpec.Of("GradeField", "enum", "item grade", detailType: "Grade"))
                // Cells below spell these the way the enum was declared, in snake_case.
                .Field(FieldSpec.Of("SkillField", "enum", "granted skill", detailType: "SkillType"))
                // Free text as a designer would actually write it, including the
                // characters that have to be escaped before reaching the HTML docs.
                .Field(FieldSpec.Of("Description", "string", "shop blurb"))
                .Field(FieldSpec.Of("Price", "int", "shop price", targetSide: "s"));
            item
                .Row("1", "Short Sword", "1", "Common", "fire_ball", "Sharp & quick; deals <b>bonus</b> damage", "100")
                .Row("2", "Leather Armor", "2", "Rare", "ice_shard", "Blocks 5% of \"physical\" hits", "250")
                .Row("3", "Small Potion", "3", "Epic", "none", "Restores 10 HP <or> 5 MP", "50");

            // Placed well clear of ItemCategory: the rect scanner grows rightward
            // through non-empty cells, so neighbours need a blank gutter.
            refs.Table(8, 1, item);

            // --- Serial fields -------------------------------------------------

            var serial = new SheetBuilder(workbook.CreateSheet("Serial"));

            var localization = new TableSpec
            {
                Name = "Localization",
                Comment = "Trailing-number columns collapse into arrays.",
            };
            localization
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Key", "string", "lookup key"))
                .Field(FieldSpec.Of("TextEn1", "string", "english text 1"))
                .Field(FieldSpec.Of("TextEn2", "string", "english text 2"))
                .Field(FieldSpec.Of("TextKo1", "string", "korean text 1"))
                .Field(FieldSpec.Of("TextKo2", "string", "korean text 2"));
            localization
                .Row("1", "greeting", "Hello", "Hi", "안녕하세요", "안녕")
                .Row("2", "farewell", "Goodbye", "Bye", "안녕히가세요", "잘가");

            serial.Table(1, 1, localization);

            // --- Delimited array cells -------------------------------------------

            var arrays = new SheetBuilder(workbook.CreateSheet("Arrays"));

            var arrayTable = new TableSpec
            {
                Name = "ArrayTypes",
                Comment = "One cell holding several delimited values, length varying per row.",
            };
            arrayTable
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Tags", "string[]", "free-form tags"))
                .Field(FieldSpec.Of("Costs", "int[]", "cost per level"))
                .Field(FieldSpec.Of("Weights", "float[]", "drop weights"))
                .Field(FieldSpec.Of("Grades", "enum[]", "allowed grades", detailType: "Grade"))
                // A serial field alongside the delimited ones: the two array kinds use
                // different wire formats and must not disturb each other.
                .Field(FieldSpec.Of("Slot1", "int", "fixed slot 1"))
                .Field(FieldSpec.Of("Slot2", "int", "fixed slot 2"));
            arrayTable
                .Row("1", "red;green;blue", "10;20;30", "0.5;0.25", "Common;Rare", "1", "2")
                // A different length in every row, which is the point of the feature.
                .Row("2", "solo", "5", "1.0;2.0;3.0;4.0", "Epic", "3", "4")
                // Empty cells are empty arrays, not errors: a row with nothing to say
                // for the column is ordinary.
                .Row("3", "", "", "", "", "5", "6")
                // Whitespace around elements is trimmed.
                .Row("4", "a; b ;c", "1; 2", "0.1", "Common; Epic", "7", "8");

            arrays.Table(1, 1, arrayTable);

            // --- Entity-level target sides ---------------------------------------

            var sides = new SheetBuilder(workbook.CreateSheet("Sides"));

            // Whole entities marked for one side. These disappear entirely from output
            // built for the other side, while the per-field markers on TestFieldTypes
            // and Item exercise column-level filtering.
            var serverOnly = new TableSpec
            {
                Name = "ServerTuning",
                Comment = "Server-only table. Must not appear in client output.",
                TargetSide = "s",
            };
            serverOnly
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Key", "string", "tuning key"))
                .Field(FieldSpec.Of("Amount", "int", "tuning amount"));
            serverOnly
                .Row("1", "spawn_rate", "35")
                .Row("2", "loot_bias", "12");

            sides.Table(1, 1, serverOnly);

            var clientOnly = new TableSpec
            {
                Name = "ClientStrings",
                Comment = "Client-only table. Must not appear in server output.",
                TargetSide = "c",
            };
            clientOnly
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Key", "string", "string key"))
                .Field(FieldSpec.Of("Text", "string", "display text"));
            clientOnly
                .Row("1", "ui.ok", "OK")
                .Row("2", "ui.cancel", "Cancel");

            sides.Table(6, 1, clientOnly);

            // --- Constants -----------------------------------------------------

            var consts = new SheetBuilder(workbook.CreateSheet("Consts"));

            consts.Const(1, 1, new ConstSpec { Name = "GameConfig", Comment = "Assorted tuning constants." }
                .Constant("MaxLevel", "int", "100", "level cap")
                .Constant("StartGold", "bigint", "1000", "gold granted to new accounts")
                .Constant("DropRate", "float", "0.25", "base drop rate")
                .Constant("DebugMode", "bool", "N", "whether debug hooks are active")
                .Constant("DefaultGrade", "enum", "Rare", "grade assigned when unspecified", detailType: "Grade")
                // The three types a constant could not previously be written in: the C#
                // generator emitted their default ToString, which is not a literal, so a
                // sheet declaring one of these produced a file that would not compile.
                .Constant("SeasonStart", "datetime", "2022-03-01 09:00:00", "when the season opens")
                .Constant("RoundLength", "timespan", "0.00:05:00", "length of one round")
                .Constant("BuildId", "uuid", "6f9619ff-8b86-d011-b42d-00c04fc964ff", "identifies this data build"));

            Save(workbook, path);
        }

        // ---------------------------------------------------- excel-typed cells

        private static void WriteExcelTyped(string path)
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("Typed");
            var b = new SheetBuilder(sheet);

            var spec = new TableSpec
            {
                Name = "ExcelTyped",
                Comment = "Values entered as real Excel types rather than text.",
            };
            spec
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("IntFromNumeric", "int", "numeric cell holding an integer"))
                .Field(FieldSpec.Of("FloatFromNumeric", "float", "numeric cell holding a fraction"))
                .Field(FieldSpec.Of("WhenFromDateCell", "datetime", "genuine Excel date cell"))
                .Field(FieldSpec.Of("BigFromNumeric", "bigint", "numeric cell beyond double precision"));

            b.Table(1, 1, spec);

            // Header block occupies rows 1..7 (marker, comment, 5 header rows), so the
            // first data row is row 8. Written cell by cell because these must carry
            // real Excel types, which the string-based TableSpec.Row cannot express.
            int row = 8;

            b.SetNumeric(1, row, 1);
            b.SetNumeric(2, row, 42);
            b.SetNumeric(3, row, 1.5);
            b.SetDate(4, row, new DateTime(2022, 1, 24, 10, 30, 0));
            b.SetNumeric(5, row, 9007199254740993d);
            row++;

            b.SetNumeric(1, row, 2);
            b.SetNumeric(2, row, -7);
            b.SetNumeric(3, row, 0.1);
            b.SetDate(4, row, new DateTime(1999, 12, 31, 23, 59, 59));
            b.SetNumeric(5, row, 1e16);

            Save(workbook, path);
        }

        // ------------------------------------------------------- layout edges

        private static void WriteLayoutEdge(string path)
        {
            var workbook = new XSSFWorkbook();

            // Entity pushed down and right, so RawSheet.Optimize has leading blank
            // rows and columns to trim before anything else can run.
            var offset = new SheetBuilder(workbook.CreateSheet("Offset"));

            var spec = new TableSpec
            {
                Name = "OffsetTable",
                Comment = "Starts at F9 rather than the top-left corner.",
            };
            spec
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Name", "string", "name"))
                .Field(FieldSpec.Of("Value", "int", "value"));
            spec
                .Row("1", "alpha", "10")
                .Row("2", "beta", "20");

            int afterFirst = offset.Table(5, 8, spec);

            // A blank row that is genuinely ragged, sitting *between* two entities.
            //
            // RawSheet.Optimize trims blank rows only at the top and bottom, so an
            // interior one survives into the column scan. Its cells stop at column 2
            // while the entity rows reach column 7, and every column left of the
            // entity is empty. IsWholeEmptyColumn therefore walks to column 4 looking
            // for content and indexes past the end of this row - it runs before the
            // padding pass that would have squared the sheet off.
            //
            // Written as explicit empty cells because a row with no cells at all is
            // not emitted to the .xlsx, and NPOI would simply skip it on import.
            int raggedRow = afterFirst + 1;
            offset.Set(0, raggedRow, "");
            offset.Set(1, raggedRow, "");
            offset.Set(2, raggedRow, "");

            // A second entity below keeps the ragged row interior rather than trailing.
            var second = new TableSpec
            {
                Name = "SecondTable",
                Comment = "Keeps the ragged row from being trimmed as a trailing blank.",
            };
            second
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Label", "string", "label"))
                .Field(FieldSpec.Of("Amount", "int", "amount"));
            second
                .Row("1", "gamma", "30");

            offset.Table(5, raggedRow + 2, second);

            Save(workbook, path);
        }

        // ------------------------------------------- foreign `Table.Field` form

        /// <summary>
        /// The documented `RefTable.RefFieldName` form of a foreign detail type.
        ///
        /// ModelCooker slices the table name with `Substring(0, dot - 1)`, dropping its
        /// last character, so this shape cannot resolve today. Kept in its own fixture
        /// so the rest of the suite still has a workbook that converts.
        /// </summary>
        private static void WriteForeignFieldRef(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Refs"));

            var category = new TableSpec
            {
                Name = "ItemCategory",
                Comment = "Target of the field-level reference below.",
            };
            category
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Name", "string", "category name"))
                .Field(FieldSpec.Of("Description", "string", "description"));
            category
                .Row("1", "Weapon", "things that hit")
                .Row("2", "Armor", "things that absorb");

            b.Table(1, 1, category);

            var item = new TableSpec
            {
                Name = "Item",
                Comment = "References a specific field rather than the whole record.",
            };
            item
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Name", "string", "item name"))
                // Resolves to ItemCategory.Name, so the field's effective type
                // becomes `string` rather than a record reference.
                .Field(FieldSpec.Of("CategoryName", "foreign", "category name by reference", detailType: "ItemCategory.Name"));
            item
                .Row("1", "Short Sword", "1")
                .Row("2", "Leather Armor", "2");

            b.Table(8, 1, item);

            Save(workbook, path);
        }

        // ------------------------------------------------------ invalid workbooks

        /// <summary>
        /// A workbook with several independent mistakes in it.
        ///
        /// Deliberately more than one, and of more than one kind: validation is
        /// supposed to report the lot in a single run rather than stopping at the
        /// first, so a fixture with a single error could not tell the difference.
        /// </summary>
        private static void WriteInvalid(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Bad"));

            var catalog = new TableSpec
            {
                Name = "Catalog",
                Comment = "Repeats a primary index value and a secondary index value.",
            };
            catalog
                .Field(FieldSpec.Of("index", "int", "primary index"))
                // A `*` prefix opts a further column into index treatment, so its
                // values must be unique too.
                .Field(FieldSpec.Of("*Code", "string", "secondary index"))
                .Field(FieldSpec.Of("Name", "string", "display name"));
            catalog
                .Row("1", "X", "first")
                // Duplicate primary index, and "X" duplicates the secondary index.
                .Row("1", "X", "second")
                .Row("3", "Z", "third");

            b.Table(1, 1, catalog);

            var orders = new TableSpec
            {
                Name = "Orders",
                Comment = "Points at a Catalog row that does not exist.",
            };
            orders
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Item", "foreign", "ordered item", detailType: "Catalog"))
                .Field(FieldSpec.Of("Qty", "int", "quantity"));
            orders
                .Row("1", "3", "2")
                // Catalog has no row 99.
                .Row("2", "99", "1");

            b.Table(6, 1, orders);

            // A reference whose target table is absent altogether, and one naming a
            // field the target does not have. Both are resolution failures rather
            // than validation failures, and used to abort the run on the spot - so
            // they never appeared alongside the problems above.
            var shipments = new TableSpec
            {
                Name = "Shipments",
                Comment = "References a table that does not exist, and a field that does not exist.",
            };
            shipments
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Warehouse", "foreign", "no such table", detailType: "NoSuchTable"))
                .Field(FieldSpec.Of("CatalogLabel", "foreign", "no such field", detailType: "Catalog.NoSuchField"));
            shipments
                .Row("1", "1", "1");

            b.Table(11, 1, shipments);

            Save(workbook, path);
        }

        /// <summary>
        /// A client-visible table referencing a server-only one.
        ///
        /// Valid as a whole, but a client build drops the target, leaving the
        /// reference pointing at a type that was never emitted.
        /// </summary>
        private static void WriteSideDangling(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Sides"));

            var serverOnly = new TableSpec
            {
                Name = "ServerOnlyTarget",
                Comment = "Excluded from client builds.",
                TargetSide = "s",
            };
            serverOnly
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Name", "string", "name"))
                .Field(FieldSpec.Of("Note", "string", "note"));
            serverOnly
                .Row("1", "alpha", "first")
                .Row("2", "beta", "second");

            b.Table(1, 1, serverOnly);

            var clientVisible = new TableSpec
            {
                Name = "ClientVisible",
                Comment = "Survives a client build, but its reference does not.",
            };
            clientVisible
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Target", "foreign", "dangles in a client build", detailType: "ServerOnlyTarget"))
                .Field(FieldSpec.Of("Label", "string", "label"));
            clientVisible
                .Row("1", "1", "one")
                .Row("2", "2", "two");

            b.Table(6, 1, clientVisible);

            Save(workbook, path);
        }

        /// <summary>
        /// `foreign[]`, which is deliberately unsupported.
        /// </summary>
        private static void WriteArrayForeign(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Bad"));

            var target = new TableSpec { Name = "Target", Comment = "Reference target." };
            target
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Name", "string", "name"))
                .Field(FieldSpec.Of("Note", "string", "note"));
            target.Row("1", "one", "first");

            b.Table(1, 1, target);

            var holder = new TableSpec { Name = "Holder", Comment = "Declares an array of references." };
            holder
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Targets", "foreign[]", "unsupported", detailType: "Target"))
                .Field(FieldSpec.Of("Label", "string", "label"));
            holder.Row("1", "1", "a");

            b.Table(6, 1, holder);

            Save(workbook, path);
        }

        /// <summary>
        /// Cell values that used to be accepted silently and now are not.
        ///
        /// A misspelled boolean became false rather than an error, which is the class of
        /// human mistake this tool exists to catch turned into wrong data.
        /// </summary>
        private static void WriteStrictValues(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Bad"));

            var spec = new TableSpec
            {
                Name = "Flags",
                Comment = "Holds a boolean that is neither a recognized word nor a number.",
            };
            spec
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Enabled", "bool", "misspelled below"))
                .Field(FieldSpec.Of("Label", "string", "label"));
            spec
                .Row("1", "Y", "fine")
                // A typo for TRUE. Used to read as false.
                .Row("2", "Ture", "typo");

            b.Table(1, 1, spec);

            Save(workbook, path);
        }

        /// <summary>
        /// A field name carrying two `*` markers, which is a typo for one.
        /// </summary>
        private static void WriteDoubleStar(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Bad"));

            var spec = new TableSpec { Name = "Doubled", Comment = "Field name carries two index markers." };
            spec
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("**Code", "string", "typo for *Code"))
                .Field(FieldSpec.Of("Label", "string", "label"));
            spec.Row("1", "A", "first");

            b.Table(1, 1, spec);

            Save(workbook, path);
        }

        /// <summary>
        /// A formula whose cached result is an error, as a division by zero leaves behind.
        /// </summary>
        private static void WriteFormulaError(string path)
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("Bad");
            var b = new SheetBuilder(sheet);

            var spec = new TableSpec { Name = "Broken", Comment = "Holds a formula that does not evaluate." };
            spec
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Ratio", "float", "computed by a formula"))
                .Field(FieldSpec.Of("Label", "string", "label"));
            spec.Row("1", "0", "placeholder");

            b.Table(1, 1, spec);

            // Header occupies rows 1..7, so the single data row is row 8. The formula is
            // written with its cached result already set to the error, because SheetMan
            // reads cached results rather than evaluating anything itself.
            b.SetFormulaError(2, 8, "1/0", NPOI.SS.UserModel.FormulaError.DIV0);

            Save(workbook, path);
        }

        /// <summary>
        /// An enum column where one cell names the label and another gives its number.
        /// </summary>
        private static void WriteEnumByValue(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Data"));

            b.Enum(1, 1, new EnumSpec { Name = "Grade", Comment = "Item grade." }
                .Label("None", "0", "unset")
                .Label("Common", "1", "common")
                .Label("Rare", "2", "rare"));

            var spec = new TableSpec { Name = "Items", Comment = "Refers to Grade both ways." };
            spec
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Grade", "enum", "by name or by number", detailType: "Grade"))
                .Field(FieldSpec.Of("Label", "string", "label"));
            spec
                .Row("1", "Rare", "written by name")
                // The same label, written as its value.
                .Row("2", "2", "written by number");

            b.Table(6, 1, spec);

            Save(workbook, path);
        }

        /// <summary>
        /// Names that collide with a keyword in one of the output languages.
        ///
        /// Whether this matters depends on how each generator cases an identifier, and the
        /// three differ: C# renders members PascalCase, which lifts every all-lowercase
        /// keyword out of the way; TypeScript renders them camelCase; C++ renders them
        /// snake_case, so `Int` becomes `int` and `Class` becomes `class`.
        ///
        /// The table name matters too - the C++ accessor exposes each table through a
        /// snake_cased method - hence a table called Template.
        ///
        /// The point of the fixture is that the toolchain gates answer the question rather
        /// than anybody reasoning about it: the suite compiles the generated C++ and C# and
        /// type-checks the generated TypeScript.
        /// </summary>
        private static void WriteReservedWords(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Data"));

            var spec = new TableSpec { Name = "Template", Comment = "Named after a C++ keyword." };
            spec
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("Class", "string", "class: keyword in C++ and C#"))
                .Field(FieldSpec.Of("Int", "int", "int: keyword in C++ and C#"))
                .Field(FieldSpec.Of("Delete", "bool", "delete: keyword in C++"))
                .Field(FieldSpec.Of("Operator", "string", "operator: keyword in C++"))
                .Field(FieldSpec.Of("Namespace", "string", "namespace: keyword in C++ and C#"))
                .Field(FieldSpec.Of("Constructor", "string", "constructor: special member in TypeScript"))
                .Field(FieldSpec.Of("Function", "string", "function: keyword in TypeScript"));
            spec
                .Row("1", "first", "10", "Y", "plus", "alpha", "ctor-a", "fn-a")
                .Row("2", "second", "20", "N", "minus", "beta", "ctor-b", "fn-b");

            b.Table(1, 1, spec);

            Save(workbook, path);
        }

        /// <summary>
        /// The conformance corpus: every type, at the values that break a reader.
        ///
        /// This exists so that adding an output language costs a small harness rather than a
        /// gate of its own. A reader in any language reads this one table and prints what it
        /// found; the suite compares that against what the JSON exporter wrote from the same
        /// cells. Nothing about the comparison is language-specific.
        ///
        /// The values are chosen from where readers have actually gone wrong:
        ///
        ///   2^53 + 1 and its negative, because a language that carries a 64-bit integer in
        ///   a double returns them changed rather than failing - which is how the binary
        ///   writer's truncation of `long` survived for years, and how a JSON reader that
        ///   parses int64 as a number still loses them.
        ///
        ///   0.1 as a float, because the shortest decimal that round-trips a 32-bit value
        ///   widens to a different double, so a reader without a narrowing step disagrees
        ///   with the binary by a hair.
        ///
        ///   varint lengths one through five, and negative values either side of zero, since
        ///   the encoding is zig-zag and a reader that shifts instead of dividing gets the
        ///   sign wrong only for some magnitudes.
        ///
        ///   an empty string, an empty array and non-ASCII text, because a length-prefixed
        ///   format makes each of those a separate path.
        ///
        /// And it carries two references into a second table, which is not about values at all.
        /// Splitting each target's output into a file per table gave every language a question
        /// it did not have before - how does one table's file reach another's - and nothing here
        /// crossed a table, so the answer went unchecked in every language but C#. A missing
        /// import or require does not compile or does not load, which is what this catches.
        ///
        /// Both kinds, because the generators treat them differently: `owner` points at a whole
        /// row, so a table's file names the other table's record type, while `tier` points at
        /// one of that row's fields and names only its type.
        /// </summary>
        private static void WriteConformance(string path)
        {
            var workbook = new XSSFWorkbook();
            var b = new SheetBuilder(workbook.CreateSheet("Vectors"));

            b.Enum(1, 1, new EnumSpec { Name = "Flag", Comment = "Enum values travel zig-zag encoded." }
                .Label("None", "0", "zero")
                .Label("One", "1", "one byte")
                .Label("Large", "1048576", "three bytes")
                .Label("Negative", "-7", "negative, so the sign is folded into the low bit"));

            var spec = new TableSpec { Name = "Vectors", Comment = "Every type at the values that break a reader." };
            spec
                .Field(FieldSpec.Of("index", "int", "primary index"))
                // None of these may end in a digit: a numbered name is folded into a
                // serial field, so `i32` and `i64` became one array called `i`.
                .Field(FieldSpec.Of("intVal", "int", "varint boundaries and both extremes"))
                .Field(FieldSpec.Of("bigVal", "bigint", "past what a double carries exactly"))
                .Field(FieldSpec.Of("floatVal", "float", "single precision"))
                .Field(FieldSpec.Of("doubleVal", "double", "double precision"))
                .Field(FieldSpec.Of("text", "string", "empty, ascii and beyond"))
                .Field(FieldSpec.Of("flag", "bool", "both values"))
                .Field(FieldSpec.Of("when", "datetime", "as ticks on the wire"))
                .Field(FieldSpec.Of("span", "timespan", "as ticks on the wire"))
                .Field(FieldSpec.Of("uid", "uuid", "sixteen bytes in .NET order"))
                .Field(FieldSpec.Of("label", "enum", "zig-zag encoded", detailType: "Flag"))
                .Field(FieldSpec.Of("ints", "int[]", "length-prefixed, including empty"))
                .Field(FieldSpec.Of("strs", "string[]", "length-prefixed strings"))

                // A whole-row reference, so a table's own file names the other table's record
                // type - which is the dependency a per-table output has to carry.
                .Field(FieldSpec.Of("owner", "foreign", "a whole row of another table",
                                    detailType: "Owners"))

                // And a field reference, which resolves to that row's value and so names only
                // its type. The generators take a different path for each.
                .Field(FieldSpec.Of("tier", "foreign", "one field of another table's row",
                                    detailType: "Owners.rank"));

            spec
                // Zero and empty everywhere: one varint byte, and the length-prefixed
                // paths at length zero.
                .Row("1", "0", "0", "0", "0", "", "N",
                     "0001-01-01 00:00:00", "00:00:00", "00000000-0000-0000-0000-000000000000",
                     "None", "", "", "0", "0")

                // The value a double cannot hold, and one varint byte short of two.
                .Row("2", "63", "9007199254740993", "0.1", "0.1", "ascii", "Y",
                     "2022-03-01 09:00:00", "0.00:05:00", "6f9619ff-8b86-d011-b42d-00c04fc964ff",
                     "One", "0;1;-1", "a;b", "1", "1")

                // Its negative, and the zig-zag boundary either side of zero.
                .Row("3", "-64", "-9007199254740993", "-0.1", "-0.1", "é한Ａ", "N",
                     "9999-12-31 23:59:59", "-0.00:05:00", "ffffffff-ffff-ffff-ffff-ffffffffffff",
                     "Negative", "-2147483648;2147483647", "", "2", "2")

                // Three varint bytes, and both 32-bit extremes.
                .Row("4", "1048576", "-1", "3.4028235E+38", "1.7976931348623157E+308", "  spaced  ", "Y",
                     "1970-01-01 00:00:00", "10675199.02:48:05", "01020304-0506-0708-090a-0b0c0d0e0f10",
                     "Large", "1048576", "one;;three", "3", "3")

                // Five varint bytes each way.
                .Row("5", "2147483647", "9223372036854775807", "1.4E-45", "5E-324", "tail", "N",
                     "2038-01-19 03:14:07", "00:00:00.0000001", "ffffffff-0000-ffff-0000-ffffffffffff",
                     "None", "134217728;-134217729", "z", "1", "3")

                // Negative zero is deliberately not here: JSON has no such value, so the
                // harness contract cannot carry it and a disagreement would say nothing
                // about the reader. A negative denormal exercises the same code path and
                // does survive the round trip.
                .Row("6", "-2147483648", "-9223372036854775808", "-1.4E-45", "-5E-324", "", "Y",
                     "2000-02-29 12:00:00", "1.00:00:00", "80000000-0000-0000-0000-000000000001",
                     "One", "", "é", "3", "1");

            b.Table(8, 1, spec);

            // The table the two references point into.
            //
            // Small on purpose: it is not here to test values - Vectors does that - but to give
            // the references somewhere real to land. Row 1 of Vectors points at 0, which is how
            // a sheet says "no reference", so the unresolved path is exercised too.
            var owners = new TableSpec
            {
                Name = "Owners",
                Comment = "Referenced by Vectors.owner and Vectors.tier.",
            };

            owners
                .Field(FieldSpec.Of("index", "int", "primary index"))
                .Field(FieldSpec.Of("name", "string", "what the referring row points at"))
                .Field(FieldSpec.Of("rank", "int", "what the field reference resolves to"));

            owners
                .Row("1", "first", "10")
                .Row("2", "second", "20")
                .Row("3", "third", "30");

            b.Table(8, 20, owners);

            // A constant set, so every language's constants file is generated, compiled and
            // read by its harness.
            //
            // Nothing gated one before. The corpus had no constant set, and neither did
            // reserved-words - the only other scenario generating for every language - so
            // splitting the output into a file per table produced a constants file per set in
            // twelve languages that nothing ever built. Rust proved the point: a constant typed
            // with an enum names that enum, the dependency graph did not say so, and the crate
            // did not compile. It took building an unrelated corpus by hand to find out.
            //
            // The enum-typed and uuid-typed constants are the two that make a constants file
            // depend on something outside itself, which is what makes them worth the place here.
            var limits = new ConstSpec
            {
                Name = "Limits",
                Comment = "Constants whose types make a constants file depend on something else.",
            };

            limits
                .Constant("MaxOwners", "int", "3", "how many rows Owners has")
                .Constant("Huge", "bigint", "9223372036854775807", "past what a double carries exactly")
                .Constant("Ratio", "float", "0.25", "single precision")
                .Constant("Precise", "double", "5E-324", "the smallest denormal")
                .Constant("Title", "string", "é한Ａ", "beyond ascii")
                .Constant("Enabled", "bool", "Y", "logical flag")
                .Constant("Epoch", "datetime", "1970-01-01 00:00:00", "as ticks on the wire")
                .Constant("Round", "timespan", "0.00:05:00", "as ticks on the wire")

                // The two that reach outside the file: an enum label, and a value the reader's
                // own type carries.
                .Constant("DefaultFlag", "enum", "Large", "names the Flag enum", detailType: "Flag")
                .Constant("BuildId", "uuid", "6f9619ff-8b86-d011-b42d-00c04fc964ff",
                          "names the reader's uuid type");

            // Column 1, well below the Flag enum: the two tables are at column 8.
            b.Const(1, 20, limits);

            Save(workbook, path);
        }

        // ------------------------------------------------------------- helpers

        private static void Save(XSSFWorkbook workbook, string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                workbook.Write(fs);

            Console.WriteLine($"  wrote {Path.GetFileName(path)}");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
                dir = dir.Parent;

            if (dir == null)
                throw new InvalidOperationException("Could not locate the repository root.");

            return dir.FullName;
        }
    }
}
