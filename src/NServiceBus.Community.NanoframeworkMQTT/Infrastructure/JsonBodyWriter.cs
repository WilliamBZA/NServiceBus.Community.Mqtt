using System;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Writes a message as the UTF-8 JSON System.Text.Json reads for the same type: members by name, enums as numbers, <see cref="DateTime" />
    /// as ISO 8601, doubles that read back exactly. Members that are <c>null</c> are left out, which reads back the same. The members are written in the
    /// ordinal order of their names, so that the output does not depend on the order reflection lists them in.
    /// </summary>
    internal static class JsonBodyWriter
    {
        const int MaxDepth = 32;
        static readonly object[] NoArguments = new object[0];

        public static byte[] Write(object message)
        {
            var output = new ByteBuffer(256);
            WriteObject(output, TypeModels.For(message.GetType()), message, 1);
            return output.ToArray();
        }

        static void WriteObject(ByteBuffer output, TypeModel model, object instance, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException("The message '" + model.Type.FullName + "' is nested more than " + MaxDepth + " levels deep. Does it refer to itself?");
            }

            output.Append((byte)'{');
            var first = true;
            for (var i = 0; i < model.Members.Length; i++)
            {
                var member = model.Members[i];
                var value = member.Getter.Invoke(instance, NoArguments);
                if (value == null)
                {
                    continue;
                }

                if (!first)
                {
                    output.Append((byte)',');
                }

                first = false;
                JsonText.WriteString(output, Encoding.UTF8.GetBytes(member.Name));
                output.Append((byte)':');

                if (member.Kind == MemberKind.Array)
                {
                    WriteArray(output, member, (Array)value, depth);
                }
                else
                {
                    WriteValue(output, member.Kind, member.Type, value, depth);
                }
            }

            output.Append((byte)'}');
        }

        static void WriteArray(ByteBuffer output, MemberModel member, Array array, int depth)
        {
            output.Append((byte)'[');
            for (var i = 0; i < array.Length; i++)
            {
                if (i > 0)
                {
                    output.Append((byte)',');
                }

                var element = array.GetValue(i);
                if (element == null)
                {
                    output.AppendAscii("null");
                }
                else
                {
                    WriteValue(output, member.ElementKind, member.ElementType, element, depth);
                }
            }

            output.Append((byte)']');
        }

        static void WriteValue(ByteBuffer output, int kind, Type type, object value, int depth)
        {
            switch (kind)
            {
                case MemberKind.String:
                    JsonText.WriteString(output, Encoding.UTF8.GetBytes((string)value));
                    break;
                case MemberKind.Bool:
                    output.AppendAscii((bool)value ? "true" : "false");
                    break;
                case MemberKind.Int:
                    output.AppendAscii(((int)value).ToString());
                    break;
                case MemberKind.Long:
                    output.AppendAscii(((long)value).ToString());
                    break;
                case MemberKind.Double:
                    output.AppendAscii(DoubleText.Format((double)value));
                    break;
                case MemberKind.DateTime:
                    output.Append((byte)'"');
                    output.AppendAscii(DateTimeText.Format((DateTime)value));
                    output.Append((byte)'"');
                    break;
                case MemberKind.Enum:
                    output.AppendAscii(((int)value).ToString());
                    break;
                case MemberKind.Object:
                    WriteObject(output, TypeModels.For(type), value, depth + 1);
                    break;
                default:
                    throw new NotSupportedException("The member kind " + kind + " cannot be written.");
            }
        }
    }
}
