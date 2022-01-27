using System;
using System.Collections.Generic;

namespace SheetMan.Models
{
    /// <summary>
    /// SerialFieldPattern
    /// </summary>
    public enum SerialFieldPattern
    {
        /// <summary>None</summary>
        None,

        /// <summary>순번이 끝에 오는 유형</summary>
        TrailingNumber,

        /// <summary>순번이 중간에 오는 유형</summary>
        MiddleNumber,
    }

    /// <summary>
    /// SerialField
    /// </summary>
    public class SerialField
    {
        /// <summary></summary>
        public string Name { get; set; } = "";

        /// <summary></summary>
        public List<Field> Fields { get; set; } = new List<Field>();

        /// <summary></summary>
        public string NamePart { get; set; } = "";

        /// <summary></summary>
        public SerialFieldPattern Pattern { get; set; } = SerialFieldPattern.None;

        // 한개의 엔트리일지라도 배열로 취급할지 여부.
        /// <summary></summary>
        public bool TreatAsArrayEvenIfSingleItem { get; set; } = false;

        /// <summary></summary>
        public bool IsIndexer => (Fields.Count > 0) && !IsArray && FirstField.Indexing; // 일단 배열은 인덱스 대상에서 제외함.

        /// <summary></summary>
        public bool IsRef => (Fields.Count > 0 ) ? Fields[0].IsRef : false;

        /// <summary></summary>
        public bool IsArray => Fields.Count > 1 || (Fields.Count == 1 && TreatAsArrayEvenIfSingleItem);

        /// <summary></summary>
        public ValueType Type => (Fields.Count > 0 ) ? Fields[0].Type : ValueType.None;

        /// <summary></summary>
        public TargetSide TargetSide => (Fields.Count > 0 ) ? Fields[0].TargetSide : TargetSide.Both;

        /// <summary></summary>
        public Field FirstField => (Fields.Count > 0) ? Fields[0] : null;
    }
}
