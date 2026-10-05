using System.Text;

namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Builds the MQTT client IDs the transport connects with. A queue is a persistent session, and a session is identified by its client ID,
    /// so the ID of a queue's consumer must be the same on every start, and two different queues must never share one (the broker would let
    /// one displace the other).
    /// </summary>
    /// <remarks>
    /// The queue and address arguments are the MQTT topics produced by <see cref="MqttAddress"/>, so <c>Sales_Billing</c> and <c>Sales/Billing</c>,
    /// which share a topic, also share a client ID. Characters outside <c>A-Z a-z 0-9 . - _ /</c> are written as <c>~XX</c>, one per UTF-8 byte,
    /// and <c>~</c> itself is escaped, so brokers that accept only a restricted client ID alphabet are served and the mapping stays injective.
    /// A queue named <c>x.declared</c> would collide with the holder session of address <c>x</c>, and a queue named <c>dispatch.{id}</c> with a dispatcher;
    /// both are far outside what NServiceBus endpoint names look like.
    /// </remarks>
    static class MqttClientId
    {
        const string Prefix = "nsb.";
        const string DeclaredSuffix = ".declared";
        const string DispatcherPrefix = Prefix + "dispatch.";

        /// <summary>The session of the consumer that receives from a queue.</summary>
        public static string ForQueue(string queueTopic) => Prefix + Sanitize(queueTopic);

        /// <summary>
        /// The holder session of a sending address that no local endpoint receives from. It is a different session from <see cref="ForQueue"/>
        /// on purpose, so declaring an address can never take over a live consumer of it.
        /// </summary>
        public static string ForDeclaredAddress(string addressTopic) => ForQueue(addressTopic) + DeclaredSuffix;

        /// <summary>The session of a dispatcher. It is unique per dispatcher, because a dispatcher is never resumed.</summary>
        public static string ForDispatcher(Guid dispatcherId) => DispatcherPrefix + dispatcherId.ToString("N");

        static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("An address cannot be empty when using the MQTT transport.", nameof(value));
            }

            var builder = new StringBuilder(value.Length);
            Span<byte> utf8 = stackalloc byte[4 * 2];
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (IsAllowed(character))
                {
                    builder.Append(character);
                    continue;
                }

                var length = char.IsHighSurrogate(character) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) ? 2 : 1;
                var bytes = Encoding.UTF8.GetBytes(value.AsSpan(i, length), utf8);
                foreach (var b in utf8[..bytes])
                {
                    builder.Append('~').Append(b.ToString("X2"));
                }

                i += length - 1;
            }

            return builder.ToString();
        }

        static bool IsAllowed(char character) =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_' or '/';
    }
}
