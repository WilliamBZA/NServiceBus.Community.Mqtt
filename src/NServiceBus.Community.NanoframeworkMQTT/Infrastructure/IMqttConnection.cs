using System;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>A message the broker delivered. Raised on the connection's own thread, so a handler of it must only enqueue.</summary>
    internal delegate void MessageReceivedHandler(string topic, byte[] payload);

    /// <summary>The connection ended without the endpoint asking for it. <paramref name="takenOver" /> is true for MQTT 5 reason code <c>0x8E</c>, another client took over the session.</summary>
    internal delegate void ConnectionLostHandler(bool takenOver);

    /// <summary>What a connection needs to know to resume the endpoint's queue session.</summary>
    internal sealed class MqttConnectOptions
    {
        public string ClientId { get; set; }

        public string Host { get; set; }

        public int Port { get; set; }

        /// <summary><c>null</c> when the broker needs no credentials.</summary>
        public string Username { get; set; }

        public string Password { get; set; }

        public uint SessionExpirySeconds { get; set; }

        /// <summary>The largest packet the broker may send the device.</summary>
        public int MaximumPacketSize { get; set; }

        public ushort KeepAliveSeconds { get; set; }

        /// <summary>How long a publish, a subscribe or an unsubscribe waits for the broker's answer.</summary>
        public TimeSpan OperationTimeout { get; set; }
    }

    /// <summary>
    /// The broker as the endpoint sees it: one MQTT 5 connection with a persistent session. <c>M2MqttConnection</c> adapts nanoFramework.M2Mqtt, and the
    /// device tests use an in-memory broker. A connection can be connected again after it has ended, and every failure is an exception whose message
    /// says what the broker answered.
    /// </summary>
    internal interface IMqttConnection
    {
        event MessageReceivedHandler MessageReceived;

        event ConnectionLostHandler ConnectionLost;

        /// <summary>Resumes the session. Returns once the broker has accepted the connection.</summary>
        void Connect(MqttConnectOptions options);

        /// <summary>Subscribes to every topic at QoS 1 and returns once the broker has confirmed all of them.</summary>
        void Subscribe(string[] topics);

        /// <summary>Returns once the broker has confirmed.</summary>
        void Unsubscribe(string[] topics);

        /// <summary>Publishes at QoS 1 without retain, and returns once the broker has acknowledged it.</summary>
        /// <param name="topic">The topic to publish to.</param>
        /// <param name="payload">The packet payload.</param>
        /// <param name="expirySeconds">The MQTT 5 message expiry interval, or <c>0</c> for a message that does not expire.</param>
        void Publish(string topic, byte[] payload, uint expirySeconds);

        /// <summary>Ends the connection with a normal DISCONNECT, which keeps the session. Does nothing when not connected.</summary>
        void Disconnect();
    }
}
