using NServiceBus.Extensibility;
using NServiceBus.Unicast.Messages;

namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Subscribes a receiver to events. A subscription is a plain subscription, in the receiver's own persistent session, to the topic of
    /// the event type. The pump keeps the set, applies it when it connects and applies it again after a reconnect.
    /// </summary>
    sealed class MqttSubscriptionManager(MqttMessagePump messagePump) : ISubscriptionManager
    {
        public async Task SubscribeAll(MessageMetadata[] eventTypes, ContextBag context, CancellationToken cancellationToken = default)
        {
            foreach (var eventType in eventTypes)
            {
                await messagePump.SubscribeToEvent(eventType.MessageType, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task Unsubscribe(MessageMetadata eventType, ContextBag context, CancellationToken cancellationToken = default) =>
            messagePump.UnsubscribeFromEvent(eventType.MessageType, cancellationToken);
    }
}
