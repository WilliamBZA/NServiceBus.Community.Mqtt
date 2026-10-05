using System;
using System.Collections;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// The message types a device has handlers for. A received message is resolved to the first type in its <c>NServiceBus.EnclosedMessageTypes</c>
    /// that is registered, and then handled by every registration the resolved type is assignable to, in registration order.
    /// </summary>
    internal sealed class HandlerRegistry
    {
        readonly ArrayList registrations = new ArrayList();
        readonly Hashtable typesByName = new Hashtable();
        readonly Hashtable handlersByType = new Hashtable();

        public HandlerRegistry(ArrayList registrations)
        {
            for (var i = 0; i < registrations.Count; i++)
            {
                var registration = (HandlerRegistration)registrations[i];
                this.registrations.Add(registration);
                typesByName[registration.MessageType.FullName] = registration.MessageType;
            }
        }

        /// <returns>The registered type of the first name that matches one, or <c>null</c>.</returns>
        public Type Resolve(string[] names)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (typesByName.Contains(names[i]))
                {
                    return (Type)typesByName[names[i]];
                }
            }

            return null;
        }

        /// <summary>The handlers to invoke for a resolved type, in registration order. The list is worked out once for every type.</summary>
        public IHandleMessages[] HandlersFor(Type resolved)
        {
            lock (handlersByType)
            {
                if (handlersByType.Contains(resolved))
                {
                    return (IHandleMessages[])handlersByType[resolved];
                }

                var handlers = new ArrayList();
                for (var i = 0; i < registrations.Count; i++)
                {
                    var registration = (HandlerRegistration)registrations[i];
                    if (TypeRelations.IsAssignableTo(resolved, registration.MessageType))
                    {
                        handlers.Add(registration.Handler);
                    }
                }

                var result = new IHandleMessages[handlers.Count];
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = (IHandleMessages)handlers[i];
                }

                handlersByType[resolved] = result;
                return result;
            }
        }
    }
}
