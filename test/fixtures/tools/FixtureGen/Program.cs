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
                .Constant("DefaultGrade", "enum", "Rare", "grade assigned when unspecified", detailType: "Grade"));

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
