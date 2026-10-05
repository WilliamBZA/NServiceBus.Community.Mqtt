using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace InteropHost;

/// <summary>
/// Reads the error queue as the holder session <c>nsb.{address}.declared</c>, in which a .NET endpoint with installers keeps the messages sent to
/// an address that nobody receives from. Every message in the session is acknowledged, so reading consumes the failed messages of earlier runs.
/// </summary>
static class ErrorQueueReader
{
    /// <returns>The headers of the failed message with this ID, or <c>null</c> if it did not arrive in time.</returns>
    public static async Task<IReadOnlyDictionary<string, string>?> WaitFor(string server, int port, string errorQueue, string messageId, TimeSpan timeout)
    {
        var topic = errorQueue.Replace('_', '/');
        var clientId = $"nsb.{topic}.declared";
        var found = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var client = new MqttFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += arguments =>
        {
            var headers = ReadHeaders(arguments.ApplicationMessage.PayloadSegment);
            if (headers is not null && headers.TryGetValue("NServiceBus.MessageId", out var id) && id == messageId)
            {
                found.TrySetResult(headers);
            }

            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(server, port)
            .WithClientId(clientId)
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
            .WithCleanStart(false)
            .WithSessionExpiryInterval((uint)TimeSpan.FromDays(7).TotalSeconds)
            .Build();

        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await client.ConnectAsync(options, cancellation.Token).ConfigureAwait(false);
            await client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), cancellation.Token).ConfigureAwait(false);

            await using var registration = cancellation.Token.Register(() => found.TrySetCanceled()).ConfigureAwait(false);
            return await found.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync().ConfigureAwait(false);
            }
        }
    }

    // {"Id":...,"Headers":{...},"Body":"base64"}
    static Dictionary<string, string>? ReadHeaders(ArraySegment<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("Headers", out var headers) || headers.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var result = new Dictionary<string, string>();
            foreach (var header in headers.EnumerateObject())
            {
                result[header.Name] = header.Value.GetString() ?? string.Empty;
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
