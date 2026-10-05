using System;
using System.Collections;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Reads the <c>NServiceBus.EnclosedMessageTypes</c> header, which lists the types a message is. Entries look like
    /// <c>Namespace.Type, Assembly, Version=...</c> and are separated by <c>;</c>.
    /// </summary>
    internal static class EnclosedMessageTypes
    {
        /// <summary>
        /// The full name of every entry, without the assembly part, in header order. For a generic type the name has <c>[[...]]</c> in it, which holds
        /// commas of its own, so a name ends at the first comma outside brackets.
        /// </summary>
        public static string[] Names(string enclosedMessageTypes)
        {
            var names = new ArrayList();
            if (enclosedMessageTypes != null)
            {
                var start = 0;
                while (start <= enclosedMessageTypes.Length)
                {
                    var separator = enclosedMessageTypes.IndexOf(';', start);
                    var end = separator < 0 ? enclosedMessageTypes.Length : separator;

                    var name = NameOf(enclosedMessageTypes.Substring(start, end - start).Trim());
                    if (name.Length > 0)
                    {
                        names.Add(name);
                    }

                    start = end + 1;
                }
            }

            var result = new string[names.Count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = (string)names[i];
            }

            return result;
        }

        static string NameOf(string entry)
        {
            var depth = 0;
            var end = entry.Length;

            for (var i = 0; i < entry.Length; i++)
            {
                var character = entry[i];
                if (character == '[')
                {
                    depth++;
                }
                else if (character == ']')
                {
                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    end = i;
                    break;
                }
            }

            return entry.Substring(0, end).Trim();
        }
    }
}
