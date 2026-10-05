#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Community.Mqtt.TestBroker;
using NServiceBus.Transport.Mqtt;

/// <summary>
/// What the standard transport suite does not cover about declaring queues when the transport initializes: delivery between
/// initialization and start, retention for the addresses an endpoint only sends to, and refusing addresses that cannot be queues.
/// </summary>
public class QueueDeclarationTests : NServiceBus.TransportTests.NServiceBusTransportTest
{
    [Test]
    public async Task Should_deliver_a_message_sent_between_initialize_and_start()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);

        await sender.Send(receiver.Queue, new() { ["Order"] = "early" });
        await receiver.Start();

        await inbox.WaitFor(1, "the message sent before the endpoint started");
        Assert.That(inbox.Items.Single().Headers["Order"], Is.EqualTo("early"));
    }

    [Test]
    public async Task Should_deliver_a_message_sent_while_the_endpoint_was_stopped()
    {
        await using var endpoints = new EndpointSet();
        var receiver = endpoints.Create("Receiver");
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await receiver.Initialize(inbox.Handle);
        await sender.Initialize((_, _) => Task.CompletedTask);
        await receiver.Start();
        await receiver.Stop();

        await sender.Send(receiver.Queue);
        await receiver.Start();

        await inbox.WaitFor(1, "the message sent while stopped");
    }

    [Test]
    public async Task Should_keep_messages_for_an_address_that_is_only_sent_to_until_a_consumer_collects_them()
    {
        await using var endpoints = new EndpointSet();
        var endpoint = endpoints.Create("Endpoint");

        await endpoint.Initialize((_, _) => Task.CompletedTask);

        // nothing has ever consumed from the error address, and nobody is connected to it
        await endpoint.Send(endpoint.ErrorQueue, new() { ["Order"] = "failed" });

        // tooling collects them as the holder session, which is the documented way to drain a declared address
        var payloads = await RawMqtt.Drain(MqttClientId.ForDeclaredAddress(MqttAddress.ToTopic(endpoint.ErrorQueue)));

        Assert.That(payloads, Has.Length.EqualTo(1));
        Assert.That(WireFormat.Decode(payloads[0]).Headers["Order"], Is.EqualTo("failed"));
    }

    [Test]
    public async Task Should_declare_a_shared_sending_address_when_many_endpoints_start_at_the_same_moment()
    {
        // every endpoint of a system sends its failed messages to the one error address, so they all declare the same holder session
        await using var endpoints = new EndpointSet();
        var sharedErrorQueue = endpoints.NewAddress("SharedErrors");

        // The takeover that interrupts a declaration can arrive at different points of it, so it takes several rounds to meet them all.
        for (var round = 0; round < 5; round++)
        {
            var starting = Enumerable.Range(0, 12)
                .Select(i => endpoints.Create($"Endpoint{round}x{i}", new() { ErrorQueue = sharedErrorQueue }))
                .Select(endpoint => endpoint.Initialize((_, _) => Task.CompletedTask))
                .ToArray();

            await Task.WhenAll(starting);
        }

        var sender = endpoints.Create("Sender");
        await sender.Initialize((_, _) => Task.CompletedTask);
        await sender.Send(sharedErrorQueue, new() { ["Order"] = "failed" });
        var payloads = await RawMqtt.Drain(MqttClientId.ForDeclaredAddress(MqttAddress.ToTopic(sharedErrorQueue)));

        Assert.That(payloads, Has.Length.EqualTo(1), "one holder session keeps one copy, however many endpoints declared it");
    }

    [Test]
    public async Task Should_not_displace_a_live_consumer_of_an_address_that_another_endpoint_declares()
    {
        var displaced = false;
        await using var endpoints = new EndpointSet();
        var consumer = endpoints.Create("Consumer", new() { OnCriticalError = (_, _, _) => displaced = true });
        var sender = endpoints.Create("Sender");
        var inbox = new Inbox();
        await consumer.Initialize(inbox.Handle);
        await consumer.Start();

        // this endpoint sends its failed messages to the consumer's queue, so it declares that address
        var declaring = endpoints.Create("Declaring", new() { ErrorQueue = consumer.Queue });
        await declaring.Initialize((_, _) => Task.CompletedTask);
        await sender.Initialize((_, _) => Task.CompletedTask);

        await sender.Send(consumer.Queue);

        await inbox.WaitFor(1, "the live consumer to receive the message");
        await inbox.AssertCount(1, "once");
        Assert.That(displaced, Is.False, "declaring the address did not take the live consumer's session over");
    }

    [Test]
    public async Task Should_not_create_any_session_when_the_host_does_not_set_up_infrastructure()
    {
        await using var endpoints = new EndpointSet();
        var endpoint = endpoints.Create("NoSetup", new() { SetupInfrastructure = false });

        await endpoint.Initialize((_, _) => Task.CompletedTask);

        var queueSession = await RawMqtt.SessionExists(MqttClientId.ForQueue(endpoint.Topic));
        var errorSession = await RawMqtt.SessionExists(MqttClientId.ForDeclaredAddress(MqttAddress.ToTopic(endpoint.ErrorQueue)));
        Assert.Multiple(() =>
        {
            Assert.That(queueSession, Is.False, "no session for the queue");
            Assert.That(errorSession, Is.False, "and none for the error address");
        });
    }

    [TestCase("Bad#Queue")]
    [TestCase("+Queue")]
    [TestCase("$SYS_Queue")]
    public async Task Should_refuse_to_start_an_endpoint_with_an_address_that_cannot_be_a_queue_before_connecting_to_the_broker(string queue)
    {
        using var proxy = new TcpProxy(MqttTestBroker.Host, MqttTestBroker.Port);
        await using var endpoint = new MqttTestEndpoint(queue, new() { Host = proxy.Host, Port = proxy.Port });

        var exception = Assert.CatchAsync<ArgumentException>(() => endpoint.Initialize((_, _) => Task.CompletedTask));

        Assert.That(exception!.Message, Does.Contain(queue));
        await AssertNothingWasCreated(proxy, MqttClientId.ForQueue(queue));
    }

    [TestCase("Bad#Errors")]
    [TestCase("$SYS_Errors")]
    public async Task Should_refuse_to_start_an_endpoint_with_a_sending_address_that_cannot_be_a_queue_before_connecting_to_the_broker(string errorQueue)
    {
        using var proxy = new TcpProxy(MqttTestBroker.Host, MqttTestBroker.Port);
        await using var endpoint = new MqttTestEndpoint(MqttTestEndpoint.NewQueue("Valid"), new() { Host = proxy.Host, Port = proxy.Port, ErrorQueue = errorQueue });

        var exception = Assert.CatchAsync<ArgumentException>(() => endpoint.Initialize((_, _) => Task.CompletedTask));

        Assert.That(exception!.Message, Does.Contain(errorQueue));
        await AssertNothingWasCreated(proxy, MqttClientId.ForQueue(endpoint.Topic), MqttClientId.ForQueue(errorQueue), MqttClientId.ForDeclaredAddress(errorQueue));
    }

    // No client connected, so no wildcard or reserved topic was subscribed to, and none of the sessions it would have used exists.
    static async Task AssertNothingWasCreated(TcpProxy proxy, params string[] clientIds)
    {
        try
        {
            Assert.That(proxy.ActiveConnections, Is.Zero, "nothing is connected to the broker");

            foreach (var clientId in clientIds)
            {
                Assert.That(await RawMqtt.SessionExists(clientId), Is.False, $"the broker has no session '{clientId}'");
            }
        }
        finally
        {
            // asking for a session creates it
            foreach (var clientId in clientIds)
            {
                await BrokerStateCleaner.ClearSession(clientId);
            }
        }
    }
}
