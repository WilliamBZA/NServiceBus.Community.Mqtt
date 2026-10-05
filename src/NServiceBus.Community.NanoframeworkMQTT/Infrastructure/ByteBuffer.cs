using System;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>A growing byte array, used to build a decoded string or a payload.</summary>
    internal sealed class ByteBuffer
    {
        byte[] bytes;
        int length;

        public ByteBuffer(int capacity)
        {
            bytes = new byte[capacity < 16 ? 16 : capacity];
        }

        public int Length
        {
            get { return length; }
        }

        public void Append(byte value)
        {
            if (length == bytes.Length)
            {
                Grow(length + 1);
            }

            bytes[length++] = value;
        }

        public void Append(byte[] source)
        {
            Append(source, 0, source.Length);
        }

        public void Append(byte[] source, int offset, int count)
        {
            if (length + count > bytes.Length)
            {
                Grow(length + count);
            }

            Array.Copy(source, offset, bytes, length, count);
            length += count;
        }

        /// <summary>Appends ASCII text. Every character must be below 0x80.</summary>
        public void AppendAscii(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                Append((byte)text[i]);
            }
        }

        public byte[] ToArray()
        {
            var result = new byte[length];
            Array.Copy(bytes, 0, result, 0, length);
            return result;
        }

        void Grow(int required)
        {
            var capacity = bytes.Length * 2;
            if (capacity < required)
            {
                capacity = required;
            }

            var grown = new byte[capacity];
            Array.Copy(bytes, 0, grown, 0, length);
            bytes = grown;
        }
    }
}
