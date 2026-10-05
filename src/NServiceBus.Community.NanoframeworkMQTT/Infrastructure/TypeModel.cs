using System;
using System.Collections;
using System.Reflection;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>The kinds of member a message can have. These are the types a device and a .NET endpoint exchange.</summary>
    internal static class MemberKind
    {
        public const int String = 1;
        public const int Bool = 2;
        public const int Int = 3;
        public const int Long = 4;
        public const int Double = 5;
        public const int DateTime = 6;
        public const int Enum = 7;
        public const int Object = 8;
        public const int Array = 9;
    }

    /// <summary>A public read/write property of a message type, with what is needed to read and write it by reflection.</summary>
    internal sealed class MemberModel
    {
        public MemberModel(string name, MethodInfo getter, MethodInfo setter, Type type, int kind, Type elementType, int elementKind)
        {
            Name = name;
            Getter = getter;
            Setter = setter;
            Type = type;
            Kind = kind;
            ElementType = elementType;
            ElementKind = elementKind;
        }

        public string Name { get; private set; }

        public MethodInfo Getter { get; private set; }

        public MethodInfo Setter { get; private set; }

        public Type Type { get; private set; }

        public int Kind { get; private set; }

        /// <summary>For an array, the type of its elements.</summary>
        public Type ElementType { get; private set; }

        public int ElementKind { get; private set; }
    }

    /// <summary>The members of a message class, sorted by name, and its constructor.</summary>
    internal sealed class TypeModel
    {
        public TypeModel(Type type, ConstructorInfo constructor)
        {
            Type = type;
            Constructor = constructor;
        }

        public Type Type { get; private set; }

        public ConstructorInfo Constructor { get; private set; }

        public MemberModel[] Members { get; internal set; }

        /// <summary>The member whose name is the given UTF-8 bytes, compared case-sensitively as System.Text.Json does by default, or <c>null</c>.</summary>
        public MemberModel Find(byte[] name)
        {
            for (var i = 0; i < Members.Length; i++)
            {
                var candidate = Members[i].Name;
                if (candidate.Length != name.Length)
                {
                    continue;
                }

                var same = true;
                for (var j = 0; j < name.Length; j++)
                {
                    if (name[j] != candidate[j])
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return Members[i];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Builds and caches the <see cref="TypeModel" /> of a message type. Building it also checks that every member has a type the device can
    /// exchange, so that an unsupported member is reported when the type is registered rather than when a message arrives.
    /// </summary>
    internal static class TypeModels
    {
        static readonly Hashtable Cache = new Hashtable();

        public static TypeModel For(Type type)
        {
            lock (Cache)
            {
                var model = (TypeModel)Cache[type];
                if (model != null)
                {
                    return model;
                }

                return Build(type);
            }
        }

        static TypeModel Build(Type type)
        {
            if (type.IsInterface || type.IsAbstract || type.IsValueType || !type.IsClass || type == typeof(string) || type == typeof(object) || type.IsArray)
            {
                throw new NotSupportedException("The type '" + type.FullName + "' cannot be exchanged with another endpoint. " + SupportedTypes);
            }

            var constructor = type.GetConstructor(new Type[0]);
            if (constructor == null)
            {
                throw new NotSupportedException("The message type '" + type.FullName + "' needs a public constructor without parameters.");
            }

            var model = new TypeModel(type, constructor);

            // a type that refers to itself (a linked list, say) finds its own model while the members are being read
            Cache[type] = model;
            try
            {
                model.Members = ReadMembers(type);
            }
            catch
            {
                Cache.Remove(type);
                throw;
            }

            return model;
        }

        static MemberModel[] ReadMembers(Type type)
        {
            var members = new ArrayList();
            var seen = new Hashtable();
            var methods = type.GetMethods();

            for (var i = 0; i < methods.Length; i++)
            {
                var getter = methods[i];
                if (!getter.IsPublic || getter.IsStatic || !getter.Name.StartsWith("get_") || getter.GetParameters().Length != 0)
                {
                    continue;
                }

                var name = getter.Name.Substring(4);
                if (seen.Contains(name))
                {
                    continue;
                }

                MethodInfo setter = null;
                for (var j = 0; j < methods.Length; j++)
                {
                    if (methods[j].IsPublic && !methods[j].IsStatic && methods[j].Name == "set_" + name && methods[j].GetParameters().Length == 1)
                    {
                        setter = methods[j];
                        break;
                    }
                }

                // a property that is not read/write is not part of the message
                if (setter == null)
                {
                    continue;
                }

                seen.Add(name, name);

                var memberType = setter.GetParameters()[0].ParameterType;
                var kind = Classify(type, name, memberType);
                Type elementType = null;
                var elementKind = 0;
                if (kind == MemberKind.Array)
                {
                    elementType = memberType.GetElementType();
                    elementKind = Classify(type, name, elementType);
                    if (elementKind == MemberKind.Array)
                    {
                        throw Unsupported(type, name, memberType);
                    }
                }

                members.Add(new MemberModel(name, getter, setter, memberType, kind, elementType, elementKind));
            }

            var sorted = new MemberModel[members.Count];
            for (var i = 0; i < sorted.Length; i++)
            {
                sorted[i] = (MemberModel)members[i];
            }

            Sort(sorted);
            return sorted;
        }

        static int Classify(Type owner, string name, Type memberType)
        {
            if (memberType == typeof(string)) return MemberKind.String;
            if (memberType == typeof(bool)) return MemberKind.Bool;
            if (memberType == typeof(int)) return MemberKind.Int;
            if (memberType == typeof(long)) return MemberKind.Long;
            if (memberType == typeof(double)) return MemberKind.Double;
            if (memberType == typeof(DateTime)) return MemberKind.DateTime;
            if (memberType.IsEnum) return MemberKind.Enum;
            if (memberType.IsArray) return MemberKind.Array;

            if (memberType.IsClass && !memberType.IsAbstract && memberType != typeof(object))
            {
                // the nested class has to be exchangeable too
                For(memberType);
                return MemberKind.Object;
            }

            throw Unsupported(owner, name, memberType);
        }

        static Exception Unsupported(Type owner, string name, Type memberType)
        {
            return new NotSupportedException("The member '" + owner.FullName + "." + name + "' has the type '" + memberType.FullName + "', which a device cannot exchange. " + SupportedTypes);
        }

        const string SupportedTypes = "A message can have string, bool, int, long, double, DateTime and enum members, nested classes made of these, and arrays of them.";

        static void Sort(MemberModel[] members)
        {
            // insertion sort by name: a message has a handful of members
            for (var i = 1; i < members.Length; i++)
            {
                var current = members[i];
                var j = i - 1;
                while (j >= 0 && CompareOrdinal(members[j].Name, current.Name) > 0)
                {
                    members[j + 1] = members[j];
                    j--;
                }

                members[j + 1] = current;
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
