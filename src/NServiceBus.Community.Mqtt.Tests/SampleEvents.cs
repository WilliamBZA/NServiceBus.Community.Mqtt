// Sample event types for the topic and hierarchy tests. They sit in their own namespaces so that the expected topics are easy to read.

namespace Sales
{
    public class OrderPlaced : NServiceBus.IEvent
    {
    }

    // Container.Inner is in src/Interop/MappingCases.cs, which both the .NET and the device tests compile.
    public partial class Container
    {
        public class Nested<T> : NServiceBus.IEvent
        {
        }
    }

    public class GenericContainer<T>
    {
        public class Nested<U> : NServiceBus.IEvent
        {
        }
    }

    public class Wrapper<T> : NServiceBus.IEvent
    {
    }

    public class Pair<TFirst, TSecond> : NServiceBus.IEvent
    {
    }
}

namespace Shipping
{
    public class OrderPlaced : NServiceBus.IEvent
    {
    }
}

namespace Hierarchy
{
    public interface IAuditable
    {
    }

    public interface IBaseEvent : NServiceBus.IEvent
    {
    }

    public class BaseEvent : IBaseEvent
    {
    }

    public class MiddleEvent : BaseEvent
    {
    }

    public class DerivedEvent : MiddleEvent, IAuditable, System.IDisposable
    {
        public void Dispose()
        {
        }
    }

    public class UnrelatedInterfacesEvent : NServiceBus.IEvent, IAuditable, IBaseEvent
    {
    }

    public class PlainMessage : NServiceBus.IMessage
    {
    }

    public class CommandMessage : NServiceBus.ICommand
    {
    }
}
