using System;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Standard base64 (RFC 4648, with padding) over ASCII bytes. It is strict in what it reads, like System.Text.Json is for a <c>byte[]</c>: the
    /// length must be a multiple of four, only the alphabet is allowed, and padding only at the end.
    /// </summary>
    internal static class Base64
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        /// <summary>The ASCII bytes of the base64 form of <paramref name="data" />.</summary>
        public static void Encode(byte[] data, ByteBuffer destination)
        {
            var i = 0;
            for (; i + 2 < data.Length; i += 3)
            {
                destination.Append((byte)Alphabet[data[i] >> 2]);
                destination.Append((byte)Alphabet[((data[i] & 0x03) << 4) | (data[i + 1] >> 4)]);
                destination.Append((byte)Alphabet[((data[i + 1] & 0x0F) << 2) | (data[i + 2] >> 6)]);
                destination.Append((byte)Alphabet[data[i + 2] & 0x3F]);
            }

            var remaining = data.Length - i;
            if (remaining == 1)
            {
                destination.Append((byte)Alphabet[data[i] >> 2]);
                destination.Append((byte)Alphabet[(data[i] & 0x03) << 4]);
                destination.Append((byte)'=');
                destination.Append((byte)'=');
            }
            else if (remaining == 2)
            {
                destination.Append((byte)Alphabet[data[i] >> 2]);
                destination.Append((byte)Alphabet[((data[i] & 0x03) << 4) | (data[i + 1] >> 4)]);
                destination.Append((byte)Alphabet[(data[i + 1] & 0x0F) << 2]);
                destination.Append((byte)'=');
            }
        }

        /// <summary>Decodes the ASCII base64 text in <paramref name="source" />, or returns <c>null</c> when it is not valid base64.</summary>
        public static byte[] Decode(byte[] source, int offset, int count)
        {
            if (count % 4 != 0)
            {
                return null;
            }

            if (count == 0)
            {
                return new byte[0];
            }

            var padding = 0;
            if (source[offset + count - 1] == (byte)'=')
            {
                padding = source[offset + count - 2] == (byte)'=' ? 2 : 1;
            }

            var result = new byte[count / 4 * 3 - padding];
            var written = 0;
            for (var i = 0; i < count; i += 4)
            {
                var quantumPadding = i + 4 == count ? padding : 0;
                var a = Value(source[offset + i]);
                var b = Value(source[offset + i + 1]);
                var c = quantumPadding == 2 ? 0 : Value(source[offset + i + 2]);
                var d = quantumPadding >= 1 ? 0 : Value(source[offset + i + 3]);
                if (a < 0 || b < 0 || c < 0 || d < 0)
                {
                    return null;
                }

                result[written++] = (byte)((a << 2) | (b >> 4));
                if (quantumPadding < 2)
                {
                    result[written++] = (byte)(((b & 0x0F) << 4) | (c >> 2));
                }

                if (quantumPadding < 1)
                {
                    result[written++] = (byte)(((c & 0x03) << 6) | d);
                }
            }

            return result;
        }

        static int Value(byte character)
        {
            if (character >= 'A' && character <= 'Z') return character - 'A';
            if (character >= 'a' && character <= 'z') return character - 'a' + 26;
            if (character >= '0' && character <= '9') return character - '0' + 52;
            if (character == '+') return 62;
            if (character == '/') return 63;
            return -1;
        }
    }
}
