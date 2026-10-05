#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Community.Mqtt.TestBroker;

using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

/// <summary>Talks to the broker without the transport, for what a device or another tool would do: publishing a payload the transport
/// did not write, and watching what the broker holds.</summary>
static class RawMqtt
{
    public static async Task Publish(string topic, byte[] payload, CancellationToken cancellationToken = default)
    {
        using var client = await Connect($"nsb.test.raw.{Guid.NewGuid():N}", cleanStart: true, cancellationToken: cancellationToken);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        var result = await client.PublishAsync(message, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"The broker did not accept the publish to '{topic}': {result.ReasonCode}");
        }

        await client.DisconnectAsync(new MqttClientDisconnectOptions { Reason = MqttClientDisconnectOptionsReason.NormalDisconnection }, cancellationToken);
    }

    /// <summary>
    /// Resumes a persistent session the way a consumer does and returns the payloads the broker delivers from it, acknowledging each one.
    /// The session is left on the broker with whatever it holds afterwards, so callers delete it with the broker state cleaner.
    /// </summary>
    public static async Task<byte[][]> Drain(string clientId, TimeSpan? wait = null, CancellationToken cancellationToken = default)
    {
        var payloads = new List<byte[]>();
        using var client = new MqttFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += args =>
        {
            lock (payloads)
            {
                payloads.Add(args.ApplicationMessage.PayloadSegment.ToArray());
            }

            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(MqttTestBroker.Host, MqttTestBroker.Port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId(clientId)
            .WithCleanStart(false)
            .WithSessionExpiryInterval(300)
            .Build();

        await client.ConnectAsync(options, cancellationToken);
        await Task.Delay(wait ?? TimeSpan.FromSeconds(1), cancellationToken);
        await client.DisconnectAsync(new MqttClientDisconnectOptions { Reason = MqttClientDisconnectOptionsReason.NormalDisconnection }, cancellationToken);

        lock (payloads)
        {
            return payloads.ToArray();
        }
    }

    /// <summary>
    /// Whether the broker holds a session for the client ID. Asking creates the session if there is none, so callers delete it with
    /// the broker state cleaner.
    /// </summary>
    public static async Task<bool> SessionExists(string clientId, CancellationToken cancellationToken = default)
    {
        using var client = new MqttFactory().CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(MqttTestBroker.Host, MqttTestBroker.Port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId(clientId)
            .WithCleanStart(false)
            .WithSessionExpiryInterval(60)
            .Build();

        var result = await client.ConnectAsync(options, cancellationToken);
        await client.DisconnectAsync(new MqttClientDisconnectOptions { Reason = MqttClientDisconnectOptionsReason.NormalDisconnection }, cancellationToken);

        return result.IsSessionPresent;
    }

    public static async Task<IMqttClient> Connect(string clientId, bool cleanStart, uint sessionExpirySeconds = 0, CancellationToken cancellationToken = default)
    {
        var client = new MqttFactory().CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(MqttTestBroker.Host, MqttTestBroker.Port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId(clientId)
            .WithCleanStart(cleanStart)
            .WithSessionExpiryInterval(sessionExpirySeconds)
            .Build();

        await client.ConnectAsync(options, cancellationToken);

        return client;
    }
}
