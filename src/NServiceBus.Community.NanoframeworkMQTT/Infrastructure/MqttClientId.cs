using System;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Builds the MQTT client ID a device connects with. A queue is a persistent session identified by its client ID, so the ID must be the one a
    /// .NET endpoint of the same name would use: <c>nsb.</c> plus the queue topic, where every character outside <c>A-Z a-z 0-9 . - _ /</c>
    /// is written as <c>~XX</c> per UTF-8 byte, and <c>~</c> itself is escaped.
    /// </summary>
    internal static class MqttClientId
    {
        const string Prefix = "nsb.";
        const string HexDigits = "0123456789ABCDEF";

        public static string ForQueue(string queueTopic)
        {
            if (MqttAddress.IsNullOrWhiteSpace(queueTopic))
            {
                throw new ArgumentException("An address cannot be empty when using the MQTT transport.", "queueTopic");
            }

            // Works on the UTF-8 bytes. Every allowed character is ASCII, and every byte of a multi-byte sequence is above 0x7F, so a byte that is
            // not allowed is exactly a byte to escape. Reading the string char by char would lose characters outside the basic plane on nanoFramework.
            var bytes = Encoding.UTF8.GetBytes(queueTopic);
            var builder = new StringBuilder(Prefix);

            for (var i = 0; i < bytes.Length; i++)
            {
                var value = bytes[i];
                if (IsAllowed(value))
                {
                    builder.Append((char)value);
                    continue;
                }

                builder.Append('~');
                builder.Append(HexDigits[(value >> 4) & 0xF]);
                builder.Append(HexDigits[value & 0xF]);
            }

            return builder.ToString();
        }

        static bool IsAllowed(byte value)
        {
            return (value >= 'a' && value <= 'z')
                || (value >= 'A' && value <= 'Z')
                || (value >= '0' && value <= '9')
                || value == '.' || value == '-' || value == '_' || value == '/';
        }
    }
}
