namespace NServiceBus.Community.Mqtt.AcceptanceTests;

using NServiceBus.AcceptanceTesting;
using NServiceBus.AcceptanceTests;
using NServiceBus.AcceptanceTests.EndpointTemplates;

// MQTT-specific: proves that the harness (the test broker, the adapter and the constraints) can run an endpoint at all
public class When_an_endpoint_starts : NServiceBusAcceptanceTest
{
    [Test]
    public async Task Should_start_on_the_test_broker()
    {
        var context = await Scenario.Define<ScenarioContext>()
            .WithEndpoint<StartedEndpoint>()
            .Done(c => c.EndpointsStarted)
            .Run();

        Assert.That(context.EndpointsStarted.Task.IsCompletedSuccessfully, Is.True);
    }

    class StartedEndpoint : EndpointConfigurationBuilder
    {
        public StartedEndpoint() => EndpointSetup<DefaultServer>();
    }
}
