using NServiceBus.Community.Mqtt.TestBroker;

/// <summary>
/// Starts the test broker once before the first test of the assembly and stops it after the last one. It has no namespace on purpose:
/// NUnit then applies it to every test in the assembly, including the suite tests, which live in their own namespaces.
/// </summary>
[SetUpFixture]
public class BrokerSetUpFixture
{
    [OneTimeSetUp]
    public Task StartBroker() => MqttTestBroker.Start();

    [OneTimeTearDown]
    public Task StopBroker() => MqttTestBroker.Stop();
}
