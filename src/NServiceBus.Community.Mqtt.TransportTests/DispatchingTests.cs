#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Community.Mqtt.TestBroker;
using NServiceBus.Community.Mqtt.TransportTests.Events;

/// <summary>
/// What the standard transport suite does not cover about sending and publishing: what the broker is told, how a failure surfaces, the
/// message ID on the wire, and releasing the sending side on shutdown. Each test uses queues of its own.
/// </summary>
public class DispatchingTests : NServiceBus.TransportTests.NServiceBusTransportTest
{
    [Test]
    public async Task Should_deliver_a_binary_body_and_unicode_headers_unchanged()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();

        byte[] body = [0, 255, 1, 2];
        await sender.Send(receiver.Queue, new() { ["a-😅-B7"] = "a-😍-b", ["MyHeader"] = "MyValue" }, body);

        await inbox.WaitFor(1, "the message");
        var received = inbox.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(received.Body, Is.EqualTo(body));
            Assert.That(received.Headers["a-😅-B7"], Is.EqualTo("a-😍-b"));
            Assert.That(received.Headers["MyHeader"], Is.EqualTo("MyValue"));
        });
    }

    [Test]
    public async Task Should_deliver_an_empty_body_without_treating_it_as_a_poison_message()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();
        LogFactory.LogItems.Clear();

        await sender.Send(receiver.Queue, body: []);

        await inbox.WaitFor(1, "the message");
        Assert.Multiple(() =>
        {
            Assert.That(inbox.Items.Single().Body, Is.Empty);
            Assert.That(LogFactory.LogItems.Where(item => item.Level >= NServiceBus.Logging.LogLevel.Error), Is.Empty, "no error is logged");
        });
    }

    [Test]
    public async Task Should_put_the_outgoing_message_id_on_the_wire_when_the_header_is_missing()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();

        await sender.Send(receiver.Queue, headers: [], messageId: "outgoing-id");

        await inbox.WaitFor(1, "the message");
        var received = inbox.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(received.Headers[Headers.MessageId], Is.EqualTo("outgoing-id"));
            Assert.That(received.NativeMessageId, Is.EqualTo("outgoing-id"));
        });
    }

    [Test]
    public async Task Should_keep_a_message_id_header_that_is_already_present()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();

        await sender.Send(receiver.Queue, new() { [Headers.MessageId] = "header-id" }, messageId: "outgoing-id");

        await inbox.WaitFor(1, "the message");
        Assert.That(inbox.Items.Single().Headers[Headers.MessageId], Is.EqualTo("header-id"));
    }

    [Test]
    public async Task Should_publish_every_operation_of_a_mixed_batch()
    {
        await using var endpoints = new EndpointSet();
        var first = endpoints.Create("First");
        var second = endpoints.Create("Second");
        var subscriber = endpoints.Create("Subscriber");
        var sender = endpoints.Create("Sender");
        var firstInbox = new Inbox();
        var secondInbox = new Inbox();
        var subscriberInbox = new Inbox();
        await first.Initialize(firstInbox.Handle);
        await second.Initialize(secondInbox.Handle);
        await subscriber.Initialize(subscriberInbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await subscriber.Subscribe(typeof(OrderPlacedForBatch));
        await first.Start();
        await second.Start();
        await subscriber.Start();

        await sender.Dispatch(
        [
            MqttTestEndpoint.Unicast(first.Queue),
            MqttTestEndpoint.Multicast(typeof(OrderPlacedForBatch)),
            MqttTestEndpoint.Unicast(second.Queue)
        ]);

        await firstInbox.WaitFor(1, "the first addressee");
        await secondInbox.WaitFor(1, "the second addressee");
        await subscriberInbox.WaitFor(1, "the subscriber");
        await subscriberInbox.AssertCount(1, "the subscriber receives the event once");
        Assert.Multiple(() =>
        {
            Assert.That(firstInbox.Count, Is.EqualTo(1));
            Assert.That(secondInbox.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_publish_nothing_and_name_the_address_when_a_destination_in_the_batch_cannot_be_used()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();

        var exception = Assert.CatchAsync<ArgumentException>(() => sender.Dispatch([MqttTestEndpoint.Unicast(receiver.Queue), MqttTestEndpoint.Unicast("Bad#Destination")]));

        Assert.That(exception!.Message, Does.Contain("Bad#Destination"));
        await inbox.AssertCount(0, "the batch is refused as a whole, so the valid operation in it is not published either");
    }

    [Test]
    public async Task Should_throw_naming_the_destination_when_the_broker_cannot_be_reached_and_work_again_once_it_is_back()
    {
        using var proxy = new TcpProxy(MqttTestBroker.Host, MqttTestBroker.Port);
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender", new() { Host = proxy.Host, Port = proxy.Port });
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();

        await sender.Send(receiver.Queue);
        await inbox.WaitFor(1, "a message while the broker is reachable");

        proxy.Cut();
        await Task.Delay(1000);

        var exception = Assert.CatchAsync<Exception>(() => sender.Send(receiver.Queue));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(receiver.Topic), "names the destination");
            Assert.That(exception.Message, Does.Contain($"{proxy.Host}:{proxy.Port}"), "and the broker");
        });
        await inbox.AssertCount(1, "the failed dispatch delivered nothing");

        proxy.Restore();
        await sender.Send(receiver.Queue);

        await inbox.WaitFor(2, "a message once the broker is reachable again, sent on a new connection without restarting the sender");
    }

    [Test]
    public async Task Should_drop_a_message_whose_time_to_be_received_passes_while_the_endpoint_is_stopped_and_keep_the_others()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);

        // the queue exists but nothing receives from it
        await sender.Send(receiver.Queue, new() { ["Order"] = "expiring" }, timeToBeReceived: TimeSpan.FromSeconds(2));
        await sender.Send(receiver.Queue, new() { ["Order"] = "lasting" }, timeToBeReceived: TimeSpan.FromMinutes(1));
        await sender.Send(receiver.Queue, new() { ["Order"] = "unlimited" });
        await Task.Delay(TimeSpan.FromSeconds(4));

        await receiver.Start();

        await inbox.WaitFor(2, "the messages that had not expired");
        await inbox.AssertCount(2, "and not the one that expired");
        Assert.That(inbox.Items.Select(item => item.Headers["Order"]), Is.EquivalentTo(new[] { "lasting", "unlimited" }));
    }

    [Test]
    public async Task Should_deliver_a_message_with_a_time_to_be_received_straight_away_to_a_running_endpoint()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();

        await sender.Send(receiver.Queue, timeToBeReceived: TimeSpan.FromSeconds(30));

        await inbox.WaitFor(1, "the message");
    }

    [Test]
    public async Task Should_apply_the_time_to_be_received_to_the_copies_of_a_published_event()
    {
        await using var endpoints = new EndpointSet();
        var subscriber = endpoints.Create("Subscriber");
        var publisher = endpoints.Create("Publisher");
        var inbox = new Inbox();
        await subscriber.Initialize(inbox.Handle);
        await publisher.Initialize((_, _) => Task.CompletedTask);

        // a subscription lives in the session, so it is there while the endpoint is stopped, once the endpoint has been started with it
        await subscriber.Subscribe(typeof(BaseEvent));
        await subscriber.Start();
        await subscriber.Stop();

        await publisher.Publish(typeof(DerivedEvent), new() { ["Order"] = "expiring" }, timeToBeReceived: TimeSpan.FromSeconds(2));
        await publisher.Publish(typeof(DerivedEvent), new() { ["Order"] = "unlimited" });
        await Task.Delay(TimeSpan.FromSeconds(4));

        await subscriber.Start();

        await inbox.WaitFor(1, "the event that had not expired");
        await inbox.AssertCount(1, "and not the one that expired");
        Assert.That(inbox.Items.Single().Headers["Order"], Is.EqualTo("unlimited"));
    }

    [Test]
    public async Task Should_end_in_an_operation_cancelled_exception_when_the_token_is_cancelled()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.CatchAsync<OperationCanceledException>(() => sender.Send(receiver.Queue, cancellationToken: cancellation.Token));
        await inbox.AssertCount(0, "a cancelled dispatch publishes nothing");
    }

    [Test]
    public async Task Should_disconnect_every_client_on_shutdown_and_refuse_publishes_after_it()
    {
        using var proxy = new TcpProxy(MqttTestBroker.Host, MqttTestBroker.Port);
        await using var endpoints = new EndpointSet();
        var endpoint = endpoints.Create("Shutdown", new() { Host = proxy.Host, Port = proxy.Port });
        await endpoint.Initialize((_, _) => Task.CompletedTask);
        await endpoint.Start();
        await Wait.Until(() => proxy.ActiveConnections == 2, "the dispatcher and the pump to be connected");

        await endpoint.Infrastructure.Shutdown();

        await Wait.Until(() => proxy.ActiveConnections == 0, "the broker to see the dispatcher and the pump disconnected");
        Assert.ThrowsAsync<ObjectDisposedException>(() => endpoint.Send(endpoint.Queue));
        Assert.DoesNotThrowAsync(() => endpoint.Infrastructure.Shutdown(), "a second shutdown does nothing");
    }

    sealed class OrderPlacedForBatch : IEvent
    {
    }
}
