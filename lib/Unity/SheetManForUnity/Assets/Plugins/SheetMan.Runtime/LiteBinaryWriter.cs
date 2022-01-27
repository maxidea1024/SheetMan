using System;
using System.Buffers;
using System.Text;

namespace SheetMan.Runtime
{
    public class LiteBinaryWriter
    {
        private ByteArray _biffer;
        private int _writtenLength;

        //@warning Do not think Data.Length is the length of actual data. Data.Length is actually capacity.

        #region Properties
        public byte[] Data => _biffer.Data;

        public int Length
        {
            get => _writtenLength;

            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("length must be > 0");

                if (value > _writtenLength)
                    throw new ArgumentOutOfRangeException("length must be <= _writtenLength");

                _writtenLength = value;
            }
        }

        public int WritableLength => MessageMaxLength - _writtenLength;

        public int Capacity => _biffer.Count;

        public int MessageMaxLength
        {
            get => _maximumMessageLength;

            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("length must be > 0");

                if (value > int.MaxValue)
                    throw new ArgumentOutOfRangeException("length must be <= int.MaxValue");

                _maximumMessageLength = value;

                if (_writtenLength > _maximumMessageLength)
                    _writtenLength = _maximumMessageLength;
            }
        }

        private int _maximumMessageLength = LiteBinaryConfig.MessageMaxLength;

        private bool _externalBufferUsed = false;
        #endregion


        #region Construction
        public void Replace(ByteArray buffer)
        {
            _biffer = buffer;
            _writtenLength = buffer.Count;
        }


        public LiteBinaryWriter()
        {
            _biffer = new ByteArray();
            _biffer.Count = LiteBinaryConfig.MessageMinLength; // Begin with a very small value.
        }

        //TODO buffer attach시에 데이터를 가져갈지 아니면 그냥 버퍼로만 사용할지 애매함.
        //명시적으로 처리하는게 좋을듯!!!

        //@warning
        // Note that the length of the buffer may not be the length of the actual data being written.
        // Do not think buffer.Count is the actual data length.
        // I do not want to make a case of this malfunction in the first place.
        // Or you can not comment every time...
        public LiteBinaryWriter(ByteArray buffer)
        {
            _biffer = buffer;
            _writtenLength = buffer.Count;
        }

        public LiteBinaryWriter(int initialCapacity)
        {
            _biffer = new ByteArray();
            _biffer.Count = initialCapacity;
        }

        #endregion


        #region Random writes
        public int MakeHole(int length)
        {
            int pos = _writtenLength;
            EnsureCapacity(length);

            // Dont'care inserted space.

            _writtenLength += length;
            return pos;
        }

        public int InsertHole(int pos, int length)
        {
            if (pos < 0)
                throw new ArgumentOutOfRangeException("pos is negative");

            if (length < 0)
                throw new ArgumentOutOfRangeException("length is negative");

            int oldWrittenLength = _writtenLength;
            int newWrittenLength;

            if (pos > _writtenLength)
                newWrittenLength = pos + length;
            else
                newWrittenLength = oldWrittenLength + length;

            //todo 먼저 체크하고 버퍼를 확장해주는게 안전하겠지?
            if (newWrittenLength > MessageMaxLength)
            {
                string text = string.Format("exceeded over maximum message length. limit={0:n0} exceeded={1:n0}", MessageMaxLength, newWrittenLength - MessageMaxLength);
                throw new LiteBinaryException(text);
            }

            _writtenLength = newWrittenLength;

            return pos;
        }

        public void PokeFixed8(int pos, byte value)
        {
            if (pos < 0 || pos >= (_writtenLength - 1))
                throw new ArgumentOutOfRangeException();

            _biffer.Data[pos] = value;
        }

        public void PokeFixed16(int pos, ushort value)
        {
            if (pos < 0 || pos >= (_writtenLength - 2))
                throw new ArgumentOutOfRangeException();

            _biffer.Data[pos + 0] = (byte)(value);
            _biffer.Data[pos + 1] = (byte)(value >> 8);
        }

        public void PokeFixed32(int pos, uint value)
        {
            if (pos < 0 || pos >= (_writtenLength - 4))
                throw new ArgumentOutOfRangeException();

            _biffer.Data[pos + 0] = (byte)(value);
            _biffer.Data[pos + 1] = (byte)(value >> 8);
            _biffer.Data[pos + 2] = (byte)(value >> 16);
            _biffer.Data[pos + 3] = (byte)(value >> 24);
        }

        public void PokeFixed64(int pos, ulong value)
        {
            if (pos < 0 || pos >= (_writtenLength - 8))
                throw new ArgumentOutOfRangeException();

            //todo Span으로 접근하는게 좋을듯 싶은데..

            _biffer.Data[pos + 0] = (byte)(value);
            _biffer.Data[pos + 1] = (byte)(value >> 8);
            _biffer.Data[pos + 2] = (byte)(value >> 16);
            _biffer.Data[pos + 3] = (byte)(value >> 24);
            _biffer.Data[pos + 4] = (byte)(value >> 32);
            _biffer.Data[pos + 5] = (byte)(value >> 40);
            _biffer.Data[pos + 6] = (byte)(value >> 48);
            _biffer.Data[pos + 7] = (byte)(value >> 56);
        }
        #endregion


        #region MessageByteCounter only function
        public void Advance(int length)
        {
            //throw new InvalidOperationException("never call Advance() for LiteBinaryWriter. this function is used for counting only!");

            //todo capacity를 넘는지 여부를 체크해야함.
            _writtenLength += length;
        }
        #endregion


        #region Fixed
        public void WriteFixed8(byte value)
        {
            EnsureCapacity(1);

            _biffer.Data[_writtenLength++] = value;
        }

        public void WriteFixed16(ushort value)
        {
            EnsureCapacity(2);

            _biffer.Data[_writtenLength + 0] = (byte)(value);
            _biffer.Data[_writtenLength + 1] = (byte)(value >> 8);

            _writtenLength += 2;
        }

        public void WriteFixed32(uint value)
        {
            EnsureCapacity(4);

            _biffer.Data[_writtenLength + 0] = (byte)(value);
            _biffer.Data[_writtenLength + 1] = (byte)(value >> 8);
            _biffer.Data[_writtenLength + 2] = (byte)(value >> 16);
            _biffer.Data[_writtenLength + 3] = (byte)(value >> 24);

            _writtenLength += 4;
        }

        public void WriteFixed64(ulong value)
        {
            EnsureCapacity(8);

            _biffer.Data[_writtenLength + 0] = (byte)(value);
            _biffer.Data[_writtenLength + 1] = (byte)(value >> 8);
            _biffer.Data[_writtenLength + 2] = (byte)(value >> 16);
            _biffer.Data[_writtenLength + 3] = (byte)(value >> 24);
            _biffer.Data[_writtenLength + 4] = (byte)(value >> 32);
            _biffer.Data[_writtenLength + 5] = (byte)(value >> 40);
            _biffer.Data[_writtenLength + 6] = (byte)(value >> 48);
            _biffer.Data[_writtenLength + 7] = (byte)(value >> 56);

            _writtenLength += 8;
        }
        #endregion


        #region Varint
        public void WriteVarint8(byte value)
        {
            EnsureCapacity(LiteBinaryHelper.MaxVarint8Length);

            if (value < (1u << 7))
            {
                _biffer.Data[_writtenLength++] = value;
            }
            else
            {
                _biffer.Data[_writtenLength++] = (byte)((value & 0x7f) | 0x80);
                _biffer.Data[_writtenLength++] = (byte)(value >> 7);
            }
        }

        public void WriteVarint16(ushort value)
        {
            EnsureCapacity(LiteBinaryHelper.MaxVarint16Length);

            int length;

            _biffer.Data[_writtenLength + 0] = (byte)(value | 0x80);
            if (value >= (1u << 7))
            {
                _biffer.Data[_writtenLength + 1] = (byte)((value >> 7) | 0x80);
                if (value >= (1u << 14))
                {
                    _biffer.Data[_writtenLength + 2] = (byte)(value >> 14);
                    length = 3;
                }
                else
                {
                    _biffer.Data[_writtenLength + 1] &= 0x7f;
                    length = 2;
                }
            }
            else
            {
                _biffer.Data[_writtenLength + 0] &= 0x7f;
                length = 1;
            }

            _writtenLength += length;
        }

        public void WriteVarint32(uint value)
        {
            EnsureCapacity(LiteBinaryHelper.MaxVarint32Length);

            int length;

            _biffer.Data[_writtenLength + 0] = (byte)(value | 0x80);
            if (value >= (1u << 7))
            {
                _biffer.Data[_writtenLength + 1] = (byte)((value >> 7) | 0x80);
                if (value >= (1u << 14))
                {
                    _biffer.Data[_writtenLength + 2] = (byte)((value >> 14) | 0x80);
                    if (value >= (1u << 21))
                    {
                        _biffer.Data[_writtenLength + 3] = (byte)((value >> 21) | 0x80);
                        if (value >= (1u << 28))
                        {
                            _biffer.Data[_writtenLength + 4] = (byte)(value >> 28);
                            length = 5;
                        }
                        else
                        {
                            _biffer.Data[_writtenLength + 3] &= 0x7F;
                            length = 4;
                        }
                    }
                    else
                    {
                        _biffer.Data[_writtenLength + 2] &= 0x7F;
                        length = 3;
                    }
                }
                else
                {
                    _biffer.Data[_writtenLength + 1] &= 0x7F;
                    length = 2;
                }
            }
            else
            {
                _biffer.Data[_writtenLength + 0] &= 0x7F;
                length = 1;
            }

            _writtenLength += length;
        }

        public void WriteVarint64(ulong value)
        {
            EnsureCapacity(LiteBinaryHelper.MaxVarint64Length);

            // Splitting into 32-bit pieces gives better performance on 32-bit processors.
            uint part0 = (uint)(value);
            uint part1 = (uint)(value >> 28);
            uint part2 = (uint)(value >> 56);

            int length;

            // Here we can't really optimize for small numbers, since the value is
            // split into three parts.  Checking for numbers < 128, for instance,
            // would require three comparisons, since you'd have to make sure part1
            // and part2 are zero.  However, if the caller is using 64-bit integers,
            // it is likely that they expect the numbers to often be very large, so
            // we probably don't want to optimize for small numbers anyway.  Thus,
            // we end up with a hard-coded binary search tree...
            if (part2 == 0)
            {
                if (part1 == 0)
                {
                    if (part0 < (1u << 14))
                    {
                        if (part0 < (1u << 7))
                        {
                            length = 1; goto length01;
                        }
                        else
                        {
                            length = 2; goto length02;
                        }
                    }
                    else
                    {
                        if (part0 < (1u << 21))
                        {
                            length = 3; goto length03;
                        }
                        else
                        {
                            length = 4; goto length04;
                        }
                    }
                }
                else
                {
                    if (part1 < (1u << 14))
                    {
                        if (part1 < (1u << 7))
                        {
                            length = 5; goto length05;
                        }
                        else
                        {
                            length = 6; goto length06;
                        }
                    }
                    else
                    {
                        if (part1 < (1u << 21))
                        {
                            length = 7; goto length07;
                        }
                        else
                        {
                            length = 8; goto length08;
                        }
                    }
                }
            }
            else
            {
                if (part2 < (1u << 7))
                {
                    length = 9; goto length09;
                }
                else
                {
                    length = 10; goto length10;
                }
            }

    length10: _biffer[_writtenLength + 9] = (byte)((part2 >> 7) | 0x80);
    length09: _biffer[_writtenLength + 8] = (byte)((part2) | 0x80);
    length08: _biffer[_writtenLength + 7] = (byte)((part1 >> 21) | 0x80);
    length07: _biffer[_writtenLength + 6] = (byte)((part1 >> 14) | 0x80);
    length06: _biffer[_writtenLength + 5] = (byte)((part1 >> 7) | 0x80);
    length05: _biffer[_writtenLength + 4] = (byte)((part1) | 0x80);
    length04: _biffer[_writtenLength + 3] = (byte)((part0 >> 21) | 0x80);
    length03: _biffer[_writtenLength + 2] = (byte)((part0 >> 14) | 0x80);
    length02: _biffer[_writtenLength + 1] = (byte)((part0 >> 7) | 0x80);
    length01: _biffer[_writtenLength + 0] = (byte)((part0) | 0x80);

            _biffer[_writtenLength + length - 1] &= 0x7F;

            _writtenLength += length;
        }


        //값이 negative일 경우에는 강제로 64비트로 확장 시켜버림.(binary 호환을 위해서.)
        //음수인 경우가 극히 드물 경우에 유용함.

        public void WriteVarint8SignExtended(sbyte value)
        {
            if (value < 0)
                WriteVarint64((ulong)value);
            else
                WriteVarint8((byte)value);
        }

        public void WriteVarint16SignExtended(short value)
        {
            if (value < 0)
                WriteVarint64((ulong)value);
            else
                WriteVarint16((ushort)value);
        }

        public void WriteVarint32SignExtended(int value)
        {
            if (value < 0)
                WriteVarint64((ulong)value);
            else
                WriteVarint32((uint)value);
        }
        #endregion


        #region Raw bytes
        public void WriteRawBytes(byte[] data)
        {
            if (data == null)
                return;

            WriteRawBytes(data, 0, data.Length);
        }

        public void WriteRawBytes(byte[] data, int length)
        {
            WriteRawBytes(data, 0, length);
        }

        public void WriteRawBytes(byte[] data, int offset, int length)
        {
            if (data == null)
                return;

            if (length < 0 || (offset + length) > data.Length)
                throw new ArgumentOutOfRangeException();

            if (length > 0)
            {
                EnsureCapacity(length);

                Array.Copy(data, offset, _biffer.Data, _writtenLength, length);
                _writtenLength += length;
            }
        }

        public void WriteRawBytes(ArraySegment<byte> data)
        {
            WriteRawBytes(data.Array, data.Offset, data.Count);
        }

        public void RemoveRange(int index, int length)
        {
            if (index < 0 || index > _writtenLength || length < 0 || (index + length) > _writtenLength)
                throw new ArgumentOutOfRangeException();

            if (length > 0)
            {
                _biffer.RemoveRange(index, length);
                _writtenLength -= length;
            }
        }
        #endregion


        #region Buffer
        public int EnsureCapacity(int length)
        {
            int requiredCapacity = _writtenLength + length;

            if (requiredCapacity > _biffer.Count)
            {
                if (requiredCapacity > MessageMaxLength)
                {
                    string text = string.Format("exceeded over maximum message length. requested-length={0:n0} limit={1:n0} exceeded={2:n0}", requiredCapacity, MessageMaxLength, requiredCapacity - MessageMaxLength);
                    throw new LiteBinaryException(text);
                }

                if (!_externalBufferUsed)
                {
                    _biffer.Count = requiredCapacity;
                }
                else
                {
                    throw new LiteBinaryException(string.Format("because you are using an external buffer, you can not increase the size of the buffer. requested-length={0}, buffer-length={1}", requiredCapacity, _biffer.Count));
                }
            }

            return _writtenLength;
        }
        #endregion


        //todo IMemory<byte>로 처리하는 코드를 넣어두자. Span으로 해야하는건가??

        /// <summary>
        /// 내부 버퍼를 액세스하는 프로퍼티 Data를 사용할 경우, Data.Length가 실제 데이터 길이보다 더 길수 있으므로
        /// 바이트 배열의 길이가 실제 길이와 같아야 할 경우에는 이 함수를 사용해서, 데이터를 구한 후 사용해야함.
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            if (Data.Length != Length)
            {
                byte[] array = new byte[Length];
                Array.Copy(Data, array, Length);
                return array;
            }
            else
            {
                return Data;
            }
        }

        public ArraySegment<byte> AsSegment()
        {
            return new ArraySegment<byte>(Data, 0, Length);
        }

        public ReadOnlyMemory<byte> AsMemory()
        {
            return new ReadOnlyMemory<byte>(Data, 0, Length);
        }
    }
}
