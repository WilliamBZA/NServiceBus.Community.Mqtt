namespace NServiceBus
{
    /// <summary>
    /// Marks a class as a message. The interface lives in the <c>NServiceBus</c> namespace so that a contract file with <c>using NServiceBus;</c> compiles unchanged for .NET and for nanoFramework.
    /// </summary>
    public interface IMessage
    {
    }

    /// <summary>
    /// Marks a class as a command.
    /// </summary>
    public interface ICommand : IMessage
    {
    }

    /// <summary>
    /// Marks a class as an event.
    /// </summary>
    public interface IEvent : IMessage
    {
    }
}
