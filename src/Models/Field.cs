using Newtonsoft.Json;
using System.Linq;

namespace SheetMan.Models
{
    /// <summary>
    /// Field
    /// </summary>
    public class Field
    {
        /// <summary>필드 이름이 정의된 시트 위치</summary>
        [JsonIgnore]
        public Location NameLocation { get; set; }
        /// <summary>필드 타입이 정의된 시트 위치</summary>
        [JsonIgnore]
        public Location TypeLocation { get; set; }
        [JsonIgnore]
        public Location DetailTypeLocation { get; set; }
        [JsonIgnore]
        public Location TargetSideLocation { get; set; }

        //TODO OwnerTable
        [JsonIgnore]
        public Table OwnerTable { get; set; }

        /// <summary></summary>
        public string RawName { get; set; }
        /// <summary>필드 이름</summary>
        public string Name { get; set; }

        /// <summary>Target side filtering option</summary>
        public TargetSide TargetSide { get; set; }

        /// <summary>필드 타입</summary>
        public string TypeName { get; set; }

        /// <summary>인덱스</summary>
        public int Index { get; set; }

        /// <summary>필드 타입</summary>
        public ValueType Type { get; set; }

        /// <summary>주석</summary>
        public string Comment { get; set; }

        /// <summary>인덱서로 사용되는지 여부</summary>
        [JsonIgnore]
        public bool Indexing { get; set; }

        /// <summary>참조 테이블</summary>
        public string RefTableName { get; set; }

        /// <summary>참조 필드</summary>
        public string RefFieldName { get; set; }

        /// <summary></summary>
        [JsonIgnore]
        public Table ResolvedRefTable { get; set; }
        /// <summary></summary>
        [JsonIgnore]
        public Field ResolvedRefField { get; set; }

        // A -> B -> C
        //   A_B_C
        [JsonIgnore]
        public string RefChainPath { get; set; }

        /// <summary></summary>
        [JsonIgnore]
        public bool IsRef => !string.IsNullOrEmpty(RefTableName);

        /// <summary></summary>
        [JsonIgnore]
        public bool IsArray
        {
            get
            {
                switch (Type)
                {
                    case ValueType.StringArray:
                    case ValueType.BoolArray:
                    case ValueType.Int32Array:
                    case ValueType.Int64Array:
                    case ValueType.FloatArray:
                    case ValueType.DoubleArray:
                    case ValueType.TimeSpanArray:
                    case ValueType.DateTimeArray:
                    case ValueType.EnumArray:
                    case ValueType.ForeignRecordArray:
                        return true;
                }

                return false;
            }
        }

        //TODO 이건 안불리는게 좋지 않을까?
        /// <summary></summary>
        [JsonIgnore]
        public Enum Enum
        {
            get
            {
                if (Type != ValueType.Enum)
                    throw new SheetManException("this type is not enum.");

                return Model.Current.GetEnum(TypeName, null);
            }
        }

        /// <summary></summary>
        public Enum EnumOrNull
        {
            get
            {
                if (Type != ValueType.Enum)
                    return null;

                return Model.Current.GetEnum(TypeName, null);
            }
        }

        //TODO DB field 특성을 기술할 수 있으면 좋을듯.
        //Length, Unique, Not NULL 등...
        /// <summary>Nullable?</summary>
        [JsonIgnore]
        public bool IsNullable { get; set; }

        /// <summary>필드 타입 길이(DB에 필드 길이)</summary>
        [JsonIgnore]
        public int Length { get; set; }
    }
}
