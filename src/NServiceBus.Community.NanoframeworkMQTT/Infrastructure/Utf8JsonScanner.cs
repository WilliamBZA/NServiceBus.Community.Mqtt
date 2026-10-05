using System;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// The low-level reading of UTF-8 JSON that the envelope reader and the message body reader share. It works on bytes, because a nanoFramework
    /// string cannot be built char by char without losing characters outside the basic plane, and it reads what System.Text.Json reads and rejects
    /// what it rejects: well-formed UTF-8 only, every JSON escape including surrogate pairs, no unescaped control characters, no trailing commas,
    /// and a nesting limit. It never recurses, because the device's stack is small and the nesting comes from the sender.
    /// </summary>
    internal abstract class Utf8JsonScanner
    {
        protected const int MaxDepth = 64; // System.Text.Json's default limit

        protected readonly byte[] data;
        protected int position;
        protected int depth;
        byte[] skipStack;

        protected Utf8JsonScanner(byte[] data)
        {
            this.data = data;
        }

        /// <summary>Creates the exception for malformed input. The message says what is wrong; the scanner adds nothing to it.</summary>
        protected abstract Exception CreateError(string reason);

        protected Exception Error(string reason)
        {
            return CreateError(reason + " (at byte " + position + ")");
        }

        // ---- strings

        /// <summary>Reads a JSON string and returns its content as UTF-8 bytes, with every escape resolved.</summary>
        protected byte[] ReadStringBytes()
        {
            if (position >= data.Length || data[position] != '"')
            {
                throw Error("expected a string");
            }

            position++;
            var buffer = new ByteBuffer(32);
            while (true)
            {
                if (position >= data.Length)
                {
                    throw Error("a string is not terminated");
                }

                var current = data[position++];
                if (current == '"')
                {
                    return buffer.ToArray();
                }

                if (current < 0x20)
                {
                    throw Error("a string holds an unescaped control character");
                }

                if (current == '\\')
                {
                    ReadEscape(buffer);
                }
                else if (current < 0x80)
                {
                    buffer.Append(current);
                }
                else
                {
                    position--;
                    CopyUtf8Sequence(buffer);
                }
            }
        }

        void ReadEscape(ByteBuffer buffer)
        {
            if (position >= data.Length)
            {
                throw Error("a string is not terminated");
            }

            var escape = data[position++];
            switch ((char)escape)
            {
                case '"':
                case '\\':
                case '/':
                    buffer.Append(escape);
                    return;
                case 'b':
                    buffer.Append(0x08);
                    return;
                case 'f':
                    buffer.Append(0x0C);
                    return;
                case 'n':
                    buffer.Append(0x0A);
                    return;
                case 'r':
                    buffer.Append(0x0D);
                    return;
                case 't':
                    buffer.Append(0x09);
                    return;
                case 'u':
                    ReadUnicodeEscape(buffer);
                    return;
                default:
                    throw Error("a string holds the invalid escape '\\" + (char)escape + "'");
            }
        }

        void ReadUnicodeEscape(ByteBuffer buffer)
        {
            var unit = ReadHex4();

            if (unit >= 0xD800 && unit <= 0xDBFF)
            {
                // a high surrogate must be followed by the escape of a low one
                if (position + 1 >= data.Length || data[position] != '\\' || data[position + 1] != 'u')
                {
                    throw Error("a string holds a high surrogate without a low surrogate");
                }

                position += 2;
                var low = ReadHex4();
                if (low < 0xDC00 || low > 0xDFFF)
                {
                    throw Error("a string holds a high surrogate without a low surrogate");
                }

                AppendCodePoint(buffer, 0x10000 + ((unit - 0xD800) << 10) + (low - 0xDC00));
                return;
            }

            if (unit >= 0xDC00 && unit <= 0xDFFF)
            {
                throw Error("a string holds a low surrogate without a high surrogate");
            }

            AppendCodePoint(buffer, unit);
        }

        int ReadHex4()
        {
            if (position + 4 > data.Length)
            {
                throw Error("a string is not terminated");
            }

            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                var digit = data[position++];
                int part;
                if (digit >= '0' && digit <= '9') part = digit - '0';
                else if (digit >= 'a' && digit <= 'f') part = digit - 'a' + 10;
                else if (digit >= 'A' && digit <= 'F') part = digit - 'A' + 10;
                else throw Error("a string holds an invalid \\u escape");

                value = (value << 4) | part;
            }

            return value;
        }

        static void AppendCodePoint(ByteBuffer buffer, int codePoint)
        {
            if (codePoint < 0x80)
            {
                buffer.Append((byte)codePoint);
            }
            else if (codePoint < 0x800)
            {
                buffer.Append((byte)(0xC0 | (codePoint >> 6)));
                buffer.Append((byte)(0x80 | (codePoint & 0x3F)));
            }
            else if (codePoint < 0x10000)
            {
                buffer.Append((byte)(0xE0 | (codePoint >> 12)));
                buffer.Append((byte)(0x80 | ((codePoint >> 6) & 0x3F)));
                buffer.Append((byte)(0x80 | (codePoint & 0x3F)));
            }
            else
            {
                buffer.Append((byte)(0xF0 | (codePoint >> 18)));
                buffer.Append((byte)(0x80 | ((codePoint >> 12) & 0x3F)));
                buffer.Append((byte)(0x80 | ((codePoint >> 6) & 0x3F)));
                buffer.Append((byte)(0x80 | (codePoint & 0x3F)));
            }
        }

        // copies one multi-byte UTF-8 sequence, and rejects what is not well-formed UTF-8: bad lead or continuation bytes, overlong forms,
        // surrogates and code points above U+10FFFF
        void CopyUtf8Sequence(ByteBuffer buffer)
        {
            var lead = data[position];
            int length;
            byte secondMin = 0x80;
            byte secondMax = 0xBF;

            if (lead >= 0xC2 && lead <= 0xDF)
            {
                length = 2;
            }
            else if (lead >= 0xE0 && lead <= 0xEF)
            {
                length = 3;
                if (lead == 0xE0) secondMin = 0xA0;
                if (lead == 0xED) secondMax = 0x9F;
            }
            else if (lead >= 0xF0 && lead <= 0xF4)
            {
                length = 4;
                if (lead == 0xF0) secondMin = 0x90;
                if (lead == 0xF4) secondMax = 0x8F;
            }
            else
            {
                throw Error("a string is not valid UTF-8");
            }

            if (position + length > data.Length)
            {
                throw Error("a string is not valid UTF-8");
            }

            var second = data[position + 1];
            if (second < secondMin || second > secondMax)
            {
                throw Error("a string is not valid UTF-8");
            }

            for (var i = 2; i < length; i++)
            {
                if ((data[position + i] & 0xC0) != 0x80)
                {
                    throw Error("a string is not valid UTF-8");
                }
            }

            buffer.Append(data, position, length);
            position += length;
        }

        // ---- numbers

        /// <summary>Validates a JSON number at the current position and moves past it. Returns whether it is written as an integer (no fraction, no exponent).</summary>
        protected bool ScanNumber()
        {
            var integer = true;
            if (Peek() == '-')
            {
                position++;
            }

            if (Peek() == '0')
            {
                position++;
            }
            else if (Peek() >= '1' && Peek() <= '9')
            {
                SkipDigits();
            }
            else
            {
                throw Error("a value is not valid JSON");
            }

            if (Peek() == '.')
            {
                integer = false;
                position++;
                if (!SkipDigits())
                {
                    throw Error("a number is not valid JSON");
                }
            }

            if (Peek() == 'e' || Peek() == 'E')
            {
                integer = false;
                position++;
                if (Peek() == '+' || Peek() == '-')
                {
                    position++;
                }

                if (!SkipDigits())
                {
                    throw Error("a number is not valid JSON");
                }
            }

            return integer;
        }

        bool SkipDigits()
        {
            var start = position;
            while (position < data.Length && data[position] >= '0' && data[position] <= '9')
            {
                position++;
            }

            return position > start;
        }

        // ---- values that are skipped

        /// <summary>Skips one JSON value, validating it, without recursion.</summary>
        protected void SkipValue()
        {
            if (skipStack == null)
            {
                skipStack = new byte[MaxDepth];
            }

            var open = 0;
            while (true)
            {
                SkipWhiteSpace();
                var start = Peek();
                var complete = true;

                if (start == '{' || start == '[')
                {
                    position++;
                    SkipWhiteSpace();
                    var closing = start == '{' ? '}' : ']';
                    if (Peek() == closing)
                    {
                        position++;
                    }
                    else
                    {
                        if (depth + open + 1 > MaxDepth)
                        {
                            throw Error("the nesting is too deep");
                        }

                        skipStack[open++] = (byte)start;
                        if (start == '{')
                        {
                            SkipMemberName();
                        }

                        complete = false;
                    }
                }
                else if (start == '"')
                {
                    ReadStringBytes();
                }
                else if (start == 't')
                {
                    ExpectLiteral("true");
                }
                else if (start == 'f')
                {
                    ExpectLiteral("false");
                }
                else if (start == 'n')
                {
                    ExpectLiteral("null");
                }
                else
                {
                    ScanNumber();
                }

                if (!complete)
                {
                    continue;
                }

                // a value is complete: continue with its parent, closing containers as long as they end
                while (true)
                {
                    if (open == 0)
                    {
                        return;
                    }

                    SkipWhiteSpace();
                    var next = Next();
                    var container = skipStack[open - 1];
                    if (next == ',')
                    {
                        if (container == '{')
                        {
                            SkipMemberName();
                        }

                        break;
                    }

                    if ((next == '}' && container == '{') || (next == ']' && container == '['))
                    {
                        open--;
                        continue;
                    }

                    throw Error("a value is not followed by ',' or the end of its container");
                }
            }
        }

        void SkipMemberName()
        {
            SkipWhiteSpace();
            ReadStringBytes();
            SkipWhiteSpace();
            Expect(':');
        }

        // ---- characters

        protected void Enter()
        {
            depth++;
            if (depth > MaxDepth)
            {
                throw Error("the nesting is too deep");
            }
        }

        protected void SkipWhiteSpace()
        {
            while (position < data.Length)
            {
                var current = data[position];
                if (current == ' ' || current == '\t' || current == '\r' || current == '\n')
                {
                    position++;
                }
                else
                {
                    return;
                }
            }
        }

        // the next byte, or 0 at the end of the data, which no valid JSON token starts with
        protected int Peek()
        {
            return position < data.Length ? data[position] : 0;
        }

        protected int Next()
        {
            if (position >= data.Length)
            {
                throw Error("the payload ends too soon");
            }

            return data[position++];
        }

        protected void Expect(char expected)
        {
            if (Peek() != expected)
            {
                throw Error("expected '" + expected + "'");
            }

            position++;
        }

        protected void ExpectLiteral(string literal)
        {
            for (var i = 0; i < literal.Length; i++)
            {
                if (position + i >= data.Length || data[position + i] != literal[i])
                {
                    throw Error("a value is not valid JSON");
                }
            }

            position += literal.Length;
        }

        protected static bool Matches(byte[] name, string expected)
        {
            if (name.Length != expected.Length)
            {
                return false;
            }

            for (var i = 0; i < name.Length; i++)
            {
                if (name[i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
