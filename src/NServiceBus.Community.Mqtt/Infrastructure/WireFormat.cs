using System.Text.Json;

namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Encodes outgoing messages into MQTT payloads and decodes incoming payloads. The payload is the JSON form of <see cref="MessageWrapper"/>
    /// with the default serializer options. That format is unchanged from 1.x, so a 2.x endpoint can still read a 1.x unicast message.
    /// </summary>
    static class WireFormat
    {
        /// <summary>
        /// Encodes a message. If its headers have no <c>NServiceBus.MessageId</c>, the outgoing message's ID is written into the payload's headers.
        /// A header that is already present is kept, and the caller's header dictionary is never changed.
        /// </summary>
        public static byte[] Encode(OutgoingMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            var headers = message.Headers;
            if (!headers.TryGetValue(Headers.MessageId, out var messageId) || string.IsNullOrEmpty(messageId))
            {
                headers = new Dictionary<string, string>(headers) { [Headers.MessageId] = message.MessageId };
            }

            return JsonSerializer.SerializeToUtf8Bytes(new MessageWrapper { Id = message.MessageId, Headers = headers, Body = message.Body.ToArray() });
        }

        /// <summary>
        /// Decodes a payload into a message. Every call returns new header and body instances, so a handler that changes them
        /// cannot affect a later decode of the same payload. If the payload has no <c>NServiceBus.MessageId</c> header, one is assigned.
        /// </summary>
        /// <exception cref="MessageDecodeException">The payload is not a valid message.</exception>
        public static DecodedMessage Decode(ReadOnlySpan<byte> payload)
        {
            MessageWrapper? wrapper;
            try
            {
                wrapper = JsonSerializer.Deserialize<MessageWrapper>(payload);
            }
            catch (JsonException ex)
            {
                throw new MessageDecodeException("The payload is not a valid message.", ex);
            }

            if (wrapper is null)
            {
                throw new MessageDecodeException("The payload is not a message: it is JSON null.");
            }

            // 'required' only checks that a member is present, so an explicit null still gets through
            if (wrapper.Headers is null || wrapper.Body is null)
            {
                throw new MessageDecodeException("The payload is not a valid message: its headers or body is null.");
            }

            if (!wrapper.Headers.TryGetValue(Headers.MessageId, out var nativeMessageId) || string.IsNullOrEmpty(nativeMessageId))
            {
                nativeMessageId = Guid.NewGuid().ToString();
                wrapper.Headers[Headers.MessageId] = nativeMessageId;
            }

            return new DecodedMessage(nativeMessageId, wrapper.Headers, wrapper.Body);
        }
    }

    readonly record struct DecodedMessage(string NativeMessageId, Dictionary<string, string> Headers, byte[] Body)
    {
        /// <summary>A copy with its own headers and body, so a handler that changes them cannot affect the next attempt at the same message.</summary>
        public DecodedMessage Copy() => new(NativeMessageId, new Dictionary<string, string>(Headers), (byte[])Body.Clone());
    }

    /// <summary>A payload could not be decoded into a message. The receiver treats it as a poison message.</summary>
    sealed class MessageDecodeException : Exception
    {
        public MessageDecodeException(string message) : base(message)
        {
        }

        public MessageDecodeException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
