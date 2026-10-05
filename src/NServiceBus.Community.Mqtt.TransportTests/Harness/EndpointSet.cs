#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Community.Mqtt.TestBroker;

/// <summary>
/// The endpoints one test creates. Disposing the set stops every endpoint first and then deletes the broker sessions they used, so
/// that clearing a session never takes it over from an endpoint that is still connected.
/// </summary>
sealed class EndpointSet : IAsyncDisposable
{
    public MqttTestEndpoint Create(string name, MqttTestEndpoint.Options? options = null)
    {
        var endpoint = new MqttTestEndpoint(MqttTestEndpoint.NewQueue(name), options);
        endpoints.Add(endpoint);

        return endpoint;
    }

    /// <summary>An endpoint on a queue that another endpoint of the test also used, such as the same endpoint after a restart.</summary>
    public MqttTestEndpoint CreateOn(string queue, MqttTestEndpoint.Options? options = null)
    {
        var endpoint = new MqttTestEndpoint(queue, options);
        endpoints.Add(endpoint);

        return endpoint;
    }

    /// <summary>An address that is not an endpoint's queue, such as a destination or an address a test sends to by hand.</summary>
    public string NewAddress(string name)
    {
        var address = MqttTestEndpoint.NewQueue(name);
        extraTopics.Add(NServiceBus.Transport.Mqtt.MqttAddress.ToTopic(address));

        return address;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var endpoint in Enumerable.Reverse(endpoints))
        {
            await endpoint.DisposeAsync();
        }

        await BrokerStateCleaner.Clear(endpoints.SelectMany(endpoint => endpoint.Topics).Concat(extraTopics));
    }

    readonly List<MqttTestEndpoint> endpoints = [];
    readonly List<string> extraTopics = [];
}
