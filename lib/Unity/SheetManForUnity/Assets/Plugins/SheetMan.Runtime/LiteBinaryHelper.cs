using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace SheetMan.Runtime
{
    public static class LiteBinaryHelper
    {
        #region Constants
        /// <summary>
        /// Maximum byte length of varint64
        /// </summary>
        public const int MaxVarint64Length = 10;

        /// <summary>
        /// Maximum byte length of varint32
        /// </summary>
        public const int MaxVarint32Length = 5;

        /// <summary>
        /// Maximum byte length of varint16
        /// </summary>
        public const int MaxVarint16Length = 3;

        /// <summary>
        /// Maximum byte length of varint8
        /// </summary>
        public const int MaxVarint8Length = 2;
        #endregion


        #region Varint
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Varint8(byte value)
        {
            if (value < (1u << 7))
                return 1;
            else
                return 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Varint16(ushort value)
        {
            if (value < (1u << 7))
                return 1;
            else if (value > (1u << 14))
                return 2;
            else
                return 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Varint32(uint value)
        {
            if (value < (1u << 7))
                return 1;
            else if (value < (1u << 14))
                return 2;
            else if (value < (1u << 21))
                return 3;
            else if (value < (1u << 28))
                return 4;
            else
                return 5;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Varint64(ulong value)
        {
            if (value < (1ul << 35)) // critical: Must use 1ul, not 1.
            {
                if (value < (1ul << 7))
                    return 1;
                else if (value < (1ul << 14))
                    return 2;
                else if (value < (1ul << 21))
                    return 3;
                else if (value < (1ul << 28))
                    return 4;
                else
                    return 5;
            }
            else
            {
                if (value < (1ul << 42))
                    return 6;
                else if (value < (1ul << 49))
                    return 7;
                else if (value < (1ul << 56))
                    return 8;
                else if (value < (1ul << 63))
                    return 9;
                else
                    return 10;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Varint8SignExtended(sbyte value)
        {
            if (value >= 0)
                return GetByteLength_Varint8((byte)value);
            else
                return MaxVarint64Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Varint16SignExtended(short value)
        {
            if (value >= 0)
                return GetByteLength_Varint16((ushort)value);
            else
                return MaxVarint64Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Varint32SignExtended(int value)
        {
            if (value >= 0)
                return GetByteLength_Varint32((uint)value);
            else
                return MaxVarint64Length;
        }
        #endregion


        #region Zigzag encoding
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ZigZagEncode8(sbyte n)
        {
            // Note: the right-shift must be arithmetic
            return (byte)((n << 1) ^ (n >> 7));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte ZigZagDecode8(byte n)
        {
            return (sbyte)((sbyte)(n >> 1) ^ -(sbyte)(n & 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ZigZagEncode16(short n)
        {
            // Note: the right-shift must be arithmetic
            return (ushort)((n << 1) ^ (n >> 15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ZigZagDecode16(ushort n)
        {
            return (short)((short)(n >> 1) ^ -(short)(n & 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ZigZagEncode32(int n)
        {
            // Note: the right-shift must be arithmetic
            return (uint)((n << 1) ^ (n >> 31));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ZigZagDecode32(uint n)
        {
            return (int)(n >> 1) ^ -(int)(n & 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ZigZagEncode64(long n)
        {
            // Note: the right-shift must be arithmetic
            return (ulong)((n << 1) ^ (n >> 63));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ZigZagDecode64(ulong n)
        {
            return (long)(n >> 1) ^ -(long)(n & 1);
        }
        #endregion


        #region Length prefixed
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_LengthPrefixed(int length)
        {
            return GetByteLength_Counter32(length) + length;
        }
        #endregion


        #region Counter32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_OptimalInt32(int value)
        {
            return GetByteLength_Varint32(ZigZagEncode32(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Counter32(int value)
        {
            return GetByteLength_OptimalInt32(value);
        }
        #endregion


        #region Counter64
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_OptimalInt64(long value)
        {
            return GetByteLength_Varint64(ZigZagEncode64(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Counter64(long value)
        {
            return GetByteLength_OptimalInt64(value);
        }
        #endregion


        #region Flex format helpers
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Int8(sbyte value)
        {
            return GetByteLength_Varint8SignExtended(value);
        }

        public static int GetByteLength_Int16(short value)
        {
            return GetByteLength_Varint16SignExtended(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Int32(int value)
        {
            return GetByteLength_Varint32SignExtended(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Int64(long value)
        {
            return GetByteLength_Varint64((ulong)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_UInt8(byte value)
        {
            return GetByteLength_Varint8(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_UInt16(ushort value)
        {
            return GetByteLength_Varint16(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_UInt32(uint value)
        {
            return GetByteLength_Varint32(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_UInt64(ulong value)
        {
            return GetByteLength_Varint64(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_SInt8(sbyte value)
        {
            return GetByteLength_Varint8(ZigZagEncode8(value));
        }

        public static int GetByteLength_SInt16(short value)
        {
            return GetByteLength_Varint16(ZigZagEncode16(value));
        }

        public static int GetByteLength_SInt32(int value)
        {
            return GetByteLength_Varint32(ZigZagEncode32(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_SInt64(long value)
        {
            return GetByteLength_Varint64(ZigZagEncode64(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Fixed8(byte value)
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Fixed16(ushort value)
        {
            return 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Fixed32(uint value)
        {
            return 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Fixed64(ulong value)
        {
            return 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_SFixed8(sbyte value)
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_SFixed16(short value)
        {
            return 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_SFixed32(int value)
        {
            return 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_SFixed64(long value)
        {
            return 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Float(float value)
        {
            return 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Double(double value)
        {
            return 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Bool(bool value)
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Enum<T>(T value) where T : struct
        {
            return GetByteLength_Varint32SignExtended(Convert.ToInt32(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_String(string value)
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            return GetByteLength_LengthPrefixed(byteCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_Bytes(byte[] value)
        {
            return GetByteLength_LengthPrefixed(value.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_DateTime(DateTime value)
        {
            return 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteLength_TimeSpan(TimeSpan value)
        {
            return 8;
        }

        public static int GetByteLength_Uuid(Guid value)
        {
            return 1 + 16; // = GetByteLength_LengthPrefixed(16);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int GetByteLength_Struct(IGeneratedStruct value)
        //{
        //    return GetByteLength_LengthPrefixed(value.GetByteLength());
        //}
        #endregion


        //#region Dynamic
        //// Note: To support different types of primitive types,
        //// Handle it internally as an object, and use box / unboxing and reflection,
        //// so the GetByteLength functions that deal with the primitive type in efficiency
        //// It is slow compared to.
        //public static int GetByteLengthDynamic<T>(T value)
        //{
        //    var valueDynamicType = DynamicType.GetDynamicType(typeof(T));
        //    return GetByteLengthDynamic<T>(value, valueDynamicType);
        //}
        //
        //public static int GetByteLengthDynamic<T>(T value, DynamicType valueDynamicType)
        //{
        //    return valueDynamicType.GetByteLength(value);
        //}
        //#endregion
    }
}
