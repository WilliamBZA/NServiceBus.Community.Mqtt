namespace NServiceBus.Community.Mqtt.Tests;

using NServiceBus.Transport.Mqtt;

[TestFixture]
public class EventTopicTests
{
    [Test]
    public void Should_use_the_full_name_for_a_plain_type()
    {
        Assert.That(EventTopic.ToTopic(typeof(Sales.OrderPlaced)), Is.EqualTo("events/Sales.OrderPlaced"));
    }

    [Test]
    public void Should_give_same_named_types_in_different_namespaces_different_topics()
    {
        var sales = EventTopic.ToTopic(typeof(Sales.OrderPlaced));
        var shipping = EventTopic.ToTopic(typeof(Shipping.OrderPlaced));

        Assert.Multiple(() =>
        {
            Assert.That(sales, Is.EqualTo("events/Sales.OrderPlaced"));
            Assert.That(shipping, Is.EqualTo("events/Shipping.OrderPlaced"));
            Assert.That(sales, Is.Not.EqualTo(shipping));
        });
    }

    [Test]
    public void Should_replace_the_plus_of_a_nested_type()
    {
        Assert.That(typeof(Sales.Container.Inner).FullName, Does.Contain("+"), "the sample type must actually be nested");
        Assert.That(EventTopic.ToTopic(typeof(Sales.Container.Inner)), Is.EqualTo("events/Sales.Container.Inner"));
    }

    [Test]
    public void Should_encode_a_generic_type_without_assembly_qualified_parts()
    {
        Assert.That(EventTopic.ToTopic(typeof(Sales.Wrapper<string>)), Is.EqualTo("events/Sales.Wrapper.1.System.String"));
    }

    [Test]
    public void Should_encode_a_nested_generic_type()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EventTopic.ToTopic(typeof(Sales.Container.Nested<int>)), Is.EqualTo("events/Sales.Container.Nested.1.System.Int32"));
            Assert.That(EventTopic.ToTopic(typeof(Sales.GenericContainer<string>.Nested<int>)), Is.EqualTo("events/Sales.GenericContainer.1.Nested.1.System.String.System.Int32"));
        });
    }

    [Test]
    public void Should_encode_nested_and_multiple_generic_arguments()
    {
        var topic = EventTopic.ToTopic(typeof(Sales.Pair<Sales.Wrapper<Shipping.OrderPlaced>, int>));

        Assert.That(topic, Is.EqualTo("events/Sales.Pair.2.Sales.Wrapper.1.Shipping.OrderPlaced.System.Int32"));
    }

    [Test]
    public void Should_distinguish_different_generic_arguments()
    {
        Assert.That(EventTopic.ToTopic(typeof(Sales.Wrapper<string>)), Is.Not.EqualTo(EventTopic.ToTopic(typeof(Sales.Wrapper<int>))));
    }

    [TestCase(typeof(Sales.OrderPlaced))]
    [TestCase(typeof(Sales.Container.Inner))]
    [TestCase(typeof(Sales.Wrapper<string>))]
    [TestCase(typeof(Sales.Container.Nested<Sales.Wrapper<int>>))]
    [TestCase(typeof(Sales.GenericContainer<string>.Nested<int>))]
    [TestCase(typeof(Sales.Pair<string, int[]>))]
    [TestCase(typeof(Sales.Wrapper<Dictionary<string, List<int>>>))]
    public void Should_produce_a_valid_publishable_topic_that_is_the_same_on_every_call(Type eventType)
    {
        var topic = EventTopic.ToTopic(eventType);

        Assert.Multiple(() =>
        {
            Assert.That(topic, Does.StartWith("events/"));
            Assert.That(topic.Substring("events/".Length).IndexOfAny(['+', '#', '/', '`', '[', ']', ',', ' ', '*', '$']), Is.EqualTo(-1), "wildcards and separators are not allowed inside the event name");
            Assert.That(topic, Does.Not.Contain("Version="));
            Assert.That(topic, Does.Not.Contain("PublicKeyToken"));
            Assert.That(EventTopic.ToTopic(eventType), Is.EqualTo(topic));
        });
    }

    [Test]
    public void Should_not_depend_on_the_assembly_a_generic_argument_comes_from()
    {
        // the argument's assembly name and version must not leak into the topic, so a runtime upgrade does not change it
        var topic = EventTopic.ToTopic(typeof(Sales.Wrapper<string>));

        Assert.That(topic, Does.Not.Contain("CoreLib"));
    }
}
