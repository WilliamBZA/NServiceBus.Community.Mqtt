#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Community.Mqtt.TestBroker;
using NServiceBus.Community.Mqtt.TransportTests.Events;
using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;

/// <summary>
/// What the standard suites do not cover about native publish/subscribe: topic names, polymorphic delivery with exactly one processed
/// copy per endpoint, unsubscribe, subscribing at any time, surviving a lost connection and explicit topics.
/// </summary>
public class PublishSubscribeTests : NServiceBus.TransportTests.NServiceBusTransportTest
{
    [Test]
    public async Task Should_deliver_an_event_once_to_each_subscribing_endpoint_and_not_to_the_others()
    {
        await using var endpoints = new EndpointSet();
        var (_, firstInbox) = await StartEndpoint(endpoints, "First", typeof(UnrelatedEvent));
        var (_, secondInbox) = await StartEndpoint(endpoints, "Second", typeof(UnrelatedEvent));
        var (_, bystanderInbox) = await StartEndpoint(endpoints, "Bystander", typeof(BaseEvent));
        var publisher = await StartPublisher(endpoints);

        await publisher.Publish(typeof(UnrelatedEvent));

        await firstInbox.WaitFor(1, "the first subscriber");
        await secondInbox.WaitFor(1, "the second subscriber");
        await firstInbox.AssertCount(1, "each endpoint handles the event once");
        Assert.Multiple(() =>
        {
            Assert.That(secondInbox.Count, Is.EqualTo(1));
            Assert.That(bystanderInbox.Count, Is.Zero, "an endpoint that did not subscribe to the type receives nothing");
        });
    }

    [Test]
    public async Task Should_not_deliver_an_event_to_a_subscriber_of_a_type_with_the_same_name_in_another_namespace()
    {
        await using var endpoints = new EndpointSet();
        var (_, sales) = await StartEndpoint(endpoints, "Sales", typeof(Events.Sales.OrderPlaced));
        var (_, shipping) = await StartEndpoint(endpoints, "Shipping", typeof(Events.Shipping.OrderPlaced));
        var publisher = await StartPublisher(endpoints);

        await publisher.Publish(typeof(Events.Shipping.OrderPlaced));

        await shipping.WaitFor(1, "the subscriber of Shipping.OrderPlaced");
        await sales.AssertCount(0, "the subscriber of Sales.OrderPlaced is not reached by Shipping.OrderPlaced");
    }

    [Test]
    public async Task Should_publish_and_subscribe_a_nested_event_type()
    {
        await using var endpoints = new EndpointSet();
        var (_, inbox) = await StartEndpoint(endpoints, "Nested", typeof(Container.NestedEvent));
        var publisher = await StartPublisher(endpoints);

        await publisher.Publish(typeof(Container.NestedEvent));

        await inbox.WaitFor(1, "the nested event");
    }

    [Test]
    public async Task Should_deliver_events_of_a_type_subscribed_to_before_start_and_one_subscribed_to_after_start()
    {
        await using var endpoints = new EndpointSet();
        var subscriber = endpoints.Create("Subscriber");
        var inbox = new Inbox();
        await subscriber.Initialize(inbox.Handle);
        await subscriber.Subscribe(typeof(UnrelatedEvent));
        await subscriber.Start();
        var publisher = await StartPublisher(endpoints);

        await publisher.Publish(typeof(UnrelatedEvent));
        await inbox.WaitFor(1, "the event subscribed to before start");

        await publisher.Publish(typeof(BaseEvent));
        await inbox.AssertCount(1, "BaseEvent is not subscribed to yet");

        await subscriber.Subscribe(typeof(BaseEvent));
        await publisher.Publish(typeof(BaseEvent));
        await inbox.WaitFor(2, "the event subscribed to after start");
    }

    [Test]
    public async Task Should_stop_delivering_after_unsubscribe_while_other_endpoints_still_receive()
    {
        await using var endpoints = new EndpointSet();
        var (leaving, leavingInbox) = await StartEndpoint(endpoints, "Leaving", typeof(UnrelatedEvent));
        var (_, stayingInbox) = await StartEndpoint(endpoints, "Staying", typeof(UnrelatedEvent));
        var publisher = await StartPublisher(endpoints);

        await publisher.Publish(typeof(UnrelatedEvent));
        await leavingInbox.WaitFor(1, "the first event at the endpoint that leaves");
        await stayingInbox.WaitFor(1, "the first event at the endpoint that stays");

        await leaving.Unsubscribe(typeof(UnrelatedEvent));
        await publisher.Publish(typeof(UnrelatedEvent));

        await stayingInbox.WaitFor(2, "the second event at the endpoint that stays");
        await leavingInbox.AssertCount(1, "the endpoint that unsubscribed receives nothing more");
    }

