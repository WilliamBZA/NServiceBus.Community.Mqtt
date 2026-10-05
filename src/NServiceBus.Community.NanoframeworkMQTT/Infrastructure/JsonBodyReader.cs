using System;
using System.Collections;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Reads a message body into a message the way System.Text.Json does with its default options: member names match case-sensitively, unknown
    /// members are skipped, a number for an <c>int</c> or <c>long</c> has to be an integer in range, an enum is a number, <c>null</c> is accepted for
    /// reference types only, and anything malformed is an error. Anything that is wrong throws <see cref="MessageDeserializationException" />.
    /// </summary>
    internal sealed class JsonBodyReader : Utf8JsonScanner
    {
        const int MaxNesting = 32;

        readonly Type rootType;

        public JsonBodyReader(byte[] body, Type rootType) : base(body)
        {
            this.rootType = rootType;
        }

        protected override Exception CreateError(string reason)
        {
            return new MessageDeserializationException("The message body is not valid for '" + rootType.FullName + "': " + reason + ".");
        }

        public object Read()
        {
            var model = TypeModels.For(rootType);

            SkipWhiteSpace();
            if (position >= data.Length)
            {
                throw Error("the body is empty");
            }

            if (Peek() != '{')
            {
                throw Error("the body is not a JSON object");
            }

            var message = ReadObject(model);

            SkipWhiteSpace();
            if (position < data.Length)
            {
                throw Error("there is data after the end of the body");
            }

            return message;
        }

        object ReadObject(TypeModel model)
        {
            Expect('{');
            EnterNesting();
            var instance = model.Constructor.Invoke(new object[0]);

            SkipWhiteSpace();
            if (Peek() == '}')
            {
                position++;
                depth--;
                return instance;
            }

            while (true)
            {
                SkipWhiteSpace();
                var name = ReadStringBytes();
                SkipWhiteSpace();
                Expect(':');
                SkipWhiteSpace();

                var member = model.Find(name);
                if (member == null)
                {
                    SkipValue();
                }
                else
                {
                    var value = member.Kind == MemberKind.Array ? ReadArray(member, model.Type) : ReadValue(member.Kind, member.Type, member.Name, model.Type);
                    member.Setter.Invoke(instance, new object[] { value });
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

            depth--;
            return instance;
        }

        object ReadArray(MemberModel member, Type owner)
        {
            if (Peek() == 'n')
            {
                ExpectLiteral("null");
                return null;
            }

            Expect('[');
            EnterNesting();

            var items = new ArrayList();
            SkipWhiteSpace();
            if (Peek() == ']')
            {
                position++;
            }
            else
            {
                while (true)
                {
                    SkipWhiteSpace();
                    items.Add(ReadValue(member.ElementKind, member.ElementType, member.Name, owner));

                    SkipWhiteSpace();
                    var next = Next();
                    if (next == ',')
                    {
                        continue;
                    }

                    if (next == ']')
                    {
                        break;
                    }

                    throw Error("expected ',' or ']' in '" + member.Name + "'");
                }
            }

            depth--;
            return ToArray(items, member);
        }

        // Without generics, an array is built for the element type: a typed array for the value types, and an object array view of an array
        // created for the element type for the classes. An enum array is passed as the integers it is made of, which is how the setter takes it.
        static object ToArray(ArrayList items, MemberModel member)
        {
            var count = items.Count;
            switch (member.ElementKind)
            {
                case MemberKind.String:
                    var strings = new string[count];
                    for (var i = 0; i < count; i++) strings[i] = (string)items[i];
                    return strings;
                case MemberKind.Bool:
                    var bools = new bool[count];
                    for (var i = 0; i < count; i++) bools[i] = (bool)items[i];
                    return bools;
                case MemberKind.Int:
                case MemberKind.Enum:
                    var ints = new int[count];
                    for (var i = 0; i < count; i++) ints[i] = (int)items[i];
                    return ints;
                case MemberKind.Long:
                    var longs = new long[count];
                    for (var i = 0; i < count; i++) longs[i] = (long)items[i];
                    return longs;
                case MemberKind.Double:
                    var doubles = new double[count];
                    for (var i = 0; i < count; i++) doubles[i] = (double)items[i];
                    return doubles;
                case MemberKind.DateTime:
                    var times = new DateTime[count];
                    for (var i = 0; i < count; i++) times[i] = (DateTime)items[i];
                    return times;
                default:
                    var objects = (object[])Array.CreateInstance(member.ElementType, count);
                    for (var i = 0; i < count; i++) objects[i] = items[i];
                    return objects;
            }
        }

        object ReadValue(int kind, Type type, string memberName, Type owner)
        {
            if (Peek() == 'n')
            {
                ExpectLiteral("null");
                if (kind == MemberKind.String || kind == MemberKind.Object)
                {
                    return null;
                }

                throw Error("'" + memberName + "' is null, but its type does not allow it");
            }

            switch (kind)
            {
                case MemberKind.String:
                    return ToString(ReadStringBytes());
                case MemberKind.Bool:
                    return ReadBool(memberName);
                case MemberKind.Int:
                    return (int)ReadInteger(memberName, int.MinValue, int.MaxValue);
                case MemberKind.Enum:
                    return (int)ReadInteger(memberName, int.MinValue, int.MaxValue);
                case MemberKind.Long:
                    return ReadInteger(memberName, long.MinValue, long.MaxValue);
                case MemberKind.Double:
                    return ReadDouble(memberName);
                case MemberKind.DateTime:
                    return ReadDateTime(memberName);
                case MemberKind.Object:
                    if (Peek() != '{')
                    {
                        throw Error("'" + memberName + "' is not a JSON object");
                    }

                    return ReadObject(TypeModels.For(type));
                default:
                    throw Error("'" + memberName + "' has a type that cannot be read");
            }
        }

        object ReadBool(string memberName)
        {
            if (Peek() == 't')
            {
                ExpectLiteral("true");
                return true;
            }

            if (Peek() == 'f')
            {
                ExpectLiteral("false");
                return false;
            }

            throw Error("'" + memberName + "' is not true or false");
        }

        // a JSON number written as an integer, within the range of the member's type
        long ReadInteger(string memberName, long minimum, long maximum)
        {
            var start = position;
            if (!IsNumberStart(Peek()))
            {
                throw Error("'" + memberName + "' is not a number");
            }

            if (!ScanNumber())
            {
                throw Error("'" + memberName + "' is not an integer");
            }

            var negative = data[start] == '-';
            var index = negative ? start + 1 : start;

            // accumulated as a negative number, because the smallest long has no positive counterpart
            long value = 0;
            for (; index < position; index++)
            {
                var digit = data[index] - '0';
                if (value < (long.MinValue + digit) / 10)
                {
                    throw Error("'" + memberName + "' is out of range");
                }

                value = value * 10 - digit;
            }

            if (!negative)
            {
                if (value == long.MinValue)
                {
                    throw Error("'" + memberName + "' is out of range");
                }

                value = -value;
            }

            if (value < minimum || value > maximum)
            {
                throw Error("'" + memberName + "' is out of range");
            }

            return value;
        }

        object ReadDouble(string memberName)
        {
            var start = position;
            if (!IsNumberStart(Peek()))
            {
                throw Error("'" + memberName + "' is not a number");
            }

            ScanNumber();
            var value = double.Parse(Encoding.UTF8.GetString(data, start, position - start));
            if (double.IsInfinity(value) || double.IsNaN(value))
            {
                throw Error("'" + memberName + "' is out of range");
            }

            return value;
        }

        object ReadDateTime(string memberName)
        {
            if (Peek() != '"')
            {
                throw Error("'" + memberName + "' is not a string");
            }

            DateTime value;
            if (!DateTimeText.TryParse(ToString(ReadStringBytes()), out value))
            {
                throw Error("'" + memberName + "' is not a valid date and time");
            }

            return value;
        }

        void EnterNesting()
        {
            Enter();
            if (depth > MaxNesting)
            {
                throw Error("the nesting is too deep");
            }
        }

        static bool IsNumberStart(int character)
        {
            return character == '-' || (character >= '0' && character <= '9');
        }

        static string ToString(byte[] utf8)
        {
            return Encoding.UTF8.GetString(utf8, 0, utf8.Length);
        }
    }
}
