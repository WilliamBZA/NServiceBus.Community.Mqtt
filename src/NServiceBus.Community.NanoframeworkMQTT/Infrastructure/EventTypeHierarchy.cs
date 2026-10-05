using System;
using System.Collections;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Lists the types an event is published as, in the order <c>NServiceBus.Community.Mqtt</c> uses: the type itself, its base classes nearest
    /// first, then the interfaces it implements in ordinal order of their full names. A subscriber to any of these types receives the event.
    /// </summary>
    internal static class EventTypeHierarchy
    {
        public static Type[] Enumerate(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException("eventType");
            }

            var types = new ArrayList();
            types.Add(eventType);

            for (var baseType = eventType.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (!IsExcluded(baseType))
                {
                    types.Add(baseType);
                }
            }

            // Reflection does not promise the order of interfaces, nor on nanoFramework that it lists the inherited ones, so collect them
            // from the type, its base classes and the interfaces themselves, and sort them for a deterministic result.
            var interfaces = new ArrayList();
            for (var type = eventType; type != null; type = type.BaseType)
            {
                AddInterfaces(type, interfaces);
            }

            var sorted = new Type[interfaces.Count];
            interfaces.CopyTo(sorted);
            SortByFullName(sorted);

            for (var i = 0; i < sorted.Length; i++)
            {
                types.Add(sorted[i]);
            }

            var result = new Type[types.Count];
            types.CopyTo(result);
            return result;
        }

        /// <summary>True for <c>object</c>, <c>System</c> types and the message markers, which are never published as a topic of their own.</summary>
        public static bool IsExcluded(Type type)
        {
            if (type == typeof(object) || type == typeof(IMessage) || type == typeof(IEvent) || type == typeof(ICommand))
            {
                return true;
            }

            // there is no Type.Namespace on nanoFramework, so System types are found by the prefix of their full name
            var fullName = type.FullName;
            return fullName != null && (fullName == "System" || fullName.StartsWith("System."));
        }

        static void AddInterfaces(Type type, ArrayList interfaces)
        {
            var declared = type.GetInterfaces();
            for (var i = 0; i < declared.Length; i++)
            {
                var candidate = declared[i];
                if (IsExcluded(candidate) || Contains(interfaces, candidate))
                {
                    continue;
                }

                interfaces.Add(candidate);
                AddInterfaces(candidate, interfaces);
            }
        }

        static bool Contains(ArrayList types, Type type)
        {
            for (var i = 0; i < types.Count; i++)
            {
                if (types[i] == type)
                {
                    return true;
                }
            }

            return false;
        }

        static void SortByFullName(Type[] types)
        {
            // insertion sort: the lists are a handful of interfaces long
            for (var i = 1; i < types.Length; i++)
            {
                var current = types[i];
                var j = i - 1;
                while (j >= 0 && CompareOrdinal(types[j].FullName, current.FullName) > 0)
                {
                    types[j + 1] = types[j];
                    j--;
                }

                types[j + 1] = current;
            }
        }

        static int CompareOrdinal(string left, string right)
        {
            var length = left.Length < right.Length ? left.Length : right.Length;
            for (var i = 0; i < length; i++)
            {
                if (left[i] != right[i])
                {
                    return left[i] < right[i] ? -1 : 1;
                }
            }

            return left.Length - right.Length;
        }
    }
}