    [Test]
    public async Task Should_keep_the_subscriptions_when_the_connection_to_the_broker_is_cut_and_restored()
    {
        using var proxy = new TcpProxy(MqttTestBroker.Host, MqttTestBroker.Port);
        await using var endpoints = new EndpointSet();
        var (_, inbox) = await StartEndpoint(endpoints, "Subscriber", [typeof(UnrelatedEvent), typeof(BaseEvent)], new() { Host = proxy.Host, Port = proxy.Port });
        var publisher = await StartPublisher(endpoints);

        proxy.Cut();
        await Task.Delay(2000);
        proxy.Restore();
        await Task.Delay(TimeSpan.FromSeconds(4));

        await publisher.Publish(typeof(UnrelatedEvent));
        await publisher.Publish(typeof(BaseEvent));

        await inbox.WaitFor(2, "an event of each subscribed type after the connection came back");
    }

    [Test]
    public async Task Should_unsubscribe_after_the_connection_comes_back_when_it_was_lost_at_the_time()
    {
        using var proxy = new TcpProxy(MqttTestBroker.Host, MqttTestBroker.Port);
        await using var endpoints = new EndpointSet();
        var (subscriber, inbox) = await StartEndpoint(endpoints, "Subscriber", [typeof(UnrelatedEvent)], new() { Host = proxy.Host, Port = proxy.Port });
        var publisher = await StartPublisher(endpoints);

        proxy.Cut();
        await Task.Delay(1000);
        await subscriber.Unsubscribe(typeof(UnrelatedEvent));
        proxy.Restore();
        await Task.Delay(TimeSpan.FromSeconds(4));

        await publisher.Publish(typeof(UnrelatedEvent));

        await inbox.AssertCount(0, "the unsubscribe is applied on the broker once the pump is connected again");
    }

