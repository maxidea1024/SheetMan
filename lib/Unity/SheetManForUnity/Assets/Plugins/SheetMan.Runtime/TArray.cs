using System;

namespace SheetMan.Runtime
{
    public class TArray<T> : ICloneable
    {
        public T[] _data;

        private int _length;
        private int _capacity;
        private int _minCapacity;
        private BufferGrowingMode _growingMode;

        public T[] Data => _data;

        public T this[int index]
        {
            get
            {
                CheckBounds(index);
                return _data[index];
            }
            set
            {
                CheckBounds(index);
                _data[index] = value;
            }
        }

        public int Count
        {
            get => _length;
            set => SetCount(value);
        }

        public BufferGrowingMode GrowingMode
        {
            get => _growingMode;
            set => _growingMode = value;
        }

        object ICloneable.Clone()
        {
            return this.Clone();
        }

        public TArray<T> Clone()
        {
            return (TArray<T>)base.MemberwiseClone();
        }

        protected void InitVars()
        {
            _data = null;
            _length = 0;
            _capacity = 0;
            _minCapacity = 0;
        }

        public void Clear()
        {
            Count = 0;
        }

        protected void CheckBounds(int index)
        {
            if (index < 0 || index >= Count)
                throw new IndexOutOfRangeException($"{index} / {Count}");
        }

        public void AddRange(T[] data)
        {
            if (data == null)
                throw new ArgumentException();

            if (data.Length == 0)
                return;

            int oldCount = Count;
            SetCount(oldCount + data.Length);
            Array.Copy(data, 0, _data, oldCount, data.Length);
        }

        public void AddRange(T[] data, int length)
        {
            if (data == null || length < 0 || data.Length < length)
                throw new ArgumentException();

            if (length == 0)
                return;

            int oldCount = Count;
            SetCount(oldCount + length);
            Array.Copy(data, 0, _data, oldCount, length);
        }

        public void AddRange(T[] data, int offset, int length)
        {
            if (data == null || length < 0 || data.Length < length || offset > data.Length)
                throw new ArgumentException();

            if (length == 0)
                return;

            int oldCount = Count;
            SetCount(oldCount + length);
            Array.Copy(data, offset, _data, oldCount, length);
        }

        public void Add(T value)
        {
            Insert(Count, value);
        }

        public void Insert(int pos, T value)
        {
            InsertRange(pos, value);
        }

        public void InsertRange(int pos, T data)
        {
            if (data == null || pos > Count || pos < 0)
                throw new ArgumentException();

            int oldCount = Count;
            SetCount(oldCount + 1);

            int expand = oldCount - pos;
            if (expand > 0)
            {
                for (int i = expand - 1; i >= 0; i--)
                    _data[i + pos + 1] = _data[i + pos];
            }
            _data[pos] = data;
        }

        public void InsertRange(int pos, T[] data)
        {
            if (data == null)
                throw new ArgumentException();

            InsertRange(pos, data, 0, data.Length);
        }

        public void InsertRange(int pos, T[] data, int length)
        {
            InsertRange(pos, data, 0, length);
        }

        public void InsertRange(int pos, T[] data, int offset, int length)
        {
            if (data == null || pos > Count || pos < 0)
                throw new ArgumentException();

            if (offset < 0 || (offset + length) >= data.Length)
                throw new ArgumentException();

            int oldCount = Count;
            SetCount(oldCount + length);

            int expand = oldCount - pos;
            if (expand > 0)
            {
                for (int i = expand - 1; i >= 0; i--)
                    _data[i + pos + 1] = _data[i + pos];
            }
            Array.Copy(data, offset, _data, pos, length);
        }

        public void RemoveRange(int pos, int lengthToRemove)
        {
            if (_data == null || pos > Count || pos < 0 || lengthToRemove < 0 || (pos + lengthToRemove) > Count)
                throw new ArgumentException();

            int shrink = Count - (pos + lengthToRemove);
            Array.Copy(_data, pos + lengthToRemove, _data, pos, shrink);
            SetCount(Count - lengthToRemove);
        }

        public void AddUninitialized(int length)
        {
            if (length < 0)
                throw new ArgumentException();

            if (length == 0)
                return;

            int oldCount = Count;
            SetCount(oldCount + length);
        }

        public void InsertUninitialized(int pos, int length)
        {
            if (pos > Count || pos < 0 || length < 0)
                throw new ArgumentException();

            int oldCount = Count;
            SetCount(oldCount + length);

            int expand = oldCount - pos;
            if (expand > 0)
            {
                for (int i = expand - 1; i >= 0; i--)
                    _data[i + pos + 1] = _data[i + pos];
            }
        }

        public void SetCount(int newCount)
        {
            if (newCount < 0)
                throw new ArgumentException($"negative array length: {newCount}");

            if (newCount != _length)
            {
                if (newCount > _capacity)
                {
                    int recommendedCapacity = CalcRecommendedCapacity(newCount);
                    if (_capacity == 0)
                        _data = new T[recommendedCapacity];
                    else
                        Array.Resize<T>(ref _data, recommendedCapacity);

                    _capacity = recommendedCapacity;
                }

                _length = newCount;
            }
        }

        private int CalcRecommendedCapacity(int desiredCapacity)
        {
            int expand;
            switch (_growingMode)
            {
                case BufferGrowingMode.Normal:
                    expand = _length / 8;
                    expand = Math.Min(expand, 1024);
                    expand = Math.Max(expand, 4);
                    return Math.Max(_minCapacity, desiredCapacity + expand);

                case BufferGrowingMode.ForSpeed:
                    expand = _length / 8;
                    expand = Math.Max(expand, 16);
                    expand = Math.Max(expand, 64);
                    return Math.Max(_minCapacity, desiredCapacity + expand);

                case BufferGrowingMode.ForMemoryUsage:
                    return Math.Max(_minCapacity, desiredCapacity);

                default:
                    throw new ArgumentException();
            }
        }

        public void UseExternalBuffer(T[] buffer, int capacity)
        {
            if (_data != null)
                throw new Exception("TArray.UseExternalBuffer");

            if (capacity < 0)
                throw new ArgumentException($"negative capacity: {capacity}", nameof(capacity));

            if (buffer == null || capacity == 0)
                return;

            _capacity = capacity;
            _data = buffer;
            _length = 0;
        }
    }
}
