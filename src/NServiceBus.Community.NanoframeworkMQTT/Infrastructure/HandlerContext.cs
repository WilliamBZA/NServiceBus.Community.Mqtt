using System.Collections;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// The context of one attempt at handling a message. What a handler sends, publishes or replies is built at once, so a message that cannot be sent
    /// fails the handler, and is collected: the endpoint dispatches the collection in order after every handler has succeeded.
    /// </summary>
    internal sealed class HandlerContext : IMessageHandlerContext
    {
        readonly OutgoingMessageBuilder builder;
        readonly ArrayList collected = new ArrayList();

        public HandlerContext(OutgoingMessageBuilder builder, string messageId, Hashtable headers)
        {
            this.builder = builder;
            MessageId = messageId;
            MessageHeaders = headers;
        }

        public string MessageId { get; private set; }

        public Hashtable MessageHeaders { get; private set; }

        public string ReplyToAddress
        {
            get { return MessageHeaders[HeaderNames.ReplyToAddress] as string; }
        }

        /// <summary>The messages to dispatch, as <see cref="OutgoingMessage" />s in the order they were issued.</summary>
        public ArrayList Collected
        {
            get { return collected; }
        }

        public void Send(object message)
        {
            Send(message, null);
        }

        public void Send(object message, SendOptions options)
        {
            collected.Add(builder.BuildSend(message, options, this));
        }

        public void Publish(object message)
        {
            Publish(message, null);
        }

        public void Publish(object message, PublishOptions options)
        {
            collected.Add(builder.BuildPublish(message, options, this));
        }

        public void Reply(object message)
        {
            Reply(message, null);
        }

        public void Reply(object message, ReplyOptions options)
        {
            collected.Add(builder.BuildReply(message, options, this));
        }
    }
}
