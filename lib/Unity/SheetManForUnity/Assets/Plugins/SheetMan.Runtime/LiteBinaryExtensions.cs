using System;
using System.Buffers;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SheetMan.Runtime
{
    public static class LiteFormatExtensions
    {
        #region SByte
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, sbyte value)
        {
            writer.WriteFixed8((byte)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out sbyte value)
        {
            if (reader.TryReadFixed8(out byte t))
            {
                value = (sbyte)t;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out sbyte value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Int16
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, short value)
        {
            writer.WriteFixed16((ushort)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out short value)
        {
            if (reader.TryReadFixed16(out ushort t))
            {
                value = (short)t;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out short value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Int32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, int value)
        {
            writer.WriteFixed32((uint)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out int value)
        {
            if (reader.TryReadFixed32(out uint t))
            {
                value = (int)t;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out int value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Int64
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, long value)
        {
            writer.WriteFixed64((uint)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out long value)
        {
            if (reader.TryReadFixed64(out ulong t))
            {
                value = (long)t;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out long value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Byte
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, byte value)
        {
            writer.WriteFixed8(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out byte value)
        {
            return reader.TryReadFixed8(out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out byte value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region UInt16
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, ushort value)
        {
            writer.WriteFixed16(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out ushort value)
        {
            return reader.TryReadFixed16(out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out ushort value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region UInt32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, uint value)
        {
            writer.WriteFixed32(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out uint value)
        {
            return reader.TryReadFixed32(out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out uint value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region UInt64
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, ulong value)
        {
            writer.WriteFixed64(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out ulong value)
        {
            return reader.TryReadFixed64(out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out ulong value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Bool
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, bool value)
        {
            writer.WriteFixed8(value ? (byte)1 : (byte)0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out bool value)
        {
            if (reader.TryReadFixed8(out byte t))
            {
                value = (t != 0);
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out bool value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Float
        [StructLayout(LayoutKind.Explicit)]
        private struct I2F
        {
            [FieldOffset(0)]
            public uint I;

            [FieldOffset(0)]
            public float F;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, float value)
        {
            I2F i2f = new I2F { F = value };
            writer.WriteFixed32(i2f.I);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out float value)
        {
            if (reader.TryReadFixed32(out uint i))
            {
                I2F i2f = new I2F { I = i };
                value = i2f.F;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out float value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Double
        [StructLayout(LayoutKind.Explicit)]
        private struct L2D
        {
            [FieldOffset(0)]
            public ulong L;

            [FieldOffset(0)]
            public double D;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, double value)
        {
            L2D l2d = new L2D { D = value };
            writer.WriteFixed64(l2d.L);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out double value)
        {
            if (reader.TryReadFixed64(out ulong l))
            {
                L2D l2d = new L2D { L = l };
                value = l2d.D;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out double value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region String
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Write(this LiteBinaryWriter writer, ReadOnlySpan<char> value)
        {
            // Begin
            int position = WriteStringBegin(writer, value.Length, out int bufferLength, out int predictedBodyOffset);

            fixed (char* valuePtr = value)
            fixed (byte* bufferPtr = writer.Data)
            {
                // Zero copy
                int encodedByteLength = Encoding.UTF8.GetBytes(valuePtr, value.Length, bufferPtr + position + predictedBodyOffset, bufferLength);

                // End
                WriteStringEnd(writer, bufferPtr + position, predictedBodyOffset, encodedByteLength);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Write(this LiteBinaryWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteCounter32(0);
                return;
            }

            //todo 변환을 위한 임시 할당을 제거할 수 있지 않을까?
            //최대 길이에 해당하는 버퍼를 잡고 실제 쓰여진 길이와 같을 경우와 아닐 경우등에
            //대해서 처리하면 될듯함.

            //TODO Fast version 문자열 write 코드는 버그가 있어서 잠시 막아둠.

//#if LEGACY_FORMATTING
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.WriteCounter32(bytes.Length);
            writer.WriteRawBytes(bytes);
//#else
//            // Begin
//            int position = WriteStringBegin(writer, value.Length, out int bufferLength, out int predictedBodyOffset);
//
//            fixed (char* valuePtr = value)
//            fixed (byte* bufferPtr = writer.Data)
//            {
//                // Zero copy conversion
//                int byteCount = Encoding.UTF8.GetBytes(valuePtr, value.Length, bufferPtr + position + predictedBodyOffset, bufferLength);
//
//                // End
//                WriteStringEnd(writer, bufferPtr + position, predictedBodyOffset, byteCount);
//            }
//#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int WriteStringBegin(LiteBinaryWriter writer, int characterLength, out int bufferLength, out int predictedBodyOffset)
        {
            // 우선 최대 버퍼 길이로 잡아둬야함.
            bufferLength = Encoding.UTF8.GetMaxByteCount(characterLength) + 5;
            int position = writer.EnsureCapacity(bufferLength);

            if (characterLength < 1u << 7)
            {
                predictedBodyOffset = 1;
            }
            else if (characterLength < 1u << 14)
            {
                predictedBodyOffset = 2;
            }
            else if (characterLength < 1u << 21)
            {
                predictedBodyOffset = 3;
            }
            else if (characterLength < 1u << 28)
            {
                predictedBodyOffset = 4;
            }
            else
            {
                predictedBodyOffset = 5;
            }

            return position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void WriteStringEnd(LiteBinaryWriter writer, byte* bufferPtr, int predictedBodyOffset, int encodedByteLength)
        {
            uint optimalByteLength = LiteBinaryHelper.ZigZagEncode32(encodedByteLength);

            if (encodedByteLength < 1u << 7)
            {
                if (predictedBodyOffset != 1)
                {
                    Memmove(bufferPtr + predictedBodyOffset, bufferPtr + 1, encodedByteLength, encodedByteLength);
                }

                // Length of UTF8 encoded string body
                bufferPtr[0] = (byte)optimalByteLength;

                writer.Advance(encodedByteLength + 1);
            }
            else if (encodedByteLength < 1u << 14)
            {
                if (predictedBodyOffset != 2)
                    Memmove(bufferPtr + predictedBodyOffset, bufferPtr + 2, encodedByteLength, encodedByteLength);

                // Length of UTF8 encoded string body
                bufferPtr[0] = (byte)(optimalByteLength | 0x80);
                bufferPtr[1] = (byte)((optimalByteLength >> 7));

                writer.Advance(encodedByteLength + 2);
            }
            else if (encodedByteLength < 1u << 21)
            {
                if (predictedBodyOffset != 3)
                    Memmove(bufferPtr + predictedBodyOffset, bufferPtr + 3, encodedByteLength, encodedByteLength);

                // Length of UTF8 encoded string body
                bufferPtr[0] = (byte)(optimalByteLength | 0x80);
                bufferPtr[1] = (byte)((optimalByteLength >> 7) | 0x80);
                bufferPtr[2] = (byte)((optimalByteLength >> 14));

                writer.Advance(encodedByteLength + 3);
            }
            else if (encodedByteLength < 1u << 28)
            {
                if (predictedBodyOffset != 4)
                    Memmove(bufferPtr + predictedBodyOffset, bufferPtr + 4, encodedByteLength, encodedByteLength);

                // Length of UTF8 encoded string body
                bufferPtr[0] = (byte)(optimalByteLength | 0x80);
                bufferPtr[1] = (byte)((optimalByteLength >> 7) | 0x80);
                bufferPtr[2] = (byte)((optimalByteLength >> 14) | 0x80);
                bufferPtr[3] = (byte)((optimalByteLength >> 21));

                writer.Advance(encodedByteLength + 4);
            }
            else
            {
                if (predictedBodyOffset != 5)
                    Memmove(bufferPtr + predictedBodyOffset, bufferPtr + 5, encodedByteLength, encodedByteLength);

                // Length of UTF8 encoded string body
                bufferPtr[0] = (byte)(optimalByteLength | 0x80);
                bufferPtr[1] = (byte)((optimalByteLength >> 7) | 0x80);
                bufferPtr[2] = (byte)((optimalByteLength >> 14) | 0x80);
                bufferPtr[3] = (byte)((optimalByteLength >> 21) | 0x80);
                bufferPtr[4] = (byte)((optimalByteLength >> 28));

                writer.Advance(encodedByteLength + 5);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void Memmove(void* source, void* destination, long destinationSizeInBytes, long sourceBytesToCopy)
        {
            //todo mono 계열에서는 overlapped memcpy를 지원하지 않는가?
            //if (Utilities.IsMono)
            //{
            //    // mono does not guarantee overlapped memcpy so for Unity and NETSTANDARD use slow path.
            //    // https://github.com/neuecc/MessagePack-CSharp/issues/562
            //    var buffer = ArrayPool<byte>.Shared.Rent((int)sourceBytesToCopy);
            //    try
            //    {
            //        fixed (byte* bufferPtr = buffer)
            //        {
            //            Buffer.MemoryCopy(source, bufferPtr, sourceBytesToCopy, sourceBytesToCopy);
            //            Buffer.MemoryCopy(bufferPtr, destination, destinationSizeInBytes, sourceBytesToCopy);
            //        }
            //    }
            //    finally
            //    {
            //        ArrayPool<byte>.Shared.Return(buffer);
            //    }
            //}

            Buffer.MemoryCopy(source, destination, destinationSizeInBytes, sourceBytesToCopy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out string value)
        {
            int rollbackPosition = reader.Position;

            // 구태여 길이 값을 이렇게할 필요가 있을까? 싶기도..
            // Flex 포맷과 호환성을 유지하기 위해서 해야함.
            // 아니면 Flex의 length-prefixed도 unsigned로 처리할까?
            if (!reader.TryReadCounter32(out int length))
            {
                reader.Position = rollbackPosition;
                value = default;
                return false;
            }
            /*
            //if (!reader.TryReadVarint32(out uint u))
            //{
            //    reader.Position = rollbackPosition;
            //    value = default;
            //    return false;
            //}
            //
            //int length = (int)u;
            */

            if (reader.ReadableLength < length)
            {
                reader.Position = rollbackPosition;
                value = default;
                return false;
            }

            //todo 길이가 유효한지 여부를 체크해야함.
            //길이가 유효하지 않다면 false를 반환해야할지 예외를 반환해야할지?
            //이 함수에서는 예외를 반환하지 않아야하지만,
            //이 함수를 employ하는 함수에서는 원인을 체크해서 적당한 예외를
            //던지는게 좋을듯 한데..

            value = Encoding.UTF8.GetString(reader.Data, reader.Position, length);
            reader.Skip(length);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out string value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region byte[]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, byte[] value)
        {
            if (value == null || value.Length == 0)
            {
                writer.WriteCounter32(0);
                return;
            }

            writer.Write(value, 0, value.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, byte[] data, int offset, int length)
        {
            writer.WriteCounter32(length);
            writer.WriteRawBytes(data, offset, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out byte[] value)
        {
            int rollbackPosition = reader.Position;

            // Read the length of the byte array.
            if (!reader.TryReadCounter32(out int length))
            {
                reader.Position = rollbackPosition;
                value = default;
                return false;
            }

            // Check if the read length is within the allowable range
            ValidateRequiredByteLength(reader, length);

            // Check whether data can be read as long as the length.
            if (!reader.CanReadable(length))
            {
                reader.Position = rollbackPosition;
                value = default;
                return false;
            }

            // Create the return object.
            // It would be nice to create a function that reads without creating a return object.
            value = new byte[length];
            Array.Copy(reader.Data, reader.Position, value, 0, length);
            reader.Skip(length);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out byte[] value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region DateTime
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, DateTime value)
        {
            writer.WriteFixed64((ulong)value.Ticks); // 10,000 ticks = 1ms
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out DateTime value)
        {
            if (reader.TryReadFixed64(out ulong ticks))
            {
                value = new DateTime((long)ticks);
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out DateTime value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region TimeSpan
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, TimeSpan value)
        {
            writer.WriteFixed64((ulong)value.Ticks); // 10,000 ticks = 1ms
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out TimeSpan value)
        {
            if (reader.TryReadFixed64(out ulong ticks))
            {
                value = new TimeSpan((long)ticks);
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out TimeSpan value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Guid
        //ARM계열에서는 align이슈가 있지 않을까?

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void GuidToByteArray(Guid value, byte[] bytes, int offset)
        {
            fixed (byte* bytesPtr = bytes)
            {
                var guidPtr = (long*)&value;
                var destPtr = (long*)(bytesPtr + offset);
                destPtr[0] = guidPtr[0];
                destPtr[1] = guidPtr[1];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe Guid ByteArrayToGuid(byte[] bytes, int offset)
        {
            Guid value;
            fixed (byte* bytesPtr = bytes)
            {
                var srcPtr = (long*)(bytesPtr + offset);
                var guidPtr = (long*)&value;
                guidPtr[0] = srcPtr[0];
                guidPtr[1] = srcPtr[1];
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, Guid value)
        {
#if LEGACY_FORMATTING
            var bytes = value.ToByteArray();
            writer.WriteRawBytes(bytes);
#else
            int position = writer.MakeHole(16);
            GuidToByteArray(value, writer.Data, position);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out Guid value)
        {
            if (reader.ReadableLength >= 16)
            {
#if LEGACY_FORMATTING
                var buffer = ArrayPool<byte>.Shared.Rent(16);
                reader.ReadRawBytes(buffer, 0, 16);
                value = new Guid(buffer);
                ArrayPool<byte>.Shared.Return(buffer);
#else
                value = ByteArrayToGuid(reader.Data, reader.Position);
                reader.Skip(16);
#endif
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out Guid value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Optimal int32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteOptimalInt32(this LiteBinaryWriter writer, int value)
        {
            uint encoded = LiteBinaryHelper.ZigZagEncode32(value);
            writer.WriteVarint32(encoded);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadOptimalInt32(this LiteBinaryReader reader, out int value)
        {
            if (reader.TryReadVarint32(out uint u))
            {
                value = LiteBinaryHelper.ZigZagDecode32(u);
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadOptimalInt32(this LiteBinaryReader reader, out int value)
        {
            if (!TryReadOptimalInt32(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Optimal int64
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteOptimalInt64(this LiteBinaryWriter writer, long value)
        {
            ulong encoded = LiteBinaryHelper.ZigZagEncode64(value);
            writer.WriteVarint64(encoded);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadOptimalInt64(this LiteBinaryReader reader, out long value)
        {
            if (reader.TryReadVarint64(out ulong u))
            {
                value = LiteBinaryHelper.ZigZagDecode64(u);
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadOptimalInt64(this LiteBinaryReader reader, out long value)
        {
            if (!TryReadOptimalInt64(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Counter32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCounter32(this LiteBinaryWriter writer, int count)
        {
            writer.WriteOptimalInt32(count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadCounter32(this LiteBinaryReader reader, out int count)
        {
            return reader.TryReadOptimalInt32(out count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadCounter32(this LiteBinaryReader reader, out int count)
        {
            reader.ReadOptimalInt32(out count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadCounter32(this LiteBinaryReader reader)
        {
            reader.ReadOptimalInt32(out int count);
            return count;
        }
        #endregion


        #region Counter64
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCounter64(this LiteBinaryWriter writer, long count)
        {
            writer.WriteOptimalInt64(count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadCounter64(this LiteBinaryReader reader, out long count)
        {
            return reader.TryReadOptimalInt64(out count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadCounter64(this LiteBinaryReader reader, out long count)
        {
            reader.ReadOptimalInt64(out count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadCounter64(this LiteBinaryReader reader)
        {
            reader.ReadOptimalInt64(out long count);
            return count;
        }
        #endregion


        /*

        #region Generated struct.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, IGeneratedStruct value)
        {
            writer.WriteStruct(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteStruct(this LiteBinaryWriter writer, IGeneratedStruct value)
        {
            //todo
            // 고정크기를 사용한다면 poke로도 처리가 가능할듯 싶은데,
            // 일단 옵션으로 처리해볼까?
            // 길이가 varint이므로 구조체의 길이를 계산해야하는데, 이 길이값이 고정 크기를 사용한다면,
            // 길이를 미리 구할 필요가 없겠다.
            // 약간의 낭비가 있겠지만, 그냥 고정으로 하고 압축으로 커버하는게 좋으려나.
            int length = value.GetByteLength();
            writer.WriteCounter32(length);

            value.Write(writer);
        }

        //todo
        //public static bool TryReadStruct<T>(this LiteBinaryReader reader, out T value) where T : IGeneratedStruct, new()

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadStruct<T>(this LiteBinaryReader reader, out T value) where T : IGeneratedStruct, new()
        {
            // Increase recursion depth.
            reader.IncreaseRecursionDepth();

            // Push view.
            var previousView = reader.PushView();

            try
            {
                // Read byte-length of struct.
                int length = reader.ReadCounter32();

                // Adjust view.
                reader.AdjustView(length);

                // Create and Read.
                value = new T();
                value.Read(reader);

                // If you have not read it to the end,
                // there is a high probability that packet-rigging is suspect or an engine bug.
                if (!reader.AtEnd)
                {
                    throw LiteBinaryException.MoreDataAvailable();
                }
            }
            finally
            {
                // Pop View.
                reader.PopView(previousView);

                // Decrease recursion depth.
                reader.DecreaseRecursionDepth();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadStruct<T>(this LiteBinaryReader reader) where T : IGeneratedStruct, new()
        {
            reader.ReadStruct<T>(out T value);
            return value;
        }
        #endregion
        
        */


        #region IPAddress
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, IPAddress value)
        {
            //todo 이 할당도 제거할 수 있을듯 싶은데...
            var bytes = value.GetAddressBytes();

            //byteswap을 했더니 문제가 발생한다. C++과 통신할때만 주의하면 될듯?
            // makes to little-endian
            //if (bytes.Length == 4)
            //    Array.Reverse(bytes);
            //else
            //{
            //    for (int i = 0; i < 16; i += 2)
            //        Array.Reverse(bytes, i, 2);
            //}

            //writer.Write(bytes);

            // optimized layout version
            writer.Write((byte)bytes.Length); //4 or 16
            writer.WriteRawBytes(bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out IPAddress value)
        {
            //todo 두번의 할당이 있을 수 있음.
            //약간 최적화를 할 필요가 있지 않을까?
            //앞에다가 1바이트의 길이를 넣어주고, 그다음 바이트 배열로 접근하는?
            //if (reader.TryRead(out byte[] bytes))
            //{
            //    value = new IPAddress(bytes);
            //    return true;
            //}

            // optimized layout version
            int originalReadPosition = reader.Position;

            if (reader.TryRead(out byte length))
            {
                if (length == 4 || length == 16)
                {
#if NO_UNITY
                    if (reader.ReadableLength >= length)
                    {
                        value = new IPAddress(new ReadOnlySpan<byte>(reader.Data, reader.Position, length));
                        reader.Skip(length);
                        return true;
                    }
#else
                    // 유니티에서는 IPAddress 생성자중에서 ReadOnlySpan을 적용할 수 없어서,
                    // 임시 버퍼를 통해서만 가능함.
                    //byte[] buffer = new byte[length];
                    //reader.ReadRawBytes(buffer, 0, length);
                    //value = new IPAddress(buffer);

                    // 할당은 제거할 수 있었지만, 두번의 복사는 피할 수 없음.
                    // 이상하게도 제대로 동작을 하지 않는다.
                    //byte[] buffer = _tlsScratchBuffer;
                    if (reader.ReadableLength >= length)
                    {
                        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
                        reader.ReadRawBytes(buffer, 0, length);
                        value = new IPAddress(buffer);
                        ArrayPool<byte>.Shared.Return(buffer);
                        return true;
                    }
#endif
                }
            }

            reader.Position = originalReadPosition;
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out IPAddress value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region IPEndPoint
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(this LiteBinaryWriter writer, IPEndPoint value)
        {
            writer.Write(value.Address);
            writer.Write((ushort)value.Port);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRead(this LiteBinaryReader reader, out IPEndPoint value)
        {
            int startingPosition = reader.Position;

            if (reader.TryRead(out IPAddress address) &&
                reader.TryRead(out ushort port))
            {
                //todo 예외가 발생할 수 있음.
                value = new IPEndPoint(address, port);
                return true;
            }

            reader.Position = startingPosition;
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(this LiteBinaryReader reader, out IPEndPoint value)
        {
            if (!TryRead(reader, out value))
                throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateRequiredByteLength(this LiteBinaryReader reader, int length)
        {
            //todo 이걸 설정할 수 있도록 해야할듯 싶은데.

            // Check negative length.
            if (length < 0)
                throw LiteBinaryException.NegativeSize();

            // Check massive.
            if (length > reader.MessageMaxLength)
                throw LiteBinaryException.MessageInSizeLimited();

            // Check readable length.
            if (reader.ReadableLength < length)
                throw LiteBinaryException.TruncatedMessage();

            // OK
        }


        /*
        #region Dynamic
        // Note: To support different types of primitive types,
        // Handle it internally as an object, and use box / unboxing and reflection,
        // so the Write functions that deal with the primitive type in efficiency
        // It is slow compared to.
        public static void WriteDynamic<T>(this LiteBinaryWriter writer, T value)
        {
            var valueType = typeof(T);
            var valueDynamicType = DynamicType.GetDynamicType(valueType);

            writer.WriteDynamic<T>(value, valueDynamicType);
        }

        // Note: To support different types of primitive types,
        // Handle it internally as an object, and use box / unboxing and reflection,
        // so the Write functions that deal with the primitive type in efficiency
        // It is slow compared to.
        public static void WriteDynamic<T>(this LiteBinaryWriter writer, T value, DynamicType valueDynamicType)
        {
            writer.WriteDynamic(value, valueDynamicType);
        }

        // Reflection-only functions
        public static void WriteDynamic(this LiteBinaryWriter writer, object value, DynamicType valueDynamicType)
        {
            if (writer.CountingOnly)
            {
                int byteLength = valueDynamicType.GetByteLength(value);
                writer.Advance(byteLength);
            }
            else
            {
                valueDynamicType.Write(writer, value);
            }
        }

        // Note: To support different types of primitive types,
        // Handle it internally as an object, and use box / unboxing and reflection,
        // so the Read functions that deal with the primitive type in efficiency
        // It is slow compared to.
        public static void ReadDynamic<T>(this LiteBinaryReader reader, out T outValue)
        {
            var valueType = typeof(T);
            var valueDynamicType = DynamicType.GetDynamicType(valueType);
            outValue = (T)reader.ReadDynamic(valueType, valueDynamicType);
        }

        // Note: To support different types of primitive types,
        // Handle it internally as an object, and use box / unboxing and reflection,
        // so the Read functions that deal with the primitive type in efficiency
        // It is slow compared to.
        public static void ReadDynamic<T>(this LiteBinaryReader reader, out T outValue, System.Type leafType, DynamicType dynamicType)
        {
            outValue = (T)reader.ReadDynamic(leafType, dynamicType);
        }

        // Reflection-only functions
        public static object ReadDynamic(this LiteBinaryReader reader, System.Type leafType, DynamicType dynamicType)
        {
            return dynamicType.Read(reader, -1, leafType);
        }
        #endregion
        */
    }
}
