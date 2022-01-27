using Newtonsoft.Json;
using System.Collections.Generic;

namespace SheetMan.Models
{
    /// <summary>
    /// 변수 정의 셋
    /// </summary>
    public class VariableSet
    {
        /// <summary>
        /// 변수 정의
        /// </summary>
        public class Variable
        {
            /// <summary>변수가 정의된 위치</summary>
            [JsonIgnore]
            public Location Location { get; set; }

            /// <summary>변수 이름</summary>
            public string Name { get; set; }
            public string RawName { get; set; }

            /// <summary>변수 타입</summary>
            public string TypeName { get; set; }

            /// <summary>변수 타입</summary>
            public ValueType Type { get; set; }

            //TODO 파싱시에 알아서 변환된 값으로 가지고 있는게 좋으려나..
            //string 타입으로 하는게 좋은걸까?
            public string ValueString { get; set; }
            public object Value { get; set; }

            /// <summary>주석</summary>
            public string Comment { get; set; }
        }

        /// <summary>변수 셋이 정의된 위치</summary>
        public Location Location { get; set; }

        /// <summary></summary>
        public TargetSide TargetSide { get; set; }

        /// <summary>변수 셋 이름</summary>
        public string Name { get; set; }
        public string RawName { get; set; }

        /// <summary>정의된 변수 목록</summary>
        public List<Variable> Variables { get; set; } = new List<Variable>();

        /// <summary>주석</summary>
        public string Comment { get; set; }
    }
}
