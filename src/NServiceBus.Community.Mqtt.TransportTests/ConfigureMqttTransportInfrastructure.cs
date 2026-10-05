using NServiceBus;
using NServiceBus.Community.Mqtt.TestBroker;
using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;
using NServiceBus.TransportTests;

/// <summary>
/// The adapter the standard transport test suite uses to create and initialize the MQTT transport. The suite reaches it through
/// TransportTestsConfiguration.
/// </summary>
public class ConfigureMqttTransportInfrastructure : IConfigureTransportInfrastructure
{
    public TransportDefinition CreateTransportDefinition() =>
        new MqttTransport(MqttTestBroker.Host, MqttTestBroker.Port)
        {
            // short, so that a crashed run leaves sessions on a long-lived broker only briefly
            SessionExpiry = TimeSpan.FromMinutes(5)
        };

    public async Task<TransportInfrastructure> Configure(TransportDefinition transportDefinition, HostSettings hostSettings, QueueAddress inputQueueName, string errorQueueName, CancellationToken cancellationToken = default)
    {
        // queue names repeat between tests and between runs, so start from a broker with none of this test's sessions on it
        topics = [MqttAddress.ToTopic(inputQueueName), MqttAddress.ToTopic(errorQueueName)];
        await BrokerStateCleaner.Clear(topics, cancellationToken);

        var mainReceiver = new ReceiveSettings("mainReceiver", inputQueueName, usePublishSubscribe: true, purgeOnStartup: false, errorQueueName);

        return await transportDefinition.Initialize(hostSettings, [mainReceiver], [errorQueueName], cancellationToken);
    }

    public Task Cleanup(CancellationToken cancellationToken = default) =>
        topics.Count == 0 ? Task.CompletedTask : BrokerStateCleaner.Clear(topics, cancellationToken);

    List<string> topics = [];
}
