using System;
using System.Collections;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>Type checks that nanoFramework's reflection has no method for: there is no <c>IsAssignableFrom</c>.</summary>
    internal static class TypeRelations
    {
        /// <summary>True when a value of <paramref name="type" /> can be used as a <paramref name="target" />: the same type, a base class or an interface it implements.</summary>
        public static bool IsAssignableTo(Type type, Type target)
        {
            if (type == target)
            {
                return true;
            }

            if (target.IsInterface)
            {
                return ImplementsInterface(type, target);
            }

            for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (baseType == target)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsEvent(Type type)
        {
            return IsAssignableTo(type, typeof(IEvent));
        }

        // Reflection does not promise that GetInterfaces lists the inherited interfaces on nanoFramework, so every class on the way up and every
        // interface found is searched.
        static bool ImplementsInterface(Type type, Type target)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var declared = current.GetInterfaces();
                for (var i = 0; i < declared.Length; i++)
                {
                    if (declared[i] == target || ImplementsInterface(declared[i], target))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool Contains(ArrayList types, Type type)
        {
            for (var i = 0; i < types.Count; i++)
            {
                if ((Type)types[i] == type)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
