#nullable enable

namespace NServiceBus.Community.Mqtt.TestBroker;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;

/// <summary>
/// The MQTT broker the broker-backed tests run against. By default it starts a Mosquitto 2.x container with the repository's
/// configuration and removes it when the run ends. If <see cref="ServerVariable"/> and <see cref="PortVariable"/> are set, it uses that
/// existing broker instead and starts nothing. Either way it checks once, with a real MQTT connection, that the broker is usable, so a
/// missing broker fails the run within seconds instead of timing out every test.
/// </summary>
/// <remarks>
/// This file is shared: the transport tests project owns it and the acceptance tests project links it, so that each project starts its own
/// broker and the two can run at the same time without seeing each other's sessions.
/// </remarks>
static class MqttTestBroker
{
    public const string ServerVariable = "MqttTransport_Server";
    public const string PortVariable = "MqttTransport_Port";

    const string Image = "eclipse-mosquitto:2";
    const int BrokerPort = 1883;
    const string ConfigurationFile = "mosquitto.conf";

    static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    static string? host;
    static int port;
    static IContainer? container;

    public static string Host => host ?? throw NotStarted();

    public static int Port => port != 0 ? port : throw NotStarted();

    /// <summary>True if the tests use a broker the caller started, so nothing here may start, stop or reset it as a whole.</summary>
    public static bool IsExistingBroker { get; private set; }

    public static async Task Start()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        var portSetting = Environment.GetEnvironmentVariable(PortVariable);

        if (string.IsNullOrWhiteSpace(server) && string.IsNullOrWhiteSpace(portSetting))
        {
            await StartContainer();
        }
        else
        {
            await UseExistingBroker(server, portSetting);
        }
    }

    public static async Task Stop()
    {
        var running = container;
        container = null;

        if (running != null)
        {
            // stops and removes the container. Testcontainers' resource reaper does the same if this process is killed first.
            await running.DisposeAsync();
        }
    }

    /// <summary>Opens a connection to the broker and closes it again. Throws if the broker does not accept an MQTT 5 connection in time.</summary>
    public static async Task Probe(string brokerHost, int brokerPort, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        using var client = new MqttFactory().CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId($"nsb.test.probe.{Guid.NewGuid():N}")
            .WithCleanStart(true)
            .WithTimeout(timeout)
            .Build();

        await client.ConnectAsync(options, cancellation.Token);
        await client.DisconnectAsync(new MqttClientDisconnectOptions { Reason = MqttClientDisconnectOptionsReason.NormalDisconnection }, CancellationToken.None);
    }

    static async Task UseExistingBroker(string? server, string? portSetting)
    {
        if (string.IsNullOrWhiteSpace(server) || !int.TryParse(portSetting, out var parsedPort) || parsedPort is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"The {ServerVariable} and {PortVariable} environment variables select an existing MQTT broker, so both must be set, and the port must be a number from 1 to 65535. " +
                $"Found {ServerVariable}='{server}' and {PortVariable}='{portSetting}'. Set both, or unset both so the tests start their own Mosquitto 2.x broker in Docker.");
        }

        try
        {
            await Probe(server, parsedPort, ProbeTimeout);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot connect to the MQTT broker at {server}:{parsedPort}, which the {ServerVariable} and {PortVariable} environment variables select. " +
                $"Start a Mosquitto 2.x broker there with src/mosquitto/mosquitto.conf (see the README), correct the variables, or unset both so the tests start their own broker in Docker. ({ex.GetType().Name}: {ex.Message})",
                ex);
        }

        host = server;
        port = parsedPort;
        IsExistingBroker = true;
    }

    static async Task StartContainer()
    {
        var configurationPath = Path.Combine(AppContext.BaseDirectory, ConfigurationFile);
        if (!File.Exists(configurationPath))
        {
            throw new FileNotFoundException($"The Mosquitto configuration '{configurationPath}' is missing from the test output. It is linked from src/mosquitto/mosquitto.conf by the test project.", configurationPath);
        }

        IContainer mosquitto;
        try
        {
            mosquitto = new ContainerBuilder(Image)
                .WithPortBinding(BrokerPort, true)
                .WithResourceMapping(await File.ReadAllBytesAsync(configurationPath), "/mosquitto/config/mosquitto.conf")
                .Build();

            await mosquitto.StartAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Docker is required to run these tests, because they start a Mosquitto 2.x broker ({Image}) in a container, and the container could not be started. " +
                $"Start Docker (with Linux containers), or point the tests at an existing Mosquitto 2.x broker by setting the {ServerVariable} and {PortVariable} environment variables. " +
                $"({ex.GetType().Name}: {ex.Message})",
                ex);
        }

        container = mosquitto;
        var brokerHost = mosquitto.Hostname == "localhost" ? "127.0.0.1" : mosquitto.Hostname;
        var brokerPort = mosquitto.GetMappedPublicPort(BrokerPort);

        try
        {
            await WaitUntilReady(brokerHost, brokerPort);
        }
        catch
        {
            await Stop();
            throw;
        }

        host = brokerHost;
        port = brokerPort;
        IsExistingBroker = false;
    }

    static async Task WaitUntilReady(string brokerHost, int brokerPort)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;

        while (true)
        {
            try
            {
                await Probe(brokerHost, brokerPort, ProbeTimeout);
                return;
            }
            catch (Exception ex) when (DateTime.UtcNow < deadline)
            {
                _ = ex;
                await Task.Delay(250);
            }
            catch (Exception ex)
            {
                var (stdout, stderr) = await container!.GetLogsAsync();
                throw new InvalidOperationException(
                    $"The Mosquitto container started, but did not accept an MQTT connection on {brokerHost}:{brokerPort} within {ReadyTimeout.TotalSeconds:0} seconds. " +
                    $"({ex.GetType().Name}: {ex.Message}){Environment.NewLine}Container output:{Environment.NewLine}{stdout}{stderr}",
                    ex);
            }
        }
    }

    static InvalidOperationException NotStarted() =>
        new("The test broker has not been started. It is started once per test run by BrokerSetUpFixture.");
}
