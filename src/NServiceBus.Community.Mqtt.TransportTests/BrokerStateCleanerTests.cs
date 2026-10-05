#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using System.Collections.Concurrent;
using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using NServiceBus.Community.Mqtt.TestBroker;
using NServiceBus.Transport.Mqtt;

[TestFixture]
public class BrokerStateCleanerTests
{
    [Test]
    public async Task Should_find_a_message_left_in_a_stale_queue_session_when_nothing_cleans_it()
    {
        // the control: without the cleaner the stale message is delivered, so the tests below can tell stale state from clean state
        var topic = NewTopic();
        var clientId = MqttClientId.ForQueue(topic);
        await LeaveStaleSession(clientId, topic);

        var resumed = await Resume(clientId);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.SessionPresent, Is.True);
            Assert.That(resumed.Received, Is.EqualTo(new[] { StaleMessage }));
        });
    }

    [Test]
    public async Task Should_delete_the_stale_message_and_subscription_of_a_queue_session()
    {
        var topic = NewTopic();
        var clientId = MqttClientId.ForQueue(topic);
        await LeaveStaleSession(clientId, topic);

        await BrokerStateCleaner.Clear([topic]);

        var resumed = await Resume(clientId);
        Assert.Multiple(() =>
        {
            Assert.That(resumed.SessionPresent, Is.False, "the session is gone");
            Assert.That(resumed.Received, Is.Empty, "and so is the message it had queued");
        });
    }

    [Test]
    public async Task Should_delete_the_stale_message_of_a_holder_session_for_a_declared_address()
    {
        var topic = NewTopic();
        var clientId = MqttClientId.ForDeclaredAddress(topic);
        await LeaveStaleSession(clientId, topic);

        await BrokerStateCleaner.Clear([topic]);

        var resumed = await Resume(clientId);
        Assert.Multiple(() =>
        {
            Assert.That(resumed.SessionPresent, Is.False);
            Assert.That(resumed.Received, Is.Empty);
        });
    }

    [Test]
    public async Task Should_leave_the_sessions_of_other_addresses_alone()
    {
        var cleaned = NewTopic();
        var other = NewTopic();
        var otherClientId = MqttClientId.ForQueue(other);
        await LeaveStaleSession(MqttClientId.ForQueue(cleaned), cleaned);
        await LeaveStaleSession(otherClientId, other);

        await BrokerStateCleaner.Clear([cleaned]);

        var resumed = await Resume(otherClientId);

        Assert.That(resumed.Received, Is.EqualTo(new[] { StaleMessage }));
    }

    [Test]
    public async Task Should_not_fail_when_there_is_nothing_to_clean_and_can_run_twice()
    {
        var topic = NewTopic();

        await BrokerStateCleaner.Clear([topic]);
        await BrokerStateCleaner.Clear([topic, topic]);

        var resumed = await Resume(MqttClientId.ForQueue(topic));
        Assert.That(resumed.SessionPresent, Is.False);
    }

    [TearDown]
    public async Task DeleteTheSessionsTheTestCreated()
    {
        await BrokerStateCleaner.Clear(topics);
        topics.Clear();
    }

    string NewTopic()
    {
        var topic = $"cleaner/{Guid.NewGuid():N}";
        topics.Add(topic);
        return topic;
    }

    readonly List<string> topics = [];

    const string StaleMessage = "stale";

    // Subscribes a persistent session to the topic and disconnects it, then publishes a message that the broker has to queue for it.
    static async Task LeaveStaleSession(string clientId, string topic)
    {
        var (subscriber, _, _) = await Connect(clientId, cleanStart: false);
        await subscriber.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(topic).WithAtLeastOnceQoS().Build());
        await subscriber.DisconnectAsync();
        subscriber.Dispose();

        var (publisher, _, _) = await Connect($"nsb.test.publisher.{Guid.NewGuid():N}", cleanStart: true);
        await publisher.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(StaleMessage))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());
        await publisher.DisconnectAsync();
        publisher.Dispose();
    }

    // Connects the way a returning consumer does, and reports whether the broker still had its session and what it delivered.
    static async Task<(bool SessionPresent, string[] Received)> Resume(string clientId)
    {
        var (client, result, received) = await Connect(clientId, cleanStart: false);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var messages = received.ToArray();

        await client.DisconnectAsync();
        client.Dispose();

        return (result.IsSessionPresent, messages);
    }

    static async Task<(IMqttClient Client, MqttClientConnectResult Result, ConcurrentQueue<string> Received)> Connect(string clientId, bool cleanStart)
    {
        var received = new ConcurrentQueue<string>();
        var client = new MqttFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e =>
        {
            received.Enqueue(Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment));
            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(MqttTestBroker.Host, MqttTestBroker.Port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId(clientId)
            .WithCleanStart(cleanStart)
            .WithSessionExpiryInterval(300)
            .Build();

        var result = await client.ConnectAsync(options);
        return (client, result, received);
    }
}
