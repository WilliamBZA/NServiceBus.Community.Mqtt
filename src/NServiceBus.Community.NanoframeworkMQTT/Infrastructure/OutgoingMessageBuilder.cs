using System;
using System.Collections;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>One publish of an outgoing message: an event has one for every type in its hierarchy.</summary>
    internal sealed class OutgoingCopy
    {
        public OutgoingCopy(string topic, string destination, byte[] payload)
        {
            Topic = topic;
            Destination = destination;
            Payload = payload;
        }

        public string Topic { get; private set; }

        /// <summary>What the copy is for in an error message: the address, or the event type.</summary>
        public string Destination { get; private set; }

        public byte[] Payload { get; private set; }
    }

    internal sealed class OutgoingMessage
    {
        public OutgoingMessage(string messageId, OutgoingCopy[] copies)
        {
            MessageId = messageId;
            Copies = copies;
        }

        public string MessageId { get; private set; }

        public OutgoingCopy[] Copies { get; private set; }
    }

    /// <summary>
    /// Turns a message the application hands over into the payloads to publish: it works out the destination or the event topics, sets the standard
    /// headers, serializes the body and checks the size of every packet. Nothing is published here, so a message that cannot be sent fails before anything is.
    /// </summary>
    internal sealed class OutgoingMessageBuilder
    {
        readonly string endpointName;
        readonly long maximumPacketSize;
        readonly Hashtable routes;
        readonly EndpointServices services;

        public OutgoingMessageBuilder(string endpointName, int maximumPacketSize, Hashtable routes, EndpointServices services)
        {
            this.endpointName = endpointName;
            this.maximumPacketSize = maximumPacketSize;
            this.routes = routes;
            this.services = services;
        }

        /// <param name="message">The message to send.</param>
        /// <param name="options">The options of the send, or <c>null</c>.</param>
        /// <param name="incoming">The message being handled, or <c>null</c> outside a handler.</param>
        public OutgoingMessage BuildSend(object message, SendOptions options, HandlerContext incoming)
        {
            var type = CheckMessage(message);
            if (TypeRelations.IsEvent(type))
            {
                throw new InvalidOperationException("The message type '" + type.FullName + "' is an event, and events must be published. Use Publish instead of Send.");
            }

            var destination = options == null ? null : options.Destination;
            if (destination == null)
            {
                destination = RouteFor(type);
                if (destination == null)
                {
                    throw new InvalidOperationException("There is no route for the message type '" + type.FullName + "'. Give the send a destination, or route the type with RouteToEndpoint.");
                }
            }

            return BuildUnicast(message, type, destination, HeaderNames.IntentSend, options == null ? null : options.Headers, incoming);
        }

        public OutgoingMessage BuildReply(object message, ReplyOptions options, HandlerContext incoming)
        {
            var type = CheckMessage(message);
            if (TypeRelations.IsEvent(type))
            {
                throw new InvalidOperationException("The message type '" + type.FullName + "' is an event, and events must be published. Use Publish instead of Reply.");
            }

            var replyTo = incoming.ReplyToAddress;
            if (replyTo == null || replyTo.Length == 0)
            {
                throw new InvalidOperationException("The message '" + incoming.MessageId + "' has no " + HeaderNames.ReplyToAddress + " header, so there is nowhere to send a reply to.");
            }

            return BuildUnicast(message, type, replyTo, HeaderNames.IntentReply, options == null ? null : options.Headers, incoming);
        }

        public OutgoingMessage BuildPublish(object message, PublishOptions options, HandlerContext incoming)
        {
            var type = CheckMessage(message);
            if (!TypeRelations.IsEvent(type))
            {
                throw new InvalidOperationException("The message type '" + type.FullName + "' is not an event, and only events can be published. Make it implement NServiceBus.IEvent, or use Send.");
            }

            // an event is published once for every type in its hierarchy, so a subscriber to a base type or an interface receives it
            var hierarchy = EventTypeHierarchy.Enumerate(type);
            var headers = StandardHeaders(type, hierarchy, HeaderNames.IntentPublish, options == null ? null : options.Headers, incoming);
            var payload = Encode(message, headers);

            var copies = new OutgoingCopy[hierarchy.Length];
            for (var i = 0; i < copies.Length; i++)
            {
                copies[i] = new OutgoingCopy(EventTopic.ToTopic(hierarchy[i]), hierarchy[i].FullName, payload);
            }

            CheckSizes(copies);
            return new OutgoingMessage((string)headers[HeaderNames.MessageId], copies);
        }

        OutgoingMessage BuildUnicast(object message, Type type, string destination, string intent, Hashtable customHeaders, HandlerContext incoming)
        {
            // a destination that cannot be used fails here, before anything is published
            var topic = MqttAddress.ToTopic(destination);

            var headers = StandardHeaders(type, EventTypeHierarchy.Enumerate(type), intent, customHeaders, incoming);
            if (intent == HeaderNames.IntentReply)
            {
                // so that the .NET saga that sent the request is the one that handles the reply
                var sagaId = incoming.MessageHeaders[HeaderNames.OriginatingSagaId] as string;
                var sagaType = incoming.MessageHeaders[HeaderNames.OriginatingSagaType] as string;
                if (sagaId != null && sagaType != null)
                {
                    headers[HeaderNames.SagaId] = sagaId;
                    headers[HeaderNames.SagaType] = sagaType;
                }
            }

            var copies = new OutgoingCopy[1];
            copies[0] = new OutgoingCopy(topic, destination, Encode(message, headers));

            CheckSizes(copies);
            return new OutgoingMessage((string)headers[HeaderNames.MessageId], copies);
        }

        Hashtable StandardHeaders(Type type, Type[] hierarchy, string intent, Hashtable customHeaders, HandlerContext incoming)
        {
            var headers = new Hashtable();

            if (customHeaders != null)
            {
                foreach (DictionaryEntry entry in customHeaders)
                {
                    headers[entry.Key] = entry.Value;
                }
            }

            var messageId = services.Ids.NewId();
            headers[HeaderNames.MessageId] = messageId;
            headers[HeaderNames.MessageIntent] = intent;
            headers[HeaderNames.EnclosedMessageTypes] = EnclosedNames(hierarchy);
            headers[HeaderNames.ContentType] = HeaderNames.JsonContentType;
            headers[HeaderNames.ReplyToAddress] = endpointName;
            headers[HeaderNames.OriginatingEndpoint] = endpointName;
            headers[HeaderNames.TimeSent] = WireTimeText.FormatTime(services.Clock.UtcNow);

            if (incoming == null)
            {
                headers[HeaderNames.ConversationId] = services.Ids.NewId();
                headers[HeaderNames.CorrelationId] = messageId;
            }
            else
            {
                var conversationId = incoming.MessageHeaders[HeaderNames.ConversationId] as string;
                headers[HeaderNames.ConversationId] = conversationId != null ? conversationId : services.Ids.NewId();

                var correlationId = incoming.MessageHeaders[HeaderNames.CorrelationId] as string;
                headers[HeaderNames.CorrelationId] = correlationId != null ? correlationId : incoming.MessageId;
                headers[HeaderNames.RelatedTo] = incoming.MessageId;
            }

            return headers;
        }

        byte[] Encode(object message, Hashtable headers)
        {
            return WireFormat.Encode((string)headers[HeaderNames.MessageId], headers, MessageBody.Serialize(message));
        }

        void CheckSizes(OutgoingCopy[] copies)
        {
            for (var i = 0; i < copies.Length; i++)
            {
                var size = MqttPacket.PublishSize(copies[i].Topic, copies[i].Payload.Length, 0);
                if (size > maximumPacketSize)
                {
                    throw new InvalidOperationException(
                        "The message for '" + copies[i].Destination + "' (topic '" + copies[i].Topic + "') would be an MQTT packet of " + size
                        + " bytes, which is larger than the maximum packet size of " + maximumPacketSize + " bytes. Nothing was published.");
                }
            }
        }

        static string EnclosedNames(Type[] hierarchy)
        {
            var names = new StringBuilder();
            for (var i = 0; i < hierarchy.Length; i++)
            {
                if (i > 0)
                {
                    names.Append(';');
                }

                names.Append(hierarchy[i].FullName);
            }

            return names.ToString();
        }

        string RouteFor(Type type)
        {
            // the type itself first, then its base classes and interfaces
            var hierarchy = EventTypeHierarchy.Enumerate(type);
            for (var i = 0; i < hierarchy.Length; i++)
            {
                if (routes.Contains(hierarchy[i].FullName))
                {
                    return (string)routes[hierarchy[i].FullName];
                }
            }

            return null;
        }

        static Type CheckMessage(object message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            return message.GetType();
        }
    }
}
