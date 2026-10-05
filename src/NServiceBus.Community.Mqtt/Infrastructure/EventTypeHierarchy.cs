namespace NServiceBus.Transport.Mqtt
{
    /// <summary>
    /// Lists the types an event is published as: the concrete type, its base classes and the interfaces it implements.
    /// A subscriber to any of these types receives the event.
    /// </summary>
    static class EventTypeHierarchy
    {
        public static IReadOnlyList<Type> Enumerate(Type eventType)
        {
            ArgumentNullException.ThrowIfNull(eventType);

            var types = new List<Type> { eventType };

            for (var baseType = eventType.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (!IsExcluded(baseType))
                {
                    types.Add(baseType);
                }
            }

            // interface order is not guaranteed by reflection, so sort for a deterministic result
            types.AddRange(eventType.GetInterfaces()
                .Where(type => !IsExcluded(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal));

            return types;
        }

        static bool IsExcluded(Type type) =>
            type == typeof(object)
            || type == typeof(IMessage)
            || type == typeof(IEvent)
            || type == typeof(ICommand)
            || type.Namespace == "System"
            || type.Namespace?.StartsWith("System.", StringComparison.Ordinal) == true;
    }
}
