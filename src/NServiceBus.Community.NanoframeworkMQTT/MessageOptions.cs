using System;
using System.Collections;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus
{
    /// <summary>Options for sending a command.</summary>
    public class SendOptions
    {
        readonly Hashtable headers = new Hashtable();

        /// <summary>The endpoint to send to. Without it, the route configured for the message type is used.</summary>
        public string Destination { get; set; }

        /// <summary>Adds a header to the message. The headers the device sets itself cannot be set.</summary>
        public void SetHeader(string key, string value)
        {
            OutgoingHeaders.Set(headers, key, value);
        }

        internal Hashtable Headers
        {
            get { return headers; }
        }
    }

    /// <summary>Options for publishing an event.</summary>
    public class PublishOptions
    {
        readonly Hashtable headers = new Hashtable();

        /// <summary>Adds a header to the message. The headers the device sets itself cannot be set.</summary>
        public void SetHeader(string key, string value)
        {
            OutgoingHeaders.Set(headers, key, value);
        }

        internal Hashtable Headers
        {
            get { return headers; }
        }
    }

    /// <summary>Options for replying to the message being handled.</summary>
    public class ReplyOptions
    {
        readonly Hashtable headers = new Hashtable();

        /// <summary>Adds a header to the message. The headers the device sets itself cannot be set.</summary>
        public void SetHeader(string key, string value)
        {
            OutgoingHeaders.Set(headers, key, value);
        }

        internal Hashtable Headers
        {
            get { return headers; }
        }
    }
}

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    internal static class OutgoingHeaders
    {
        public static void Set(Hashtable headers, string key, string value)
        {
            if (key == null || key.Length == 0)
            {
                throw new ArgumentException("A header needs a name.", "key");
            }

            if (HeaderNames.IsReserved(key))
            {
                throw new ArgumentException("The header '" + key + "' is set by the endpoint and cannot be set on a message.", "key");
            }

            headers[key] = value;
        }
    }
}
