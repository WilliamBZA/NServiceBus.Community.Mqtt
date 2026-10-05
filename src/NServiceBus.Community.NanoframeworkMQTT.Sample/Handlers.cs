using System;
using Contracts;

namespace NServiceBus.Community.NanoframeworkMQTT.Sample
{
    /// <summary>Opens the valve: replies with its status, then publishes that it was opened.</summary>
    internal sealed class OpenValveHandler : IHandleMessages
    {
        public void Handle(object message, IMessageHandlerContext context)
        {
            var command = (OpenValve)message;

            var status = new ValveStatusResponse();
            status.ValveId = command.ValveId;
            status.IsOpen = true;
            status.Percent = command.Percent;
            context.Reply(status);

            var opened = new ValveOpened();
            opened.ValveId = command.ValveId;
            opened.Percent = command.Percent;
            context.Publish(opened);
        }
    }

    /// <summary>
    /// Handles every valve event, because the device is subscribed to the base type: the event a .NET endpoint publishes as <c>ValveOpened</c>
    /// reaches it as a <c>ValveEvent</c>. It answers with a command, so the sender can see that the event arrived.
    /// </summary>
    internal sealed class ValveEventHandler : IHandleMessages
    {
        public void Handle(object message, IMessageHandlerContext context)
        {
            var valveEvent = (ValveEvent)message;

            var acknowledgement = new ValveEventAcknowledged();
            acknowledgement.ValveId = valveEvent.ValveId;
            context.Send(acknowledgement);
        }
    }

    /// <summary>Always throws, so that the message is retried and then ends up in the error queue. The exception message counts the attempts.</summary>
    internal sealed class AlwaysFailsHandler : IHandleMessages
    {
        string lastMessageId;
        int attempt;

        public void Handle(object message, IMessageHandlerContext context)
        {
            // the handler is one instance that handles one message at a time, so the count restarts when another message begins
            if (context.MessageId != lastMessageId)
            {
                lastMessageId = context.MessageId;
                attempt = 0;
            }

            attempt++;
            throw new InvalidOperationException("The sample handler for AlwaysFails always throws (attempt " + attempt + ").");
        }
    }
}
