using System;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus
{
    public sealed partial class DeviceEndpoint
    {
        /// <summary>Sends a command to the endpoint its route names. Returns after the broker has acknowledged it.</summary>
        public void Send(object message)
        {
            Send(message, null);
        }

        /// <summary>Sends a command to the destination in the options, or else to the route of its type. Returns after the broker has acknowledged it.</summary>
        public void Send(object message, SendOptions options)
        {
            EnsureRunning();
            Dispatch(builder.BuildSend(message, options, null));
        }

        /// <summary>Publishes an event, once for every type in its hierarchy. Returns after the broker has acknowledged every copy.</summary>
        public void Publish(object message)
        {
            Publish(message, null);
        }

        /// <summary>Publishes an event, once for every type in its hierarchy. Returns after the broker has acknowledged every copy.</summary>
        public void Publish(object message, PublishOptions options)
        {
            EnsureRunning();
            Dispatch(builder.BuildPublish(message, options, null));
        }

        /// <summary>Subscribes to an event type, and returns after the broker has confirmed it. Subscriptions are applied again after a reconnect.</summary>
        public void Subscribe(Type eventType)
        {
            EnsureRunning();
            var topic = CheckEventTopic(eventType);
            EnsureConnected("subscribe to '" + eventType.FullName + "'");

            // recorded first, so that a copy of the event that arrives before the confirmation is already known to be subscribed
            lock (subscriptionLock)
            {
                subscribedTopics[eventType.FullName] = topic;
            }

            try
            {
                connection.Subscribe(new string[] { topic });
            }
            catch (Exception exception)
            {
                lock (subscriptionLock)
                {
                    subscribedTopics.Remove(eventType.FullName);
                }

                throw new InvalidOperationException("Could not subscribe to '" + eventType.FullName + "' (topic '" + topic + "') through the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + ". (" + exception.Message + ")", exception);
            }
        }

        /// <summary>Unsubscribes from an event type, and returns after the broker has confirmed it. The other subscriptions are kept.</summary>
        public void Unsubscribe(Type eventType)
        {
            EnsureRunning();
            var topic = CheckEventTopic(eventType);
            EnsureConnected("unsubscribe from '" + eventType.FullName + "'");

            try
            {
                connection.Unsubscribe(new string[] { topic });
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Could not unsubscribe from '" + eventType.FullName + "' (topic '" + topic + "') through the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + ". (" + exception.Message + ")", exception);
            }

            lock (subscriptionLock)
            {
                subscribedTopics.Remove(eventType.FullName);
            }
        }

        static string CheckEventTopic(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException("eventType");
            }

            if (!TypeRelations.IsEvent(eventType))
            {
                throw new ArgumentException("The type '" + eventType.FullName + "' is not an event. Only a type that implements NServiceBus.IEvent can be subscribed to.", "eventType");
            }

            return EventTopic.ToTopic(eventType);
        }

        void EnsureConnected(string action)
        {
            if (displaced)
            {
                throw DisplacedError("Cannot " + action + ".");
            }

            if (!connected)
            {
                throw new InvalidOperationException("Cannot " + action + ": the device is not connected to the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + ".");
            }
        }

        InvalidOperationException DisplacedError(string text)
        {
            return new InvalidOperationException(text + " The endpoint '" + configuration.EndpointName + "' was displaced: another client took over its session, because only one consumer per queue is supported. Every device needs its own endpoint name.");
        }

        /// <summary>Publishes every copy of a message, in order. The first failure stops it.</summary>
        void Dispatch(OutgoingMessage message)
        {
            for (var i = 0; i < message.Copies.Length; i++)
            {
                var copy = message.Copies[i];
                PublishToBroker(copy.Destination, copy.Topic, copy.Payload);
            }
        }

        void PublishToBroker(string destination, string topic, byte[] payload)
        {
            lock (dispatchLock)
            {
                if (displaced)
                {
                    throw DisplacedError("Could not dispatch the message to '" + destination + "' (topic '" + topic + "').");
                }

                if (!connected)
                {
                    throw new InvalidOperationException("Could not dispatch the message to '" + destination + "' (topic '" + topic + "'): the device is not connected to the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + ".");
                }

                try
                {
                    connection.Publish(topic, payload, 0);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("Could not dispatch the message to '" + destination + "' (topic '" + topic + "') through the MQTT broker at " + connectOptions.Host + ":" + connectOptions.Port + ". (" + exception.Message + ")", exception);
                }
            }
        }
    }
}
