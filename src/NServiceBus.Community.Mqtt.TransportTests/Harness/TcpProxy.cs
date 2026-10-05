#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Community.Mqtt.TestBroker;


using System.Net;
using System.Net.Sockets;

/// <summary>
/// A TCP proxy between a test endpoint and the broker, so a test can cut the connection and restore it, the way a network
/// failure does, without touching the broker (other tests run on it).
/// </summary>
sealed class TcpProxy : IDisposable
{
    public TcpProxy(string targetHost, int targetPort)
    {
        this.targetHost = targetHost;
        this.targetPort = targetPort;

        listener = Listen(0);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public string Host => "127.0.0.1";

    public int Port { get; }

    /// <summary>How many client connections are open through the proxy. Each one is a client the broker sees as connected.</summary>
    public int ActiveConnections => Snapshot().Length / 2;

    /// <summary>Closes every connection that goes through the proxy, and refuses new ones until <see cref="Restore"/>.</summary>
    public void Cut()
    {
        listener.Stop();

        foreach (var connection in Snapshot())
        {
            connection.Dispose();
        }
    }

    public void Restore() => listener = Listen(Port);

    public void Dispose()
    {
        Cut();
        disposed = true;
    }

    TcpListener Listen(int port)
    {
        var newListener = new TcpListener(IPAddress.Loopback, port);
        newListener.Start();
        _ = AcceptLoop(newListener);

        return newListener;
    }

    async Task AcceptLoop(TcpListener acceptingListener)
    {
        try
        {
            while (!disposed)
            {
                var client = await acceptingListener.AcceptTcpClientAsync();
                _ = Forward(client);
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException or InvalidOperationException)
        {
            // the listener was stopped
        }
    }

    async Task Forward(TcpClient client)
    {
        var upstream = new TcpClient();
        try
        {
            await upstream.ConnectAsync(targetHost, targetPort);
        }
        catch (Exception)
        {
            client.Dispose();
            upstream.Dispose();
            return;
        }

        Track(client);
        Track(upstream);

        try
        {
            await Task.WhenAny(
                client.GetStream().CopyToAsync(upstream.GetStream()),
                upstream.GetStream().CopyToAsync(client.GetStream()));
        }
        catch (Exception)
        {
            // one side was closed
        }
        finally
        {
            client.Dispose();
            upstream.Dispose();
            Untrack(client);
            Untrack(upstream);
        }
    }

    void Track(TcpClient connection)
    {
        lock (connections)
        {
            connections.Add(connection);
        }
    }

    void Untrack(TcpClient connection)
    {
        lock (connections)
        {
            connections.Remove(connection);
        }
    }

    TcpClient[] Snapshot()
    {
        lock (connections)
        {
            return connections.ToArray();
        }
    }

    readonly string targetHost;
    readonly int targetPort;
    readonly List<TcpClient> connections = [];
    TcpListener listener;
    volatile bool disposed;
}
