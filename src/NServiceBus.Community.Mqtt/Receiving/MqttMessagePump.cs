using System.Collections.Concurrent;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using NServiceBus.Extensibility;
using NServiceBus.Logging;

namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Receives from one NServiceBus queue. The queue is a durable, single-consumer MQTT 5 session: a stable client ID, a persistent session and
    /// plain QoS 1 subscriptions, so the broker keeps the queue's messages while the endpoint is offline and a second consumer of the same
    /// queue takes the session over from the first (which is reported as a critical error, because scale-out is not supported).
    /// </summary>
    /// <remarks>
    /// The MQTTnet message handler only puts a message into a bounded channel. A worker loop takes messages from the channel and runs them
    /// under a <see cref="ConcurrencyLimiter"/>, so handlers never block the client's read loop or keep-alive.
    /// A message is acknowledged to the broker only when the endpoint is done with it (see <see cref="TransportTransactionMode"/>),
    /// and a message that is not acknowledged is redelivered by the broker when the session reconnects.
    /// </remarks>
    sealed class MqttMessagePump : IMessageReceiver, IDisposable
    {
        // How many unacknowledged messages the broker may send at once. It is not tied to the concurrency limit, so that raising the limit
        // while running takes effect without a reconnect. It also sizes the intake channel, so the channel can never fill up while the broker honours it.
        const int MinimumReceiveMaximum = 256;
        static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
        static readonly TimeSpan MaximumReconnectDelay = TimeSpan.FromSeconds(30);
        static readonly TimeSpan RecoverabilityFailureDelay = TimeSpan.FromMilliseconds(500);
        static readonly ILog Logger = LogManager.GetLogger<MqttMessagePump>();

        public MqttMessagePump(
            string id,
            string receiveAddress,
            MqttConnectionSettings connection,
            TransportTransactionMode transactionMode,
            bool usePublishSubscribe,
            IEnumerable<string> explicitTopics,
            Action<string, Exception, CancellationToken> onCritical)
        {
            Id = id;
            ReceiveAddress = receiveAddress;
            this.connection = connection;
            this.transactionMode = transactionMode;
            this.explicitTopics = explicitTopics.Distinct().ToArray();
            this.onCritical = onCritical;
            clientId = MqttClientId.ForQueue(receiveAddress);
            Subscriptions = usePublishSubscribe ? new MqttSubscriptionManager(this) : null;
        }

        public string Id { get; }

        public string ReceiveAddress { get; }

        public ISubscriptionManager? Subscriptions { get; }

        public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken = default)
        {
            this.onMessage = onMessage;
            this.onError = onError;
            limiter = new ConcurrencyLimiter(limitations.MaxConcurrency);

            return Task.CompletedTask;
        }

        public Task ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken = default)
        {
            limiter?.SetLimit(limitations.MaxConcurrency);

            return Task.CompletedTask;
        }

        public async Task StartReceive(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (onMessage is null || onError is null || limiter is null)
            {
                throw new InvalidOperationException("The message pump must be initialized before it is started.");
            }

            if (started)
            {
                throw new InvalidOperationException($"The message pump for '{ReceiveAddress}' has already been started.");
            }

            started = true;
            stopping = false;
            sessionTakenOver = false;
            processingTokenSource = new CancellationTokenSource();
            workerTokenSource = new CancellationTokenSource();
            reconnectTokenSource = new CancellationTokenSource();
            receiveMaximum = (ushort)Math.Min(ushort.MaxValue, Math.Max(limiter.Limit, MinimumReceiveMaximum));
            intake = Channel.CreateBounded<ReceivedMessage>(new BoundedChannelOptions(receiveMaximum) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
            workerLoop = Task.Run(() => RunWorkerLoop(intake.Reader, workerTokenSource.Token), CancellationToken.None);

            var connected = false;
            try
            {
                await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await ConnectAsync(cancellationToken).ConfigureAwait(false);
                    connected = true;
                }
                finally
                {
                    connectionLock.Release();
                }
            }
            finally
            {
                if (!connected)
                {
                    // nothing was received yet, so this only releases what the start acquired
                    await StopReceive(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        public async Task StopReceive(CancellationToken cancellationToken = default)
        {
            if (!started)
            {
                return;
            }

            started = false;
            stopping = true;

            // stop looking for work: nothing new is taken from the intake, and buffered messages stay unacknowledged for redelivery
            reconnectTokenSource!.Cancel();
            intake!.Writer.TryComplete();
            workerTokenSource!.Cancel();

            // Handlers are only cancelled if the caller gives up waiting for them. Otherwise stopping waits for them to finish.
            using (cancellationToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), processingTokenSource))
            {
                await workerLoop!.ConfigureAwait(false);
                await Task.WhenAll(inFlight.Values).ConfigureAwait(false);
            }

            if (reconnectLoop is not null)
            {
                await reconnectLoop.ConfigureAwait(false);
            }

            await connectionLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                connectionLock.Release();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            reconnectTokenSource?.Cancel();
            workerTokenSource?.Cancel();
            processingTokenSource?.Cancel();

            client?.DisposeQuietly();
            client = null;

            reconnectTokenSource?.Dispose();
            workerTokenSource?.Dispose();
            processingTokenSource?.Dispose();
            connectionLock.Dispose();
        }

        // Subscribing works before and after StartReceive. Before, the subscription is applied when the pump connects.
        internal async Task SubscribeToEvent(Type eventType, CancellationToken cancellationToken = default)
        {
            var topic = EventTopic.ToTopic(eventType);
            subscribedEventTypes[FullName(eventType)] = topic;

            await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                pendingUnsubscribes.Remove(topic);

                if (client is { IsConnected: true } connected)
                {
                    await TrySubscribeAsync(connected, [topic], cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                connectionLock.Release();
            }
        }

        internal async Task UnsubscribeFromEvent(Type eventType, CancellationToken cancellationToken = default)
        {
            if (!subscribedEventTypes.TryRemove(FullName(eventType), out var topic))
            {
                return;
            }

            await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (client is { IsConnected: true } connected)
                {
                    try
                    {
                        await connected.UnsubscribeAsync(topic, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex) when (!ex.IsCausedBy(cancellationToken) && !connected.IsConnected)
                    {
                        Logger.Warn($"The connection was lost while unsubscribing from '{topic}'. It is removed after the pump reconnects.", ex);
                    }
                }

                // the broker still has the subscription in the session, so it is removed when the pump is next connected
                pendingUnsubscribes.Add(topic);
            }
            finally
            {
                connectionLock.Release();
            }
        }

        // Callers hold connectionLock.
        async Task ConnectAsync(CancellationToken cancellationToken)
        {
            var newClient = new MqttFactory().CreateMqttClient();
            var generation = Interlocked.Increment(ref connectionGeneration);

            newClient.ApplicationMessageReceivedAsync += args => OnMessageReceived(generation, args);
            newClient.DisconnectedAsync += args => OnDisconnected(newClient, args);

            try
            {
                // the queue's session is kept across connections: a purge deletes it when the transport is set up, never here
                var options = connection.CreateOptions(clientId, cleanStart: false, receiveMaximum: receiveMaximum);
                await newClient.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
            {
                newClient.DisposeQuietly();
                throw connection.Unreachable($"receiving from '{ReceiveAddress}'", ex);
            }
            catch
            {
                newClient.DisposeQuietly();
                throw;
            }

            var previous = client;
            client = newClient;
            previous?.DisposeQuietly();

            try
            {
                var topics = new HashSet<string> { ReceiveAddress };
                topics.UnionWith(explicitTopics);
                topics.UnionWith(subscribedEventTypes.Values);

                // The broker keeps the subscriptions of a session that is still present, but the set may have changed while disconnected,
                // and subscribing again to a topic that is already subscribed only replaces the subscription.
                await TrySubscribeAsync(newClient, topics, cancellationToken).ConfigureAwait(false);

                foreach (var topic in pendingUnsubscribes.ToArray())
                {
                    await newClient.UnsubscribeAsync(topic, cancellationToken).ConfigureAwait(false);
                    pendingUnsubscribes.Remove(topic);
                }
            }
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
            {
                if (newClient.IsConnected)
                {
                    throw;
                }

                // The connection dropped before the subscriptions were all applied. OnDisconnected could not tell, because the client was not
                // current yet, so the supervisor is started from here. It applies all the subscriptions again.
                Logger.Warn($"The connection to the MQTT broker at {connection.Server}:{connection.Port} was lost while subscribing.", ex);
                StartReconnecting();
            }
        }

        async Task TrySubscribeAsync(IMqttClient target, IEnumerable<string> topics, CancellationToken cancellationToken)
        {
            var builder = new MqttClientSubscribeOptionsBuilder();
            foreach (var topic in topics)
            {
                builder.WithTopicFilter(new MqttTopicFilterBuilder().WithTopic(topic).WithAtLeastOnceQoS().Build());
            }

            var subscribeOptions = builder.Build();
            if (subscribeOptions.TopicFilters.Count == 0)
            {
                return;
            }

            try
            {
                var result = await target.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);

                var rejected = result.Items.Where(item => item.ResultCode is not (MqttClientSubscribeResultCode.GrantedQoS0 or MqttClientSubscribeResultCode.GrantedQoS1 or MqttClientSubscribeResultCode.GrantedQoS2)).ToArray();
                if (rejected.Length > 0)
                {
                    throw new InvalidOperationException($"The MQTT broker at {connection.Server}:{connection.Port} refused the subscription to {string.Join(", ", rejected.Select(item => $"'{item.TopicFilter.Topic}' ({item.ResultCode})"))}.");
                }
            }
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken) && ex is not InvalidOperationException && !target.IsConnected)
            {
                // the pump reconnects and applies the whole subscription set again
                Logger.Warn("The connection was lost while subscribing. The subscriptions are applied after the pump reconnects.", ex);
            }
        }

        async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            var current = client;
            client = null;

            if (current is null)
            {
                return;
            }

            try
            {
                // the session is kept: it was created with a session expiry, and a normal disconnect does not end it
                await current.DisconnectQuietly(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                current.DisposeQuietly();
            }
        }

#pragma warning disable PS0018 // The event handlers have the signatures MQTTnet gives them
        async Task OnMessageReceived(long generation, MqttApplicationMessageReceivedEventArgs args)
        {
            // the message is acknowledged by whoever finishes with it, and not when this handler returns
            args.AutoAcknowledge = false;

            var message = new ReceivedMessage(args, generation, args.ApplicationMessage.Topic, args.ApplicationMessage.PayloadSegment.ToArray());
            var stopCancellationToken = workerTokenSource!.Token;

            try
            {
                // Waits only if the broker sends more than ReceiveMaximum messages. Leaving the message unacknowledged is always safe.
                await intake!.Writer.WriteAsync(message, stopCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopCancellationToken.IsCancellationRequested)
            {
                // stopping: the broker redelivers the message when the session is next resumed
            }
            catch (ChannelClosedException)
            {
                // stopping
            }
        }

        Task OnDisconnected(IMqttClient disconnected, MqttClientDisconnectedEventArgs args)
        {
            // A disconnect we asked for, one from a connection that has since been replaced, or one during startup (the client is not
            // current until it has connected) is not a lost connection.
            if (stopping || !ReferenceEquals(disconnected, client))
            {
                return Task.CompletedTask;
            }

            if (args.Reason == MqttClientDisconnectReason.SessionTakenOver)
            {
                // another consumer connected with this queue's client ID. Reconnecting would only make the two take the session off each other.
                sessionTakenOver = true;
                var message = $"The consumer of queue '{ReceiveAddress}' was displaced by another consumer of the same queue. Only one consumer per queue is supported, so every instance needs its own endpoint name. This endpoint stops receiving from '{ReceiveAddress}'.";
                onCritical(message, new InvalidOperationException(message, args.Exception), CancellationToken.None);

                return Task.CompletedTask;
            }

            Logger.Warn($"The connection to the MQTT broker at {connection.Server}:{connection.Port} was lost ({args.Reason}). Reconnecting.", args.Exception);
            StartReconnecting();

            return Task.CompletedTask;
        }
#pragma warning restore PS0018

        void StartReconnecting()
        {
            if (stopping || sessionTakenOver || Interlocked.CompareExchange(ref reconnecting, 1, 0) != 0)
            {
                return;
            }

            reconnectLoop = Task.Run(() => ReconnectAsync(reconnectTokenSource!.Token), CancellationToken.None);
        }

        async Task ReconnectAsync(CancellationToken cancellationToken)
        {
            var delay = InitialReconnectDelay;

            try
            {
                var connected = false;
                while (!connected && !cancellationToken.IsCancellationRequested && !sessionTakenOver)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                    await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (client is not { IsConnected: true })
                        {
                            await ConnectAsync(cancellationToken).ConfigureAwait(false);
                            Logger.Info($"Reconnected to the MQTT broker at {connection.Server}:{connection.Port}.");
                        }

                        connected = client is { IsConnected: true };
                    }
                    catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
                    {
                        delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaximumReconnectDelay.Ticks));
                        Logger.Warn($"Could not reconnect to the MQTT broker at {connection.Server}:{connection.Port}. Trying again in {delay.TotalSeconds:0} seconds.", ex);
                    }
                    finally
                    {
                        connectionLock.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // stopping
            }
            finally
            {
                Interlocked.Exchange(ref reconnecting, 0);
            }

            // the new connection can already have been lost again, while this loop was still marked as running
            if (!cancellationToken.IsCancellationRequested && !sessionTakenOver && client is not { IsConnected: true })
            {
                StartReconnecting();
            }
        }

        void RemoveInFlight(long processingId) => inFlight.TryRemove(processingId, out _);

        // Two tokens on purpose. Stopping ends this loop (the stop token), but the handlers it started carry the processing token, which is
        // only cancelled if the caller gives up waiting for them. The catch below is about the stop token alone.
