using System;
using Hierarchy;
using Interop;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace Hierarchy
{
    // The same sample hierarchy as the .NET unit tests use, to see that the device lists inherited interfaces and leaves out the System ones.
    public interface IAuditable
    {
    }

    public interface IBaseEvent : NServiceBus.IEvent
    {
    }

    public class BaseEvent : IBaseEvent
    {
    }

    public class MiddleEvent : BaseEvent
    {
    }

    public class DerivedEvent : MiddleEvent, IAuditable, IDisposable
    {
        public void Dispose()
        {
        }
    }

    public class UnrelatedInterfacesEvent : NServiceBus.IEvent, IAuditable, IBaseEvent
    {
    }

    public class PlainMessage : NServiceBus.IMessage
    {
    }

    public class CommandMessage : NServiceBus.ICommand
    {
    }
}

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    [TestClass]
    public class EventTopicTests
    {
        [TestMethod]
        public void Event_types_map_to_the_expected_topic()
        {
            foreach (EventTopicCase mappingCase in MappingCases.EventTopics)
            {
                Assert.AreEqual(mappingCase.Topic, EventTopic.ToTopic(mappingCase.EventType), mappingCase.EventType.FullName);
            }
        }

        [TestMethod]
        public void A_nested_type_has_no_plus_in_its_topic()
        {
            Assert.AreEqual("events/Sales.Container.Inner", EventTopic.ToTopic(typeof(Sales.Container.Inner)));
        }

        [TestMethod]
        public void Event_types_are_published_as_the_expected_types_in_order()
        {
            foreach (HierarchyCase mappingCase in MappingCases.Hierarchies)
            {
                var types = EventTypeHierarchy.Enumerate(mappingCase.EventType);

                Assert.AreEqual(mappingCase.FullNames.Length, types.Length, mappingCase.EventType.FullName + ": number of types");
                for (var i = 0; i < types.Length; i++)
                {
                    Assert.AreEqual(mappingCase.FullNames[i], types[i].FullName, mappingCase.EventType.FullName + ": type " + i);
                }
            }
        }

        [TestMethod]
        public void The_hierarchy_lists_the_concrete_type_base_classes_then_inherited_and_declared_interfaces()
        {
            AssertHierarchy(
                typeof(DerivedEvent),
                "Hierarchy.DerivedEvent", "Hierarchy.MiddleEvent", "Hierarchy.BaseEvent", "Hierarchy.IAuditable", "Hierarchy.IBaseEvent");
        }

        [TestMethod]
        public void The_hierarchy_leaves_out_object_system_types_and_the_markers()
        {
            var types = EventTypeHierarchy.Enumerate(typeof(DerivedEvent));

            for (var i = 0; i < types.Length; i++)
            {
                Assert.IsFalse(types[i] == typeof(object), "object");
                Assert.IsFalse(types[i] == typeof(IDisposable), "IDisposable");
                Assert.IsFalse(types[i] == typeof(IEvent), "IEvent");
                Assert.IsFalse(types[i] == typeof(IMessage), "IMessage");
                Assert.IsFalse(types[i] == typeof(ICommand), "ICommand");
            }
        }

        [TestMethod]
        public void A_type_with_nothing_but_marker_ancestry_is_published_as_itself()
        {
            AssertHierarchy(typeof(PlainMessage), "Hierarchy.PlainMessage");
            AssertHierarchy(typeof(CommandMessage), "Hierarchy.CommandMessage");
        }

        [TestMethod]
        public void Every_unrelated_interface_gets_its_own_copy()
        {
            AssertHierarchy(
                typeof(UnrelatedInterfacesEvent),
                "Hierarchy.UnrelatedInterfacesEvent", "Hierarchy.IAuditable", "Hierarchy.IBaseEvent");
        }

        [TestMethod]
        public void An_interface_published_as_itself_has_only_itself()
        {
            AssertHierarchy(typeof(IBaseEvent), "Hierarchy.IBaseEvent");
        }

        [TestMethod]
        public void The_hierarchy_is_deterministic()
        {
            var first = EventTypeHierarchy.Enumerate(typeof(DerivedEvent));
            var second = EventTypeHierarchy.Enumerate(typeof(DerivedEvent));

            Assert.AreEqual(first.Length, second.Length, "length");
            for (var i = 0; i < first.Length; i++)
            {
                Assert.IsTrue(first[i] == second[i], "type " + i);
            }
        }

        static void AssertHierarchy(Type eventType, params string[] expectedFullNames)
        {
            var types = EventTypeHierarchy.Enumerate(eventType);

            Assert.AreEqual(expectedFullNames.Length, types.Length, eventType.FullName + ": number of types");
            for (var i = 0; i < types.Length; i++)
            {
                Assert.AreEqual(expectedFullNames[i], types[i].FullName, eventType.FullName + ": type " + i);
            }
        }
    }
}
