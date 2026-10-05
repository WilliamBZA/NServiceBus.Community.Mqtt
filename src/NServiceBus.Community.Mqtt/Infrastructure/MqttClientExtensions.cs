using MQTTnet.Client;

namespace NServiceBus.Transport.Mqtt
{
    static class MqttClientExtensions
    {
        static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Releases the client. MQTTnet cleans a client up when the broker ends its connection, and again when the client is disposed, and
        /// the second cleanup throws <see cref="ObjectDisposedException"/> if it meets the first one half way. That is a client that is gone
        /// either way, so it is not an error here.
        /// </summary>
        public static void DisposeQuietly(this IMqttClient client)
        {
            try
            {
                client.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // already cleaned up
            }
        }

        /// <summary>
        /// Ends the connection with a normal DISCONNECT, which keeps the session of a client that has one. It does not throw: a connection
        /// that cannot be closed cleanly is ended by the broker.
        /// </summary>
        public static async Task DisconnectQuietly(this IMqttClient client, CancellationToken cancellationToken = default)
        {
            if (!client.IsConnected)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutCancellationToken = timeout.Token;
            timeout.CancelAfter(DisconnectTimeout);

            try
            {
                await client.DisconnectAsync(new MqttClientDisconnectOptions { Reason = MqttClientDisconnectOptionsReason.NormalDisconnection }, timeoutCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCancellationToken.IsCancellationRequested)
            {
                // the broker did not answer in time
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // the connection was already gone
            }
        }
    }
}
