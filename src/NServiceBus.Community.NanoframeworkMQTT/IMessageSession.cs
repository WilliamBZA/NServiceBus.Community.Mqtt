using System;

namespace NServiceBus
{
    /// <summary>
    /// What the application uses to send and publish from outside a handler, and to subscribe. Every operation returns after the broker has
    /// acknowledged what it asked for, and fails with an error otherwise.
    /// </summary>
    public interface IMessageSession
    {
        /// <summary>Sends a command to the endpoint its type is routed to.</summary>
        void Send(object message);

        /// <summary>Sends a command to the destination in the options, or else to the endpoint its type is routed to.</summary>
        void Send(object message, SendOptions options);

        /// <summary>Publishes an event, once for every type in its hierarchy.</summary>
        void Publish(object message);

        /// <summary>Publishes an event, once for every type in its hierarchy.</summary>
        void Publish(object message, PublishOptions options);

        /// <summary>Subscribes to an event type.</summary>
        void Subscribe(Type eventType);

        /// <summary>Unsubscribes from an event type. The other subscriptions are kept.</summary>
        void Unsubscribe(Type eventType);
    }
}
