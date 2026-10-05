using System;
using System.Collections;
using System.Threading;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus
{
    /// <summary>
    /// An NServiceBus endpoint on a device. It has its own queue, a persistent MQTT 5 session that a .NET endpoint of the same name would use, handles
    /// one message at a time, and speaks the wire contract of <c>NServiceBus.Community.Mqtt</c>. A message is acknowledged to the broker when it arrives,
    /// before it is handled, so a message that is waiting or in progress when the device stops, crashes or loses power is lost.
    /// </summary>
    public sealed partial class DeviceEndpoint : IMessageSession
    {
        const int InitialBackOffMilliseconds = 1000;
        const int MaximumBackOffMilliseconds = 30000;
        const ushort KeepAliveSeconds = 60;

        readonly DeviceEndpointConfiguration configuration;
        readonly EndpointServices services;
        readonly IMqttConnection connection;
        readonly EndpointLog log;
        readonly string queueTopic;
        readonly HandlerRegistry handlers;
        readonly OutgoingMessageBuilder builder;
        readonly MqttConnectOptions connectOptions;

        readonly object stateLock = new object();
        readonly ManualResetEvent stopSignal = new ManualResetEvent(false);
        bool started;
        bool stopping;
        bool stopped;
        bool connected;
        bool displaced;
        bool reconnecting;
        Thread processingThread;
        Thread reconnectThread;

        // the event types the device is subscribed to: the type's full name to its event topic
        readonly Hashtable subscribedTopics = new Hashtable();
        readonly object subscriptionLock = new object();

        // serializes the publishes of the application's threads and of the processing thread
        readonly object dispatchLock = new object();

        DeviceEndpoint(DeviceEndpointConfiguration configuration, IMqttConnection connection, EndpointServices services)
        {
            this.configuration = configuration;
            this.connection = connection;
            this.services = services;
            log = new EndpointLog(configuration.Logger);

            queueTopic = MqttAddress.ToTopic(configuration.EndpointName);
            handlers = new HandlerRegistry(configuration.Registrations);
            builder = new OutgoingMessageBuilder(configuration.EndpointName, configuration.MaximumPacketSize, configuration.Routes, services);

            connectOptions = new MqttConnectOptions();
            connectOptions.ClientId = MqttClientId.ForQueue(queueTopic);
            connectOptions.Host = configuration.Server;
            connectOptions.Port = configuration.Port;
            connectOptions.Username = configuration.Username;
            connectOptions.Password = configuration.Password;
            connectOptions.SessionExpirySeconds = ToSeconds(configuration.SessionExpiry);
            connectOptions.MaximumPacketSize = configuration.MaximumPacketSize;
            connectOptions.KeepAliveSeconds = KeepAliveSeconds;
            connectOptions.OperationTimeout = configuration.DispatchTimeout;

            var eventTypes = configuration.EventTypesToSubscribe();
            for (var i = 0; i < eventTypes.Count; i++)
            {
                var eventType = (Type)eventTypes[i];
                subscribedTopics[eventType.FullName] = EventTopic.ToTopic(eventType);
            }
        }

        /// <summary>
        /// Connects to the broker, resumes the device's queue session, subscribes to the queue and to the subscribed events, and waits for the broker to
        /// confirm. Throws when the broker cannot be reached or refuses, and leaves no connection or thread behind.
        /// </summary>
        public static DeviceEndpoint Start(DeviceEndpointConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            return Start(configuration, new M2MqttConnection(), EndpointServices.CreateDefault());
        }

        internal static DeviceEndpoint Start(DeviceEndpointConfiguration configuration, IMqttConnection connection, EndpointServices services)
        {
            var endpoint = new DeviceEndpoint(configuration, connection, services);
            endpoint.StartUp();
            return endpoint;
        }

        void StartUp()
        {
            connection.MessageReceived += OnMessageReceived;
            connection.ConnectionLost += OnConnectionLost;

            try
            {
                connection.Connect(connectOptions);
            }
            catch (Exception exception)
            {
                Detach();
                throw new InvalidOperationException(
                    "Could not connect to the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + ". Check that the broker is running, that it supports MQTT 5 and persistent sessions, that the host and port are correct and that the credentials are accepted. ("
                    + exception.Message + ")",
                    exception);
            }

            try
            {
                connection.Subscribe(AllTopics());
            }
            catch (Exception exception)
            {
                DisconnectQuietly();
                Detach();
                throw new InvalidOperationException(
                    "Connected to the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + ", but it did not accept the subscriptions of the endpoint '" + configuration.EndpointName + "'. (" + exception.Message + ")",
                    exception);
            }

            lock (stateLock)
            {
                started = true;
                connected = true;
                processingThread = new Thread(ProcessLoop);
                processingThread.Start();
            }
        }

        /// <summary>
        /// Stops the endpoint. It lets the message in progress finish, processes the messages that have already arrived until they are done or the drain
        /// timeout passes, logs how many were left, and disconnects in a way that keeps the session. A second stop does nothing. Do not call it from a handler.
        /// </summary>
        public void Stop()
        {
            lock (stateLock)
            {
                if (stopping)
                {
                    return;
                }

                if (Thread.CurrentThread == processingThread)
                {
                    throw new InvalidOperationException("The endpoint cannot be stopped from one of its handlers, because stop waits for the handler to return.");
                }

                stopping = true;
                drainDeadline = DateTime.UtcNow.AddTicks(configuration.StopDrainTimeout.Ticks);
            }

            stopSignal.Set();
            SignalIntake();

            processingThread.Join();

            var leftOver = IntakeCount();
            if (leftOver > 0)
            {
                log.Warning("The endpoint '" + configuration.EndpointName + "' stopped with " + leftOver + " received message(s) that were not processed. They were already acknowledged to the broker, so they are lost.", null);
            }

            connected = false;
            DisconnectQuietly();

            Thread reconnect;
            lock (stateLock)
            {
                reconnect = reconnectThread;
            }

            if (reconnect != null)
            {
                reconnect.Join();
            }

            Detach();
            stopped = true;
        }

        void Detach()
        {
            connection.MessageReceived -= OnMessageReceived;
            connection.ConnectionLost -= OnConnectionLost;
        }

        void DisconnectQuietly()
        {
            try
            {
                connection.Disconnect();
            }
            catch (Exception exception)
            {
                log.Warning("The connection to the broker could not be closed cleanly. The broker ends it.", exception);
            }
        }

        void EnsureRunning()
        {
            if (stopping || stopped)
            {
                throw new InvalidOperationException("The endpoint '" + configuration.EndpointName + "' has been stopped.");
            }
        }

        /// <summary>The queue topic and the topic of every subscribed event type.</summary>
        string[] AllTopics()
        {
            lock (subscriptionLock)
            {
                var topics = new string[subscribedTopics.Count + 1];
                topics[0] = queueTopic;

                var index = 1;
                foreach (DictionaryEntry entry in subscribedTopics)
                {
                    topics[index++] = (string)entry.Value;
                }

                return topics;
            }
        }

        // nanoFramework's System.Math is a package of its own
        static int Min(int left, int right)
        {
            return left < right ? left : right;
        }

        static uint ToSeconds(TimeSpan value)
        {
            // rounded up, as the .NET transport does
            var seconds = (value.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond;
            return (uint)seconds;
        }

        // ---- critical errors

        void RaiseCriticalError(string description, Exception exception)
        {
            log.Error("Critical error: " + description, exception);

            var callback = configuration.OnCriticalError;
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(description, exception);
            }
            catch (Exception callbackException)
            {
                log.Error("The critical error callback threw an exception.", callbackException);
            }
        }

        // ---- connection events

        void OnConnectionLost(bool takenOver)
        {
            if (stopping)
            {
                return;
            }

            if (takenOver)
            {
                HandleTakeover();
                return;
            }

            lock (stateLock)
            {
                connected = false;

                // a connection that is being restored is not restored twice
                if (reconnecting || displaced || !started)
                {
                    return;
                }

                reconnecting = true;
                reconnectThread = new Thread(ReconnectLoop);
                reconnectThread.Start();
            }

            log.Warning("The connection to the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + " was lost. The endpoint '" + configuration.EndpointName + "' reconnects.", null);
        }

        void HandleTakeover()
        {
            lock (stateLock)
            {
                if (displaced)
                {
                    return;
                }

                displaced = true;
                connected = false;
            }

            var dropped = DropIntake();
            if (dropped > 0)
            {
                log.Error("The endpoint '" + configuration.EndpointName + "' dropped " + dropped + " received message(s), which were already acknowledged to the broker, because its session was taken over.", null);
            }

            RaiseCriticalError(
                "Another client took over the session of the endpoint '" + configuration.EndpointName + "' (MQTT reason code 0x8E). Only one consumer per queue is supported, so every device needs its own endpoint name. This device does not reconnect, and sends and publishes fail until it is restarted.",
                null);
        }

        void ReconnectLoop()
        {
            var backOff = InitialBackOffMilliseconds;
            var attempt = 0;

            while (true)
            {
                if (services.Delay.Wait(TimeSpan.FromMilliseconds(backOff), stopSignal) || stopping || displaced)
                {
                    lock (stateLock)
                    {
                        reconnecting = false;
                    }

                    return;
                }

                attempt++;
                try
                {
                    connection.Connect(connectOptions);
                    connection.Subscribe(AllTopics());
                }
                catch (Exception exception)
                {
                    log.Error("Attempt " + attempt + " to reconnect to the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + " failed. Trying again in " + (Min(backOff * 2, MaximumBackOffMilliseconds) / 1000) + " s. (" + exception.Message + ")", exception);
                    DisconnectQuietly();
                    backOff = Min(backOff * 2, MaximumBackOffMilliseconds);
                    continue;
                }

                lock (stateLock)
                {
                    connected = !displaced;
                    reconnecting = false;
                }

                log.Information("The endpoint '" + configuration.EndpointName + "' reconnected to the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + ".");
                return;
            }
        }
    }
}
