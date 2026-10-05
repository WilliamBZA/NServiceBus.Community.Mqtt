using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using NServiceBus.Performance.TimeToBeReceived;

namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Sends and publishes messages. Dispatch completes only after the broker has acknowledged every publish, and fails, naming the
    /// destination, if the broker is unreachable or rejects one. The dispatcher's own session is not kept: it never receives anything.
    /// </summary>
    sealed class MqttDispatcher(MqttConnectionSettings connection) : IMessageDispatcher
    {
        public async Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
        {
            // All addresses and topics are worked out first, so an address that cannot be used fails the dispatch before anything is published.
            var publishes = new List<(OutgoingMessage Message, string Topic, string Destination, uint? Expiry)>();

            foreach (var operation in outgoingMessages.UnicastTransportOperations)
            {
                publishes.Add((operation.Message, MqttAddress.ToTopic(operation.Destination), operation.Destination, ToMessageExpiry(operation.Properties.DiscardIfNotReceivedBefore)));
            }

            foreach (var operation in outgoingMessages.MulticastTransportOperations)
            {
                var expiry = ToMessageExpiry(operation.Properties.DiscardIfNotReceivedBefore);

                // an event is published once for every type in its hierarchy, so a subscriber to a base type or an interface receives it
                foreach (var type in EventTypeHierarchy.Enumerate(operation.MessageType))
                {
                    publishes.Add((operation.Message, EventTopic.ToTopic(type), type.FullName ?? type.Name, expiry));
                }
            }

            // in order, and the first failure stops the batch
            foreach (var (message, topic, destination, expiry) in publishes)
            {
                await Publish(message, topic, destination, expiry, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The time to be received of a message is the MQTT 5 message expiry interval: the broker drops the message if it has not delivered it
        /// by then, including a message that is waiting in the session of an endpoint that is offline. The interval is in whole seconds, so a
        /// fraction of a second is rounded up, and it is never zero, which would expire the message at once.
        /// </summary>
        /// <returns>null if the message does not expire.</returns>
        internal static uint? ToMessageExpiry(DiscardIfNotReceivedBefore? discardIfNotReceivedBefore)
        {
            if (discardIfNotReceivedBefore?.MaxTime is not { } maxTime || maxTime >= TimeSpan.FromSeconds(uint.MaxValue))
            {
                return null;
            }

            return (uint)Math.Max(1, Math.Ceiling(maxTime.TotalSeconds));
        }

        public Task Connect(CancellationToken cancellationToken = default) => GetConnectedClient(cancellationToken);

        public async Task Shutdown(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                shutDown = true;

                var current = client;
                client = null;

                if (current is null)
                {
                    return;
                }

                try
                {
                    await current.DisconnectQuietly(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    current.DisposeQuietly();
                }
            }
            finally
            {
                gate.Release();
            }
        }

        async Task Publish(OutgoingMessage message, string topic, string destination, uint? expiry, CancellationToken cancellationToken)
        {
            var builder = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithPayload(WireFormat.Encode(message));

            if (expiry.HasValue)
            {
                builder.WithMessageExpiryInterval(expiry.Value);
            }

            var mqttMessage = builder.Build();

            // A connection that was lost since the last dispatch is reconnected once, inline. If that fails too, the dispatch fails,
            // and the caller's recoverability policy decides what happens to the message.
            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                MqttClientPublishResult result;
                try
                {
                    var current = await GetConnectedClient(cancellationToken).ConfigureAwait(false);

                    try
                    {
                        result = await current.PublishAsync(mqttMessage, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!ex.IsCausedBy(cancellationToken) && !current.IsConnected && attempt == 1)
                    {
                        // the connection dropped under this publish, so try it once more on a new one
                        continue;
                    }
                }
                catch (Exception ex) when (!ex.IsCausedBy(cancellationToken) && ex is not ObjectDisposedException)
                {
                    throw new InvalidOperationException($"Could not dispatch the message to '{destination}' (topic '{topic}') through the MQTT broker at {connection.Server}:{connection.Port}. ({ex.GetType().Name}: {ex.Message})", ex);
                }

                // A publish with no subscribers still succeeds: the broker accepted it, there is just nobody to give it to.
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException($"The MQTT broker at {connection.Server}:{connection.Port} did not accept the message for '{destination}' (topic '{topic}'): {result.ReasonCode} {result.ReasonString}".TrimEnd());
                }

                return;
            }
        }

        async Task<IMqttClient> GetConnectedClient(CancellationToken cancellationToken)
        {
            if (client is { IsConnected: true } connected)
            {
                return connected;
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(shutDown, this);

                if (client is { IsConnected: true } alreadyReconnected)
                {
                    return alreadyReconnected;
                }

                client?.DisposeQuietly();
                client = null;

                var fresh = new MqttFactory().CreateMqttClient();
                try
                {
                    await fresh.ConnectAsync(connection.CreateOptions(MqttClientId.ForDispatcher(Guid.NewGuid()), cleanStart: true, sessionExpiry: TimeSpan.Zero), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
                {
                    fresh.DisposeQuietly();
                    throw connection.Unreachable("dispatching", ex);
                }
                catch
                {
                    fresh.DisposeQuietly();
                    throw;
                }

                client = fresh;
                return fresh;
            }
            finally
            {
                gate.Release();
            }
        }

        readonly SemaphoreSlim gate = new(1, 1);
        volatile IMqttClient? client;
        bool shutDown;
    }
}
