using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;

namespace NServiceBus
{
    public class MqttTransport : TransportDefinition
    {
        public MqttTransport(string server, int port = 1883)
            : base(TransportTransactionMode.ReceiveOnly,
                supportsDelayedDelivery: false,
                supportsPublishSubscribe: true,
                supportsTTBR: true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(server);

            if (port is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be between 1 and 65535.");
            }

            Server = server;
            Port = port;
            subscriptions = new List<string>();
        }

        public string Server { get; }

        public int Port { get; }

        /// <summary>
        /// How long the broker keeps an endpoint's session, and the messages queued in it, while the endpoint is offline.
        /// Defaults to 7 days. Must be between one second and the largest interval MQTT 5 can express (about 136 years).
        /// </summary>
        public TimeSpan SessionExpiry
        {
            get => sessionExpiry;
            set
            {
                if (value < MinSessionExpiry || value > MaxSessionExpiry)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"The session expiry must be between {MinSessionExpiry} and {MaxSessionExpiry}.");
                }

                sessionExpiry = value;
            }
        }

        public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() => SupportedTransactionModes;

        public override async Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
        {
            var infrastructure = new MqttTransportInfrastructure(hostSettings, this, receivers, sendingAddresses, new MqttConnectionSettings(Server, Port, SessionExpiry));

            var initialized = false;
            try
            {
                infrastructure.ConfigureReceiveInfrastructure(subscriptions);
                await infrastructure.ConfigureSendInfrastructure(cancellationToken).ConfigureAwait(false);
                await infrastructure.SetUpQueues(cancellationToken).ConfigureAwait(false);

                initialized = true;
                return infrastructure;
            }
            finally
            {
                if (!initialized)
                {
                    // nothing has been handed out yet, so nobody else can release what was acquired
                    await infrastructure.Shutdown(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        public void SubscribeTo(string topic)
        {
            subscriptions.Add(topic);
        }

        // None acknowledges on receipt and ReceiveOnly after processing. MQTT has no way to make a send atomic with a receive.
        static readonly TransportTransactionMode[] SupportedTransactionModes = [TransportTransactionMode.None, TransportTransactionMode.ReceiveOnly];
        static readonly TimeSpan MinSessionExpiry = TimeSpan.FromSeconds(1);
        static readonly TimeSpan MaxSessionExpiry = TimeSpan.FromSeconds(uint.MaxValue);

        List<string> subscriptions;
        TimeSpan sessionExpiry = TimeSpan.FromDays(7);
    }
}