// Event types for the publish/subscribe tests. They live in their own namespaces so that same-named types can be told apart.

namespace NServiceBus.Community.Mqtt.TransportTests.Events
{
    public class BaseEvent : IEvent
    {
    }

    public class DerivedEvent : BaseEvent
    {
    }

    public interface IFoo : IEvent
    {
    }

    public interface IBar : IEvent
    {
    }

    public class FooBarEvent : IFoo, IBar
    {
    }

    public class UnrelatedEvent : IEvent
    {
    }

    public class Container
    {
        public class NestedEvent : IEvent
        {
        }
    }
}

namespace NServiceBus.Community.Mqtt.TransportTests.Events.Sales
{
    public class OrderPlaced : IEvent
    {
    }
}

namespace NServiceBus.Community.Mqtt.TransportTests.Events.Shipping
{
    public class OrderPlaced : IEvent
    {
    }
}
