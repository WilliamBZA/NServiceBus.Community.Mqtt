using System;
using System.Collections;
using System.Threading;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus
{
    public sealed partial class DeviceEndpoint
    {
        // the messages that have arrived and are waiting for the processing thread
        readonly ArrayList intake = new ArrayList();
        readonly AutoResetEvent intakeSignal = new AutoResetEvent(false);
        DateTime drainDeadline;

        sealed class ReceivedMessage
        {
            public ReceivedMessage(string topic, byte[] payload)
            {
                Topic = topic;
                Payload = payload;
            }

            public string Topic { get; private set; }

            public byte[] Payload { get; private set; }
        }

        /// <summary>
        /// Runs on the connection's thread, which also raises the broker's acknowledgements that a handler's own sends wait for, so it only queues
        /// the message and wakes the processing thread. It never blocks.
        /// </summary>
        void OnMessageReceived(string topic, byte[] payload)
        {
            lock (intake)
            {
                if (displaced)
                {
                    return;
                }

                intake.Add(new ReceivedMessage(topic, payload));
            }

            intakeSignal.Set();
        }

        void SignalIntake()
        {
            intakeSignal.Set();
        }

        int IntakeCount()
        {
            lock (intake)
            {
                return intake.Count;
            }
        }

        int DropIntake()
        {
            lock (intake)
            {
                var count = intake.Count;
                intake.Clear();
                return count;
            }
        }

        /// <summary>The next message, or <c>null</c> when the endpoint is stopping and has nothing more to do: the intake is empty or the drain timeout has passed.</summary>
        ReceivedMessage TakeNext()
        {
            while (true)
            {
                lock (intake)
                {
                    if (stopping && (intake.Count == 0 || DateTime.UtcNow.Ticks >= drainDeadline.Ticks))
                    {
                        return null;
                    }

                    if (intake.Count > 0)
                    {
                        var next = (ReceivedMessage)intake[0];
                        intake.RemoveAt(0);
                        return next;
                    }
                }

                intakeSignal.WaitOne();
            }
        }

        void ProcessLoop()
        {
            while (true)
            {
                var next = TakeNext();
                if (next == null)
                {
                    return;
                }

                try
                {
                    Process(next);
                }
                catch (Exception exception)
                {
                    RaiseCriticalError("The processing loop of the endpoint '" + configuration.EndpointName + "' failed while it processed a message that arrived on topic '" + next.Topic + "'. The endpoint keeps running.", exception);
                }
            }
        }

        void Process(ReceivedMessage received)
        {
            WireEnvelope envelope;
            try
            {
                envelope = WireFormat.Decode(received.Payload);
            }
            catch (MessageDecodeException exception)
            {
                // not a message at all, so there is nothing to put in the error queue
                log.Error("Discarded a payload that arrived on topic '" + received.Topic + "' because it is not a message: " + exception.Message, exception);
                return;
            }

            var headers = envelope.Headers;
            var enclosedMessageTypes = headers[HeaderNames.EnclosedMessageTypes] as string;

            bool designated;
            lock (subscriptionLock)
            {
                designated = DesignatedCopy.IsDesignated(received.Topic, enclosedMessageTypes, subscribedTopics);
            }

            if (!designated)
            {
                log.Debug("Dropped a copy of event '" + MessageIdOf(envelope) + "' that arrived on topic '" + received.Topic + "'. Another copy is the one that is processed.");
                return;
            }

            // messages that can never succeed skip the retries
            var contentType = headers[HeaderNames.ContentType] as string;
            if (contentType != null && !IsJson(contentType))
            {
                ForwardToErrorQueue(envelope, new MessageDeserializationException("The message '" + MessageIdOf(envelope) + "' has the content type '" + contentType + "', and only JSON is supported."));
                return;
            }

            if (enclosedMessageTypes == null)
            {
                ForwardToErrorQueue(envelope, new MessageDeserializationException("The message '" + MessageIdOf(envelope) + "' has no " + HeaderNames.EnclosedMessageTypes + " header, so its type is unknown."));
                return;
            }

            var type = handlers.Resolve(EnclosedMessageTypes.Names(enclosedMessageTypes));
            if (type == null)
            {
                ForwardToErrorQueue(envelope, new MessageDeserializationException("None of the message types '" + enclosedMessageTypes + "' of the message '" + MessageIdOf(envelope) + "' has a handler registered on the endpoint '" + configuration.EndpointName + "'."));
                return;
            }

            var attempts = configuration.ImmediateRetries + 1;
            Exception failure = null;
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                // every attempt starts from the original headers and body
                var attemptHeaders = CopyHeaders(headers);

                object message;
                try
                {
                    message = MessageBody.Deserialize(envelope.Body, type);
                }
                catch (MessageDeserializationException exception)
                {
                    ForwardToErrorQueue(envelope, exception);
                    return;
                }
                catch (Exception exception)
                {
                    ForwardToErrorQueue(envelope, new MessageDeserializationException("The body of the message '" + MessageIdOf(envelope) + "' cannot be read as '" + type.FullName + "': " + exception.Message));
                    return;
                }

                try
                {
                    var context = new HandlerContext(builder, MessageIdOf(envelope), attemptHeaders);
                    var matching = handlers.HandlersFor(type);
                    for (var i = 0; i < matching.Length; i++)
                    {
                        matching[i].Handle(message, context);
                    }

                    DispatchCollected(context.Collected);
                    return;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    log.Warning("Attempt " + attempt + " of " + attempts + " at handling the message '" + MessageIdOf(envelope) + "' of type '" + type.FullName + "' failed.", exception);
                }
            }

            ForwardToErrorQueue(envelope, failure);
        }

        void DispatchCollected(ArrayList collected)
        {
            for (var i = 0; i < collected.Count; i++)
            {
                Dispatch((OutgoingMessage)collected[i]);
            }
        }

        /// <summary>
        /// Sends the failed message to the error queue with the failure headers. If that fails, the device raises a critical error, waits with a back-off,
        /// and tries again, until it works or the endpoint stops. It processes nothing else in the meantime, so that the order is kept.
        /// </summary>
        void ForwardToErrorQueue(WireEnvelope envelope, Exception exception)
        {
            var messageId = MessageIdOf(envelope);

            var headers = CopyHeaders(envelope.Headers);
            headers[HeaderNames.FailedQ] = configuration.EndpointName;
            headers[HeaderNames.TimeOfFailure] = WireTimeText.FormatTime(services.Clock.UtcNow);
            headers[HeaderNames.ExceptionType] = exception.GetType().FullName;
            headers[HeaderNames.ExceptionMessage] = exception.Message == null ? "" : exception.Message;
            headers[HeaderNames.ExceptionStackTrace] = exception.StackTrace == null ? "" : exception.StackTrace;
            if (exception.InnerException != null)
            {
                headers[HeaderNames.InnerExceptionType] = exception.InnerException.GetType().FullName;
            }

            headers[HeaderNames.ProcessingEndpoint] = configuration.EndpointName;

            var payload = WireFormat.Encode(envelope.Id, headers, envelope.Body);
            var errorQueue = configuration.ErrorQueue;
            var topic = MqttAddress.ToTopic(errorQueue);

            var backOff = InitialBackOffMilliseconds;
            while (true)
            {
                try
                {
                    PublishToBroker(errorQueue, topic, payload);
                    log.Warning("The message '" + messageId + "' failed processing and was moved to the error queue '" + errorQueue + "'.", exception);
                    return;
                }
                catch (Exception forwardException)
                {
                    RaiseCriticalError(
                        "The message '" + messageId + "' failed processing, and it could not be forwarded to the error queue '" + errorQueue + "'. The device tries again in " + (backOff / 1000) + " s, and processes no other message until it is forwarded.",
                        forwardException);

                    if (services.Delay.Wait(TimeSpan.FromMilliseconds(backOff), stopSignal) || stopping)
                    {
                        log.Error("The endpoint '" + configuration.EndpointName + "' stopped before the message '" + messageId + "' could be forwarded to the error queue '" + errorQueue + "'. The message is lost.", null);
                        return;
                    }

                    backOff = Min(backOff * 2, MaximumBackOffMilliseconds);
                }
            }
        }

        static string MessageIdOf(WireEnvelope envelope)
        {
            var header = envelope.Headers[HeaderNames.MessageId] as string;
            return header != null ? header : envelope.Id;
        }

        static bool IsJson(string contentType)
        {
            // application/json, with or without parameters such as a charset
            var lower = contentType.Trim().ToLower();
            return lower == HeaderNames.JsonContentType || lower.StartsWith(HeaderNames.JsonContentType + ";");
        }

        static Hashtable CopyHeaders(Hashtable headers)
        {
            var copy = new Hashtable();
            foreach (DictionaryEntry entry in headers)
            {
                copy[entry.Key] = entry.Value;
            }

            return copy;
        }
    }
}
