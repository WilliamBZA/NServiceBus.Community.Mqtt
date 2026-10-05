namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Reads the <c>NServiceBus.EnclosedMessageTypes</c> header, which lists the types a message is, as assembly-qualified names.
    /// </summary>
    static class EnclosedMessageTypes
    {
        // Entries look like "Namespace.Type, Assembly, Version=..." and separate with ';'. For a generic type the name has "[[...]]" in it,
        // which holds commas of its own, so the full name is everything up to the first comma outside brackets.
        public static IEnumerable<string> Names(string enclosedMessageTypes)
        {
            foreach (var entry in enclosedMessageTypes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var depth = 0;
                var end = entry.Length;

                for (var i = 0; i < entry.Length; i++)
                {
                    if (entry[i] == '[')
                    {
                        depth++;
                    }
                    else if (entry[i] == ']')
                    {
                        depth--;
                    }
                    else if (entry[i] == ',' && depth == 0)
                    {
                        end = i;
                        break;
                    }
                }

                yield return entry[..end].Trim();
            }
        }
    }
}
