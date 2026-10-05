namespace NServiceBus.Community.Mqtt.Tests;

using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;

[TestFixture]
public class MqttAddressTests
{
    [TestCase("Sales_Billing", "Sales/Billing")]
    [TestCase("Sales", "Sales")]
    [TestCase("a_b_c", "a/b/c")]
    [TestCase("error", "error")]
    [TestCase("Sales/Billing", "Sales/Billing")]
    public void Should_replace_underscores_with_topic_separators(string address, string expectedTopic)
    {
        Assert.That(MqttAddress.ToTopic(address), Is.EqualTo(expectedTopic));
    }

    [TestCase("a#b")]
    [TestCase("#")]
    [TestCase("+x")]
    [TestCase("Sales_+_Billing")]
    [TestCase("$SYS/x")]
    [TestCase("$share")]
    public void Should_reject_wildcards_and_dollar_prefixed_addresses_and_name_the_address(string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => MqttAddress.ToTopic(address));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain($"'{address}'"));
            Assert.That(exception.ParamName, Is.EqualTo("address"));
        });
    }

    [Test]
    public void Should_explain_the_wildcard_restriction()
    {
        var exception = Assert.Throws<ArgumentException>(() => MqttAddress.ToTopic("a#b"));

        Assert.That(exception!.Message, Does.Contain("'+' or '#'"));
    }

    [Test]
    public void Should_explain_the_dollar_restriction()
    {
        var exception = Assert.Throws<ArgumentException>(() => MqttAddress.ToTopic("$SYS/x"));

        Assert.That(exception!.Message, Does.Contain("'$'"));
    }

    [Test]
    public void Should_allow_a_dollar_sign_that_is_not_the_first_character()
    {
        Assert.That(MqttAddress.ToTopic("a$b"), Is.EqualTo("a$b"));
    }

    [TestCase("")]
    [TestCase(" ")]
    public void Should_reject_an_empty_address(string address)
    {
        Assert.Throws<ArgumentException>(() => MqttAddress.ToTopic(address));
    }

    [Test]
    public void Should_reject_a_null_address()
    {
        Assert.Throws<ArgumentException>(() => MqttAddress.ToTopic((string)null!));
    }

    [Test]
    public void Should_map_the_base_address_of_a_queue_address()
    {
        Assert.That(MqttAddress.ToTopic(new QueueAddress("Sales_Billing")), Is.EqualTo("Sales/Billing"));
    }

    [Test]
    public void Should_append_the_discriminator_and_qualifier_so_instance_specific_queues_are_distinct()
    {
        var main = MqttAddress.ToTopic(new QueueAddress("Sales"));
        var instance = MqttAddress.ToTopic(new QueueAddress("Sales", discriminator: "instance-1"));
        var qualified = MqttAddress.ToTopic(new QueueAddress("Sales", qualifier: "Retries"));
        var both = MqttAddress.ToTopic(new QueueAddress("Sales", discriminator: "instance-1", qualifier: "Retries"));

        Assert.Multiple(() =>
        {
            Assert.That(main, Is.EqualTo("Sales"));
            Assert.That(instance, Is.EqualTo("Sales-instance-1"));
            Assert.That(qualified, Is.EqualTo("Sales.Retries"));
            Assert.That(both, Is.EqualTo("Sales-instance-1.Retries"));
        });
    }

    [Test]
    public void Should_validate_the_composed_queue_address()
    {
        var exception = Assert.Throws<ArgumentException>(() => MqttAddress.ToTopic(new QueueAddress("Sales", discriminator: "a#b")));

        Assert.That(exception!.Message, Does.Contain("Sales-a#b"));
    }

    [Test]
    public void Should_use_the_same_mapping_in_the_transport_infrastructure()
    {
        var hostSettings = new HostSettings("Sales", string.Empty, new StartupDiagnosticEntries(), (_, _, _) => { }, setupInfrastructure: false);
        var infrastructure = new MqttTransportInfrastructure(hostSettings, new MqttTransport("localhost"), [], [], new MqttConnectionSettings("localhost", 1883, TimeSpan.FromDays(7)));

        Assert.That(infrastructure.ToTransportAddress(new QueueAddress("Sales_Billing")), Is.EqualTo("Sales/Billing"));
        Assert.Throws<ArgumentException>(() => infrastructure.ToTransportAddress(new QueueAddress("$SYS")));
    }
}
