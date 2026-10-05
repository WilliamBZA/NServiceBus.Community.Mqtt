using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>Sizes of MQTT 5 packets, so that a message that the broker would refuse or drop is rejected before anything is published.</summary>
    internal static class MqttPacket
    {
        // the property identifier of the message expiry interval, and its four bytes
        const int MessageExpiryPropertyLength = 5;

        /// <summary>
        /// The exact size of a QoS 1 PUBLISH packet: the fixed header, the remaining length, the topic, the packet identifier, the properties
        /// (none, except the message expiry interval when there is one) and the payload.
        /// </summary>
        public static long PublishSize(string topic, int payloadLength, uint expirySeconds)
        {
            var topicLength = Encoding.UTF8.GetBytes(topic).Length;
            var propertiesLength = expirySeconds == 0 ? 0 : MessageExpiryPropertyLength;

            long remaining = 2L + topicLength + 2 + VariableByteLength(propertiesLength) + propertiesLength + payloadLength;
            return 1 + VariableByteLength(remaining) + remaining;
        }

        /// <summary>The number of bytes of MQTT's variable byte integer for a value.</summary>
        public static int VariableByteLength(long value)
        {
            if (value < 128)
            {
                return 1;
            }

            if (value < 16384)
            {
                return 2;
            }

            if (value < 2097152)
            {
                return 3;
            }

            return 4;
        }
    }
}
