namespace NServiceBus.Community.Mqtt.Tests;

using NServiceBus.Transport.Mqtt;

[TestFixture]
public class MqttClientIdTests
{
    [TestCase("error", "nsb.error")]
    [TestCase("Sales", "nsb.Sales")]
    [TestCase("Sales/Billing", "nsb.Sales/Billing")]
    [TestCase("Sales-instance-1.Retries", "nsb.Sales-instance-1.Retries")]
    public void Should_prefix_the_queue_topic(string queueTopic, string expectedClientId)
    {
        Assert.That(MqttClientId.ForQueue(queueTopic), Is.EqualTo(expectedClientId));
    }

    [Test]
    public void Should_be_the_same_on_every_call_for_the_same_queue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MqttClientId.ForQueue("Sales/Billing"), Is.EqualTo(MqttClientId.ForQueue("Sales/Billing")));
            Assert.That(MqttClientId.ForQueue("a b/ünï"), Is.EqualTo(MqttClientId.ForQueue("a b/ünï")));
            Assert.That(MqttClientId.ForDeclaredAddress("error"), Is.EqualTo(MqttClientId.ForDeclaredAddress("error")));
        });
    }

    [Test]
    public void Should_give_an_address_that_maps_to_the_same_topic_the_same_client_id()
    {
        Assert.That(
            MqttClientId.ForQueue(MqttAddress.ToTopic("Sales_Billing")),
            Is.EqualTo(MqttClientId.ForQueue(MqttAddress.ToTopic("Sales/Billing"))));
    }

    [TestCase("a b", "nsb.a~20b")]
    [TestCase("a+b", "nsb.a~2Bb")]
    [TestCase("a#b", "nsb.a~23b")]
    [TestCase("a$b", "nsb.a~24b")]
    [TestCase("a:b", "nsb.a~3Ab")]
    [TestCase("a~b", "nsb.a~7Eb")]
    [TestCase("ünï", "nsb.~C3~BCn~C3~AF")]
    [TestCase("a😅b", "nsb.a~F0~9F~98~85b")]
    public void Should_escape_characters_a_broker_may_reject(string queueTopic, string expectedClientId)
    {
        Assert.That(MqttClientId.ForQueue(queueTopic), Is.EqualTo(expectedClientId));
    }

    [TestCase("Sales/Billing")]
    [TestCase("a b\tc\r\nd")]
    [TestCase("+#$/~")]
    [TestCase("ünï-😅-B7=😍")]
    [TestCase("ab\ud800cd")]
    [TestCase("日本語")]
    public void Should_only_use_characters_every_broker_accepts(string queueTopic)
    {
        var ids = new[]
        {
            MqttClientId.ForQueue(queueTopic),
            MqttClientId.ForDeclaredAddress(queueTopic),
            MqttClientId.ForDispatcher(Guid.NewGuid())
        };

        foreach (var id in ids)
        {
            Assert.That(id, Does.Match("^[A-Za-z0-9._~/-]+$"), id);
        }
    }

    [Test]
    public void Should_not_give_two_different_queues_the_same_client_id()
    {
        // 'a b' and 'a~20b' differ only because '~' itself is escaped, and sanitising must never merge distinct queues
        var topics = new[] { "a b", "a-b", "a_b", "a.b", "a~20b", "a~b", "a+b", "a#b", "a$b", "ab", "aB", "a/b" };
        var ids = topics.Select(MqttClientId.ForQueue).ToArray();

        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void Should_use_a_different_session_for_the_holder_of_a_declared_address()
    {
        var queue = MqttClientId.ForQueue("error");
        var holder = MqttClientId.ForDeclaredAddress("error");

        Assert.Multiple(() =>
        {
            Assert.That(holder, Is.EqualTo("nsb.error.declared"));
            Assert.That(holder, Is.Not.EqualTo(queue), "declaring an address must never take over a live consumer of it");
        });
    }

    [Test]
    public void Should_name_a_dispatcher_after_its_id()
    {
        var id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Multiple(() =>
        {
            Assert.That(MqttClientId.ForDispatcher(id), Is.EqualTo("nsb.dispatch.0f8fad5bd9cb469fa16570867728950e"));
            Assert.That(MqttClientId.ForDispatcher(Guid.NewGuid()), Is.Not.EqualTo(MqttClientId.ForDispatcher(Guid.NewGuid())));
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(null)]
    public void Should_reject_an_empty_queue(string? queueTopic)
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => MqttClientId.ForQueue(queueTopic!));
            Assert.Throws<ArgumentException>(() => MqttClientId.ForDeclaredAddress(queueTopic!));
        });
    }
}