#pragma warning disable PS0021
        async Task RunWorkerLoop(ChannelReader<ReceivedMessage> reader, CancellationToken stopCancellationToken)
        {
            try
            {
                await foreach (var message in reader.ReadAllAsync(stopCancellationToken).ConfigureAwait(false))
                {
                    await limiter!.WaitAsync(stopCancellationToken).ConfigureAwait(false);

                    // Run, so that a handler that does synchronous work before its first await cannot hold up the other workers
                    var processingCancellationToken = processingTokenSource!.Token;
                    var processing = Task.Run(() => ProcessMessage(message, processingCancellationToken), CancellationToken.None);

                    var processingId = Interlocked.Increment(ref lastProcessingId);
                    inFlight.TryAdd(processingId, processing);
                    _ = processing.ContinueWith(_ => RemoveInFlight(processingId), TaskScheduler.Default);
                }
            }
            catch (OperationCanceledException) when (stopCancellationToken.IsCancellationRequested)
            {
                // stopping
            }
        }
#pragma warning restore PS0021

        async Task ProcessMessage(ReceivedMessage received, CancellationToken processingCancellationToken)
        {
            try
            {
                await Process(received, processingCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (processingCancellationToken.IsCancellationRequested)
            {
                // The stop was cancelled while this message was being handled. Recoverability does not run, and the message is not
                // acknowledged, so the broker redelivers it.
            }
            catch (Exception ex) when (!ex.IsCausedBy(processingCancellationToken))
            {
                Logger.Error($"An unexpected error stopped the processing of a message from '{received.Topic}'. The message is not acknowledged.", ex);
            }
            finally
            {
                limiter!.Release();
            }
        }

        async Task Process(ReceivedMessage received, CancellationToken processingCancellationToken)
        {
            // None means the broker is told the message is done before it is handled, and it is never delivered again
            if (transactionMode == TransportTransactionMode.None)
            {
                await AcknowledgeAsync(received, processingCancellationToken).ConfigureAwait(false);
            }

            DecodedMessage decoded;
            try
            {
                decoded = WireFormat.Decode(received.Payload);
            }
            catch (MessageDecodeException ex)
            {
                // a poison message would otherwise be redelivered for ever, and stall everything behind it
                Logger.Error($"Discarded a message from '{received.Topic}' that is not a valid message.", ex);
                await AcknowledgeAsync(received, processingCancellationToken).ConfigureAwait(false);
                return;
            }

            if (!IsDesignatedCopy(received.Topic, decoded.Headers))
            {
                await AcknowledgeAsync(received, processingCancellationToken).ConfigureAwait(false);
                return;
            }

            for (var processingAttempt = 1; ; processingAttempt++)
            {
                processingCancellationToken.ThrowIfCancellationRequested();

                // Every attempt starts from a fresh copy of the message, so headers that the handler or recoverability changed cannot
                // leak into the next attempt. The extension bag is shared by the message context and the error context of an attempt.
                var context = new ContextBag();
                var transaction = new TransportTransaction();
                var attempt = decoded.Copy();

                try
                {
                    await onMessage!(new MessageContext(attempt.NativeMessageId, attempt.Headers, attempt.Body, transaction, ReceiveAddress, context), processingCancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsCausedBy(processingCancellationToken))
                {
                    var failed = decoded.Copy();
                    var errorContext = new ErrorContext(ex, failed.Headers, failed.NativeMessageId, failed.Body, transaction, processingAttempt, ReceiveAddress, context);

                    ErrorHandleResult result;
                    try
                    {
                        result = await onError!(errorContext, processingCancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception onErrorException) when (!onErrorException.IsCausedBy(processingCancellationToken))
                    {
                        onCritical($"Failed to execute recoverability policy for message with native ID: `{decoded.NativeMessageId}`", onErrorException, processingCancellationToken);

                        if (stopping)
                        {
                            // The message is not acknowledged, so it is not lost.
                            return;
                        }

                        await Task.Delay(RecoverabilityFailureDelay, processingCancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (result == ErrorHandleResult.Handled)
                    {
                        await AcknowledgeAsync(received, processingCancellationToken).ConfigureAwait(false);
                        return;
                    }

                    continue;
                }

                await AcknowledgeAsync(received, processingCancellationToken).ConfigureAwait(false);
                return;
            }
        }

        // An event is published once for every type in its hierarchy, so an endpoint subscribed to several of them receives several copies.
        // Exactly one of them is processed: the one for the first type in the message's own type list (the concrete type comes first)
        // that the endpoint is subscribed to. The rule only needs the topic, the endpoint's subscriptions and the header.
        bool IsDesignatedCopy(string topic, Dictionary<string, string> headers)
        {
            if (!topic.StartsWith(EventTopic.Prefix, StringComparison.Ordinal) || !headers.TryGetValue(Headers.EnclosedMessageTypes, out var enclosedMessageTypes))
            {
                return true;
            }

            foreach (var typeName in EnclosedMessageTypes.Names(enclosedMessageTypes))
            {
                if (subscribedEventTypes.TryGetValue(typeName, out var designatedTopic))
                {
                    return designatedTopic == topic;
                }
            }

            return true;
        }

        static string FullName(Type type) => type.FullName ?? type.Name;

        async Task AcknowledgeAsync(ReceivedMessage message, CancellationToken cancellationToken)
        {
            if (message.Acknowledged)
            {
                return;
            }

            message.Acknowledged = true;

            // An acknowledgement belongs to the connection the message arrived on. After a reconnect the broker redelivers the message instead.
            if (message.Generation != Interlocked.Read(ref connectionGeneration) || client is not { IsConnected: true })
            {
                Logger.Debug($"The connection that delivered a message from '{message.Topic}' has been lost, so it is not acknowledged and the broker delivers it again.");
                return;
            }

            try
            {
                await message.Args.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
            {
                Logger.Warn($"Could not acknowledge a message from '{message.Topic}'. The broker delivers it again.", ex);
            }
        }

        sealed class ReceivedMessage(MqttApplicationMessageReceivedEventArgs args, long generation, string topic, byte[] payload)
        {
            public MqttApplicationMessageReceivedEventArgs Args { get; } = args;

            public long Generation { get; } = generation;

            public string Topic { get; } = topic;

            public byte[] Payload { get; } = payload;

            public bool Acknowledged { get; set; }
        }

        readonly MqttConnectionSettings connection;
        readonly TransportTransactionMode transactionMode;
        readonly string[] explicitTopics;
        readonly Action<string, Exception, CancellationToken> onCritical;
        readonly string clientId;
        readonly SemaphoreSlim connectionLock = new(1, 1);
        readonly ConcurrentDictionary<string, string> subscribedEventTypes = new();
        readonly HashSet<string> pendingUnsubscribes = [];
        readonly ConcurrentDictionary<long, Task> inFlight = new();

        OnMessage? onMessage;
        OnError? onError;
        ConcurrencyLimiter? limiter;
        Channel<ReceivedMessage>? intake;
        Task? workerLoop;
        Task? reconnectLoop;
        CancellationTokenSource? processingTokenSource;
        CancellationTokenSource? workerTokenSource;
        CancellationTokenSource? reconnectTokenSource;
        IMqttClient? client;
        ushort receiveMaximum;
        long connectionGeneration;
        long lastProcessingId;
        int reconnecting;
        volatile bool started;
        volatile bool stopping;
        volatile bool sessionTakenOver;
        bool disposed;
    }
}
