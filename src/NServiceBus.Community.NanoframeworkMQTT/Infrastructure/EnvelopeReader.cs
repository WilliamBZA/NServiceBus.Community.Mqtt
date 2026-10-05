using System;
using System.Collections;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// A reader for exactly the envelope <see cref="WireFormat" /> describes. It reads what System.Text.Json reads, and rejects what it rejects:
    /// properties in any order, whitespace, unknown properties of any JSON type, every JSON escape including surrogate pairs, base64 bodies.
    /// A payload that is not valid JSON, is JSON <c>null</c>, or lacks headers or a body is a <see cref="MessageDecodeException" />.
    /// </summary>
    internal sealed class EnvelopeReader : Utf8JsonScanner
    {
        public EnvelopeReader(byte[] data) : base(data)
        {
        }

        protected override Exception CreateError(string reason)
        {
            return new MessageDecodeException("The payload is not a valid message: " + reason + ".");
        }

        public WireEnvelope Read()
        {
            SkipWhiteSpace();
            if (position >= data.Length)
            {
                throw Error("the payload is empty");
            }

            if (data[position] == 'n')
            {
                ExpectLiteral("null");
                throw Error("the payload is JSON null");
            }

            Expect('{');
            depth = 1;

            string id = null;
            Hashtable headers = null;
            byte[] body = null;
            var headersSeen = false;
            var bodySeen = false;

            SkipWhiteSpace();
            if (Peek() == '}')
            {
                position++;
            }
            else
            {
                while (true)
                {
                    SkipWhiteSpace();
                    var name = ReadStringBytes();
                    SkipWhiteSpace();
                    Expect(':');
                    SkipWhiteSpace();

                    if (Matches(name, "Id"))
                    {
                        id = ReadNullableString();
                    }
                    else if (Matches(name, "Headers"))
                    {
                        headers = ReadHeaders();
                        headersSeen = true;
                    }
                    else if (Matches(name, "Body"))
                    {
                        body = ReadBody();
                        bodySeen = true;
                    }
                    else
                    {
                        SkipValue();
                    }

                    SkipWhiteSpace();
                    var next = Next();
                    if (next == ',')
                    {
                        continue;
                    }

                    if (next == '}')
                    {
                        break;
                    }

                    throw Error("expected ',' or '}'");
                }
            }

            SkipWhiteSpace();
            if (position < data.Length)
            {
                throw Error("there is data after the end of the message");
            }

            if (!headersSeen || headers == null)
            {
                throw Error("its headers are missing or null");
            }

            if (!bodySeen || body == null)
            {
                throw Error("its body is missing or null");
            }

            return new WireEnvelope(id, headers, body);
        }

        string ReadNullableString()
        {
            if (Peek() == 'n')
            {
                ExpectLiteral("null");
                return null;
            }

            return ToString(ReadStringBytes());
        }

        Hashtable ReadHeaders()
        {
            if (Peek() == 'n')
            {
                ExpectLiteral("null");
                return null;
            }

            Expect('{');
            Enter();
            var headers = new Hashtable();

            SkipWhiteSpace();
            if (Peek() == '}')
            {
                position++;
                depth--;
                return headers;
            }

            while (true)
            {
                SkipWhiteSpace();
                var key = ToString(ReadStringBytes());
                SkipWhiteSpace();
                Expect(':');
                SkipWhiteSpace();

                var valueStart = Peek();
                if (valueStart == 'n')
                {
                    ExpectLiteral("null");
                    headers[key] = null;
                }
                else if (valueStart == '"')
                {
                    headers[key] = ToString(ReadStringBytes());
                }
                else
                {
                    throw Error("the value of header '" + key + "' is not a string");
                }

                SkipWhiteSpace();
                var next = Next();
                if (next == ',')
                {
                    continue;
                }

                if (next == '}')
                {
                    break;
                }

                throw Error("expected ',' or '}' in the headers");
            }

            depth--;
            return headers;
        }

        byte[] ReadBody()
        {
            if (Peek() == 'n')
            {
                ExpectLiteral("null");
                return null;
            }

            var text = ReadStringBytes();
            var body = Base64.Decode(text, 0, text.Length);
            if (body == null)
            {
                throw Error("its body is not valid base64");
            }

            return body;
        }

        static string ToString(byte[] utf8)
        {
            return Encoding.UTF8.GetString(utf8, 0, utf8.Length);
        }
    }
}
