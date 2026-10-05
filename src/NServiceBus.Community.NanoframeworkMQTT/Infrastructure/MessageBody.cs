using System;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// The JSON body of a message. It writes the UTF-8 JSON that NServiceBus's System.Text.Json serializer reads for the same type, and reads
    /// what that serializer writes, for the member types a device and a .NET endpoint exchange: string, bool, int, long, double, DateTime in UTC,
    /// enums, nested classes made of these, and arrays of them. It is built on reflection and on the hardened scanner the envelope reader uses,
    /// rather than on nanoFramework.Json, which does not read a null int array, reads a DateTime fraction wrongly, ignores an offset, does not escape
    /// control characters, and loses digits of some doubles.
    /// </summary>
    internal static class MessageBody
    {
        /// <exception cref="NotSupportedException">The message type has a member the device cannot exchange.</exception>
        public static byte[] Serialize(object message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            return JsonBodyWriter.Write(message);
        }

        /// <exception cref="MessageDeserializationException">The body is not valid JSON for the type.</exception>
        /// <exception cref="NotSupportedException">The message type has a member the device cannot exchange.</exception>
        public static object Deserialize(byte[] body, Type type)
        {
            if (body == null)
            {
                throw new ArgumentNullException("body");
            }

            return new JsonBodyReader(body, type).Read();
        }
    }
}
