using System;
using System.Collections;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>A decoded envelope: the message ID, the headers (string to string) and the body.</summary>
    internal sealed class WireEnvelope
    {
        public WireEnvelope(string id, Hashtable headers, byte[] body)
        {
            Id = id;
            Headers = headers;
            Body = body;
        }

        /// <summary>The <c>Id</c> of the envelope. It can be <c>null</c>; the message ID header is what identifies the message.</summary>
        public string Id { get; private set; }

        public Hashtable Headers { get; private set; }

        public byte[] Body { get; private set; }
    }

    /// <summary>A payload could not be decoded into a message. The receiver logs it and discards it.</summary>
    internal sealed class MessageDecodeException : Exception
    {
        public MessageDecodeException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// The payload format of <c>NServiceBus.Community.Mqtt</c>: UTF-8 JSON, <c>{"Id":string|null,"Headers":{string:string},"Body":base64}</c>.
    /// </summary>
    internal static class WireFormat
    {
        /// <exception cref="MessageDecodeException">The payload is not a valid message.</exception>
        public static WireEnvelope Decode(byte[] payload)
        {
            if (payload == null)
            {
                throw new MessageDecodeException("The payload is empty.");
            }

            return new EnvelopeReader(payload).Read();
        }

        /// <summary>Encodes a message. <paramref name="id" /> can be <c>null</c>; <paramref name="headers" /> maps strings to strings.</summary>
        public static byte[] Encode(string id, Hashtable headers, byte[] body)
        {
            if (headers == null)
            {
                throw new ArgumentNullException("headers");
            }

            if (body == null)
            {
                throw new ArgumentNullException("body");
            }

            return EnvelopeWriter.Write(id, headers, body);
        }
    }
}
