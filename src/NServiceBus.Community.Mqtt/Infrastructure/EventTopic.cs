using System.Text;

namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Derives the MQTT topic an event type is published to. The topic is built from the full, namespace-qualified name, so same-named types in
    /// different namespaces do not share a topic. It is valid for publish and subscribe for nested and generic types, and it is stable across
    /// process restarts and runtime versions (assembly-qualified parts are dropped).
    /// </summary>
    static class EventTopic
    {
        public const string Prefix = "events/";

        public static string ToTopic(Type eventType)
        {
            ArgumentNullException.ThrowIfNull(eventType);

            return Prefix + Encode(eventType);
        }

        static string Encode(Type type)
        {
            if (!type.IsGenericType || type.IsGenericTypeDefinition)
            {
                return Sanitize(type.FullName ?? type.Name);
            }

            var builder = new StringBuilder(Sanitize(type.GetGenericTypeDefinition().FullName!));
            foreach (var argument in type.GetGenericArguments())
            {
                builder.Append('.').Append(Encode(argument));
            }

            return builder.ToString();
        }

        // '+' and '#' are MQTT wildcards, '/' would add topic levels, and the rest would make the name ambiguous or unwieldy.
        static string Sanitize(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                builder.Append(character is '+' or '/' or '#' or '`' or '[' or ']' or ',' or ' ' or '*' ? '.' : character);
            }

            return builder.ToString();
        }
    }
}
