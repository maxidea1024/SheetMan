using System;

namespace SheetMan.Runtime
{
    public class ByteArray : TArray<byte>
    {
        static public ByteArray Empty => _empty;
        static private readonly ByteArray _empty = new ByteArray();

        public ByteArray()
        {
            base.GrowingMode = BufferGrowingMode.Normal;
            base.InitVars();
        }

        public ByteArray(byte[] data, int offset, int length)
        {
            base.GrowingMode = BufferGrowingMode.Normal;
            base.InitVars();
            base.AddRange(data, offset, length);
        }

        public ByteArray(byte[] data, int length)
        {
            base.GrowingMode = BufferGrowingMode.Normal;
            base.InitVars();
            base.AddRange(data, length);
        }

        public ByteArray(byte[] data)
        {
            base.GrowingMode = BufferGrowingMode.Normal;
            base.InitVars();
            base.AddRange(data);
        }

        public ByteArray(int initialUninitializedLength)
        {
            base.GrowingMode = BufferGrowingMode.Normal;
            base.InitVars();
            base.Count = initialUninitializedLength;
        }

        public new ByteArray Clone()
        {
            return (ByteArray)base.MemberwiseClone();
        }

        public override string ToString()
        {
            return Convert.ToBase64String(Data, 0, Count, Base64FormattingOptions.None);
        }
    }
}
