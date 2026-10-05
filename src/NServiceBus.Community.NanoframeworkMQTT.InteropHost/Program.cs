using Contracts;
using InteropHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus;

// The .NET side of the manual board test. It runs an NServiceBus endpoint on the MQTT transport next to a device that runs the sample device app,
// checks that they understand each other, prints the result of each check, and exits with a non-zero code if any check failed.

var server = Environment.GetEnvironmentVariable("MqttTransport_Server") ?? "localhost";
var portText = Environment.GetEnvironmentVariable("MqttTransport_Port");
var port = portText is null ? 1883 : int.Parse(portText);
var timeout = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("InteropHost_TimeoutSeconds"), out var seconds) ? seconds : 20);

Console.WriteLine($"Interop host: broker {server}:{port}, device endpoint '{InteropEndpoints.Device}', timeout {timeout.TotalSeconds:0} s per check.");

var configuration = new EndpointConfiguration(InteropEndpoints.Host);
configuration.EnableInstallers();
configuration.UseSerialization<SystemJsonSerializer>();
configuration.SendFailedMessagesTo(InteropEndpoints.ErrorQueue);
// the MQTT transport has no delayed delivery, so there are no delayed retries
configuration.Recoverability().Delayed(delayed => delayed.NumberOfRetries(0));

var transport = new MqttTransport(server, port);
var routing = configuration.UseTransport(transport);
routing.RouteToEndpoint(typeof(OpenValve), InteropEndpoints.Device);
routing.RouteToEndpoint(typeof(AlwaysFails), InteropEndpoints.Device);

var results = new List<CheckResult>();
IHost? host = null;
try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Services.AddNServiceBusEndpoint(configuration);

    host = builder.Build();
    await host.StartAsync().ConfigureAwait(false);

    var checks = new InteropChecks(host.Services.GetRequiredService<IMessageSession>(), server, port, timeout);
    results.Add(await checks.CommandAndReply().ConfigureAwait(false));
    results.Add(await checks.DeviceEventReachesDotNetSubscriber().ConfigureAwait(false));
    results.Add(await checks.DotNetEventReachesDeviceThroughBaseType().ConfigureAwait(false));
    results.Add(await checks.FailedMessageEndsUpInTheErrorQueue().ConfigureAwait(false));
    results.Add(CheckResult.Skipped("A device message with a time to be received expires", "time to be received is not available on the device yet"));
}
catch (Exception exception)
{
    Console.Error.WriteLine($"The interop host could not run: {exception.Message}");
    results.Add(CheckResult.Failed("The interop host starts and connects to the broker", exception.Message));
}
finally
{
    if (host is not null)
    {
        await host.StopAsync().ConfigureAwait(false);
        host.Dispose();
    }
}

Console.WriteLine();
foreach (var result in results)
{
    Console.WriteLine(result);
}

var failed = results.Count(result => result.Outcome == Outcome.Failed);
var passed = results.Count(result => result.Outcome == Outcome.Passed);
var skipped = results.Count(result => result.Outcome == Outcome.Skipped);
Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped.");

return failed == 0 ? 0 : 1;
