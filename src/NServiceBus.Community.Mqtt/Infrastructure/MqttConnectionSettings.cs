using MQTTnet.Client;
using MQTTnet.Formatter;

namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Where the broker is and how the transport's sessions are configured. Every connection the transport makes (pumps, dispatcher and
    /// queue declaration) is created from here, so they all speak MQTT 5 and report an unreachable broker the same way.
    /// </summary>
    sealed record MqttConnectionSettings(string Server, int Port, TimeSpan SessionExpiry)
    {
        public MqttClientOptions CreateOptions(string clientId, bool cleanStart, TimeSpan? sessionExpiry = null, ushort? receiveMaximum = null)
        {
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(Server, Port)
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .WithClientId(clientId)
                .WithCleanStart(cleanStart)
                .WithSessionExpiryInterval(ToSeconds(sessionExpiry ?? SessionExpiry));

            if (receiveMaximum.HasValue)
            {
                builder.WithReceiveMaximum(receiveMaximum.Value);
            }

            return builder.Build();
        }

        public InvalidOperationException Unreachable(string purpose, Exception innerException) =>
            new($"Could not connect to the MQTT broker at {Server}:{Port} ({purpose}). Check that the broker is running, that it supports MQTT 5 and persistent sessions, and that the server and port are correct. ({innerException.GetType().Name}: {innerException.Message})", innerException);

        static uint ToSeconds(TimeSpan value) => (uint)Math.Min(Math.Ceiling(value.TotalSeconds), uint.MaxValue);
    }
}
