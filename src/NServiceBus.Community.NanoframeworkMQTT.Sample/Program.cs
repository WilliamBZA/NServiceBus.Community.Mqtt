extern alias NetworkHelperPackage;

using System;
using System.Diagnostics;
using System.Threading;
using Contracts;
using nanoFramework.Networking;
using Windows.Devices.WiFi;

namespace NServiceBus.Community.NanoframeworkMQTT.Sample
{
    /// <summary>
    /// A device that is an NServiceBus endpoint: it connects to Wi-Fi, sets its clock, and starts an endpoint that handles the commands and
    /// events of the interop host. The README describes how to run the board test with it.
    /// </summary>
    public class Program
    {
        public static void Main()
        {
            if (!ConnectToWifi())
            {
                Debug.WriteLine("Could not connect to Wi-Fi. Check the settings in SampleSettings.cs.");
                Thread.Sleep(Timeout.Infinite);
            }

            // NServiceBus puts the time a message was sent in its headers, so the clock has to be right before the endpoint starts
            SetClock();

            var configuration = new DeviceEndpointConfiguration(SampleSettings.EndpointName, SampleSettings.BrokerHost, SampleSettings.BrokerPort);
            configuration.SessionExpiry = TimeSpan.FromHours(1);
            configuration.OnCriticalError = OnCriticalError;
            configuration.RouteToEndpoint(typeof(ValveEventAcknowledged), SampleSettings.HostEndpointName);
            configuration.RegisterHandler(typeof(OpenValve), new OpenValveHandler());
            configuration.RegisterHandler(typeof(ValveEvent), new ValveEventHandler());
            configuration.RegisterHandler(typeof(AlwaysFails), new AlwaysFailsHandler());

            var endpoint = DeviceEndpoint.Start(configuration);
            Debug.WriteLine("The endpoint '" + SampleSettings.EndpointName + "' is running. Free memory after start: " + nanoFramework.Runtime.Native.GC.Run(true) + " bytes.");

            // the endpoint works on its own threads; stop it with endpoint.Stop() before the program ends
            Thread.Sleep(Timeout.Infinite);
        }

        static void OnCriticalError(string description, Exception exception)
        {
            Debug.WriteLine("Critical error: " + description);
        }

        static bool ConnectToWifi()
        {
            var cancellation = new CancellationTokenSource(60000);
            // the NetworkHelper package is aliased, because System.Net has a NetworkHelper of its own that only uses a network configuration stored on the device
            return NetworkHelperPackage::nanoFramework.Networking.NetworkHelper.ConnectWifiDhcp(SampleSettings.WifiSsid, SampleSettings.WifiPassword, WiFiReconnectionKind.Automatic, true, 0, cancellation.Token);
        }

        static void SetClock()
        {
            Sntp.Server1 = "pool.ntp.org";
            Sntp.Start();

            // wait for the first answer; a device that has not been told the time starts in 2011 or earlier
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow.Year < 2024 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(500);
            }
        }
    }
}
