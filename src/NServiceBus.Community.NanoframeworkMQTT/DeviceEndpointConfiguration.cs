using System;
using System.Collections;
using Microsoft.Extensions.Logging;
using nanoFramework.Logging.Debug;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus
{
    /// <summary>
    /// The settings of a device endpoint. The endpoint name is also the queue address, and it names the persistent MQTT 5 session that holds the
    /// messages sent to the device while it is offline. A device needs a name of its own: two devices with the same name take the session off each other.
    /// The configuration is read when the endpoint starts, so a change after that has no effect.
    /// </summary>
    public class DeviceEndpointConfiguration
    {
        const long TicksPerSecond = 10000000L;
        const long MaximumSessionExpirySeconds = 4294967295L;
        const int MaximumPacketSizeLimit = 268435455;

        string username;
        string password;
        TimeSpan sessionExpiry = TimeSpan.FromSeconds(7 * 24 * 60 * 60);
        int immediateRetries = 5;
        string errorQueue = "error";
        int maximumPacketSize = 16384;
        TimeSpan dispatchTimeout = TimeSpan.FromSeconds(10);
        TimeSpan stopDrainTimeout = TimeSpan.FromSeconds(10);
        ILogger logger = new DebugLogger("NServiceBus.Community.NanoframeworkMQTT");

        readonly ArrayList registrations = new ArrayList();
        readonly Hashtable routes = new Hashtable();
        readonly ArrayList subscriptions = new ArrayList();
        readonly ArrayList optedOut = new ArrayList();

        /// <param name="endpointName">The endpoint name, which is also the queue address. It cannot be empty, contain <c>+</c> or <c>#</c>, or start with <c>$</c>.</param>
        /// <param name="server">The host of the MQTT 5 broker.</param>
        /// <param name="port">The port of the broker, from 1 to 65535.</param>
        public DeviceEndpointConfiguration(string endpointName, string server, int port = 1883)
        {
            // validates the name, and the exception names it
            MqttAddress.ToTopic(endpointName);

            if (MqttAddress.IsNullOrWhiteSpace(server))
            {
                throw new ArgumentException("The broker's host cannot be empty.", "server");
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port", "The port " + port + " is not a valid port. It must be from 1 to 65535.");
            }

            EndpointName = endpointName;
            Server = server;
            Port = port;
        }

        internal string EndpointName { get; private set; }

        internal string Server { get; private set; }

        internal int Port { get; private set; }

        internal string Username
        {
            get { return username; }
        }

        internal string Password
        {
            get { return password; }
        }

        /// <summary>Sends this username and password when connecting.</summary>
        public void UseCredentials(string username, string password)
        {
            if (username == null || username.Length == 0)
            {
                throw new ArgumentException("The username cannot be empty.", "username");
            }

            if (password == null)
            {
                throw new ArgumentNullException("password");
            }

            this.username = username;
            this.password = password;
        }

        /// <summary>How long the broker keeps the device's queue session, and the messages in it, while the device is offline. From one second up to 4294967295 seconds. The default is 7 days.</summary>
        public TimeSpan SessionExpiry
        {
            get { return sessionExpiry; }
            set
            {
                if (value.Ticks < TicksPerSecond || value.Ticks > MaximumSessionExpirySeconds * TicksPerSecond)
                {
                    throw new ArgumentOutOfRangeException("value", "The session expiry must be from one second up to 4294967295 seconds.");
                }

                sessionExpiry = value;
            }
        }

        /// <summary>How many times a failed message is handled again at once before it goes to the error queue. The default is 5, and 0 turns retries off.</summary>
        public int ImmediateRetries
        {
            get { return immediateRetries; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value", "The number of immediate retries cannot be negative.");
                }

                immediateRetries = value;
            }
        }

        /// <summary>The queue failed messages are sent to. The default is <c>error</c>.</summary>
        public string ErrorQueue
        {
            get { return errorQueue; }
            set
            {
                // validates the address, and the exception names it
                MqttAddress.ToTopic(value);
                errorQueue = value;
            }
        }

        /// <summary>The largest MQTT packet the device declares it can receive, and the largest it sends. The default is 16384 bytes.</summary>
        public int MaximumPacketSize
        {
            get { return maximumPacketSize; }
            set
            {
                if (value < 1 || value > MaximumPacketSizeLimit)
                {
                    throw new ArgumentOutOfRangeException("value", "The maximum packet size must be from 1 to " + MaximumPacketSizeLimit + " bytes.");
                }

                maximumPacketSize = value;
            }
        }

        /// <summary>How long a send, a publish, a subscribe or an unsubscribe waits for the broker. The default is 10 seconds. A message that timed out may still reach the broker.</summary>
        public TimeSpan DispatchTimeout
        {
            get { return dispatchTimeout; }
            set
            {
                if (value.Ticks <= 0)
                {
                    throw new ArgumentOutOfRangeException("value", "The dispatch timeout must be longer than zero.");
                }

                dispatchTimeout = value;
            }
        }

        /// <summary>How long stop keeps processing the messages that have already arrived. The default is 10 seconds, and zero means to stop after the message in progress.</summary>
        public TimeSpan StopDrainTimeout
        {
            get { return stopDrainTimeout; }
            set
            {
                if (value.Ticks < 0)
                {
                    throw new ArgumentOutOfRangeException("value", "The stop drain timeout cannot be negative.");
                }

                stopDrainTimeout = value;
            }
        }

        /// <summary>Where the endpoint logs. The default writes to the debug output.</summary>
        public ILogger Logger
        {
            get { return logger; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                logger = value;
            }
        }

        /// <summary>Called for critical errors. Without it, they are logged at error level.</summary>
        public CriticalErrorCallback OnCriticalError { get; set; }

        /// <summary>Registers a handler for a message type, and for the types that derive from or implement it. Handlers of one message run in the order they were registered.</summary>
        public void RegisterHandler(Type messageType, IHandleMessages handler)
        {
            if (messageType == null)
            {
                throw new ArgumentNullException("messageType");
            }

            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            registrations.Add(new HandlerRegistration(messageType, handler));
        }

        /// <summary>Routes a command type to an endpoint. A destination given with a send takes precedence.</summary>
        public void RouteToEndpoint(Type messageType, string destination)
        {
            if (messageType == null)
            {
                throw new ArgumentNullException("messageType");
            }

            // validates the destination, and the exception names it
            MqttAddress.ToTopic(destination);

            routes[messageType.FullName] = destination;
        }

        /// <summary>Subscribes to an event type when the endpoint starts. Handled event types are subscribed automatically.</summary>
        public void Subscribe(Type eventType)
        {
            CheckEventType(eventType);

            optedOut.Remove(eventType);
            if (!TypeRelations.Contains(subscriptions, eventType))
            {
                subscriptions.Add(eventType);
            }
        }

        /// <summary>Takes back a subscription made before the endpoint starts, and keeps a handled event type from being subscribed automatically.</summary>
        public void Unsubscribe(Type eventType)
        {
            CheckEventType(eventType);

            subscriptions.Remove(eventType);
            if (!TypeRelations.Contains(optedOut, eventType))
            {
                optedOut.Add(eventType);
            }
        }

        static void CheckEventType(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException("eventType");
            }

            if (!TypeRelations.IsEvent(eventType))
            {
                throw new ArgumentException("The type '" + eventType.FullName + "' is not an event. Only a type that implements NServiceBus.IEvent can be subscribed to.", "eventType");
            }
        }

        internal ArrayList Registrations
        {
            get { return registrations; }
        }

        internal Hashtable Routes
        {
            get { return routes; }
        }

        /// <summary>The event types to subscribe to at start: the explicit subscriptions, and the handled event types that were not taken back.</summary>
        internal ArrayList EventTypesToSubscribe()
        {
            var types = new ArrayList();
            for (var i = 0; i < subscriptions.Count; i++)
            {
                types.Add(subscriptions[i]);
            }

            for (var i = 0; i < registrations.Count; i++)
            {
                var type = ((HandlerRegistration)registrations[i]).MessageType;
                if (TypeRelations.IsEvent(type) && !TypeRelations.Contains(types, type) && !TypeRelations.Contains(optedOut, type))
                {
                    types.Add(type);
                }
            }

            return types;
        }
    }

    internal sealed class HandlerRegistration
    {
        public HandlerRegistration(Type messageType, IHandleMessages handler)
        {
            MessageType = messageType;
            Handler = handler;
        }

        public Type MessageType { get; private set; }

        public IHandleMessages Handler { get; private set; }
    }
}
