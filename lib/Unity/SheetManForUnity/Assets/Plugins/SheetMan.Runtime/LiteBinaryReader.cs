using System;
using System.Text;
using System.Runtime.CompilerServices;

namespace SheetMan.Runtime
{
    public class LiteBinaryReader
    {
        public struct View
        {
            internal int offset;
            internal int end;
        }

        /// <summary>
        /// Internal shareable message buffer.
        /// </summary>
        private ByteArray _buffer;

        /// <summary>
        /// Current position.
        /// </summary>
        private int _position;

        /// <summary>
        /// Message data length.
        /// </summary>
        private int _length;

        /// <summary>
        /// Message reading recursion limit.
        /// </summary>
        private int _recursionLimit = LiteBinaryConfig.DefaultRecursionLimit;

        /// <summary>
        /// Current reading recursion depth.
        /// </summary>
        private int _recursionDepth = 0;

        /// <summary>
        /// Current scoped readable range.
        /// </summary>
        private View _view;

        #region Properties
        public byte[] Data => _buffer.Data;

        public int Length => _length;

        public int Position
        {
            get => _position;

            set
            {
                if (value < 0 || (value < _view.offset || value > _view.end))
                    throw new ArgumentOutOfRangeException("Position must be located between _view.offset and _view.end");

                _position = value;
            }
        }

        public int ReadableLength => _view.end - _position;

        public bool AtEnd => _position >= _view.end;

        private int _maximumMessageLength = LiteBinaryConfig.MessageMaxLength;

        public int MessageMaxLength
        {
            get => _maximumMessageLength;
            set => _maximumMessageLength = value;
        }
        #endregion


        #region Construction
        public void Replace(ByteArray buffer)
        {
            PreValidations.CheckNotNull(buffer, nameof(buffer));

            _buffer = buffer;
            _length = buffer.Count;

            _position = 0;

            _view.offset = 0;
            _view.end = _length;
        }

        public void Replace(ByteArray buffer, int length)
        {
            PreValidations.CheckNotNull(buffer, nameof(buffer));
            if (length < 0 || length > buffer.Count)
                throw new Exception();

            _buffer = buffer;
            _length = length;

            _position = 0;

            _view.offset = 0;
            _view.end = _length;
        }


        public LiteBinaryReader()
        {
            _position = 0;
            _buffer = null;
            _length = 0;

            _view.offset = 0;
            _view.end = 0;
        }

        public LiteBinaryReader(LiteBinaryReader source)
        {
            PreValidations.CheckNotNull(source, nameof(source));

            _position = source._position;
            _buffer = PreValidations.CheckNotNull(source._buffer, nameof(_buffer));
            _length = source._length;

            //_view.offset = 0;
            _view.offset = _position;
            _view.end = _length;
        }

        public LiteBinaryReader(LiteBinaryWriter source) : this(source.AsSegment())
        {
        }

        public LiteBinaryReader(ByteArray source)
        {
            PreValidations.CheckNotNull(source, nameof(source));

            _buffer = source;
            _length = source.Count;

            _position = 0;

            _view.offset = _position;
            _view.end = _length;
        }

        public LiteBinaryReader(ByteArray source, int length)
        {
            PreValidations.CheckNotNull(source, nameof(source));

            if (length < 0 || length > source.Count)
                throw new ArgumentOutOfRangeException(nameof(length), "length must be non-negative and within the buffer");

            _buffer = source;
            _length = length;

            _position = 0;
            _view.offset = 0;
            _view.end = _length;
        }


        public LiteBinaryReader(byte[] data) : this(data, 0, data.Length)
        {
        }

        public LiteBinaryReader(byte[] data, int length) : this(data, 0, length)
        {
        }

        public LiteBinaryReader(ArraySegment<byte> segment) : this(segment.Array, segment.Offset, segment.Count)
        {
        }

        public LiteBinaryReader(byte[] data, int offset, int length)
        {
            _buffer = new ByteArray();
            _buffer.UseExternalBuffer(data, length);

            // 하한 값은 _view.offset을 통해서 제한할 수 있으므로 문제될건 없음.
            _position = offset;
            _length = length;

            _view.offset = offset;
            _view.end = length;
        }

        #endregion


        public void Attach(ByteArray data)
        {
            _buffer = data;
            _position = 0;
            _length = _buffer.Count;
            _view.offset = 0;
            _view.end = _length;
        }


        #region Seeking
        public void SeekToBegin()
        {
            _position = _view.offset;
        }

