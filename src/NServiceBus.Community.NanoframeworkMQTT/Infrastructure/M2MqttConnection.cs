using System;
using System.Collections;
using System.Threading;
using nanoFramework.M2Mqtt;
using nanoFramework.M2Mqtt.Messages;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// <see cref="IMqttConnection" /> over nanoFramework.M2Mqtt. M2Mqtt raises every event on one dispatch thread, except <c>ConnectionClosedRequest</c>, which comes
    /// on the receive thread and carries the broker's DISCONNECT. A publish, a subscribe and an unsubscribe return a packet identifier at once and are confirmed by an event, so each
    /// waits on its own event, which the handler sets. The identifier is recorded under the same lock the handler takes, so a confirmation that arrives before the caller has
    /// recorded it waits for it.
    /// </summary>
    internal sealed class M2MqttConnection : IMqttConnection
    {
        // MQTT 5 reason code of a DISCONNECT sent because another client connected with the same client ID
        const MqttReasonCode SessionTakenOver = MqttReasonCode.SessionTakenOver;

        readonly object gate = new object();
        readonly Hashtable publishes = new Hashtable();
        readonly Hashtable subscribes = new Hashtable();
        readonly Hashtable unsubscribes = new Hashtable();

        MqttClient client;
        TimeSpan operationTimeout;
        bool disconnectRequested;
        MqttReasonCode closedReason = MqttReasonCode.Success;

        public event MessageReceivedHandler MessageReceived;

        public event ConnectionLostHandler ConnectionLost;

        public void Connect(MqttConnectOptions options)
        {
            Release();

            operationTimeout = options.OperationTimeout;
            disconnectRequested = false;
            closedReason = MqttReasonCode.Success;

            var created = new MqttClient(options.Host, options.Port, false, null, null, MqttSslProtocols.None);
            created.ProtocolVersion = MqttProtocolVersion.Version_5;
            created.SessionExpiryInterval = options.SessionExpirySeconds;
            created.MaximumPacketSize = (uint)options.MaximumPacketSize;

            created.MqttMsgPublishReceived += OnPublishReceived;
            created.MqttMsgPublished += OnPublished;
            created.MqttMsgSubscribed += OnSubscribed;
            created.MqttMsgUnsubscribed += OnUnsubscribed;
            created.ConnectionClosedRequest += OnConnectionClosedRequest;
            created.ConnectionClosed += OnConnectionClosed;

            MqttReasonCode reason;
            try
            {
                // cleanSession is false: the queue is a persistent session
                reason = created.Connect(options.ClientId, options.Username, options.Password, false, options.KeepAliveSeconds);
            }
            catch (Exception)
            {
                Detach(created);
                throw;
            }

            if (reason != MqttReasonCode.Success)
            {
                Detach(created);
                throw new InvalidOperationException("The broker refused the connection: " + reason + ".");
            }

            lock (gate)
            {
                client = created;
            }
        }

        public void Subscribe(string[] topics)
        {
            var levels = new MqttQoSLevel[topics.Length];
            for (var i = 0; i < levels.Length; i++)
            {
                levels[i] = MqttQoSLevel.AtLeastOnce;
            }

            var pending = new Pending();
            lock (gate)
            {
                subscribes[CurrentClient().Subscribe(topics, levels)] = pending;
            }

            Wait(pending, subscribes, "confirm the subscription");
        }

        public void Unsubscribe(string[] topics)
        {
            var pending = new Pending();
            lock (gate)
            {
                unsubscribes[CurrentClient().Unsubscribe(topics)] = pending;
            }

            Wait(pending, unsubscribes, "confirm the unsubscription");
        }

        public void Publish(string topic, byte[] payload, uint expirySeconds)
        {
            if (expirySeconds != 0)
            {
                throw new NotSupportedException("A message expiry needs a release of nanoFramework.M2Mqtt that can set it on a publish.");
            }

            var pending = new Pending();
            lock (gate)
            {
                publishes[CurrentClient().Publish(topic, payload, null, null, MqttQoSLevel.AtLeastOnce, false)] = pending;
            }

            Wait(pending, publishes, "acknowledge the message");
        }

        public void Disconnect()
        {
            MqttClient current;
            lock (gate)
            {
                disconnectRequested = true;
                current = client;
                client = null;
            }

            if (current != null)
            {
                Detach(current);
                CloseQuietly(current);
            }

            FailAll("the connection was closed");
        }

        MqttClient CurrentClient()
        {
            if (client == null || !client.IsConnected)
            {
                throw new InvalidOperationException("The connection to the broker is closed.");
            }

            return client;
        }

        void Wait(Pending pending, Hashtable table, string description)
        {
            if (!pending.Done.WaitOne((int)(operationTimeout.Ticks / TimeSpan.TicksPerMillisecond), false))
            {
                lock (gate)
                {
                    RemovePending(table, pending);
                }

                throw new InvalidOperationException("The broker did not " + description + " within " + (operationTimeout.Ticks / TimeSpan.TicksPerSecond) + " s.");
            }

            if (!pending.Success)
            {
                throw new InvalidOperationException("The broker did not " + description + ": " + pending.Detail);
            }
        }

        static void RemovePending(Hashtable table, Pending pending)
        {
            object found = null;
            foreach (DictionaryEntry entry in table)
            {
                if (entry.Value == pending)
                {
                    found = entry.Key;
                    break;
                }
            }

            if (found != null)
            {
                table.Remove(found);
            }
        }

        void Complete(Hashtable table, ushort id, bool success, string detail)
        {
            Pending pending;
            lock (gate)
            {
                pending = table[id] as Pending;
                table.Remove(id);
            }

            if (pending != null)
            {
                pending.Success = success;
                pending.Detail = detail;
                pending.Done.Set();
            }
        }

        void FailAll(string detail)
        {
            FailTable(publishes, detail);
            FailTable(subscribes, detail);
            FailTable(unsubscribes, detail);
        }

        void FailTable(Hashtable table, string detail)
        {
            Pending[] all;
            lock (gate)
            {
                all = new Pending[table.Count];
                var index = 0;
                foreach (DictionaryEntry entry in table)
                {
                    all[index++] = (Pending)entry.Value;
                }

                table.Clear();
            }

            for (var i = 0; i < all.Length; i++)
            {
                all[i].Success = false;
                all[i].Detail = detail;
                all[i].Done.Set();
            }
        }

        // ---- events of M2Mqtt

        void OnPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            var handler = MessageReceived;
            if (handler != null)
            {
                handler(e.Topic, e.Message);
            }
        }

        void OnPublished(object sender, MqttMsgPublishedEventArgs e)
        {
            Complete(publishes, e.MessageId, e.IsPublished, "it reported the publish as failed");
        }

        void OnSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            var granted = true;
            for (var i = 0; i < e.GrantedQoSLevels.Length; i++)
            {
                if (e.GrantedQoSLevels[i] == MqttQoSLevel.GrantedFailure)
                {
                    granted = false;
                }
            }

            Complete(subscribes, e.MessageId, granted, "it did not grant a QoS 1 subscription");
        }

        void OnUnsubscribed(object sender, MqttMsgUnsubscribedEventArgs e)
        {
            Complete(unsubscribes, e.MessageId, true, null);
        }

        // raised on the receive thread, before ConnectionClosed, with the broker's DISCONNECT
        void OnConnectionClosedRequest(object sender, ConnectionClosedRequestEventArgs e)
        {
            closedReason = e.Message.ResonCode;
        }

        void OnConnectionClosed(object sender, EventArgs e)
        {
            bool requested;
            lock (gate)
            {
                requested = disconnectRequested;
            }

            FailAll("the connection to the broker was lost");
            if (requested)
            {
                return;
            }

            var handler = ConnectionLost;
            if (handler != null)
            {
                handler(closedReason == SessionTakenOver);
            }
        }

        void Release()
        {
            MqttClient current;
            lock (gate)
            {
                current = client;
                client = null;
            }

            if (current != null)
            {
                Detach(current);
                CloseQuietly(current);
            }
        }

        void Detach(MqttClient detached)
        {
            detached.MqttMsgPublishReceived -= OnPublishReceived;
            detached.MqttMsgPublished -= OnPublished;
            detached.MqttMsgSubscribed -= OnSubscribed;
            detached.MqttMsgUnsubscribed -= OnUnsubscribed;
            detached.ConnectionClosedRequest -= OnConnectionClosedRequest;
            detached.ConnectionClosed -= OnConnectionClosed;
        }

        static void CloseQuietly(MqttClient closing)
        {
            try
            {
                if (closing.IsConnected)
                {
                    closing.Disconnect();
                }
            }
            catch (Exception)
            {
                // the connection was already gone
            }
        }

        sealed class Pending
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public bool Success;
            public string Detail;
        }
    }
}
