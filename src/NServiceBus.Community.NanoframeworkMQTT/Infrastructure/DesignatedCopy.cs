using System;
using System.Collections;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// An event is published once for every type in its hierarchy, so a device subscribed to several of them receives several copies. Exactly one
    /// is processed: the copy for the first type in the message's own type list that the device is subscribed to. This is the rule
    /// <c>NServiceBus.Community.Mqtt</c> applies, and it needs only the topic, the subscriptions and the header.
    /// </summary>
    internal static class DesignatedCopy
    {
        /// <param name="topic">The topic the copy arrived on.</param>
        /// <param name="enclosedMessageTypes">The <c>NServiceBus.EnclosedMessageTypes</c> header, or <c>null</c> when the message has none.</param>
        /// <param name="subscribedTopics">The device's event subscriptions: the type's full name (string) to its event topic (string).</param>
        /// <returns><c>false</c> only for a copy that must be acknowledged and dropped.</returns>
        public static bool IsDesignated(string topic, string enclosedMessageTypes, Hashtable subscribedTopics)
        {
            // a message without the header, or on a topic that is not an event topic, is processed as it is
            if (enclosedMessageTypes == null || !topic.StartsWith(EventTopic.Prefix))
            {
                return true;
            }

            var names = EnclosedMessageTypes.Names(enclosedMessageTypes);
            for (var i = 0; i < names.Length; i++)
            {
                if (subscribedTopics.Contains(names[i]))
                {
                    return (string)subscribedTopics[names[i]] == topic;
                }
            }

            return true;
        }
    }
}
