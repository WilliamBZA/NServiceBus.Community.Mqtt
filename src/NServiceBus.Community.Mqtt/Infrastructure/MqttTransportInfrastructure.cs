namespace NServiceBus.Transport.Mqtt
{
    class MqttTransportInfrastructure : TransportInfrastructure
    {
        public MqttTransportInfrastructure(HostSettings settings, MqttTransport transport, ReceiveSettings[] receiverSettings, string[] sendingAddresses, MqttConnectionSettings connection)
        {
            this.settings = settings;
            this.transport = transport;
            this.receiverSettings = receiverSettings;
            this.connection = connection;

            // An address that cannot be a topic must fail here, before anything has connected, subscribed or created a session.
            receiveTopics = receiverSettings.Select(receiver => ToTransportAddress(receiver.ReceiveAddress)).ToArray();

            // An address that a local receiver reads from already has its queue session. The others get a holder session.
            holderTopics = sendingAddresses.Select(MqttAddress.ToTopic).Except(receiveTopics).Distinct().ToArray();
        }

        public string Server => connection.Server;

        public int Port => connection.Port;

        public void ConfigureReceiveInfrastructure(IReadOnlyCollection<string> explicitTopics)
        {
            this.explicitTopics = explicitTopics.ToArray();

            var receivers = new Dictionary<string, IMessageReceiver>();

            foreach (var receiverSetting in receiverSettings)
            {
                receivers.Add(receiverSetting.Id, CreateReceiver(receiverSetting, this.explicitTopics));
            }

            Receivers = receivers;
        }

        public IMessageReceiver CreateReceiver(ReceiveSettings receiveSettings, IReadOnlyCollection<string> explicitTopics) =>
            new MqttMessagePump(
                receiveSettings.Id,
                ToTransportAddress(receiveSettings.ReceiveAddress),
                connection,
                transport.TransportTransactionMode,
                receiveSettings.UsePublishSubscribe,
                explicitTopics,
                settings.CriticalErrorAction);

        public async Task ConfigureSendInfrastructure(CancellationToken cancellationToken = default)
        {
            var dispatcher = new MqttDispatcher(connection);
            Dispatcher = dispatcher;

            await dispatcher.Connect(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Purges the queues that were asked to be purged and, when the host asks for infrastructure to be set up, makes every address the
        /// endpoint receives from or sends to a queue: it keeps the messages sent to it until a consumer collects them, even if none has ever
        /// connected. This is the moment NServiceBus expects a purge to happen. Feature startup tasks run after it and can send messages
        /// to the endpoint's own queue, and those must not be purged.
        /// </summary>
        public async Task SetUpQueues(CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < receiveTopics.Length; i++)
            {
                var purge = receiverSettings[i].PurgeOnStartup;

                if (settings.SetupInfrastructure)
                {
                    await QueueDeclaration.DeclareQueue(connection, receiveTopics[i], explicitTopics, purge, cancellationToken).ConfigureAwait(false);
                }
                else if (purge)
                {
                    await QueueDeclaration.PurgeQueue(connection, receiveTopics[i], cancellationToken).ConfigureAwait(false);
                }
            }

            if (!settings.SetupInfrastructure)
            {
                return;
            }

            foreach (var topic in holderTopics)
            {
                await QueueDeclaration.DeclareSendingAddress(connection, topic, cancellationToken).ConfigureAwait(false);
            }
        }

        public override async Task Shutdown(CancellationToken cancellationToken = default)
        {
            await Task.WhenAll(Receivers.Values.Select(receiver => receiver.StopReceive(cancellationToken))).ConfigureAwait(false);

            foreach (var receiver in Receivers.Values.OfType<IDisposable>())
            {
                receiver.Dispose();
            }

            if (Dispatcher is MqttDispatcher dispatcher)
            {
                await dispatcher.Shutdown(cancellationToken).ConfigureAwait(false);
            }
        }

        public override string ToTransportAddress(QueueAddress address) => MqttAddress.ToTopic(address);

        readonly HostSettings settings;
        readonly ReceiveSettings[] receiverSettings;
        readonly MqttTransport transport;
        readonly MqttConnectionSettings connection;
        readonly string[] receiveTopics;
        readonly string[] holderTopics;
        string[] explicitTopics = [];
    }
}
