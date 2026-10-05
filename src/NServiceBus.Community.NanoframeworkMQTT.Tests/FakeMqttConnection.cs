using System;
using System.Collections;
using System.Threading;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>A message that the fake broker received from a publisher.</summary>
    internal sealed class PublishedMessage
    {
        public PublishedMessage(string clientId, string topic, byte[] payload, uint expirySeconds)
        {
            ClientId = clientId;
            Topic = topic;
            Payload = payload;
            ExpirySeconds = expirySeconds;
        }

        /// <summary>The client that published it, or <c>null</c> when a test published it as a foreign client.</summary>
        public string ClientId { get; private set; }

        public string Topic { get; private set; }

        public byte[] Payload { get; private set; }

        public uint ExpirySeconds { get; private set; }
    }

    /// <summary>
    /// An in-memory MQTT 5 broker for the device tests: persistent sessions that keep their subscriptions and the messages sent while the client is
    /// offline, QoS 1 subscriptions, takeover of a session with reason <c>0x8E</c>, a maximum packet size per client, and failures a test can switch on.
    /// Messages are delivered on the thread that publishes, which only enqueues, as the endpoint does.
    /// </summary>
    internal sealed class FakeBroker
    {
        readonly object gate = new object();
        readonly Hashtable sessions = new Hashtable();
        readonly ArrayList published = new ArrayList();

        /// <summary>When false, a connect fails as if nothing listened at the host and port.</summary>
        public bool Reachable = true;

        /// <summary>When set, a connect is refused with this text.</summary>
        public string RefuseConnectWith;

        /// <summary>A subscribe to this topic is answered with a failure code.</summary>
        public string RejectSubscriptionTo;

        public int ConnectCount;

        public ArrayList Published
        {
            get
            {
                lock (gate)
                {
                    return (ArrayList)published.Clone();
                }
            }
        }

        public int PublishedCount(string topic)
        {
            var count = 0;
            var all = Published;
            for (var i = 0; i < all.Count; i++)
            {
                if (((PublishedMessage)all[i]).Topic == topic)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Messages sent by the clients, or by a test as a foreign publisher, to a topic.</summary>
        public ArrayList PublishedTo(string topic)
        {
            var result = new ArrayList();
            var all = Published;
            for (var i = 0; i < all.Count; i++)
            {
                if (((PublishedMessage)all[i]).Topic == topic)
                {
                    result.Add(all[i]);
                }
            }

            return result;
        }

        /// <summary>Publishes as a client that is not under test, such as a .NET endpoint.</summary>
        public void PublishAsForeignClient(string topic, byte[] payload)
        {
            Accept(null, topic, payload, 0);
        }

        /// <summary>Ends the session: its subscriptions and the messages waiting in it are gone.</summary>
        public void ExpireSession(string clientId)
        {
            lock (gate)
            {
                sessions.Remove(clientId);
            }
        }

        /// <summary>The broker's side of a lost connection, such as a restart: the client sees the connection end without asking for it.</summary>
        public void DropConnection(string clientId)
        {
            DropConnection(clientId, false);
        }

        /// <summary>A lost connection after which the session no longer exists, such as a broker that restarted without persistence. Both happen before the client can reconnect.</summary>
        public void DropConnectionAndExpireSession(string clientId)
        {
            DropConnection(clientId, true);
        }

        void DropConnection(string clientId, bool expireSession)
        {
            FakeMqttConnection online;
            lock (gate)
            {
                var session = sessions[clientId] as Session;
                online = session == null ? null : session.Online;
                if (session != null)
                {
                    session.Online = null;
                    if (expireSession)
                    {
                        sessions.Remove(clientId);
                    }
                }
            }

            if (online != null)
            {
                online.EndWithoutRequest(false);
            }
        }

        public bool IsSubscribed(string clientId, string topic)
        {
            lock (gate)
            {
                var session = sessions[clientId] as Session;
                return session != null && session.Subscriptions.Contains(topic);
            }
        }

        public bool IsOnline(string clientId)
        {
            lock (gate)
            {
                var session = sessions[clientId] as Session;
                return session != null && session.Online != null;
            }
        }

        public bool HasSession(string clientId)
        {
            lock (gate)
            {
                return sessions.Contains(clientId);
            }
        }

        public int WaitingFor(string clientId)
        {
            lock (gate)
            {
                var session = sessions[clientId] as Session;
                return session == null ? 0 : session.Waiting.Count;
            }
        }

        // ---- what a connection does

        internal void Connect(FakeMqttConnection connection, MqttConnectOptions options)
        {
            FakeMqttConnection displaced = null;
            ArrayList backlog = null;

            lock (gate)
            {
                ConnectCount++;

                if (!Reachable)
                {
                    throw new InvalidOperationException("No connection could be made because the target machine actively refused it.");
                }

                if (RefuseConnectWith != null)
                {
                    throw new InvalidOperationException("The broker refused the connection: " + RefuseConnectWith + ".");
                }

                var session = sessions[options.ClientId] as Session;
                if (session == null)
                {
                    session = new Session();
                    sessions[options.ClientId] = session;
                }

                if (session.Online != null && session.Online != connection)
                {
                    displaced = session.Online;
                }

                session.Online = connection;
                session.MaximumPacketSize = options.MaximumPacketSize;

                backlog = session.Waiting;
                session.Waiting = new ArrayList();
            }

            if (displaced != null)
            {
                displaced.EndWithoutRequest(true);
            }

            // a resumed session delivers what it kept, in order, right after the connection is accepted
            for (var i = 0; i < backlog.Count; i++)
            {
                var message = (PublishedMessage)backlog[i];
                connection.Receive(message.Topic, message.Payload);
            }
        }

        internal void Subscribe(string clientId, string[] topics)
        {
            lock (gate)
            {
                var session = (Session)sessions[clientId];
                for (var i = 0; i < topics.Length; i++)
                {
                    if (topics[i] == RejectSubscriptionTo)
                    {
                        throw new InvalidOperationException("The broker did not grant the subscription to '" + topics[i] + "' (reason code 0x80).");
                    }
                }

                for (var i = 0; i < topics.Length; i++)
                {
                    session.Subscriptions[topics[i]] = true;
                }
            }
        }

        internal void Unsubscribe(string clientId, string[] topics)
        {
            lock (gate)
            {
                var session = (Session)sessions[clientId];
                for (var i = 0; i < topics.Length; i++)
                {
                    session.Subscriptions.Remove(topics[i]);
                }
            }
        }

        internal void Disconnected(string clientId, FakeMqttConnection connection)
        {
            lock (gate)
            {
                var session = sessions[clientId] as Session;
                if (session != null && session.Online == connection)
                {
                    session.Online = null;
                }
            }
        }

        internal void Accept(string clientId, string topic, byte[] payload, uint expirySeconds)
        {
            var message = new PublishedMessage(clientId, topic, payload, expirySeconds);

            lock (gate)
            {
                published.Add(message);
            }

            // delivered outside the lock, so that a client that publishes from its receive callback cannot deadlock
            ArrayList deliveries = new ArrayList();
            lock (gate)
            {
                foreach (DictionaryEntry entry in sessions)
                {
                    var session = (Session)entry.Value;
                    if (!session.Subscriptions.Contains(topic))
                    {
                        continue;
                    }

                    // a client that declared a maximum packet size is never sent a larger packet
                    if (MqttPacket.PublishSize(topic, payload.Length, expirySeconds) > session.MaximumPacketSize)
                    {
                        continue;
                    }

                    if (session.Online == null)
                    {
                        session.Waiting.Add(message);
                    }
                    else
                    {
                        deliveries.Add(session.Online);
                    }
                }
            }

            for (var i = 0; i < deliveries.Count; i++)
            {
                ((FakeMqttConnection)deliveries[i]).Receive(topic, payload);
            }
        }

        sealed class Session
        {
            public readonly Hashtable Subscriptions = new Hashtable();
            public ArrayList Waiting = new ArrayList();
            public FakeMqttConnection Online;
            public int MaximumPacketSize = int.MaxValue;
        }
    }

    /// <summary>The client side of the fake broker, as the endpoint sees an MQTT connection.</summary>
    internal sealed class FakeMqttConnection : IMqttConnection
    {
        readonly FakeBroker broker;
        readonly object gate = new object();
        string clientId;
        bool connected;
        int publishFailures;
        int publishesThatSucceed;
        string publishFailureText;
        int connectFailures;
        string connectFailureText;

        public FakeMqttConnection(FakeBroker broker)
        {
            this.broker = broker;
        }

        public event MessageReceivedHandler MessageReceived;

        public event ConnectionLostHandler ConnectionLost;

        public MqttConnectOptions LastOptions;
        public int Connects;
        public int Disconnects;
        public int PublishAttempts;

        /// <summary>Every subscribe call, as the topics of that call.</summary>
        public readonly ArrayList SubscribeCalls = new ArrayList();

        public readonly ArrayList UnsubscribeCalls = new ArrayList();

        /// <summary>The reason a connection was ended by the broker: true for a takeover.</summary>
        public ArrayList LostConnections = new ArrayList();

        public bool IsConnected
        {
            get
            {
                lock (gate)
                {
                    return connected;
                }
            }
        }

        /// <summary>The next publishes fail with this text.</summary>
        public void FailPublishes(int count, string text)
        {
            FailPublishesAfter(0, count, text);
        }

        /// <summary>The next publishes fail with this text, after <paramref name="succeed" /> publishes that work.</summary>
        public void FailPublishesAfter(int succeed, int count, string text)
        {
            lock (gate)
            {
                publishesThatSucceed = succeed;
                publishFailures = count;
                publishFailureText = text;
            }
        }

        /// <summary>The next connects fail with this text, which includes the reconnects.</summary>
        public void FailConnects(int count, string text)
        {
            lock (gate)
            {
                connectFailures = count;
                connectFailureText = text;
            }
        }

        public void Connect(MqttConnectOptions options)
        {
            lock (gate)
            {
                Connects++;
                if (connectFailures > 0)
                {
                    connectFailures--;
                    throw new InvalidOperationException(connectFailureText);
                }
            }

            LastOptions = options;
            clientId = options.ClientId;
            broker.Connect(this, options);

            lock (gate)
            {
                connected = true;
            }
        }

        public void Subscribe(string[] topics)
        {
            CheckConnected();
            lock (gate)
            {
                SubscribeCalls.Add(topics);
            }

            broker.Subscribe(clientId, topics);
        }

        public void Unsubscribe(string[] topics)
        {
            CheckConnected();
            lock (gate)
            {
                UnsubscribeCalls.Add(topics);
            }

            broker.Unsubscribe(clientId, topics);
        }

        public void Publish(string topic, byte[] payload, uint expirySeconds)
        {
            CheckConnected();
            lock (gate)
            {
                PublishAttempts++;
                if (publishesThatSucceed > 0)
                {
                    publishesThatSucceed--;
                }
                else if (publishFailures > 0)
                {
                    publishFailures--;
                    throw new InvalidOperationException(publishFailureText);
                }
            }

            broker.Accept(clientId, topic, payload, expirySeconds);
        }

        public void Disconnect()
        {
            lock (gate)
            {
                if (!connected)
                {
                    return;
                }

                connected = false;
                Disconnects++;
            }

            broker.Disconnected(clientId, this);
        }

        void CheckConnected()
        {
            lock (gate)
            {
                if (!connected)
                {
                    throw new InvalidOperationException("The connection to the broker is closed.");
                }
            }
        }

        internal void Receive(string topic, byte[] payload)
        {
            var handler = MessageReceived;
            if (handler != null)
            {
                handler(topic, payload);
            }
        }

        /// <summary>The broker ended the connection: a takeover (reason 0x8E) or a drop.</summary>
        internal void EndWithoutRequest(bool takenOver)
        {
            lock (gate)
            {
                connected = false;
                LostConnections.Add(takenOver);
            }

            var handler = ConnectionLost;
            if (handler != null)
            {
                handler(takenOver);
            }
        }
    }
}
