using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Creates the broker sessions that make an address a queue before anything receives from it, so a message sent to the address early is
    /// kept. A session exists on the broker, with its subscriptions and queued messages, from the first connection with <c>CleanStart=false</c>
    /// until its session expiry has passed after the last disconnect.
    /// </summary>
    static class QueueDeclaration
    {
        /// <summary>
        /// Declares a queue by connecting with its consumer's client ID and subscribing, then disconnecting while keeping the session.
        /// The consumer resumes this session when it starts.
        /// </summary>
        /// <param name="purge">Start from a deleted session, which discards the subscriptions and messages the queue held.</param>
        public static Task DeclareQueue(MqttConnectionSettings connection, string queueTopic, IEnumerable<string> additionalTopics, bool purge, CancellationToken cancellationToken = default) =>
            DeclareSession(connection, MqttClientId.ForQueue(queueTopic), additionalTopics.Prepend(queueTopic), cleanStart: purge, $"declaring the queue '{queueTopic}'", cancellationToken);

        /// <summary>
        /// Deletes a queue's session, with the messages in it, without creating a new one. It is for a host that does not set up infrastructure
        /// but still asks for the queue to be purged: the consumer creates the session when it starts.
        /// </summary>
        public static async Task PurgeQueue(MqttConnectionSettings connection, string queueTopic, CancellationToken cancellationToken = default)
        {
            var client = new MqttFactory().CreateMqttClient();

            try
            {
                try
                {
                    // starting clean deletes the old session, and a session expiry of zero deletes the new, empty one when this connection ends
                    await client.ConnectAsync(connection.CreateOptions(MqttClientId.ForQueue(queueTopic), cleanStart: true, sessionExpiry: TimeSpan.Zero), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
                {
                    throw connection.Unreachable($"purging the queue '{queueTopic}'", ex);
                }

                await client.DisconnectQuietly(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                client.DisposeQuietly();
            }
        }

        /// <summary>
        /// Declares an address that this endpoint sends to but does not receive from, such as the error and audit queues. The messages
        /// are kept in a holder session. It is not the session of the address's consumer, so it never takes a live consumer over, and a consumer that
        /// starts later has to collect from it as <c>nsb.{address}.declared</c>.
        /// </summary>
        public static Task DeclareSendingAddress(MqttConnectionSettings connection, string addressTopic, CancellationToken cancellationToken = default) =>
            DeclareSession(connection, MqttClientId.ForDeclaredAddress(addressTopic), [addressTopic], cleanStart: false, $"declaring the address '{addressTopic}'", cancellationToken);

        // Endpoints that start at the same moment all declare the same sending addresses (every endpoint of a system sends to the one error
        // and audit address), and the declarations of an address share one client ID. The broker lets the later connection take the session over
        // from the earlier one, which ends the earlier declaration half way. Declaring is idempotent, so it is simply done again, after a
        // random pause so that the competing declarations do not keep displacing each other.
        const int MaximumDeclarationAttempts = 25;

        static async Task DeclareSession(MqttConnectionSettings connection, string clientId, IEnumerable<string> topics, bool cleanStart, string purpose, CancellationToken cancellationToken)
        {
            var topicList = topics.Distinct().ToArray();

            for (var attempt = 1; ; attempt++)
            {
                if (await TryDeclareSession(connection, clientId, topicList, cleanStart, purpose, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                if (attempt == MaximumDeclarationAttempts)
                {
                    throw new InvalidOperationException($"Could not declare the session '{clientId}' on the MQTT broker at {connection.Server}:{connection.Port} ({purpose}): the connection was lost {MaximumDeclarationAttempts} times in a row, most likely because other clients kept taking the session over. Only one client at a time can use the client ID '{clientId}'.");
                }

                await Task.Delay(Random.Shared.Next(10, 50 + 20 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <returns>false if the connection ended, normally because another client took the session over, before the declaration was complete.</returns>
        static async Task<bool> TryDeclareSession(MqttConnectionSettings connection, string clientId, string[] topics, bool cleanStart, string purpose, CancellationToken cancellationToken)
        {
            var client = new MqttFactory().CreateMqttClient();

            try
            {
                return await TryDeclareSession(client, connection, clientId, topics, cleanStart, purpose, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                client.DisposeQuietly();
            }
        }

        static async Task<bool> TryDeclareSession(IMqttClient client, MqttConnectionSettings connection, string clientId, string[] topics, bool cleanStart, string purpose, CancellationToken cancellationToken)
        {
            // Resuming a session that already holds messages makes the broker start delivering them at once. This connection only creates the
            // session, so it must not consume anything: it leaves what it is sent unacknowledged, and the broker keeps those messages for the
            // consumer that resumes the session. A receive maximum of 1 keeps that to a single message.
            client.ApplicationMessageReceivedAsync += LeaveUnacknowledged;

            try
            {
                await client.ConnectAsync(connection.CreateOptions(clientId, cleanStart, receiveMaximum: 1), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
            {
                throw connection.Unreachable(purpose, ex);
            }

            try
            {
                var builder = new MqttClientSubscribeOptionsBuilder();
                foreach (var topic in topics)
                {
                    builder.WithTopicFilter(new MqttTopicFilterBuilder().WithTopic(topic).WithAtLeastOnceQoS().Build());
                }

                var result = await client.SubscribeAsync(builder.Build(), cancellationToken).ConfigureAwait(false);

                var rejected = result.Items.Where(item => item.ResultCode is not (MqttClientSubscribeResultCode.GrantedQoS0 or MqttClientSubscribeResultCode.GrantedQoS1 or MqttClientSubscribeResultCode.GrantedQoS2)).ToArray();
                if (rejected.Length > 0)
                {
                    throw new InvalidOperationException($"The MQTT broker at {connection.Server}:{connection.Port} refused the subscription to {string.Join(", ", rejected.Select(item => $"'{item.TopicFilter.Topic}' ({item.ResultCode})"))} ({purpose}).");
                }
            }
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken) && (ex is MqttClientUnexpectedDisconnectReceivedException || !client.IsConnected))
            {
                // The takeover DISCONNECT can arrive before the subscribe is sent (the client is then simply "not connected") or while it
                // waits for its answer (an unexpected DISCONNECT). The client's own disconnect event can come later than both, so it is not used.
                return false;
            }

            // The session is complete once the broker has acknowledged the subscription, whatever happens to this connection after that.
            // It outlives the connection, because it was created with a session expiry.
            await client.DisconnectQuietly(cancellationToken).ConfigureAwait(false);

            return true;
        }

#pragma warning disable PS0018 // The signature is the one MQTTnet's event has
        static Task LeaveUnacknowledged(MqttApplicationMessageReceivedEventArgs args)
        {
            args.AutoAcknowledge = false;

            return Task.CompletedTask;
        }
#pragma warning restore PS0018
    }
}
