#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

static class Wait
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static async Task Until(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {description}.");
            }

            await Task.Delay(25);
        }
    }

    public static async Task<T> For<T>(Task<T> task, string description, TimeSpan? timeout = null)
    {
        try
        {
            return await task.WaitAsync(timeout ?? DefaultTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
    }

    public static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}
