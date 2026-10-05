namespace NServiceBus.Community.NanoframeworkMQTT.Sample
{
    /// <summary>
    /// The settings to edit before the sample is flashed to a board. The values here are placeholders. Keep real Wi-Fi credentials out of
    /// source control.
    /// </summary>
    internal static class SampleSettings
    {
        public const string WifiSsid = "your-wifi-name";

        public const string WifiPassword = "your-wifi-password";

        /// <summary>The host of the MQTT 5 broker, reached from the board's network. For a broker on your PC, use the PC's address on the network, not localhost.</summary>
        public const string BrokerHost = "192.168.1.10";

        public const int BrokerPort = 1883;

        /// <summary>The device's endpoint name, and so its queue. The interop host sends to it by this name, so leave it as it is for the board test.</summary>
        public const string EndpointName = "NanoInterop_Device";

        /// <summary>The endpoint name of the interop host, which the sample sends its acknowledgements to.</summary>
        public const string HostEndpointName = "NanoInterop_Host";
    }
}
