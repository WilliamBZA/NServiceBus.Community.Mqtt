using System;
using System.Collections;
using System.Reflection;
using System.Text;
using nanoFramework.TestFramework;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>
    /// Compares the public types and members of the device assembly with the approved list in <c>ApprovedPublicApi.cs</c>. This plays the part of the
    /// approval test on the .NET side, because the tool that writes the .NET public API cannot run on nanoFramework. A change to the public surface
    /// fails this test until the approved list is updated on purpose.
    /// </summary>
    [TestClass]
    public class PublicApiTests
    {
        [TestMethod]
        public void The_public_surface_of_the_assembly_is_the_approved_one()
        {
            var actual = Describe(typeof(DeviceEndpoint).Assembly);
            var approved = ApprovedPublicApi.Lines;

            var problems = new StringBuilder();
            for (var i = 0; i < actual.Length; i++)
            {
                if (!Contains(approved, actual[i]))
                {
                    problems.Append("\nnot approved: ").Append(actual[i]);
                }
            }

            for (var i = 0; i < approved.Length; i++)
            {
                if (!Contains(actual, approved[i]))
                {
                    problems.Append("\nmissing: ").Append(approved[i]);
                }
            }

            Assert.AreEqual(0, problems.Length, "The public API differs from the approved list:" + problems);
        }

        [TestMethod]
        public void The_description_lists_the_endpoint_and_its_members()
        {
            var actual = Describe(typeof(DeviceEndpoint).Assembly);

            Assert.IsTrue(Contains(actual, "class NServiceBus.DeviceEndpoint : NServiceBus.IMessageSession"), "the endpoint class");
            Assert.IsTrue(Contains(actual, "NServiceBus.DeviceEndpoint.Stop() : System.Void"), "its Stop method");
        }

        [TestMethod]
        public void Nothing_in_the_infrastructure_namespace_is_public()
        {
            var actual = Describe(typeof(DeviceEndpoint).Assembly);

            for (var i = 0; i < actual.Length; i++)
            {
                Assert.IsTrue(actual[i].IndexOf("NServiceBus.Community.NanoframeworkMQTT.Infrastructure") < 0, actual[i]);
            }
        }

        static bool Contains(string[] lines, string line)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i] == line)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>One line for every public type and every public member of a public type, sorted.</summary>
        static string[] Describe(Assembly assembly)
        {
            var lines = new ArrayList();
            var types = assembly.GetTypes();
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (!type.IsPublic)
                {
                    continue;
                }

                lines.Add(Kind(type) + " " + type.FullName + Bases(type));

                var constructors = type.GetConstructors();
                for (var c = 0; c < constructors.Length; c++)
                {
                    if (constructors[c].IsPublic)
                    {
                        lines.Add(type.FullName + ".ctor(" + Parameters(constructors[c]) + ")");
                    }
                }

                var methods = type.GetMethods();
                for (var m = 0; m < methods.Length; m++)
                {
                    var method = methods[m];
                    if (method.IsPublic && method.DeclaringType == type)
                    {
                        lines.Add((method.IsStatic ? "static " : "") + type.FullName + "." + method.Name + "(" + Parameters(method) + ") : " + method.ReturnType.FullName);
                    }
                }

                var fields = type.GetFields();
                for (var f = 0; f < fields.Length; f++)
                {
                    if (!type.IsEnum)
                    {
                        lines.Add("field " + type.FullName + "." + fields[f].Name + " : " + fields[f].FieldType.FullName);
                    }
                }
            }

            var result = new string[lines.Count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = (string)lines[i];
            }

            return Sorted(result);
        }

        static string Kind(Type type)
        {
            if (type.IsInterface)
            {
                return "interface";
            }

            if (type.IsEnum)
            {
                return "enum";
            }

            if (type.BaseType != null && type.BaseType.FullName == "System.MulticastDelegate")
            {
                return "delegate";
            }

            return type.IsAbstract ? "abstract class" : "class";
        }

        static string Bases(Type type)
        {
            var bases = new StringBuilder();
            if (type.BaseType != null && type.BaseType != typeof(object) && !type.IsEnum && Kind(type) != "delegate")
            {
                bases.Append(type.BaseType.FullName);
            }

            var interfaces = type.GetInterfaces();
            for (var i = 0; i < interfaces.Length; i++)
            {
                if (bases.Length > 0)
                {
                    bases.Append(", ");
                }

                bases.Append(interfaces[i].FullName);
            }

            return bases.Length == 0 ? "" : " : " + bases;
        }

        static string Parameters(MethodBase method)
        {
            var parameters = method.GetParameters();
            var text = new StringBuilder();
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(parameters[i].ParameterType.FullName);
            }

            return text.ToString();
        }

        static string[] Sorted(string[] lines)
        {
            // insertion sort by ordinal comparison: the lists are short
            for (var i = 1; i < lines.Length; i++)
            {
                var current = lines[i];
                var j = i - 1;
                while (j >= 0 && CompareOrdinal(lines[j], current) > 0)
                {
                    lines[j + 1] = lines[j];
                    j--;
                }

                lines[j + 1] = current;
            }

            return lines;
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
