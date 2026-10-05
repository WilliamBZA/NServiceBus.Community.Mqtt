namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Community.Mqtt.TestBroker;

[TestFixture]
public class BrokerTests
{
    [Test]
    public async Task Should_reach_the_test_broker_with_an_mqtt_connection()
    {
        await MqttTestBroker.Probe(MqttTestBroker.Host, MqttTestBroker.Port, TimeSpan.FromSeconds(5));

        TestContext.WriteLine($"Test broker: {MqttTestBroker.Host}:{MqttTestBroker.Port} ({(MqttTestBroker.IsExistingBroker ? "existing broker selected by the environment variables" : "container started by the test run")})");
    }
}
