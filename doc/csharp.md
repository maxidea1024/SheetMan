# C# 코드 생성 예제

```csharp
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Soma.LiteBinarySerialization;

namespace StaticData
{
    #region Static tables
    public partial class Tables
    {
        public delegate Task<byte[]> ReadAllBytesAsyncDelegate(string filename);

        public static ReadAllBytesAsyncDelegate ReadAllBytesAsync = async (string filename) => {
        #if UNITY_EDITOR || UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID
            filename = filename.Replace('\\', '/');
            var data await UnityEngine.Resources.LoadAsync(filename) as UnityEngine.TextAsset;
            if (data == null)
                throw new SheetManException($"Cannot read a file '{filename}'");

            return data.bytes;
        #else
            var bytes = await System.IO.File.ReadAllBytesAsync(filename);
            if (bytes == null)
                throw new SheetManException($"Cannot read a file '{filename}'");

            return bytes;
        #endif
        };

        /// <summary>
        /// Property for StageDescText table.
        /// </summary>
        public static StageDescTextTable StageDescText { get; private set; }

        /// <summary>
        /// Property for TestFieldTypes table.
        /// </summary>
        public static TestFieldTypesTable TestFieldTypes { get; private set; }

        /// <summary>
        /// Property for Localization table.
        /// </summary>
        public static LocalizationTable Localization { get; private set; }

        /// <summary>
        /// Property for Template1 table.
        /// </summary>
        public static Template1Table Template1 { get; private set; }

        /// <summary>
        /// Property for SerialFields table.
        /// </summary>
        public static SerialFieldsTable SerialFields { get; private set; }

        /// <summary>
        /// Property for Account table.
        /// </summary>
        public static AccountTable Account { get; private set; }

        /// <summary>
        /// Property for BankAccount table.
        /// </summary>
        public static BankAccountTable BankAccount { get; private set; }

        /// <summary>
        /// Property for BankAccountDetail table.
        /// </summary>
        public static BankAccountDetailTable BankAccountDetail { get; private set; }

        /// <summary>
        /// Read all tables.
        /// </summary>
        public static async Task ReadAllAsync(string basePath = "", string fileExtension = ".table")
        {
            var tasks = new List<Task>();

            StageDescText = new StageDescTextTable();
            tasks.Add(StageDescText.ReadAsync(System.IO.Path.Combine(basePath, $"StageDescText{fileExtension}")));

            TestFieldTypes = new TestFieldTypesTable();
            tasks.Add(TestFieldTypes.ReadAsync(System.IO.Path.Combine(basePath, $"TestFieldTypes{fileExtension}")));

            Localization = new LocalizationTable();
            tasks.Add(Localization.ReadAsync(System.IO.Path.Combine(basePath, $"Localization{fileExtension}")));

            Template1 = new Template1Table();
            tasks.Add(Template1.ReadAsync(System.IO.Path.Combine(basePath, $"Template1{fileExtension}")));

            SerialFields = new SerialFieldsTable();
            tasks.Add(SerialFields.ReadAsync(System.IO.Path.Combine(basePath, $"SerialFields{fileExtension}")));

            Account = new AccountTable();
            tasks.Add(Account.ReadAsync(System.IO.Path.Combine(basePath, $"Account{fileExtension}")));

            BankAccount = new BankAccountTable();
            tasks.Add(BankAccount.ReadAsync(System.IO.Path.Combine(basePath, $"BankAccount{fileExtension}")));

            BankAccountDetail = new BankAccountDetailTable();
            tasks.Add(BankAccountDetail.ReadAsync(System.IO.Path.Combine(basePath, $"BankAccountDetail{fileExtension}")));

            await Task.WhenAll(tasks);

            SolveCrossReferences();
        }

        /// <summary>
        /// Solve cross references.
        /// </summary>
        private static void SolveCrossReferences()
        {
            foreach (var record in Account.Records)
            {
                if (record._bank_BankAccount_index > 0)
                {
                    record.SetReference_Bank_INTERNAL(BankAccount.GetByIndex(record._bank_BankAccount_index));
                    record._bank_F = true;
                }
            }

            foreach (var record in BankAccount.Records)
            {
                if (record._detail_BankAccountDetail_index > 0)
                {
                    record.SetReference_Detail_INTERNAL(BankAccountDetail.GetByIndex(record._detail_BankAccountDetail_index));
                    record._detail_F = true;
                }
            }
        }
    }
    #endregion

    #region StageDescTextTable
    /// <summary>
    /// 로컬라이제이션 테스트 테이블입니다.
    /// </summary>
    [System.Serializable]
    public partial class StageDescTextTable
    {
        #region Record
        [System.Serializable]
        public partial class Record
        {
            #region Fields
            /// <summary>
            /// 인덱스(필수)
            /// </summary>
            public int Index => _index;
            private int _index;

            /// <summary>
            /// 영문 텍스트
            /// </summary>
            public string[] TextArray => _textArray;
            public const int TextArray_N = 2;
            private string[] _textArray = new string[TextArray_N];
            #endregion

            #region Read record
            /// <summary>
            /// Reads a table data.
            /// </summary>
            public Task ReadAsync(LiteBinaryReader reader)
            {
                reader.Read(out _index);

                for (int i = 0; i < TextArray_N; ++i)
                {
                    reader.Read(out _textArray[i]);
                }

                return Task.CompletedTask;
            }
            #endregion

            #region ToString
            public override string ToString()
            {
                var sb = new StringBuilder("{");
                sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
                sb.Append(",\"TextArray\":"); ToStringHelper.ToString(TextArray, sb);
                sb.Append("}");
                return sb.ToString();
            }
            #endregion
        }
        #endregion

        /// <summary>
        /// All records.
        /// </summary>
        public List<Record> Records => _records;
        private readonly List<Record> _records = new List<Record>();

        #region Indexing by 'Index'
        public Dictionary<int, Record> RecordsByIndex => _recordsByIndex;
        private readonly Dictionary<int, Record> _recordsByIndex = new Dictionary<int, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByIndex(int key)
        {
            if (!TryGetByIndex(key, out Record record))
                throw new SheetManException($"There is no record in table `StageDescText` that corresponds to field `Index` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByIndex(int key, out Record result) => _recordsByIndex.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsIndex(int key) => _recordsByIndex.ContainsKey(key);
        #endregion // Indexing by `Index`

        /// <summary>
        /// Read a table from specified file.
        /// </summary>
        public async Task ReadAsync(string filename)
        {
            var bytes = await Tables.ReadAllBytesAsync(filename);
            LiteBinaryReader reader = new LiteBinaryReader(bytes);
            await ReadAsync(reader);
        }

        /// <summary>
        /// Read a table from specified reader.
        /// </summary>
        public async Task ReadAsync(LiteBinaryReader reader)
        {
            uint version = 0;
            reader.Read(out version);

            int count = reader.ReadCounter32();
            for (int i = 0; i < count; i++)
            {
                var record = new Record();
                await record.ReadAsync(reader);
                _records.Add(record);
            }

            // Additional index mapping
            foreach (var record in _records)
                _recordsByIndex.Add(record.Index, record);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToStringHelper.ToString(_records, sb);
            return sb.ToString();
        }
    }
    #endregion

    #region TestFieldTypesTable
    /// <summary>
    /// 필드 타입 테스트용 테이블입니다.
    /// </summary>
    [System.Serializable]
    public partial class TestFieldTypesTable
    {
        #region Record
        [System.Serializable]
        public partial class Record
        {
            #region Fields
            /// <summary>
            /// 인덱스(필수)
            /// </summary>
            public int Index => _index;
            private int _index;

            /// <summary>
            /// string field
            /// </summary>
            public string StringField => _stringField;
            private string _stringField;

            /// <summary>
            /// boolean field
            /// </summary>
            public bool BoolField => _boolField;
            private bool _boolField;

            /// <summary>
            /// 32 bit integer field
            /// </summary>
            public int IntField => _intField;
            private int _intField;

            /// <summary>
            /// float field
            /// </summary>
            public float FloatField => _floatField;
            private float _floatField;

            /// <summary>
            /// double field
            /// </summary>
            public double DoubleField => _doubleField;
            private double _doubleField;

            /// <summary>
            /// datetime field
            /// </summary>
            public System.DateTime DatetimeField => _datetimeField;
            private System.DateTime _datetimeField;

            /// <summary>
            /// timespan field
            /// </summary>
            public System.TimeSpan TimespanField => _timespanField;
            private System.TimeSpan _timespanField;

            /// <summary>
            /// uuid field
            /// </summary>
            public System.Guid UuidField => _uuidField;
            private System.Guid _uuidField;

            /// <summary>
            /// value type
            /// </summary>
            public global::StaticData.ValueType ValueType => _valueType;
            private global::StaticData.ValueType _valueType;
            #endregion

            #region Read record
            /// <summary>
            /// Reads a table data.
            /// </summary>
            public Task ReadAsync(LiteBinaryReader reader)
            {
                int tempEnumInt = 0;
                reader.Read(out _index);

                reader.Read(out _stringField);

                reader.Read(out _boolField);

                reader.Read(out _intField);

                reader.Read(out _floatField);

                reader.Read(out _doubleField);

                reader.Read(out _datetimeField);

                reader.Read(out _timespanField);

                reader.Read(out _uuidField);

                tempEnumInt = reader.ReadOptimalInt32();
                _valueType = (global::StaticData.ValueType)tempEnumInt;

                return Task.CompletedTask;
            }
            #endregion

            #region ToString
            public override string ToString()
            {
                var sb = new StringBuilder("{");
                sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
                sb.Append(",\"StringField\":"); ToStringHelper.ToString(StringField, sb);
                sb.Append(",\"BoolField\":"); ToStringHelper.ToString(BoolField, sb);
                sb.Append(",\"IntField\":"); ToStringHelper.ToString(IntField, sb);
                sb.Append(",\"FloatField\":"); ToStringHelper.ToString(FloatField, sb);
                sb.Append(",\"DoubleField\":"); ToStringHelper.ToString(DoubleField, sb);
                sb.Append(",\"DatetimeField\":"); ToStringHelper.ToString(DatetimeField, sb);
                sb.Append(",\"TimespanField\":"); ToStringHelper.ToString(TimespanField, sb);
                sb.Append(",\"UuidField\":"); ToStringHelper.ToString(UuidField, sb);
                sb.Append(",\"ValueType\":"); ToStringHelper.ToString(ValueType, sb);
                sb.Append("}");
                return sb.ToString();
            }
            #endregion
        }
        #endregion

        /// <summary>
        /// All records.
        /// </summary>
        public List<Record> Records => _records;
        private readonly List<Record> _records = new List<Record>();

        #region Indexing by 'Index'
        public Dictionary<int, Record> RecordsByIndex => _recordsByIndex;
        private readonly Dictionary<int, Record> _recordsByIndex = new Dictionary<int, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByIndex(int key)
        {
            if (!TryGetByIndex(key, out Record record))
                throw new SheetManException($"There is no record in table `TestFieldTypes` that corresponds to field `Index` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByIndex(int key, out Record result) => _recordsByIndex.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsIndex(int key) => _recordsByIndex.ContainsKey(key);
        #endregion // Indexing by `Index`

        #region Indexing by 'StringField'
        public Dictionary<string, Record> RecordsByStringField => _recordsByStringField;
        private readonly Dictionary<string, Record> _recordsByStringField = new Dictionary<string, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByStringField(string key)
        {
            if (!TryGetByStringField(key, out Record record))
                throw new SheetManException($"There is no record in table `TestFieldTypes` that corresponds to field `StringField` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByStringField(string key, out Record result) => _recordsByStringField.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsStringField(string key) => _recordsByStringField.ContainsKey(key);
        #endregion // Indexing by `StringField`

        /// <summary>
        /// Read a table from specified file.
        /// </summary>
        public async Task ReadAsync(string filename)
        {
            var bytes = await Tables.ReadAllBytesAsync(filename);
            LiteBinaryReader reader = new LiteBinaryReader(bytes);
            await ReadAsync(reader);
        }

        /// <summary>
        /// Read a table from specified reader.
        /// </summary>
        public async Task ReadAsync(LiteBinaryReader reader)
        {
            uint version = 0;
            reader.Read(out version);

            int count = reader.ReadCounter32();
            for (int i = 0; i < count; i++)
            {
                var record = new Record();
                await record.ReadAsync(reader);
                _records.Add(record);
            }

            // Additional index mapping
            foreach (var record in _records)
                _recordsByIndex.Add(record.Index, record);
            foreach (var record in _records)
                _recordsByStringField.Add(record.StringField, record);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToStringHelper.ToString(_records, sb);
            return sb.ToString();
        }
    }
    #endregion

    #region LocalizationTable
    /// <summary>
    /// 로컬라이제이션 테이블입니다.
    /// </summary>
    [System.Serializable]
    public partial class LocalizationTable
    {
        #region Record
        [System.Serializable]
        public partial class Record
        {
            #region Fields
            /// <summary>
            /// 인덱스(필수)
            /// </summary>
            public int Index => _index;
            private int _index;

            /// <summary>
            /// 스트링 키
            /// </summary>
            public string Key => _key;
            private string _key;

            /// <summary>
            /// 설명
            /// </summary>
            public string Description => _description;
            private string _description;

            /// <summary>
            /// 영어
            /// </summary>
            public string English => _english;
            private string _english;

            /// <summary>
            /// 한글
            /// </summary>
            public string Korean => _korean;
            private string _korean;

            /// <summary>
            /// 스페니시
            /// </summary>
            public string Spanish => _spanish;
            private string _spanish;

            /// <summary>
            /// 중국어
            /// </summary>
            public string Chinese => _chinese;
            private string _chinese;

            /// <summary>
            /// 프랑스어
            /// </summary>
            public string French => _french;
            private string _french;

            /// <summary>
            /// 독일어
            /// </summary>
            public string German => _german;
            private string _german;

            /// <summary>
            /// 인도네시아어
            /// </summary>
            public string Indonesian => _indonesian;
            private string _indonesian;

            /// <summary>
            /// 일본어
            /// </summary>
            public string Japanese => _japanese;
            private string _japanese;

            /// <summary>
            /// 포루투칼어
            /// </summary>
            public string Portuguese => _portuguese;
            private string _portuguese;

            /// <summary>
            /// 러시아어
            /// </summary>
            public string Russian => _russian;
            private string _russian;

            /// <summary>
            /// 베트남어
            /// </summary>
            public string Vietnamese => _vietnamese;
            private string _vietnamese;
            #endregion

            #region Read record
            /// <summary>
            /// Reads a table data.
            /// </summary>
            public Task ReadAsync(LiteBinaryReader reader)
            {
                reader.Read(out _index);

                reader.Read(out _key);

                reader.Read(out _description);

                reader.Read(out _english);

                reader.Read(out _korean);

                reader.Read(out _spanish);

                reader.Read(out _chinese);

                reader.Read(out _french);

                reader.Read(out _german);

                reader.Read(out _indonesian);

                reader.Read(out _japanese);

                reader.Read(out _portuguese);

                reader.Read(out _russian);

                reader.Read(out _vietnamese);

                return Task.CompletedTask;
            }
            #endregion

            #region ToString
            public override string ToString()
            {
                var sb = new StringBuilder("{");
                sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
                sb.Append(",\"Key\":"); ToStringHelper.ToString(Key, sb);
                sb.Append(",\"Description\":"); ToStringHelper.ToString(Description, sb);
                sb.Append(",\"English\":"); ToStringHelper.ToString(English, sb);
                sb.Append(",\"Korean\":"); ToStringHelper.ToString(Korean, sb);
                sb.Append(",\"Spanish\":"); ToStringHelper.ToString(Spanish, sb);
                sb.Append(",\"Chinese\":"); ToStringHelper.ToString(Chinese, sb);
                sb.Append(",\"French\":"); ToStringHelper.ToString(French, sb);
                sb.Append(",\"German\":"); ToStringHelper.ToString(German, sb);
                sb.Append(",\"Indonesian\":"); ToStringHelper.ToString(Indonesian, sb);
                sb.Append(",\"Japanese\":"); ToStringHelper.ToString(Japanese, sb);
                sb.Append(",\"Portuguese\":"); ToStringHelper.ToString(Portuguese, sb);
                sb.Append(",\"Russian\":"); ToStringHelper.ToString(Russian, sb);
                sb.Append(",\"Vietnamese\":"); ToStringHelper.ToString(Vietnamese, sb);
                sb.Append("}");
                return sb.ToString();
            }
            #endregion
        }
        #endregion

        /// <summary>
        /// All records.
        /// </summary>
        public List<Record> Records => _records;
        private readonly List<Record> _records = new List<Record>();

        #region Indexing by 'Index'
        public Dictionary<int, Record> RecordsByIndex => _recordsByIndex;
        private readonly Dictionary<int, Record> _recordsByIndex = new Dictionary<int, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByIndex(int key)
        {
            if (!TryGetByIndex(key, out Record record))
                throw new SheetManException($"There is no record in table `Localization` that corresponds to field `Index` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByIndex(int key, out Record result) => _recordsByIndex.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsIndex(int key) => _recordsByIndex.ContainsKey(key);
        #endregion // Indexing by `Index`

        #region Indexing by 'Key'
        public Dictionary<string, Record> RecordsByKey => _recordsByKey;
        private readonly Dictionary<string, Record> _recordsByKey = new Dictionary<string, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByKey(string key)
        {
            if (!TryGetByKey(key, out Record record))
                throw new SheetManException($"There is no record in table `Localization` that corresponds to field `Key` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByKey(string key, out Record result) => _recordsByKey.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsKey(string key) => _recordsByKey.ContainsKey(key);
        #endregion // Indexing by `Key`

        /// <summary>
        /// Read a table from specified file.
        /// </summary>
        public async Task ReadAsync(string filename)
        {
            var bytes = await Tables.ReadAllBytesAsync(filename);
            LiteBinaryReader reader = new LiteBinaryReader(bytes);
            await ReadAsync(reader);
        }

        /// <summary>
        /// Read a table from specified reader.
        /// </summary>
        public async Task ReadAsync(LiteBinaryReader reader)
        {
            uint version = 0;
            reader.Read(out version);

            int count = reader.ReadCounter32();
            for (int i = 0; i < count; i++)
            {
                var record = new Record();
                await record.ReadAsync(reader);
                _records.Add(record);
            }

            // Additional index mapping
            foreach (var record in _records)
                _recordsByIndex.Add(record.Index, record);
            foreach (var record in _records)
                _recordsByKey.Add(record.Key, record);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToStringHelper.ToString(_records, sb);
            return sb.ToString();
        }
    }
    #endregion

    #region Template1Table
    /// <summary>
    /// 기본 템플릿 테이블입니다.
    ///
    /// 새로 테이블을 만드실때 이 템플릿을 기반으로 작성하세요.
    /// </summary>
    [System.Serializable]
    public partial class Template1Table
    {
        #region Record
        [System.Serializable]
        public partial class Record
        {
            #region Fields
            /// <summary>
            /// 인덱스 필드입니다.
            /// </summary>
            public int Index => _index;
            private int _index;

            /// <summary>
            /// 키 필드입니다.
            /// </summary>
            public string Key => _key;
            private string _key;

            /// <summary>
            /// 값 필드입니다.
            /// </summary>
            public bool Value => _value;
            private bool _value;
            #endregion

            #region Read record
            /// <summary>
            /// Reads a table data.
            /// </summary>
            public Task ReadAsync(LiteBinaryReader reader)
            {
                reader.Read(out _index);

                reader.Read(out _key);

                reader.Read(out _value);

                return Task.CompletedTask;
            }
            #endregion

            #region ToString
            public override string ToString()
            {
                var sb = new StringBuilder("{");
                sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
                sb.Append(",\"Key\":"); ToStringHelper.ToString(Key, sb);
                sb.Append(",\"Value\":"); ToStringHelper.ToString(Value, sb);
                sb.Append("}");
                return sb.ToString();
            }
            #endregion
        }
        #endregion

        /// <summary>
        /// All records.
        /// </summary>
        public List<Record> Records => _records;
        private readonly List<Record> _records = new List<Record>();

        #region Indexing by 'Index'
        public Dictionary<int, Record> RecordsByIndex => _recordsByIndex;
        private readonly Dictionary<int, Record> _recordsByIndex = new Dictionary<int, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByIndex(int key)
        {
            if (!TryGetByIndex(key, out Record record))
                throw new SheetManException($"There is no record in table `Template1` that corresponds to field `Index` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByIndex(int key, out Record result) => _recordsByIndex.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsIndex(int key) => _recordsByIndex.ContainsKey(key);
        #endregion // Indexing by `Index`

        #region Indexing by 'Key'
        public Dictionary<string, Record> RecordsByKey => _recordsByKey;
        private readonly Dictionary<string, Record> _recordsByKey = new Dictionary<string, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByKey(string key)
        {
            if (!TryGetByKey(key, out Record record))
                throw new SheetManException($"There is no record in table `Template1` that corresponds to field `Key` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByKey(string key, out Record result) => _recordsByKey.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsKey(string key) => _recordsByKey.ContainsKey(key);
        #endregion // Indexing by `Key`

        /// <summary>
        /// Read a table from specified file.
        /// </summary>
        public async Task ReadAsync(string filename)
        {
            var bytes = await Tables.ReadAllBytesAsync(filename);
            LiteBinaryReader reader = new LiteBinaryReader(bytes);
            await ReadAsync(reader);
        }

        /// <summary>
        /// Read a table from specified reader.
        /// </summary>
        public async Task ReadAsync(LiteBinaryReader reader)
        {
            uint version = 0;
            reader.Read(out version);

            int count = reader.ReadCounter32();
            for (int i = 0; i < count; i++)
            {
                var record = new Record();
                await record.ReadAsync(reader);
                _records.Add(record);
            }

            // Additional index mapping
            foreach (var record in _records)
                _recordsByIndex.Add(record.Index, record);
            foreach (var record in _records)
                _recordsByKey.Add(record.Key, record);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToStringHelper.ToString(_records, sb);
            return sb.ToString();
        }
    }
    #endregion

    #region SerialFieldsTable
    /// <summary>
    /// 로컬라이제이션 테스트 테이블입니다.
    /// </summary>
    [System.Serializable]
    public partial class SerialFieldsTable
    {
        #region Record
        [System.Serializable]
        public partial class Record
        {
            #region Fields
            /// <summary>
            /// 인덱스(필수)
            /// </summary>
            public int Index => _index;
            private int _index;

            /// <summary>
            /// 영문 텍스트1
            /// </summary>
            public string[] TextEnArray => _textEnArray;
            public const int TextEnArray_N = 2;
            private string[] _textEnArray = new string[TextEnArray_N];
            #endregion

            #region Read record
            /// <summary>
            /// Reads a table data.
            /// </summary>
            public Task ReadAsync(LiteBinaryReader reader)
            {
                reader.Read(out _index);

                for (int i = 0; i < TextEnArray_N; ++i)
                {
                    reader.Read(out _textEnArray[i]);
                }

                return Task.CompletedTask;
            }
            #endregion

            #region ToString
            public override string ToString()
            {
                var sb = new StringBuilder("{");
                sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
                sb.Append(",\"TextEnArray\":"); ToStringHelper.ToString(TextEnArray, sb);
                sb.Append("}");
                return sb.ToString();
            }
            #endregion
        }
        #endregion

        /// <summary>
        /// All records.
        /// </summary>
        public List<Record> Records => _records;
        private readonly List<Record> _records = new List<Record>();

        #region Indexing by 'Index'
        public Dictionary<int, Record> RecordsByIndex => _recordsByIndex;
        private readonly Dictionary<int, Record> _recordsByIndex = new Dictionary<int, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByIndex(int key)
        {
            if (!TryGetByIndex(key, out Record record))
                throw new SheetManException($"There is no record in table `SerialFields` that corresponds to field `Index` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByIndex(int key, out Record result) => _recordsByIndex.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsIndex(int key) => _recordsByIndex.ContainsKey(key);
        #endregion // Indexing by `Index`

        /// <summary>
        /// Read a table from specified file.
        /// </summary>
        public async Task ReadAsync(string filename)
        {
            var bytes = await Tables.ReadAllBytesAsync(filename);
            LiteBinaryReader reader = new LiteBinaryReader(bytes);
            await ReadAsync(reader);
        }

        /// <summary>
        /// Read a table from specified reader.
        /// </summary>
        public async Task ReadAsync(LiteBinaryReader reader)
        {
            uint version = 0;
            reader.Read(out version);

            int count = reader.ReadCounter32();
            for (int i = 0; i < count; i++)
            {
                var record = new Record();
                await record.ReadAsync(reader);
                _records.Add(record);
            }

            // Additional index mapping
            foreach (var record in _records)
                _recordsByIndex.Add(record.Index, record);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToStringHelper.ToString(_records, sb);
            return sb.ToString();
        }
    }
    #endregion

    #region AccountTable
    /// <summary>
    /// 회원 목록 테이블입니다.
    /// </summary>
    [System.Serializable]
    public partial class AccountTable
    {
        #region Record
        [System.Serializable]
        public partial class Record
        {
            #region Fields
            /// <summary>
            /// index
            /// </summary>
            public int Index => _index;
            private int _index;

            /// <summary>
            /// name
            /// </summary>
            public string Name => _name;
            private string _name;

            /// <summary>
            /// bank
            /// </summary>
            public BankAccountTable.Record Bank => _bank;
            private BankAccountTable.Record _bank;
            public void SetReference_Bank_INTERNAL(BankAccountTable.Record value) => _bank = value;
            public int _bank_BankAccount_index;
            public bool _bank_F = false;

            /// <summary>
            /// item1
            /// </summary>
            public int[] ItemArray => _itemArray;
            public const int ItemArray_N = 3;
            private int[] _itemArray = new int[ItemArray_N];
            #endregion

            #region Read record
            /// <summary>
            /// Reads a table data.
            /// </summary>
            public Task ReadAsync(LiteBinaryReader reader)
            {
                reader.Read(out _index);

                reader.Read(out _name);

                reader.Read(out _bank_BankAccount_index);
                _bank = default(BankAccountTable.Record); // will be assigned.
                _bank_F = false;

                for (int i = 0; i < ItemArray_N; ++i)
                {
                    reader.Read(out _itemArray[i]);
                }

                return Task.CompletedTask;
            }
            #endregion

            #region ToString
            public override string ToString()
            {
                var sb = new StringBuilder("{");
                sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
                sb.Append(",\"Name\":"); ToStringHelper.ToString(Name, sb);
                sb.Append(",\"Bank\":"); ToStringHelper.ToString(Bank, sb);
                sb.Append(",\"ItemArray\":"); ToStringHelper.ToString(ItemArray, sb);
                sb.Append("}");
                return sb.ToString();
            }
            #endregion
        }
        #endregion

        /// <summary>
        /// All records.
        /// </summary>
        public List<Record> Records => _records;
        private readonly List<Record> _records = new List<Record>();

        #region Indexing by 'Index'
        public Dictionary<int, Record> RecordsByIndex => _recordsByIndex;
        private readonly Dictionary<int, Record> _recordsByIndex = new Dictionary<int, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByIndex(int key)
        {
            if (!TryGetByIndex(key, out Record record))
                throw new SheetManException($"There is no record in table `Account` that corresponds to field `Index` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByIndex(int key, out Record result) => _recordsByIndex.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsIndex(int key) => _recordsByIndex.ContainsKey(key);
        #endregion // Indexing by `Index`

        /// <summary>
        /// Read a table from specified file.
        /// </summary>
        public async Task ReadAsync(string filename)
        {
            var bytes = await Tables.ReadAllBytesAsync(filename);
            LiteBinaryReader reader = new LiteBinaryReader(bytes);
            await ReadAsync(reader);
        }

        /// <summary>
        /// Read a table from specified reader.
        /// </summary>
        public async Task ReadAsync(LiteBinaryReader reader)
        {
            uint version = 0;
            reader.Read(out version);

            int count = reader.ReadCounter32();
            for (int i = 0; i < count; i++)
            {
                var record = new Record();
                await record.ReadAsync(reader);
                _records.Add(record);
            }

            // Additional index mapping
            foreach (var record in _records)
                _recordsByIndex.Add(record.Index, record);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToStringHelper.ToString(_records, sb);
            return sb.ToString();
        }
    }
    #endregion

    #region BankAccountTable
    /// <summary>
    /// 은행 계좌 정보입니다.
    /// </summary>
    [System.Serializable]
    public partial class BankAccountTable
    {
        #region Record
        [System.Serializable]
        public partial class Record
        {
            #region Fields
            /// <summary>
            /// index
            /// </summary>
            public int Index => _index;
            private int _index;

            /// <summary>
            /// *name
            /// </summary>
            public string Name => _name;
            private string _name;

            /// <summary>
            /// balance
            /// </summary>
            public int Balance => _balance;
            private int _balance;

            /// <summary>
            /// detail
            /// </summary>
            public BankAccountDetailTable.Record Detail => _detail;
            private BankAccountDetailTable.Record _detail;
            public void SetReference_Detail_INTERNAL(BankAccountDetailTable.Record value) => _detail = value;
            public int _detail_BankAccountDetail_index;
            public bool _detail_F = false;
            #endregion

            #region Read record
            /// <summary>
            /// Reads a table data.
            /// </summary>
            public Task ReadAsync(LiteBinaryReader reader)
            {
                reader.Read(out _index);

                reader.Read(out _name);

                reader.Read(out _balance);

                reader.Read(out _detail_BankAccountDetail_index);
                _detail = default(BankAccountDetailTable.Record); // will be assigned.
                _detail_F = false;

                return Task.CompletedTask;
            }
            #endregion

            #region ToString
            public override string ToString()
            {
                var sb = new StringBuilder("{");
                sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
                sb.Append(",\"Name\":"); ToStringHelper.ToString(Name, sb);
                sb.Append(",\"Balance\":"); ToStringHelper.ToString(Balance, sb);
                sb.Append(",\"Detail\":"); ToStringHelper.ToString(Detail, sb);
                sb.Append("}");
                return sb.ToString();
            }
            #endregion
        }
        #endregion

        /// <summary>
        /// All records.
        /// </summary>
        public List<Record> Records => _records;
        private readonly List<Record> _records = new List<Record>();

        #region Indexing by 'Index'
        public Dictionary<int, Record> RecordsByIndex => _recordsByIndex;
        private readonly Dictionary<int, Record> _recordsByIndex = new Dictionary<int, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByIndex(int key)
        {
            if (!TryGetByIndex(key, out Record record))
                throw new SheetManException($"There is no record in table `BankAccount` that corresponds to field `Index` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByIndex(int key, out Record result) => _recordsByIndex.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsIndex(int key) => _recordsByIndex.ContainsKey(key);
        #endregion // Indexing by `Index`

        #region Indexing by 'Name'
        public Dictionary<string, Record> RecordsByName => _recordsByName;
        private readonly Dictionary<string, Record> _recordsByName = new Dictionary<string, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByName(string key)
        {
            if (!TryGetByName(key, out Record record))
                throw new SheetManException($"There is no record in table `BankAccount` that corresponds to field `Name` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByName(string key, out Record result) => _recordsByName.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsName(string key) => _recordsByName.ContainsKey(key);
        #endregion // Indexing by `Name`

        /// <summary>
        /// Read a table from specified file.
        /// </summary>
        public async Task ReadAsync(string filename)
        {
            var bytes = await Tables.ReadAllBytesAsync(filename);
            LiteBinaryReader reader = new LiteBinaryReader(bytes);
            await ReadAsync(reader);
        }

        /// <summary>
        /// Read a table from specified reader.
        /// </summary>
        public async Task ReadAsync(LiteBinaryReader reader)
        {
            uint version = 0;
            reader.Read(out version);

            int count = reader.ReadCounter32();
            for (int i = 0; i < count; i++)
            {
                var record = new Record();
                await record.ReadAsync(reader);
                _records.Add(record);
            }

            // Additional index mapping
            foreach (var record in _records)
                _recordsByIndex.Add(record.Index, record);
            foreach (var record in _records)
                _recordsByName.Add(record.Name, record);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToStringHelper.ToString(_records, sb);
            return sb.ToString();
        }
    }
    #endregion

    #region BankAccountDetailTable
    /// <summary>
    /// 은행계좌 디테일 정보입니다.
    /// </summary>
    [System.Serializable]
    public partial class BankAccountDetailTable
    {
        #region Record
        [System.Serializable]
        public partial class Record
        {
            #region Fields
            /// <summary>
            /// index
            /// </summary>
            public int Index => _index;
            private int _index;

            /// <summary>
            /// name
            /// </summary>
            public string Name => _name;
            private string _name;

            /// <summary>
            /// value
            /// </summary>
            public double Value => _value;
            private double _value;
            #endregion

            #region Read record
            /// <summary>
            /// Reads a table data.
            /// </summary>
            public Task ReadAsync(LiteBinaryReader reader)
            {
                reader.Read(out _index);

                reader.Read(out _name);

                reader.Read(out _value);

                return Task.CompletedTask;
            }
            #endregion

            #region ToString
            public override string ToString()
            {
                var sb = new StringBuilder("{");
                sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
                sb.Append(",\"Name\":"); ToStringHelper.ToString(Name, sb);
                sb.Append(",\"Value\":"); ToStringHelper.ToString(Value, sb);
                sb.Append("}");
                return sb.ToString();
            }
            #endregion
        }
        #endregion

        /// <summary>
        /// All records.
        /// </summary>
        public List<Record> Records => _records;
        private readonly List<Record> _records = new List<Record>();

        #region Indexing by 'Index'
        public Dictionary<int, Record> RecordsByIndex => _recordsByIndex;
        private readonly Dictionary<int, Record> _recordsByIndex = new Dictionary<int, Record>();

        /// <summary>
        /// Gets the value associated with the specified key. throw SheetManException if not found.
        /// </summary>
        public Record GetByIndex(int key)
        {
            if (!TryGetByIndex(key, out Record record))
                throw new SheetManException($"There is no record in table `BankAccountDetail` that corresponds to field `Index` value {key}");

            return record;
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        public bool TryGetByIndex(int key, out Record result) => _recordsByIndex.TryGetValue(key, out result);

        /// <summary>
        /// Determines whether the table contains the specified key.
        /// </summary>
        public bool ContainsIndex(int key) => _recordsByIndex.ContainsKey(key);
        #endregion // Indexing by `Index`

        /// <summary>
        /// Read a table from specified file.
        /// </summary>
        public async Task ReadAsync(string filename)
        {
            var bytes = await Tables.ReadAllBytesAsync(filename);
            LiteBinaryReader reader = new LiteBinaryReader(bytes);
            await ReadAsync(reader);
        }

        /// <summary>
        /// Read a table from specified reader.
        /// </summary>
        public async Task ReadAsync(LiteBinaryReader reader)
        {
            uint version = 0;
            reader.Read(out version);

            int count = reader.ReadCounter32();
            for (int i = 0; i < count; i++)
            {
                var record = new Record();
                await record.ReadAsync(reader);
                _records.Add(record);
            }

            // Additional index mapping
            foreach (var record in _records)
                _recordsByIndex.Add(record.Index, record);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToStringHelper.ToString(_records, sb);
            return sb.ToString();
        }
    }
    #endregion

    #region Enums
    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=556509414&range=B3
    /// <summary>
    /// 전투 상태에 관련된 enum 정의입니다.
    /// </summary>
    public enum BattleState
    {
        /// <summary>
        /// None (automatically inserted by SheetMan)
        /// </summary>
        None = 0,
        /// <summary>
        /// 대기
        /// </summary>
        Idle = 101,
        /// <summary>
        /// 이동
        /// </summary>
        Move = 102,
        /// <summary>
        /// 공격
        /// </summary>
        Attack = 103,
        /// <summary>
        /// 스킬
        /// </summary>
        Skill = 111,
        /// <summary>
        /// 캐스팅
        /// </summary>
        Cast = 112,
        /// <summary>
        /// 사망
        /// </summary>
        Dead = 199,
        /// <summary>
        /// 피격
        /// </summary>
        Hit = 201,
        /// <summary>
        /// 기절
        /// </summary>
        Stun = 202,
        /// <summary>
        /// 밀려남
        /// </summary>
        Knockback = 203,
        /// <summary>
        /// 중독
        /// </summary>
        Poison = 301
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct BattleStateComparer : IEqualityComparer<BattleState>
    {
        public bool Equals(BattleState x, BattleState y)
        {
            return x == y;
        }

        public int GetHashCode(BattleState obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=918996371&range=B21
    /// <summary>
    /// 전투 상태에 관련된 enum 정의입니다.
    /// </summary>
    public enum ValueType
    {
        /// <summary>
        /// 알수 없는 타입
        /// </summary>
        None = 1,
        /// <summary>
        /// 32 bits integer
        /// </summary>
        Int = 2,
        /// <summary>
        /// 64 bits integer
        /// </summary>
        Bigint = 3,
        /// <summary>
        /// boolean
        /// </summary>
        Boolean = 4,
        /// <summary>
        /// floating point number
        /// </summary>
        Float = 5,
        /// <summary>
        /// double precise floating point number
        /// </summary>
        Double = 6,
        /// <summary>
        /// datetime
        /// </summary>
        Datetime = 7,
        /// <summary>
        /// timespan
        /// </summary>
        Timespan = 8,
        /// <summary>
        /// uuid
        /// </summary>
        Uuid = 9,
        /// <summary>
        /// enum
        /// </summary>
        Enum = 10,
        /// <summary>
        /// foregin table record
        /// </summary>
        Foreign = 11
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct ValueTypeComparer : IEqualityComparer<ValueType>
    {
        public bool Equals(ValueType x, ValueType y)
        {
            return x == y;
        }

        public int GetHashCode(ValueType obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1402776195&range=B2
    /// <summary>
    /// 전투 상태에 관련된 enum 정의입니다. #1
    /// </summary>
    public enum BattleState1
    {
        /// <summary>
        /// None (automatically inserted by SheetMan)
        /// </summary>
        None = 0,
        /// <summary>
        /// 대기
        /// </summary>
        Idle = 101,
        /// <summary>
        /// 이동
        /// </summary>
        Move = 102,
        /// <summary>
        /// 공격
        /// </summary>
        Attack = 103,
        /// <summary>
        /// 스킬
        /// </summary>
        Skill = 111,
        /// <summary>
        /// 캐스팅
        /// </summary>
        Cast = 112,
        /// <summary>
        /// 사망
        /// </summary>
        Dead = 199,
        /// <summary>
        /// 피격
        /// </summary>
        Hit = 201,
        /// <summary>
        /// 기절
        /// </summary>
        Stun = 202,
        /// <summary>
        /// 밀려남
        /// </summary>
        Knockback = 203
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct BattleState1Comparer : IEqualityComparer<BattleState1>
    {
        public bool Equals(BattleState1 x, BattleState1 y)
        {
            return x == y;
        }

        public int GetHashCode(BattleState1 obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1402776195&range=E2
    /// <summary>
    /// 전투 상태에 관련된 enum 정의입니다. #2
    /// </summary>
    public enum BattleState2
    {
        /// <summary>
        /// None (automatically inserted by SheetMan)
        /// </summary>
        None = 0,
        /// <summary>
        /// 대기
        /// </summary>
        Idle = 101,
        /// <summary>
        /// 이동
        /// </summary>
        Move = 102,
        /// <summary>
        /// 공격
        /// </summary>
        Attack = 103,
        /// <summary>
        /// 스킬
        /// </summary>
        Skill = 111,
        /// <summary>
        /// 캐스팅
        /// </summary>
        Cast = 112,
        /// <summary>
        /// 사망
        /// </summary>
        Dead = 199,
        /// <summary>
        /// 피격
        /// </summary>
        Hit = 201,
        /// <summary>
        /// 기절
        /// </summary>
        Stun = 202,
        /// <summary>
        /// 밀려남
        /// </summary>
        Knockback = 203
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct BattleState2Comparer : IEqualityComparer<BattleState2>
    {
        public bool Equals(BattleState2 x, BattleState2 y)
        {
            return x == y;
        }

        public int GetHashCode(BattleState2 obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1402776195&range=B14
    /// <summary>
    /// 전투 상태에 관련된 enum 정의입니다. #3
    /// </summary>
    public enum BattleState3
    {
        /// <summary>
        /// None (automatically inserted by SheetMan)
        /// </summary>
        None = 0,
        /// <summary>
        /// 대기
        /// </summary>
        Idle = 101,
        /// <summary>
        /// 이동
        /// </summary>
        Move = 102,
        /// <summary>
        /// 공격
        /// </summary>
        Attack = 103,
        /// <summary>
        /// 스킬
        /// </summary>
        Skill = 111,
        /// <summary>
        /// 캐스팅
        /// </summary>
        Cast = 112,
        /// <summary>
        /// 사망
        /// </summary>
        Dead = 199,
        /// <summary>
        /// 피격
        /// </summary>
        Hit = 201,
        /// <summary>
        /// 기절
        /// </summary>
        Stun = 202,
        /// <summary>
        /// 밀려남
        /// </summary>
        Knockback = 203
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct BattleState3Comparer : IEqualityComparer<BattleState3>
    {
        public bool Equals(BattleState3 x, BattleState3 y)
        {
            return x == y;
        }

        public int GetHashCode(BattleState3 obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1402776195&range=E14
    /// <summary>
    /// 전투 상태에 관련된 enum 정의입니다. #4
    /// </summary>
    public enum BattleState4
    {
        /// <summary>
        /// None (automatically inserted by SheetMan)
        /// </summary>
        None = 0,
        /// <summary>
        /// 대기
        /// </summary>
        Idle = 101,
        /// <summary>
        /// 이동
        /// </summary>
        Move = 102,
        /// <summary>
        /// 공격
        /// </summary>
        Attack = 103,
        /// <summary>
        /// 스킬
        /// </summary>
        Skill = 111,
        /// <summary>
        /// 캐스팅
        /// </summary>
        Cast = 112,
        /// <summary>
        /// 사망
        /// </summary>
        Dead = 199,
        /// <summary>
        /// 피격
        /// </summary>
        Hit = 201,
        /// <summary>
        /// 기절
        /// </summary>
        Stun = 202,
        /// <summary>
        /// 밀려남
        /// </summary>
        Knockback = 203
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct BattleState4Comparer : IEqualityComparer<BattleState4>
    {
        public bool Equals(BattleState4 x, BattleState4 y)
        {
            return x == y;
        }

        public int GetHashCode(BattleState4 obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=B2
    /// <summary>
    /// 캐릭터 레벨업 시 제공할 캐릭터 어빌리티
    /// </summary>
    public enum AbilityEffect
    {
        /// <summary>
        /// 최대 체력 % 단위로 증가
        /// </summary>
        MaxHpRatio = 0,
        /// <summary>
        /// 체력 회복 용도로 쓰이게 될 효과
        /// </summary>
        CurrentHpValue = 1,
        /// <summary>
        /// 캐릭터 기본 공격력 % 단위로 증가
        /// </summary>
        AtkRatio = 2,
        /// <summary>
        /// 캐릭터 기본 공격의 크리티컬 확률 % 단위로 증가
        /// </summary>
        CriRatio = 3,
        /// <summary>
        /// 캐릭터 기본 공격의 크리티컬 데미지 % 단위로 증가
        /// </summary>
        CriDmg = 4,
        /// <summary>
        /// 캐릭터 기본 공격 공격속도 % 단위로 증가
        /// </summary>
        AtkSpdRatio = 5,
        /// <summary>
        /// 최대 마나량 % 단위로 증가
        /// </summary>
        MaxMana = 6,
        /// <summary>
        /// 마나 충전 속도 % 단위로 증가
        /// </summary>
        RefillMana = 7,
        /// <summary>
        /// 캐릭터의 기본 공격이 1회 증가하여 연속으로 공격
        /// </summary>
        AtkCnt = 8,
        /// <summary>
        /// 명중한 투사체가 근처의 적에게 1회 더 튕겨나갑니다.
        /// </summary>
        ReactionCnt = 9,
        /// <summary>
        /// 적에게 명중한 투사체가 적을 뚫고 지나갑니다.
        /// </summary>
        PenetRatio = 10,
        /// <summary>
        /// 명중한 투사체가 폭발하여 주변의 적에게 추가 데미지를 입힙니다.
        /// </summary>
        Explosion = 11
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct AbilityEffectComparer : IEqualityComparer<AbilityEffect>
    {
        public bool Equals(AbilityEffect x, AbilityEffect y)
        {
            return x == y;
        }

        public int GetHashCode(AbilityEffect obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=F2
    /// <summary>
    /// 인게임 등장 아이템 Type 정의
    /// </summary>
    public enum ItemType
    {
        /// <summary>
        /// HP 포션
        /// </summary>
        PotionHp = 0,
        /// <summary>
        /// Mana 포션
        /// </summary>
        PotionMana = 1,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct ItemTypeComparer : IEqualityComparer<ItemType>
    {
        public bool Equals(ItemType x, ItemType y)
        {
            return x == y;
        }

        public int GetHashCode(ItemType obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J2
    /// <summary>
    /// 스킬 발동 타입
    /// </summary>
    public enum SkillActivateType
    {
        /// <summary>
        /// 시전 즉시 데미지
        /// </summary>
        DirectDmg = 0,
        /// <summary>
        /// 시전 즉시 보조스킬 효과 적용
        /// </summary>
        DirectAssist = 1,
        /// <summary>
        /// 시전 즉시 소환
        /// </summary>
        DirectSummon = 2,
        /// <summary>
        /// 일정 시간 경과 시점에서 데미지
        /// </summary>
        DelayDmg = 3,
        /// <summary>
        /// 일정 시간 경과 시점에서 보조스킬 효과 적용
        /// </summary>
        DelayAssist = 4,
        /// <summary>
        /// 일정 시간 경과 시점에서 소환
        /// </summary>
        DelaySummon = 5,
        /// <summary>
        /// 각각의 대상에게 시전 시간부터 일정 시간동안 효과 유지되는 보조 효과
        /// </summary>
        DurationTimeAssist = 6,
        /// <summary>
        /// 발동 영역 위치에
        /// 시전 시간부터 일정 시간동안
        /// Tick마다 데미지
        /// </summary>
        DurationTimeDmg = 7,
        /// <summary>
        /// 발동 영역 위치에
        /// 시전 시간부터 일정 시간동안 효과 유지
        /// </summary>
        Tile = 8,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct SkillActivateTypeComparer : IEqualityComparer<SkillActivateType>
    {
        public bool Equals(SkillActivateType x, SkillActivateType y)
        {
            return x == y;
        }

        public int GetHashCode(SkillActivateType obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=F9
    /// <summary>
    /// 아이템 / 스킬 등의 효과 대상 Type 정의
    /// </summary>
    public enum TargetEffect
    {
        /// <summary>
        /// HP +d%
        /// </summary>
        HPRatio = 0,
        /// <summary>
        /// HP +n
        /// </summary>
        HPValue = 1,
        /// <summary>
        /// Mana +d%
        /// </summary>
        ManaRatio = 2,
        /// <summary>
        /// Mana +n
        /// </summary>
        ManaValue = 3,
        /// <summary>
        /// 공격력 +d%
        /// </summary>
        AtkRatio = 4
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct TargetEffectComparer : IEqualityComparer<TargetEffect>
    {
        public bool Equals(TargetEffect x, TargetEffect y)
        {
            return x == y;
        }

        public int GetHashCode(TargetEffect obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J16
    /// <summary>
    /// 인게임 등장 마나 스킬의 적용 스테이터스 Type 정의
    /// </summary>
    public enum ManaSkillEffectType
    {
        /// <summary>
        /// 최대 마나량
        /// </summary>
        MaxMana = 0,
        /// <summary>
        /// 마나 충전 속도
        /// </summary>
        RefillMana = 1,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct ManaSkillEffectTypeComparer : IEqualityComparer<ManaSkillEffectType>
    {
        public bool Equals(ManaSkillEffectType x, ManaSkillEffectType y)
        {
            return x == y;
        }

        public int GetHashCode(ManaSkillEffectType obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=B22
    /// <summary>
    /// 스킬 효과 적용 비율을 정의합니다.
    /// </summary>
    public enum SkillEffect
    {
        /// <summary>
        /// 체력 : 최대 체력 %
        /// - 모든 %(ratio) 수치는 우선 합산 처리 후 마지막에 대상에 x 적용
        /// </summary>
        MaxHpRatio = 0,
        /// <summary>
        /// 체력 : 최대 체력 Value
        /// </summary>
        MaxHpValue = 1,
        /// <summary>
        /// 체력 : 현재 체력 %
        /// </summary>
        CurHpRatio = 2,
        /// <summary>
        /// 체력 : 현재 체력 Value
        /// </summary>
        CurHpValue = 3,
        /// <summary>
        /// 데미지 : 즉시 데미지 Value
        /// </summary>
        DmgValue = 4,
        /// <summary>
        /// 공격력 : 공격력 %
        /// - 모든 %(ratio) 수치는 우선 합산 처리 후 마지막에 대상에 x 적용
        /// </summary>
        AtkRatio = 5,
        /// <summary>
        /// 공격력 : 공격력 Value.
        /// </summary>
        AtkValue = 6,
        /// <summary>
        /// 백분율 : 캐릭터 기본 공격 크리티컬 발동 %
        /// - 모든 %(ratio) 수치는 우선 합산 처리 후 마지막에 대상에 x 적용
        /// </summary>
        CriticalPercentageRatio = 7,
        /// <summary>
        /// 백분율 : 캐릭터 기본 공격 크리티컬 데미지 %
        /// - 모든 %(ratio) 수치는 우선 합산 처리 후 마지막에 대상에 x 적용
        /// </summary>
        CriticalDamageRatio = 8,
        /// <summary>
        /// 백분율 : 캐릭터 기본 공격속도 %
        /// </summary>
        AtkSpdRatio = 9,
        /// <summary>
        /// 마나 : 최대 마나량 Value
        /// </summary>
        MaxManaValue = 10,
        /// <summary>
        /// 백분율 : 마나 충전 속도 %
        /// - 모든 %(ratio) 수치는 우선 합산 처리 후 마지막에 대상에 x 적용
        /// </summary>
        RefillManaRatio = 11,
        /// <summary>
        /// 공격 횟수 : 캐릭터의 기본 공격의 공격 횟수 Value
        /// </summary>
        AtkCnt = 12,
        /// <summary>
        /// 튕김 횟수 : 투사체 다른 적을 향해 튕김 횟수 Value
        /// </summary>
        ReactionCnt = 13,
        /// <summary>
        /// 관통 횟수 : 투사체 관통 최대 횟수 Value
        /// </summary>
        PenetRatioCnt = 14,
        /// <summary>
        /// 데미지 : 명중한 투사체가 폭발하여 주변의 적에게 추가 데미지 Value
        /// </summary>
        ExplosionDmg = 15,
        /// <summary>
        /// 데미지 : 일정 시간 적이 Tick당 받는 데미지 Value
        /// </summary>
        BleedingDmg = 16,
        /// <summary>
        /// 투사체 개수 : 기본 공격 시 발사되는 투사체 개수 Value
        /// </summary>
        ShotCnt = 17,
        /// <summary>
        /// 시간(s) :
        /// - 대상 빙결 처리 되는 Value(sec)
        /// - 빙결 상태에서는 모든 피해를 받지 않으며, 캐스팅 / 시전 중이던 공격 취소 X
        /// </summary>
        FreezeTimeSec = 18,
        /// <summary>
        /// 시간(s) :
        /// - 대상 스턴 처리 되는 Value(sec)
        /// - 스턴 상태에서는 모든 피해를 그대로 받으며, 캐스팅 / 시전 중이던 공격 취소
        /// </summary>
        StunTimeSec = 19,
        /// <summary>
        /// 레벨 : 직전에 사용한 스킬 카드를 1회 더 사용 시 본래 레벨에 추가로 붙는 레벨 Value
        /// - 기본적으로 사용 카드의 레벨은 반사경의 레벨로 적용됨
        /// </summary>
        OneMoreUseExtraLv = 20,
        /// <summary>
        /// 마나 : 직전에 사용한 스킬 카드를 1회 더 사용 시 본래 코스트에 추가로 붙는 마냐 Value
        /// </summary>
        OneMoreUseExtraCost = 21
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct SkillEffectComparer : IEqualityComparer<SkillEffect>
    {
        public bool Equals(SkillEffect x, SkillEffect y)
        {
            return x == y;
        }

        public int GetHashCode(SkillEffect obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J23
    /// <summary>
    /// 장비 아이템 카테고리 정의
    /// </summary>
    public enum Equipment
    {
        /// <summary>
        /// 장비_무기
        /// </summary>
        EquipWeapon = 0,
        /// <summary>
        /// 장비_갑옷
        /// </summary>
        EquipArmor = 1,
        /// <summary>
        /// 장비_반지_왼쪽
        /// </summary>
        EquipRingL = 2,
        /// <summary>
        /// 장비_반지_오른쪽
        /// </summary>
        EquipRingR = 3,
        /// <summary>
        /// 신발
        /// </summary>
        EquipShoes = 4,
        /// <summary>
        /// 목걸이
        /// </summary>
        EquipNecklass = 5,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct EquipmentComparer : IEqualityComparer<Equipment>
    {
        public bool Equals(Equipment x, Equipment y)
        {
            return x == y;
        }

        public int GetHashCode(Equipment obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=F33
    /// <summary>
    /// 인게임 등장 스킬 Type 정의
    /// </summary>
    public enum SkillIndicatorType
    {
        /// <summary>
        /// 고정 타겟(사용자 본인)
        /// </summary>
        Self = 0,
        /// <summary>
        /// 방향 지정
        /// </summary>
        Direction = 1,
        /// <summary>
        /// 위치 지정
        /// </summary>
        Position = 2,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct SkillIndicatorTypeComparer : IEqualityComparer<SkillIndicatorType>
    {
        public bool Equals(SkillIndicatorType x, SkillIndicatorType y)
        {
            return x == y;
        }

        public int GetHashCode(SkillIndicatorType obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J34
    /// <summary>
    /// 장비 아이템 카테고리 정의
    /// </summary>
    public enum EquipmentSlot
    {
        /// <summary>
        /// 장비_무기
        /// </summary>
        EquipWeapon = 0,
        /// <summary>
        /// 장비_갑옷
        /// </summary>
        EquipArmor = 1,
        /// <summary>
        /// 장비_반지_왼쪽
        /// </summary>
        EquipRingL = 2,
        /// <summary>
        /// 장비_반지_오른쪽
        /// </summary>
        EquipRingR = 3,
        /// <summary>
        /// 신발
        /// </summary>
        EquipShoes = 4,
        /// <summary>
        /// 목걸이
        /// </summary>
        EquipNecklass = 5,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct EquipmentSlotComparer : IEqualityComparer<EquipmentSlot>
    {
        public bool Equals(EquipmentSlot x, EquipmentSlot y)
        {
            return x == y;
        }

        public int GetHashCode(EquipmentSlot obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=F42
    /// <summary>
    /// 공격 가능한 moveType의 정의
    /// </summary>
    public enum AttackTarget
    {
        /// <summary>
        /// 공중/지상 모두
        /// </summary>
        All = 0,
        /// <summary>
        /// 공중만
        /// </summary>
        Air = 1,
        /// <summary>
        /// 지상만
        /// </summary>
        Ground = 2,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct AttackTargetComparer : IEqualityComparer<AttackTarget>
    {
        public bool Equals(AttackTarget x, AttackTarget y)
        {
            return x == y;
        }

        public int GetHashCode(AttackTarget obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J45
    /// <summary>
    /// 아이템 / 스킬 등의 효과 대상 Type 정의
    /// </summary>
    public enum ItemRarity
    {
        /// <summary>
        /// 일반(1단계)
        /// </summary>
        Common = 0,
        /// <summary>
        /// 희귀(2단계)
        /// </summary>
        Rare = 1,
        /// <summary>
        /// 영웅(3단계)
        /// </summary>
        Hero = 2,
        /// <summary>
        /// 전설(4단계)
        /// </summary>
        Legend = 3,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct ItemRarityComparer : IEqualityComparer<ItemRarity>
    {
        public bool Equals(ItemRarity x, ItemRarity y)
        {
            return x == y;
        }

        public int GetHashCode(ItemRarity obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=F50
    /// <summary>
    /// 공격 대상을 선택하는 규칙들의 정의입니다.
    /// </summary>
    public enum TargetRule
    {
        /// <summary>
        /// 범위 내 가장 가까운 적
        /// </summary>
        EnemyNear = 0,
        /// <summary>
        /// 범위 내 가장 먼 적
        /// </summary>
        EnemyFar = 1,
        /// <summary>
        /// 범위 내 적 중 무작위
        /// </summary>
        EnemyRandom = 2,
        /// <summary>
        /// 시전자 본인
        /// </summary>
        Self = 3,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct TargetRuleComparer : IEqualityComparer<TargetRule>
    {
        public bool Equals(TargetRule x, TargetRule y)
        {
            return x == y;
        }

        public int GetHashCode(TargetRule obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J54
    /// <summary>
    /// 스킬을 대상에 적용할때의 옵션입니다.
    /// </summary>
    public enum SkillIntoTarget
    {
        /// <summary>
        /// 유저가 소환할 수 있는 소환 대상
        /// 몬스터의 특정 항목
        /// </summary>
        Monster = 0,
        /// <summary>
        /// 유저가 사용 가능한 액티브
        /// 스킬의 특정 항목
        /// </summary>
        SkillActive = 1,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct SkillIntoTargetComparer : IEqualityComparer<SkillIntoTarget>
    {
        public bool Equals(SkillIntoTarget x, SkillIntoTarget y)
        {
            return x == y;
        }

        public int GetHashCode(SkillIntoTarget obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=F59
    /// <summary>
    /// 스킬이 적용될 대상
    /// </summary>
    public enum SkillTarget
    {
        /// <summary>
        /// 유저 캐릭터만을 대상
        /// </summary>
        CharOnly = 0,
        /// <summary>
        /// 유저 캐릭터 및 소환수
        /// </summary>
        Allience = 1,
        /// <summary>
        /// 적 Only
        /// </summary>
        EnemyOnly = 2
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct SkillTargetComparer : IEqualityComparer<SkillTarget>
    {
        public bool Equals(SkillTarget x, SkillTarget y)
        {
            return x == y;
        }

        public int GetHashCode(SkillTarget obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J61
    /// <summary>
    /// 어빌리티 캐스팅
    /// </summary>
    public enum AbilityCast
    {
        /// <summary>
        /// 즉시 적용
        /// </summary>
        Instant = 0,
        /// <summary>
        /// 소환 스킬 사용 시 적용
        /// </summary>
        Summon = 1,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct AbilityCastComparer : IEqualityComparer<AbilityCast>
    {
        public bool Equals(AbilityCast x, AbilityCast y)
        {
            return x == y;
        }

        public int GetHashCode(AbilityCast obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=B67
    /// <summary>
    /// 스킬 타입에 대한 정의입니다.
    /// </summary>
    public enum SkillType
    {
        /// <summary>
        /// 소환 스킬 이외의 액티브 스킬
        ///  - 사용 시 즉시 발동
        /// </summary>
        SkillActive = 0,
        /// <summary>
        /// 소환 스킬
        /// - 사용 시 즉시 발동
        /// </summary>
        SkillSummon = 1,
        /// <summary>
        /// 기본 공격
        /// - 사용 시 즉시 발동
        /// </summary>
        NormalAttack = 2,
        /// <summary>
        /// 상태에 따른 스킬
        /// - 상태 조건 따라 발동
        /// </summary>
        SkillCondition = 3,
        /// <summary>
        /// 해당 개체 사망 시 발동
        /// </summary>
        SkillAfterDie = 4,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct SkillTypeComparer : IEqualityComparer<SkillType>
    {
        public bool Equals(SkillType x, SkillType y)
        {
            return x == y;
        }

        public int GetHashCode(SkillType obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J68
    /// <summary>
    /// 해당 몬스터의 이동 타입 정의
    /// </summary>
    public enum MoveType
    {
        /// <summary>
        /// 공중 이동
        /// </summary>
        Air = 0,
        /// <summary>
        /// 지상 이동
        /// </summary>
        Ground = 1,
        /// <summary>
        /// 공중 / 지상을 아우르는 덩치
        /// </summary>
        All = 2,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct MoveTypeComparer : IEqualityComparer<MoveType>
    {
        public bool Equals(MoveType x, MoveType y)
        {
            return x == y;
        }

        public int GetHashCode(MoveType obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=F72
    /// <summary>
    /// 효과가 발동할 조건입니다.
    /// </summary>
    public enum EffectCondition
    {
        /// <summary>
        /// 대상의 HP가 {0}% 이하
        /// </summary>
        UnderCurrentHP = 0,
        /// <summary>
        /// 대상의 HP가 {0}% 이상
        /// </summary>
        MoreCurrentHP = 0,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct EffectConditionComparer : IEqualityComparer<EffectCondition>
    {
        public bool Equals(EffectCondition x, EffectCondition y)
        {
            return x == y;
        }

        public int GetHashCode(EffectCondition obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=J76
    /// <summary>
    /// 공격 유형입니다.
    /// </summary>
    public enum AttackType
    {
        /// <summary>
        /// 단일 타겟 지정
        /// </summary>
        Target = 0,
        /// <summary>
        /// 영역 공격
        /// </summary>
        Area = 1,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct AttackTypeComparer : IEqualityComparer<AttackType>
    {
        public bool Equals(AttackType x, AttackType y)
        {
            return x == y;
        }

        public int GetHashCode(AttackType obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=B77
    /// <summary>
    /// 효과의 범주입니다.
    /// </summary>
    public enum EffectCategory
    {
        /// <summary>
        /// 주황(장판) 색상 스킬들을 대상으로 Equip 테이블 파라메터 중 EffectType에서 정의된 효과 적용
        /// </summary>
        SkillColorOrange = 0,
        /// <summary>
        /// 노랑(메즈,보조) 색상 스킬들을 대상으로 Equip 테이블 파라메터 중 EffectType에서 정의된 효과 적용
        /// </summary>
        SkillColorYellow = 1,
        /// <summary>
        /// 녹색(버프 / 디버프) 스킬들을 대상으로 Equip 테이블 파라메터 중 EffectType에서 정의된 효과 적용
        /// </summary>
        SkillColorGreen = 2,
        /// <summary>
        /// 파랑(소환) 색상 스킬들을 대상으로 EffectType에서 정의된 효과 적용
        /// </summary>
        SkillColorBlue = 3,
        /// <summary>
        /// 빨강(딜링) 색상 스킬들을 대상으로 EffectType에서 정의된 효과 적용
        /// </summary>
        SkillColorRed = 4,
        /// <summary>
        /// 검정(기타) 색상 스킬들을 대상으로 EffectType에서 정의된 효과 적용
        /// </summary>
        SkillColorBlack = 5,
        /// <summary>
        /// 사용 중인 유저 캐릭터의 스테이터스를 대상으로 EffectType에서 정의된 효과 적용
        /// </summary>
        CharStatus = 6,
        /// <summary>
        /// 사용 중인 유저 캐릭터의 어빌리티(패시브)를 대상으로 EffectType에서
        /// 정의된 효과 적용(Level++)
        /// </summary>
        CharAbility = 7,
        /// <summary>
        /// 색상과 관련 없이 모두
        /// </summary>
        AllSkill = 8,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct EffectCategoryComparer : IEqualityComparer<EffectCategory>
    {
        public bool Equals(EffectCategory x, EffectCategory y)
        {
            return x == y;
        }

        public int GetHashCode(EffectCategory obj)
        {
            return (int)obj;
        }
    }

    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1216928919&range=F79
    /// <summary>
    /// UI 텍스트에 표시할 스킬 색상값들입니다.
    /// </summary>
    public enum SkillColor
    {
        /// <summary>
        /// 타일
        /// </summary>
        Orange = 0,
        /// <summary>
        /// 메즈/보조
        /// </summary>
        Yellow = 1,
        /// <summary>
        /// 버프/디버프
        /// </summary>
        Green = 2,
        /// <summary>
        /// 소환
        /// </summary>
        Blue = 3,
        /// <summary>
        /// 데미지
        /// </summary>
        Red = 4,
        /// <summary>
        /// 반사경 등 번외 스킬
        /// </summary>
        Black = 5,
        /// <summary>
        /// none
        /// </summary>
        None = 99
    }

    /// <summary>
    /// Helper class for avoiding boxing as dictionary key.
    /// </summary>
    public struct SkillColorComparer : IEqualityComparer<SkillColor>
    {
        public bool Equals(SkillColor x, SkillColor y)
        {
            return x == y;
        }

        public int GetHashCode(SkillColor obj)
        {
            return (int)obj;
        }
    }
    #endregion

    #region Constants
    // Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=556509414&range=F3
    /// <summary>
    /// 공용으로 사용되는 상수 값들의 정의입니다.
    /// </summary>
    public static class Constants1
    {
        /// <summary>
        /// 소유 가능한 최대 아이템 갯수입니다.
        /// </summary>
        public static readonly int MaxItemCount { get; }
        /// <summary>
        /// 기본 쿨타임 값입니다.
        /// </summary>
        public static readonly float DefaultCooltime { get; }
        /// <summary>
        /// 최대 플레이어 갯수입니다.
        /// </summary>
        public static readonly int MaxPlayerCount { get; }
        /// <summary>
        /// 타임아웃 값입니다. (초)
        /// </summary>
        public static readonly float Timeout { get; }
        /// <summary>
        /// 기본 배틀 상태입니다.
        /// </summary>
        public static readonly global::StaticData.BattleState DefaultBattleState { get; }

        /// <summary>
        /// Static constructor for initialize static variables.
        /// </summary>
        static Constants1()
        {
            MaxItemCount = 100;
            DefaultCooltime = 22f;
            MaxPlayerCount = 1024;
            Timeout = 3.5f;
            DefaultBattleState = global::StaticData.BattleState.Idle;
        }
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Exception about SheetMan.
    /// </summary>
    public class SheetManException : System.Exception
    {
        public SheetManException()
        {
        }

        public SheetManException(string message) : base(message)
        {
        }

        public SheetManException(string message, System.Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Collection helper class.
    /// </summary>
    public static class CollectionsHelper
    {
        /// <summary>
        /// This will return true if the two collections are value-wise the same.
        /// If the collection contains a collection, the collections will be compared using this method.
        /// </summary>
        public static bool Equals(IEnumerable first, IEnumerable second)
        {
            if (first == null && second == null)
                return true;

            if (first == null || second == null)
                return false;

            var fiter = first.GetEnumerator();
            var siter = second.GetEnumerator();

            var fnext = fiter.MoveNext();
            var snext = siter.MoveNext();

            while (fnext && snext)
            {
                var fenum = fiter.Current as IEnumerable;
                var senum = siter.Current as IEnumerable;

                if (fenum != null && senum != null)
                {
                    if (!Equals(fenum, senum))
                        return false;
                }
                else if (fenum == null ^ senum == null)
                {
                    return false;
                }
                else if (!Equals(fiter.Current, siter.Current))
                {
                    return false;
                }

                fnext = fiter.MoveNext();
                snext = siter.MoveNext();
            }

            return fnext == snext;
        }

        /// <summary>
        /// This returns a hashcode based on the value of the enumerable.
        /// </summary>
        public static int GetHashCode(IEnumerable enumerable)
        {
            if (enumerable == null)
                return 0;

            int hashcode = 0;
            foreach (var item in enumerable)
            {
                int objHash = !(item is IEnumerable enumerableItem) ? item.GetHashCode() : GetHashCode(enumerableItem);

                unchecked
                {
                    hashcode = (hashcode * 397) ^ objHash;
                }
            }

            return hashcode;
        }
    }
    /// <summary>
    /// ToString helper class.
    /// </summary>
    public static class ToStringHelper
    {
        public static void ToString(object self, StringBuilder target, bool first = true)
        {
            if (!first)
                target.Append(", ");

            bool firstChild = true;
            if (self is string)
            {
                target.Append('"');
                target.Append(self);
                target.Append('"');
            }
            else if (self is IDictionary dictionary)
            {
                target.Append("{");
                foreach (DictionaryEntry pair in dictionary)
                {
                    if (firstChild)
                        firstChild = false;
                    else
                        target.Append(", ");

                    target.Append("{");
                    ToString(pair.Key, target, true);
                    target.Append(", ");
                    ToString(pair.Value, target, true);
                    target.Append("}");
                }
                target.Append("}");
            }
            else if (self is IEnumerable enumerable)
            {
                target.Append("[");
                foreach (var element in enumerable)
                {
                    ToString(element, target, firstChild);
                    firstChild = false;
                }
                target.Append("]");
            }
            else
            {
                target.Append(self);
            }
        }
    }
    #endregion

} // namespace StaticData
```
