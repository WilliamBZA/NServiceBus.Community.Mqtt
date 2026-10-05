#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Community.Mqtt.TestBroker;

using NServiceBus.Extensibility;
using NServiceBus.Performance.TimeToBeReceived;
using NServiceBus.Routing;
using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;
using NServiceBus.Unicast.Messages;

/// <summary>
/// An endpoint's transport, set up the way NServiceBus core sets it up, for the tests that need more than the suite's own harness offers:
/// a chosen concurrency, purge on startup, another broker address (for the TCP proxy), several consumers of one queue, and a handle on the
/// dispatcher to send with.
/// </summary>
sealed class MqttTestEndpoint : IAsyncDisposable
{
    public MqttTestEndpoint(string queue, Options? options = null)
    {
        Queue = queue;
        this.options = options ?? new Options();
    }

    public string Queue { get; }

    public string Topic => MqttAddress.ToTopic(Queue);

    public string ErrorQueue => options.ErrorQueue ?? $"{Queue}.error";

    /// <summary>The topics of the addresses this endpoint uses, which is what the broker state cleaner needs to delete its sessions.</summary>
    public string[] Topics => [Topic, MqttAddress.ToTopic(ErrorQueue)];

    public IMessageReceiver Receiver => receiver ?? throw new InvalidOperationException("Call Initialize first.");

    public TransportInfrastructure Infrastructure => infrastructure ?? throw new InvalidOperationException("Call Initialize first.");

    public static string NewQueue(string name) => $"{name}.{Guid.NewGuid():N}";

    public async Task Initialize(OnMessage onMessage, OnError? onError = null, CancellationToken cancellationToken = default)
    {
        var transport = new MqttTransport(options.Host ?? MqttTestBroker.Host, options.Port ?? MqttTestBroker.Port)
        {
            // short, so that a crashed run leaves sessions on a long-lived broker only briefly
            SessionExpiry = TimeSpan.FromMinutes(5),
            TransportTransactionMode = options.TransactionMode
        };

        foreach (var topic in options.ExplicitTopics)
        {
            transport.SubscribeTo(topic);
        }

        var hostSettings = new HostSettings(
            Queue,
            string.Empty,
            new StartupDiagnosticEntries(),
            options.OnCriticalError ?? ((message, exception, _) => throw new InvalidOperationException($"Unexpected critical error: {message}", exception)),
            setupInfrastructure: options.SetupInfrastructure);

        var receiveSettings = new ReceiveSettings("mainReceiver", new QueueAddress(Queue), options.UsePublishSubscribe, options.PurgeOnStartup, ErrorQueue);

        infrastructure = await transport.Initialize(hostSettings, [receiveSettings], [ErrorQueue], cancellationToken);
        receiver = infrastructure.Receivers.Single().Value;

        await receiver.Initialize(
            new PushRuntimeSettings(options.Concurrency),
            onMessage,
            onError ?? ((_, _) => Task.FromResult(ErrorHandleResult.Handled)),
            cancellationToken);
    }

    public Task Start(CancellationToken cancellationToken = default) => Receiver.StartReceive(cancellationToken);

    public Task Stop(CancellationToken cancellationToken = default) => receiver?.StopReceive(cancellationToken) ?? Task.CompletedTask;

    public Task Send(string destinationQueue, Dictionary<string, string>? headers = null, byte[]? body = null, string? messageId = null, TimeSpan? timeToBeReceived = null, CancellationToken cancellationToken = default) =>
        Dispatch([Unicast(destinationQueue, headers, body, messageId, timeToBeReceived)], cancellationToken);

    /// <summary>Publishes an event the way core does: one multicast operation carrying the concrete event type, with the type list in the headers.</summary>
    public Task Publish(Type eventType, Dictionary<string, string>? headers = null, byte[]? body = null, TimeSpan? timeToBeReceived = null, CancellationToken cancellationToken = default) =>
        Dispatch([Multicast(eventType, headers, body, timeToBeReceived)], cancellationToken);

    public Task Dispatch(TransportOperation[] operations, CancellationToken cancellationToken = default) =>
        Infrastructure.Dispatcher.Dispatch(new TransportOperations(operations), new TransportTransaction(), cancellationToken);

    public static TransportOperation Unicast(string destinationQueue, Dictionary<string, string>? headers = null, byte[]? body = null, string? messageId = null, TimeSpan? timeToBeReceived = null) =>
        new(new OutgoingMessage(messageId ?? Guid.NewGuid().ToString(), headers ?? [], body ?? []), new UnicastAddressTag(destinationQueue), Properties(timeToBeReceived));

    static DispatchProperties Properties(TimeSpan? timeToBeReceived) =>
        timeToBeReceived is { } maxTime ? new DispatchProperties { DiscardIfNotReceivedBefore = new DiscardIfNotReceivedBefore(maxTime) } : new DispatchProperties();

    public static TransportOperation Multicast(Type eventType, Dictionary<string, string>? headers = null, byte[]? body = null, TimeSpan? timeToBeReceived = null)
    {
        // core lists the concrete type first, then the types it conforms to, and the transport relies on that order
        var enclosedMessageTypes = string.Join(";", EventTypeHierarchy.Enumerate(eventType).Select(type => type.AssemblyQualifiedName));
        var allHeaders = new Dictionary<string, string>(headers ?? []) { [Headers.EnclosedMessageTypes] = enclosedMessageTypes };

        return new TransportOperation(new OutgoingMessage(Guid.NewGuid().ToString(), allHeaders, body ?? []), new MulticastAddressTag(eventType), Properties(timeToBeReceived));
    }

    public Task Subscribe(params Type[] eventTypes) =>
        Receiver.Subscriptions!.SubscribeAll(eventTypes.Select(type => new MessageMetadata(type)).ToArray(), new ContextBag());

    public Task Unsubscribe(Type eventType) =>
        Receiver.Subscriptions!.Unsubscribe(new MessageMetadata(eventType), new ContextBag());

    public async ValueTask DisposeAsync()
    {
        if (receiver is not null)
        {
            await receiver.StopReceive(CancellationToken.None);
        }

        if (infrastructure is not null)
        {
            await infrastructure.Shutdown(CancellationToken.None);
        }
    }

    readonly Options options;
    TransportInfrastructure? infrastructure;
    IMessageReceiver? receiver;

    public sealed record Options
    {
        public string? Host { get; init; }

        public int? Port { get; init; }

        public int Concurrency { get; init; } = 8;

        public bool PurgeOnStartup { get; init; }

        public bool UsePublishSubscribe { get; init; } = true;

        public bool SetupInfrastructure { get; init; } = true;

        /// <summary>The address the endpoint sends failed messages to. By default one next to its queue.</summary>
        public string? ErrorQueue { get; init; }

        public TransportTransactionMode TransactionMode { get; init; } = TransportTransactionMode.ReceiveOnly;

        public string[] ExplicitTopics { get; init; } = [];

        public Action<string, Exception, CancellationToken>? OnCriticalError { get; init; }
    }
}
