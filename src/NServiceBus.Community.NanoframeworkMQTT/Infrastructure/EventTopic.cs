using System;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Derives the MQTT topic an event type is published to, the same way <c>NServiceBus.Community.Mqtt</c> does for a non-generic type: <c>events/</c>
    /// plus the namespace-qualified name, where <c>+ / # ` [ ] ,</c> space and <c>*</c> become <c>.</c>.
    /// </summary>
    internal static class EventTopic
    {
        public const string Prefix = "events/";

        public static string ToTopic(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException("eventType");
            }

            return Prefix + Sanitize(eventType.FullName != null ? eventType.FullName : eventType.Name);
        }

        // '+' and '#' are MQTT wildcards, '/' would add topic levels, and the rest would make the name ambiguous or unwieldy.
        // Done on the UTF-8 bytes, because a string's characters outside the basic plane cannot be copied char by char. All of these are ASCII.
        static string Sanitize(string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            for (var i = 0; i < bytes.Length; i++)
            {
                switch ((char)bytes[i])
                {
                    case '+':
                    case '/':
                    case '#':
                    case '`':
                    case '[':
                    case ']':
                    case ',':
                    case ' ':
                    case '*':
                        bytes[i] = (byte)'.';
                        break;
                }
            }

            return new string(Encoding.UTF8.GetChars(bytes));
        }
    }
}
