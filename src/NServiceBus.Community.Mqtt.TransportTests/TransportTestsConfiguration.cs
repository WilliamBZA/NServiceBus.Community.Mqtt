namespace NServiceBus.TransportTests;

/// <summary>
/// Tells the standard transport test suite which adapter creates and initializes the MQTT transport.
/// </summary>
public partial class TransportTestsConfiguration
{
    public IConfigureTransportInfrastructure CreateTransportConfiguration() => new ConfigureMqttTransportInfrastructure();
}
