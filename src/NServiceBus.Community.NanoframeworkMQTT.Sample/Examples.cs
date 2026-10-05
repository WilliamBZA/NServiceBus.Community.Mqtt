using System;
using Contracts;

namespace NServiceBus.Community.NanoframeworkMQTT.Sample
{
    /// <summary>
    /// The code examples of the README's device section. Nothing here runs: the file is in the sample so that the examples are compiled, and a unit
    /// test checks that the README shows exactly this code.
    /// </summary>
    internal static class Examples
    {
        public static void Settings(DeviceEndpointConfiguration configuration)
        {
            configuration.UseCredentials("gate-01", "a-secret");
            configuration.SessionExpiry = TimeSpan.FromHours(1);
            configuration.ImmediateRetries = 3;
            configuration.ErrorQueue = "error";
            configuration.MaximumPacketSize = 8192;
            configuration.DispatchTimeout = TimeSpan.FromSeconds(5);
        }

        public static void SendFromTheMainLoop(IMessageSession session)
        {
            var command = new OpenValve();
            command.ValveId = "V-1";
            command.Percent = 75;

            // to the endpoint the type is routed to
            session.Send(command);

            // or to an endpoint named in the options, with a header of your own
            var options = new SendOptions();
            options.Destination = "Plant_Backup";
            options.SetHeader("my.header", "a value");
            session.Send(command, options);
        }

        public static void PublishAndSubscribe(DeviceEndpoint endpoint)
        {
            var opened = new ValveOpened();
            opened.ValveId = "V-1";
            opened.Percent = 100;

            // one copy for ValveOpened, one for ValveEvent, and one for each interface
            endpoint.Publish(opened);

            // handled events are subscribed to when the endpoint starts, and others can be subscribed to at any time
            endpoint.Subscribe(typeof(PriceChanged));
            endpoint.Unsubscribe(typeof(PriceChanged));
        }

        public static void Stop(DeviceEndpoint endpoint)
        {
            endpoint.Stop();
        }
    }
}
