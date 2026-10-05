using System;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>Writing JSON strings, which the envelope writer and the message body writer share.</summary>
    internal static class JsonText
    {
        const string HexDigits = "0123456789ABCDEF";

        /// <summary>
        /// Writes a JSON string from its UTF-8 bytes. Only the quote, the backslash and control characters are escaped; everything else is raw UTF-8,
        /// which System.Text.Json reads. Every byte of a multi-byte UTF-8 sequence is above 0x7F, so none is mistaken for one of those.
        /// </summary>
        public static void WriteString(ByteBuffer output, byte[] utf8)
        {
            output.Append((byte)'"');
            for (var i = 0; i < utf8.Length; i++)
            {
                var value = utf8[i];
                if (value == '"')
                {
                    output.AppendAscii("\\\"");
                }
                else if (value == '\\')
                {
                    output.AppendAscii("\\\\");
                }
                else if (value < 0x20)
                {
                    WriteControlCharacter(output, value);
                }
                else
                {
                    output.Append(value);
                }
            }

            output.Append((byte)'"');
        }

        static void WriteControlCharacter(ByteBuffer output, byte value)
        {
            switch (value)
            {
                case 0x08:
                    output.AppendAscii("\\b");
                    return;
                case 0x09:
                    output.AppendAscii("\\t");
                    return;
                case 0x0A:
                    output.AppendAscii("\\n");
                    return;
                case 0x0C:
                    output.AppendAscii("\\f");
                    return;
                case 0x0D:
                    output.AppendAscii("\\r");
                    return;
            }

            output.AppendAscii("\\u00");
            output.Append((byte)HexDigits[value >> 4]);
            output.Append((byte)HexDigits[value & 0x0F]);
        }
    }
}