        public void SeekToEnd()
        {
            _position = _view.end;
        }

        public void Skip(int amount)
        {
            if (amount != 0)
            {
                if (amount < 0)
                    throw new ArgumentOutOfRangeException(nameof(amount), "amount must be 0 or positive.");

                int newOffset = (_position + amount);
                if (newOffset < _view.offset)
                    throw new ArgumentOutOfRangeException();

                if (newOffset > _view.end)
                    throw LiteBinaryException.TruncatedMessage();

                _position += amount;
            }
        }
        #endregion


        #region Utilities
        public bool CanReadable(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "length must be 0 or positive.");

            return ReadableLength >= length;
        }
        #endregion


        #region Random reads
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte PeekFixed8(int pos)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort PeekFixed16(int pos)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PeekFixed32(int pos)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong PeekFixed64(int pos)
        {
            throw new NotImplementedException();
        }
        #endregion


        #region Read raw bytes
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[] ReadRawBytes(int length)
        {
            byte[] buffer = new byte[length];
            ReadRawBytes(buffer, 0, length);
            return buffer;
        }

        // Note: Do not resize the buffer as long as it is read internally.
        // If you use an external shared buffer in case of fitting, there may be a problem.
        public void ReadRawBytes(byte[] buffer, int offset, int length)
        {
            // If buffer is null and length == 0, do not treat it as an error, just ignore it.
            // For TArray<T>, if .Count is 0, the internal buffer is null.

            //PreValidations.CheckNotNull(buffer, nameof(buffer));
            if (buffer == null)
            {
                //length도 0이어야함.
                if (length != 0)
                    throw new InvalidOperationException();
                return;
            }

            if (length < 0 || offset < 0)
                throw new ArgumentOutOfRangeException();

            if (length == 0)
                return;

            // Because we are going to read the data into a fixed array object, (offset + length) must be within the normal range.
            if ((offset + length) > buffer.Length)
                throw new ArgumentOutOfRangeException();

            // check massive.
            if (length > MessageMaxLength)
                throw LiteBinaryException.MessageInSizeLimited();

            // check truncation.
            int newOffset = (_position + length);
            if (newOffset > _view.end)
                throw LiteBinaryException.TruncatedMessage();

            Array.Copy(_buffer.Data, _position, buffer, offset, length);
            _position = newOffset;
        }
        #endregion


