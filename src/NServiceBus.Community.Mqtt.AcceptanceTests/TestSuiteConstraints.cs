#nullable enable

namespace NServiceBus.AcceptanceTests;

using NServiceBus.AcceptanceTesting.Support;

/// <summary>
/// Tells the standard acceptance suite which capabilities the transport has, so tests that need others are ignored through the suite's
/// own Requires gates. The values agree with the capabilities MqttTransport declares.
/// </summary>
public partial class TestSuiteConstraints
{
    public bool SupportsDtc => false;

    public bool SupportsCrossQueueTransactions => false;

    public bool SupportsNativePubSub => true;

    public bool SupportsDelayedDelivery => false;

    // the suite's persistence has no outbox, so the tests that need one are ignored through Requires.OutboxPersistence
    public bool SupportsOutbox => false;

    public bool SupportsPurgeOnStartup => true;

    public IConfigureEndpointTestExecution CreateTransportConfiguration() => new ConfigureEndpointMqttTransport();

    public IConfigureEndpointTestExecution CreatePersistenceConfiguration() => new ConfigureEndpointAcceptanceTestingPersistence();
}
