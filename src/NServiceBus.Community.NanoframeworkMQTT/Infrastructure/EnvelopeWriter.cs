using System;
using System.Collections;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// A writer for the envelope <see cref="WireFormat" /> describes. It escapes only the quote, the backslash and control characters, and writes
    /// everything else as raw UTF-8, which System.Text.Json reads. It works on UTF-8 bytes, because a nanoFramework string cannot be copied char
    /// by char without losing characters outside the basic plane. Headers are written in the ordinal order of their UTF-8 bytes, because a
    /// <see cref="Hashtable" /> has no order of its own and the output has to be the same every time.
    /// </summary>
    internal static class EnvelopeWriter
    {
        public static byte[] Write(string id, Hashtable headers, byte[] body)
        {
            var output = new ByteBuffer(256 + body.Length * 4 / 3);

            output.AppendAscii("{\"Id\":");
            if (id == null)
            {
                output.AppendAscii("null");
            }
            else
            {
                JsonText.WriteString(output, Encoding.UTF8.GetBytes(id));
            }

            output.AppendAscii(",\"Headers\":{");
            WriteHeaders(output, headers);
            output.AppendAscii("},\"Body\":\"");
            Base64.Encode(body, output);
            output.AppendAscii("\"}");

            return output.ToArray();
        }

        static void WriteHeaders(ByteBuffer output, Hashtable headers)
        {
            var keys = new byte[headers.Count][];
            var values = new string[headers.Count];
            var count = 0;
            foreach (DictionaryEntry entry in headers)
            {
                var key = Encoding.UTF8.GetBytes((string)entry.Key);

                // insertion sort, by the bytes of the key: a message carries a dozen headers or so
                var position = count;
                while (position > 0 && CompareOrdinal(keys[position - 1], key) > 0)
                {
                    keys[position] = keys[position - 1];
                    values[position] = values[position - 1];
                    position--;
                }

                keys[position] = key;
                values[position] = (string)entry.Value;
                count++;
            }

            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    output.Append((byte)',');
                }

                JsonText.WriteString(output, keys[i]);
                output.Append((byte)':');
                if (values[i] == null)
                {
                    output.AppendAscii("null");
                }
                else
                {
                    JsonText.WriteString(output, Encoding.UTF8.GetBytes(values[i]));
                }
            }
        }

        static int CompareOrdinal(byte[] left, byte[] right)
        {
            var length = left.Length < right.Length ? left.Length : right.Length;
            for (var i = 0; i < length; i++)
            {
                if (left[i] != right[i])
                {
                    return left[i] < right[i] ? -1 : 1;
                }
            }

            return left.Length - right.Length;
        }
    }
}
