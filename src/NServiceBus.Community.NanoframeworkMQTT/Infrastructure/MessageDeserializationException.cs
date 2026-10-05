using System;

// The namespace and the name are the ones NServiceBus uses for a body that cannot be deserialized, and the full name is what is written as the
// exception type of a failed message, so that error tooling groups these failures the same way.
namespace NServiceBus
{
    internal sealed class MessageDeserializationException : Exception
    {
        public MessageDeserializationException(string message) : base(message)
        {
        }
    }
}
