namespace NServiceBus
{
    /// <summary>
    /// Handles messages of the type it is registered for, and of the types that derive from or implement it. It is non-generic, because nanoFramework
    /// generics are still a firmware preview, so the handler casts the message. One instance serves every message, and
    /// a device handles one message at a time, so an instance never runs concurrently.
    /// </summary>
    public interface IHandleMessages
    {
        /// <summary>Handles a message. An exception makes the endpoint handle the message again, and after the last retry sends it to the error queue.</summary>
        /// <param name="message">The deserialized message, of the type the handler is registered for or one that derives from it.</param>
        /// <param name="context">What the handler uses to send, publish and reply.</param>
        void Handle(object message, IMessageHandlerContext context);
    }
}
