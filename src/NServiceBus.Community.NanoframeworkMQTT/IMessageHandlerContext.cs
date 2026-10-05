using System.Collections;

namespace NServiceBus
{
    /// <summary>
    /// What a handler uses while it handles a message. Messages sent, published or replied here are collected, and dispatched in order only after
    /// every handler has succeeded. If a handler throws, none of them is dispatched.
    /// </summary>
    public interface IMessageHandlerContext
    {
        /// <summary>The ID of the message being handled.</summary>
        string MessageId { get; }

        /// <summary>The address replies go to, from the incoming <c>NServiceBus.ReplyToAddress</c> header, or <c>null</c> if the message has none.</summary>
        string ReplyToAddress { get; }

        /// <summary>A copy of the incoming headers for this attempt. A change does not reach the next attempt.</summary>
        Hashtable MessageHeaders { get; }

        /// <summary>Sends a command to the endpoint its type is routed to.</summary>
        void Send(object message);

        /// <summary>Sends a command to the destination in the options, or else to the endpoint its type is routed to.</summary>
        void Send(object message, SendOptions options);

        /// <summary>Publishes an event, once for every type in its hierarchy.</summary>
        void Publish(object message);

        /// <summary>Publishes an event, once for every type in its hierarchy.</summary>
        void Publish(object message, PublishOptions options);

        /// <summary>Replies to the message being handled. It fails if the message has no reply-to address.</summary>
        void Reply(object message);

        /// <summary>Replies to the message being handled. It fails if the message has no reply-to address.</summary>
        void Reply(object message, ReplyOptions options);
    }
}
