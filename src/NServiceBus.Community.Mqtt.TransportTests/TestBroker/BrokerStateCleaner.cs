#nullable enable

namespace NServiceBus.Community.Mqtt.TestBroker;

using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using NServiceBus.Transport.Mqtt;

/// <summary>
/// Deletes the broker state the tests leave behind. Queue names repeat from one test to the next, and from one run to the next on a
/// long-lived broker, while the transport keeps a queue's messages in a persistent session. A message that an earlier test left in
/// a queue's session would be delivered to the next test that uses the same queue name, so every test starts and ends with the sessions
/// of the addresses it uses deleted.
/// </summary>
/// <remarks>
/// A session is deleted by connecting with its client ID and CleanStart=true, which discards the stored subscriptions and queued messages.
/// Client IDs are deterministic, so every session a test can have created is known: the queue's own session and the holder session
/// that queue declaration creates for an address.
/// </remarks>
static class BrokerStateCleaner
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <param name="topics">The MQTT topics of the addresses the test uses, as <see cref="MqttAddress"/> maps them.</param>
    public static async Task Clear(IEnumerable<string> topics, CancellationToken cancellationToken = default)
    {
        foreach (var topic in topics.Distinct())
        {
            await DeleteSession(MqttClientId.ForQueue(topic), cancellationToken);
            await DeleteSession(MqttClientId.ForDeclaredAddress(topic), cancellationToken);
        }
    }

    /// <summary>Deletes one session by its client ID, for sessions that are not named by an address the transport accepts.</summary>
    public static Task ClearSession(string clientId, CancellationToken cancellationToken = default) => DeleteSession(clientId, cancellationToken);

    // A session expiry of 0 means the empty session that this connection creates ends again as soon as it disconnects.
    static async Task DeleteSession(string clientId, CancellationToken cancellationToken)
    {
        using var client = new MqttFactory().CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(MqttTestBroker.Host, MqttTestBroker.Port)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId(clientId)
            .WithCleanStart(true)
            .WithSessionExpiryInterval(0)
            .WithTimeout(ConnectTimeout)
            .Build();

        await client.ConnectAsync(options, cancellationToken);
        await client.DisconnectAsync(new MqttClientDisconnectOptions { Reason = MqttClientDisconnectOptionsReason.NormalDisconnection }, cancellationToken);
    }
}
