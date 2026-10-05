namespace NServiceBus.Community.Mqtt.Tests;

using Hierarchy;
using NServiceBus.Transport.Mqtt;

[TestFixture]
public class EventTypeHierarchyTests
{
    [Test]
    public void Should_list_the_concrete_type_base_classes_then_interfaces_in_a_fixed_order()
    {
        var hierarchy = EventTypeHierarchy.Enumerate(typeof(DerivedEvent));

        Assert.That(hierarchy, Is.EqualTo(new[]
        {
            typeof(DerivedEvent),
            typeof(MiddleEvent),
            typeof(BaseEvent),
            typeof(IAuditable),
            typeof(IBaseEvent)
        }));
    }

    [Test]
    public void Should_exclude_object_system_types_and_the_nservicebus_marker_types()
    {
        var hierarchy = EventTypeHierarchy.Enumerate(typeof(DerivedEvent));

        Assert.Multiple(() =>
        {
            Assert.That(hierarchy, Does.Not.Contain(typeof(object)));
            Assert.That(hierarchy, Does.Not.Contain(typeof(IDisposable)));
            Assert.That(hierarchy, Does.Not.Contain(typeof(IEvent)));
            Assert.That(hierarchy, Does.Not.Contain(typeof(IMessage)));
            Assert.That(hierarchy, Does.Not.Contain(typeof(ICommand)));
        });
    }

    [Test]
    public void Should_return_only_the_type_itself_for_a_type_with_nothing_but_marker_ancestry()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EventTypeHierarchy.Enumerate(typeof(Sales.OrderPlaced)), Is.EqualTo(new[] { typeof(Sales.OrderPlaced) }));
            Assert.That(EventTypeHierarchy.Enumerate(typeof(PlainMessage)), Is.EqualTo(new[] { typeof(PlainMessage) }));
            Assert.That(EventTypeHierarchy.Enumerate(typeof(CommandMessage)), Is.EqualTo(new[] { typeof(CommandMessage) }));
        });
    }

    [Test]
    public void Should_list_every_unrelated_interface_so_each_has_its_own_topic()
    {
        var hierarchy = EventTypeHierarchy.Enumerate(typeof(UnrelatedInterfacesEvent));

        Assert.That(hierarchy, Is.EqualTo(new[] { typeof(UnrelatedInterfacesEvent), typeof(IAuditable), typeof(IBaseEvent) }));
    }

    [Test]
    public void Should_handle_an_interface_as_the_published_type()
    {
        // when an interface event is published the multicast operation carries the interface itself; its only parent here is the IEvent marker
        var hierarchy = EventTypeHierarchy.Enumerate(typeof(IBaseEvent));

        Assert.That(hierarchy, Is.EqualTo(new[] { typeof(IBaseEvent) }));
    }

    [Test]
    public void Should_be_deterministic()
    {
        Assert.That(EventTypeHierarchy.Enumerate(typeof(DerivedEvent)), Is.EqualTo(EventTypeHierarchy.Enumerate(typeof(DerivedEvent))));
    }

    [Test]
    public void Should_produce_one_distinct_topic_per_type_for_the_sample_event()
    {
        var topics = EventTypeHierarchy.Enumerate(typeof(DerivedEvent)).Select(EventTopic.ToTopic).ToArray();

        Assert.That(topics, Is.EqualTo(new[]
        {
            "events/Hierarchy.DerivedEvent",
            "events/Hierarchy.MiddleEvent",
            "events/Hierarchy.BaseEvent",
            "events/Hierarchy.IAuditable",
            "events/Hierarchy.IBaseEvent"
        }));
    }
}