        #region Read fixed
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadFixed8(out byte value)
        {
            if (ReadableLength >= 1)
            {
                value = _buffer.Data[_position++];
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadFixed8()
        {
            if (TryReadFixed8(out byte value))
                return value;

            throw LiteBinaryException.TruncatedMessage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadFixed16(out ushort value)
        {
            if (ReadableLength >= 2)
            {
                //BitConverter를 사용해서 얻는 이점이 있을까?
                if (BitConverter.IsLittleEndian)
                {
                    // If the byte-order of the host is little-endian,
                    // Since the default byte order is used, no byte-swapping is required.
                    value = BitConverter.ToUInt16(_buffer.Data, _position);
                }
                else
                {
                    //todo unsafe코드로 처리가 가능한가?
                    value = (ushort)(_buffer.Data[_position + 0]);
                    value |= (ushort)(_buffer.Data[_position + 1] << 8);

                    //value = (ushort)_data[_position];
                    //value|= (ushort)_data[_position] << 8;
                }

                _position += 2;

                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadFixed16()
        {
            if (TryReadFixed16(out ushort value))
                return value;

            throw LiteBinaryException.TruncatedMessage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadFixed32(out uint value)
        {
            if (ReadableLength >= 4)
            {
                if (BitConverter.IsLittleEndian)
                {
                    // If the byte-order of the host is little-endian,
                    // Since the default byte order is used, no byte-swapping is required.
                    //todo 이게 제일 빠른 방법인가?
                    value = BitConverter.ToUInt32(_buffer.Data, _position);
                }
                else
                {
                    value = (uint)(_buffer.Data[_position + 0]);
                    value |= (uint)(_buffer.Data[_position + 1] << 8);
                    value |= (uint)(_buffer.Data[_position + 2] << 16);
                    value |= (uint)(_buffer.Data[_position + 3] << 24);
                }

                _position += 4;

                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadFixed32()
        {
            if (TryReadFixed32(out uint value))
                return value;

            throw LiteBinaryException.TruncatedMessage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadFixed64(out ulong value)
        {
            if (ReadableLength >= 8)
            {
                if (BitConverter.IsLittleEndian)
                {
                    // If the byte-order of the host is little-endian,
                    // Since the default byte order is used, no byte-swapping is required.
                    value = BitConverter.ToUInt64(_buffer.Data, _position);
                }
                else
                {
                    value = ((ulong)_buffer.Data[_position + 0]);
                    value |= ((ulong)_buffer.Data[_position + 1] << 8);
                    value |= ((ulong)_buffer.Data[_position + 2] << 16);
                    value |= ((ulong)_buffer.Data[_position + 3] << 24);
                    value |= ((ulong)_buffer.Data[_position + 4] << 32);
                    value |= ((ulong)_buffer.Data[_position + 5] << 40);
                    value |= ((ulong)_buffer.Data[_position + 6] << 48);
                    value |= ((ulong)_buffer.Data[_position + 7] << 56);
                }

                _position += 8;

                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadFixed64()
        {
            if (TryReadFixed64(out ulong value))
                return value;

            throw LiteBinaryException.TruncatedMessage();
        }
        #endregion


        #region Varint
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadVarint8(out byte value)
        {
            if (ReadableLength >= 1 && _buffer.Data[_position] < 128) // Fastest path
            {
                value = _buffer.Data[_position++];
                return true;
            }

            return TryReadVarint8Fallback(out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadVarint16(out ushort value)
        {
            if (ReadableLength >= 1 && _buffer.Data[_position] < 128) // Fastest path
            {
                value = _buffer.Data[_position++];
                return true;
            }

            return TryReadVarint16Fallback(out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadVarint32(out uint value)
        {
            if (ReadableLength >= 1 && _buffer.Data[_position] < 128) // Fastest path
            {
                value = _buffer.Data[_position++];
                return true;
            }

            return TryReadVarint32Fallback(out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadVarint64(out ulong value)
        {
            if (ReadableLength >= 1 && _buffer.Data[_position] < 128) // Fastest path
            {
                value = _buffer.Data[_position++];
                return true;
            }

            return TryReadVarint64Fallback(out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadVarint8()
        {
            if (!TryReadVarint8(out byte value))
                throw LiteBinaryException.MalformedVarint();

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadVarint16()
        {
            if (!TryReadVarint16(out ushort value))
                throw LiteBinaryException.MalformedVarint();

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVarint32()
        {
            if (!TryReadVarint32(out uint value))
                throw LiteBinaryException.MalformedVarint();

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadVarint64()
        {
            if (!TryReadVarint64(out ulong value))
                throw LiteBinaryException.MalformedVarint();

            return value;
        }


        // Varint Fallback
        // The following functions should not be called directly.
        // These functions are called sub-optimal in case that TryReadVarintX cannot process as 1 byte.

        private bool TryReadVarint8Fallback(out byte value)
        {
            value = 0;

            //if (ReadableLength >= LiteBinaryHelper.MaxVarint8Length ||
            //    (ReadableLength >= 1 && _buffer.Data[_view.end - 1] < 128)) // fastest path (호출한 함수에서 이미 체크하고 들어왔으므로 두번 체크하는 것이 되는)
            if (ReadableLength >= LiteBinaryHelper.MaxVarint8Length)
            {
                int end = ReadVarint8FromBuffer(_buffer.Data, _position, ref value);
                if (end >= 0)
                {
                    _position = end;
                    return true;
                }

                return false;
            }
            else
            {
                return TryReadVarint8Slow(ref value);
            }
        }

        private bool TryReadVarint16Fallback(out ushort value)
        {
            value = 0;

            //if (ReadableLength >= LiteBinaryHelper.MaxVarint16Length ||
            //    (ReadableLength >= 1 && _buffer.Data[_view.end - 1] < 128)) // fastest path (호출한 함수에서 이미 체크하고 들어왔으므로 두번 체크하는 것이 되는)
            if (ReadableLength >= LiteBinaryHelper.MaxVarint16Length)
            {
                int end = ReadVarint16FromBuffer(_buffer.Data, _position, ref value);
                if (end >= 0)
                {
                    _position = end;
                    return true;
                }

                return false;
            }
            else
            {
                return TryReadVarint16Slow(ref value);
            }
        }

        private bool TryReadVarint32Fallback(out uint value)
        {
            value = 0;

            //if (ReadableLength >= LiteBinaryHelper.MaxVarint32Length ||
            //    (ReadableLength >= 1 && _buffer.Data[_view.end - 1] < 128)) // fastest path (호출한 함수에서 이미 체크하고 들어왔으므로 두번 체크하는 것이 되는)
            if (ReadableLength >= LiteBinaryHelper.MaxVarint32Length)
            {
                int end = ReadVarint32FromBuffer(_buffer.Data, _position, ref value);
                if (end >= 0)
                {
                    _position = end;
                    return true;
                }

                return false;
            }
            else
            {
                return TryReadVarint32Slow(ref value);
            }
        }

        private bool TryReadVarint64Fallback(out ulong value)
        {
            value = 0;

            //if (ReadableLength >= LiteBinaryHelper.MaxVarint64Length ||
            //    (ReadableLength >= 1 && _buffer.Data[_view.end - 1] < 128)) // fastest path (호출한 함수에서 이미 체크하고 들어왔으므로 두번 체크하는 것이 되는)
            if (ReadableLength >= LiteBinaryHelper.MaxVarint64Length)
            {
                int end = ReadVarint64FromBuffer(_buffer.Data, _position, ref value);
                if (end >= 0)
                {
                    _position = end;
                    return true;
                }

                return false;
            }
            else
            {
                return TryReadVarint64Slow(ref value);
            }
        }


        // Varint from buffer

        //todo unsafe구현을 한번 해볼까 싶은데...
        //코드를 조금더 이쁘게 다듬을 수 없을까?
        private int ReadVarint8FromBuffer(byte[] buffer, int offset, ref byte value)
        {
            byte result;
            byte b;

            b = buffer[offset++]; result = b; if (b < 128) goto done;
            result -= 0x80;

            b = buffer[offset++]; result += (byte)(b << 7); if (b < 128) goto done;
            //"result -= 0x80 << 7;" is irrevelant.

            // If the reader is larger than 8 bits, we still need to read it all
            // and discard the high-order bits.
            int lookaheadLength = LiteBinaryHelper.MaxVarint64Length - LiteBinaryHelper.MaxVarint8Length;
            for (int i = 0; i < lookaheadLength; ++i)
            {
                b = buffer[offset++]; if (b < 128) goto done;
            }

            // We have overrun the maximum size of a varint (10 bytes).
            // Assume the data is corrupt.
            return -1;

        done:
            value = result;
            return offset;
        }

        //todo unsafe구현을 한번 해볼까 싶은데...
        private int ReadVarint16FromBuffer(byte[] buffer, int offset, ref ushort value)
        {
            ushort result;
            byte b;

            b = buffer[offset++]; result = b; if (b < 128) goto done;
            result -= 0x80;

            b = buffer[offset++]; result += (ushort)(b << 7); if (b < 128) goto done;
            result -= 0x80 << 7;

            b = buffer[offset++]; result += (ushort)(b << 14); if (b < 128) goto done;
            //"result -= 0x80 << 14;" is irrevelant.

            // if the reader is larger than 8 bits, we still need to read it all
            // and discard the high-order bits.
            int lookaheadLength = LiteBinaryHelper.MaxVarint64Length - LiteBinaryHelper.MaxVarint16Length;
            for (int i = 0; i < lookaheadLength; ++i)
            {
                b = buffer[offset++]; if (b < 128) goto done;
            }

            // we have overrun the maximum size of a varint (10 bytes).  assume the data is corrupt.
            return -1;

        done:
            value = result;
            return offset;
        }

        //todo unsafe구현을 한번 해볼까 싶은데...
        private int ReadVarint32FromBuffer(byte[] buffer, int offset, ref uint value)
        {
            uint result;
            uint b;

            b = buffer[offset++]; result = b; if (b < 128) goto done;
            result -= 0x80;

            b = buffer[offset++]; result += b << 7; if (b < 128) goto done;
            result -= 0x80 << 7;

            b = buffer[offset++]; result += b << 14; if (b < 128) goto done;
            result -= 0x80 << 14;

            b = buffer[offset++]; result += b << 21; if (b < 128) goto done;
            result -= 0x80 << 21;

            b = buffer[offset++]; result += b << 28; if (b < 128) goto done;
            // "result -= 0x80 << 28;" is irrevelant.

            // If the reader is larger than 8 bits, we still need to read it all
            // and discard the high-order bits.
            int lookaheadLength = LiteBinaryHelper.MaxVarint64Length - LiteBinaryHelper.MaxVarint32Length;
            for (int i = 0; i < lookaheadLength; ++i)
            {
                b = buffer[offset++]; if (b < 128) goto done;
            }

            // We have overrun the maximum size of a varint (10 bytes).
            // Assume the data is corrupt.
            return -1;

        done:
            value = result;
            return offset;
        }

        //todo unsafe구현을 한번 해볼까 싶은데...
        private int ReadVarint64FromBuffer(byte[] buffer, int offset, ref ulong value)
        {
            // Fast path:
            // We have enough bytes left in the buffer to guarantee that
            // this read won't cross the end, so we can skip the checks.

            uint b;

            // Splitting into 32-bit pieces gives better performance on 32-bit processors.
            uint part0 = 0, part1 = 0, part2 = 0;

            b = buffer[offset++]; part0 = b; if (b < 128) goto done; // 1
            part0 -= 0x80;

            b = buffer[offset++]; part0 += b << 7; if (b < 128) goto done; // 2
            part0 -= 0x80 << 7;

            b = buffer[offset++]; part0 += b << 14; if (b < 128) goto done; // 3
            part0 -= 0x80 << 14;

            b = buffer[offset++]; part0 += b << 21; if (b < 128) goto done; // 4
            part0 -= 0x80 << 21;

            b = buffer[offset++]; part1 = b; if (b < 128) goto done; // 5
            part1 -= 0x80;

            b = buffer[offset++]; part1 += b << 7; if (b < 128) goto done; // 6
            part1 -= 0x80 << 7;

            b = buffer[offset++]; part1 += b << 14; if (b < 128) goto done; // 7
            part1 -= 0x80 << 14;

            b = buffer[offset++]; part1 += b << 21; if (b < 128) goto done; // 8
            part1 -= 0x80 << 21;

            b = buffer[offset++]; part2 = b; if (b < 128) goto done; // 9
            part2 -= 0x80;

            b = buffer[offset++]; part2 += b << 7; if (b < 128) goto done; // 10 - worst case!
                                                                           // "part2 -= 0x80 << 7" is irrelevant because (0x80 << 7) << 56 is 0.

            // We have overrun the maximum size of a varint (10 bytes).
            // The data must be corrupt.
            return -1;

        done:
            value = ((ulong)part0) | ((ulong)part1 << 28) | ((ulong)part2 << 56);
            return offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReadVarint8Slow(ref byte value)
        {
            ulong u = 0;
            if (TryReadVarint64Slow(ref u))
            {
                value = (byte)u;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReadVarint16Slow(ref ushort value)
        {
            ulong u = 0;
            if (TryReadVarint64Slow(ref u))
            {
                value = (ushort)u;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReadVarint32Slow(ref uint value)
        {
            ulong u = 0;
            if (TryReadVarint64Slow(ref u))
            {
                value = (uint)u;
                return true;
            }

            return false;
        }

        private bool TryReadVarint64Slow(ref ulong value)
        {
            int savedReadPosition = _position;

            ulong result = 0;
            int length = 0;
            int shift = 0;
            byte b;
            do
            {
                if (_position == _view.end || length == LiteBinaryHelper.MaxVarint64Length)
                {
                    // Restore original reading position.
                    _position = savedReadPosition;
                    return false;
                }

                b = _buffer.Data[_position++];

                result |= ((ulong)(b & 0x7f)) << shift;
                shift += 7;

                length++;
            }
            while (b >= 128);

            value = result;
            return true;
        }
        #endregion


        #region Recursion limitation
        public void SetRecursionLimit(int newLimit)
        {
            if (newLimit < 0 || newLimit > 1024)
                throw new ArgumentOutOfRangeException();

            _recursionLimit = newLimit;
        }

        public void IncreaseRecursionDepth()
        {
            _recursionDepth++;

            if (_recursionDepth > _recursionLimit)
                throw LiteBinaryException.RecursionLimitExceeded();
        }

        public void DecreaseRecursionDepth()
        {
            if (_recursionDepth <= 0)
                throw LiteBinaryException.UnderflowRecursionDepth();

            _recursionDepth--;
        }
        #endregion


        #region Scoped readable bytes limitation
        public View PushView()
        {
            return _view;
        }

        public void AdjustView(int length)
        {
            if (length < 0 || (_position + length) > _view.end)
                throw new ArgumentOutOfRangeException(nameof(length));

            _view.offset = _position;
            _view.end = _position + length;
        }

        public void PopView(View previousView)
        {
            if (previousView.offset < 0 || previousView.end < _view.end)
                throw new ArgumentOutOfRangeException();

            _view = previousView;
        }

        public bool IsViewAdjusted()
        {
            return _view.end != _length;
        }
        #endregion
    }
}
