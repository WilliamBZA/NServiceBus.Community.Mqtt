using System;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Maps NServiceBus queue addresses to MQTT topics, in the same way <c>NServiceBus.Community.Mqtt</c> does: <c>_</c> becomes <c>/</c>.
    /// </summary>
    internal static class MqttAddress
    {
        public static string ToTopic(string address)
        {
            if (IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("An address cannot be empty when using the MQTT transport.", "address");
            }

            if (address.IndexOf('+') >= 0 || address.IndexOf('#') >= 0)
            {
                throw Invalid(address, "it contains the MQTT wildcard character '+' or '#'");
            }

            if (address[0] == '$')
            {
                throw Invalid(address, "it starts with '$', which MQTT reserves for broker topics");
            }

            // nanoFramework's string has no Replace, and a string holds UTF-8 whose characters outside the basic plane cannot be copied
            // one char at a time, so the replacement is done on the bytes. A '_' byte is never part of a multi-byte UTF-8 sequence.
            var bytes = Encoding.UTF8.GetBytes(address);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'_')
                {
                    bytes[i] = (byte)'/';
                }
            }

            return new string(Encoding.UTF8.GetChars(bytes));
        }

        public static bool IsNullOrWhiteSpace(string value)
        {
            if (value == null)
            {
                return true;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (!IsWhiteSpace(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // the set that .NET's char.IsWhiteSpace recognizes, which nanoFramework's char does not have
        static bool IsWhiteSpace(char character)
        {
            // numeric codes, because this compiler reads a unicode escape for a line separator as a real line break, even inside a literal
            int code = character;
            return (code >= 0x0009 && code <= 0x000D)
                || code == 0x0020
                || code == 0x0085
                || code == 0x00A0
                || code == 0x1680
                || (code >= 0x2000 && code <= 0x200A)
                || code == 0x2028
                || code == 0x2029
                || code == 0x202F
                || code == 0x205F
                || code == 0x3000;
        }

        static ArgumentException Invalid(string address, string reason)
        {
            return new ArgumentException(
                "The address '" + address + "' cannot be used with the MQTT transport because " + reason + ". Addresses map to MQTT topics, so they cannot contain '+' or '#' and cannot start with '$'.",
                "address");
        }
    }
}
