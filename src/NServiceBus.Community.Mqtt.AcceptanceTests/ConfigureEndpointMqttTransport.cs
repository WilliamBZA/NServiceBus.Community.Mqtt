#nullable enable

using NServiceBus;
using NServiceBus.AcceptanceTesting.Support;
using NServiceBus.Community.Mqtt.TestBroker;
using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;

/// <summary>
/// The adapter the standard acceptance test suite uses to put the MQTT transport on an endpoint, and to clean up after the test.
/// </summary>
public class ConfigureEndpointMqttTransport : IConfigureEndpointTestExecution
{
    public Task Configure(string endpointName, EndpointConfiguration configuration, RunSettings settings, PublisherMetadata publisherMetadata)
    {
        transport = new BrokerCleaningMqttTransport(MqttTestBroker.Host, MqttTestBroker.Port)
        {
            // short, so that a crashed run leaves sessions on a long-lived broker only briefly
            SessionExpiry = TimeSpan.FromMinutes(5)
        };

        // MQTT has native publish/subscribe, so there are no publisher registrations to apply
        configuration.UseTransport(transport);

        return Task.CompletedTask;
    }

    public Task Cleanup() => transport?.DeleteSessions() ?? Task.CompletedTask;

    BrokerCleaningMqttTransport? transport;
}

/// <summary>
/// The MQTT transport, plus deletion of the broker sessions the endpoint uses. The transport is given every address the endpoint
/// receives from or sends to, including instance-specific and satellite addresses that the test adapter cannot know when it is configured.
/// </summary>
class BrokerCleaningMqttTransport(string server, int port) : MqttTransport(server, port)
{
    public override async Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
    {
        var receiveTopics = receivers.Select(receiver => MqttAddress.ToTopic(receiver.ReceiveAddress)).ToArray();
        var sharedTopics = receivers.Select(receiver => receiver.ErrorQueue).Concat(sendingAddresses).Select(MqttAddress.ToTopic).ToArray();

        lock (touchedTopics)
        {
            touchedTopics.UnionWith(receiveTopics);
            touchedTopics.UnionWith(sharedTopics);
        }

        // Start from a broker with none of this endpoint's own queue sessions from an earlier test or run on it. The error and audit addresses
        // are not cleaned here: other endpoints of the test may be receiving from them right now, and reconnecting with their session
        // would take it over. They are cleaned once the test is over. A test that restarts an endpoint must find the messages that were
        // queued for it, so each queue is cleaned only the first time this test uses it.
        await BrokerStateCleaner.Clear(receiveTopics.Where(IsFirstUseInThisTest), cancellationToken);

        return await base.Initialize(hostSettings, receivers, sendingAddresses, cancellationToken);
    }

    // Runs after the test, when every endpoint has stopped.
    public Task DeleteSessions()
    {
        string[] topics;
        lock (touchedTopics)
        {
            topics = touchedTopics.ToArray();
        }

        return BrokerStateCleaner.Clear(topics);
    }

    static bool IsFirstUseInThisTest(string topic)
    {
        lock (cleanedInTest)
        {
            return cleanedInTest.Add($"{TestContext.CurrentContext.Test.ID}|{topic}");
        }
    }

    readonly HashSet<string> touchedTopics = [];
    static readonly HashSet<string> cleanedInTest = [];
}