    [Test]
    public async Task Should_deliver_a_derived_event_to_subscribers_of_a_base_type_and_of_an_interface()
    {
        await using var endpoints = new EndpointSet();
        var (_, derived) = await StartEndpoint(endpoints, "Derived", typeof(DerivedEvent));
        var (_, baseType) = await StartEndpoint(endpoints, "Base", typeof(BaseEvent));
        var (_, iface) = await StartEndpoint(endpoints, "Interface", typeof(IFoo));
        var publisher = await StartPublisher(endpoints);

        await publisher.Publish(typeof(DerivedEvent));
        await publisher.Publish(typeof(FooBarEvent));

        await derived.WaitFor(1, "the subscriber of the concrete type");
        await baseType.WaitFor(1, "the subscriber of the base class");
        await iface.WaitFor(1, "the subscriber of the interface");
        await iface.AssertCount(1, "and the interface subscriber only gets the event that implements it");
        Assert.Multiple(() =>
        {
            Assert.That(derived.Count, Is.EqualTo(1));
            Assert.That(baseType.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_deliver_the_event_once_to_an_endpoint_subscribed_to_both_the_base_and_the_derived_type()
    {
        await using var endpoints = new EndpointSet();
        var (_, inbox) = await StartEndpoint(endpoints, "Both", [typeof(BaseEvent), typeof(DerivedEvent)]);
        var publisher = await StartPublisher(endpoints);

        await publisher.Publish(typeof(DerivedEvent));

        await inbox.WaitFor(1, "the event");
        await inbox.AssertCount(1, "one copy is processed, not one per matching subscription");
    }

    [Test]
    public async Task Should_deliver_an_event_that_implements_two_unrelated_interfaces_once_to_each_endpoint()
    {
        await using var endpoints = new EndpointSet();
        var (_, foo) = await StartEndpoint(endpoints, "Foo", typeof(IFoo));
        var (_, bar) = await StartEndpoint(endpoints, "Bar", typeof(IBar));
        var publisher = await StartPublisher(endpoints);

        await publisher.Publish(typeof(FooBarEvent));

        await foo.WaitFor(1, "the subscriber of IFoo");
        await bar.WaitFor(1, "the subscriber of IBar");
        await foo.AssertCount(1, "IFoo once");
        Assert.That(bar.Count, Is.EqualTo(1), "IBar once");
    }

    [Test]
    public async Task Should_process_a_copy_without_the_enclosed_message_types_header_whatever_topic_it_arrives_on()
    {
        await using var endpoints = new EndpointSet();
        var (_, inbox) = await StartEndpoint(endpoints, "Subscriber", [typeof(BaseEvent), typeof(DerivedEvent)]);

        // a device publishing straight to an event topic does not write the header, so the designated-copy rule cannot apply
        var message = WireFormat.Encode(new OutgoingMessage("device-message", new Dictionary<string, string> { ["Order"] = "raw" }, new byte[] { 1, 2, 3 }));
        await RawMqtt.Publish(EventTopic.ToTopic(typeof(BaseEvent)), message);

        await inbox.WaitFor(1, "the message");
        await inbox.AssertCount(1, "once");
        Assert.That(inbox.Items.Single().Body, Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Should_process_messages_on_a_topic_added_with_subscribe_to()
    {
        await using var endpoints = new EndpointSet();
        var (_, inbox) = await StartEndpoint(endpoints, "Device", [], new() { ExplicitTopics = ["sensors/gate"] });

        var message = WireFormat.Encode(new OutgoingMessage("gate-reading", new Dictionary<string, string> { ["Order"] = "open" }, Array.Empty<byte>()));
        await RawMqtt.Publish("sensors/gate", message);

        await inbox.WaitFor(1, "the message published to sensors/gate");
        Assert.That(inbox.Items.Single().Headers["Order"], Is.EqualTo("open"));
    }

    [Test]
    public async Task Should_keep_receiving_on_the_explicit_topic_after_the_connection_is_cut_and_restored()
    {
        using var proxy = new TcpProxy(MqttTestBroker.Host, MqttTestBroker.Port);
        await using var endpoints = new EndpointSet();
        var (_, inbox) = await StartEndpoint(endpoints, "Device", [], new() { Host = proxy.Host, Port = proxy.Port, ExplicitTopics = ["sensors/gate"] });

        proxy.Cut();
        await Task.Delay(2000);
        proxy.Restore();
        await Task.Delay(TimeSpan.FromSeconds(4));

        await RawMqtt.Publish("sensors/gate", WireFormat.Encode(new OutgoingMessage("gate-reading", [], Array.Empty<byte>())));

        await inbox.WaitFor(1, "a message on the explicit topic after the connection came back");
    }

    [Test]
    public async Task Should_expose_no_subscription_manager_for_a_receiver_that_does_not_use_publish_subscribe()
    {
        await using var endpoints = new EndpointSet();
        var instanceSpecific = endpoints.Create("InstanceSpecific", new() { UsePublishSubscribe = false });
        var regular = endpoints.Create("Regular");

        await instanceSpecific.Initialize((_, _) => Task.CompletedTask);
        await regular.Initialize((_, _) => Task.CompletedTask);

        Assert.Multiple(() =>
        {
            Assert.That(instanceSpecific.Receiver.Subscriptions, Is.Null);
            Assert.That(regular.Receiver.Subscriptions, Is.Not.Null);
        });
    }

    static async Task<(MqttTestEndpoint Endpoint, Inbox Inbox)> StartEndpoint(EndpointSet endpoints, string name, Type eventType) =>
        await StartEndpoint(endpoints, name, [eventType]);

    static async Task<(MqttTestEndpoint Endpoint, Inbox Inbox)> StartEndpoint(EndpointSet endpoints, string name, Type[] eventTypes, MqttTestEndpoint.Options? options = null)
    {
        var endpoint = endpoints.Create(name, options);
        var inbox = new Inbox();
        await endpoint.Initialize(inbox.Handle);
        if (eventTypes.Length > 0)
        {
            await endpoint.Subscribe(eventTypes);
        }

        await endpoint.Start();

        return (endpoint, inbox);
    }

    static async Task<MqttTestEndpoint> StartPublisher(EndpointSet endpoints)
    {
        var publisher = endpoints.Create("Publisher");
        await publisher.Initialize((_, _) => Task.CompletedTask);

        return publisher;
    }
}
