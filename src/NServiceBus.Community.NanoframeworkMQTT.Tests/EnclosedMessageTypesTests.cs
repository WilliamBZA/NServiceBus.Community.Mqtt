using System;
using System.Collections;
using Interop;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    [TestClass]
    public class EnclosedMessageTypesTests
    {
        const string ValveOpenedHeader = "Contracts.ValveOpened, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.ValveEvent, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAlarm, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAudited, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        const string ValveOpenedTopic = "events/Contracts.ValveOpened";
        const string ValveEventTopic = "events/Contracts.ValveEvent";
        const string AlarmTopic = "events/Contracts.IAlarm";
        const string AuditedTopic = "events/Contracts.IAudited";

        [TestMethod]
        public void Header_values_yield_the_expected_names()
        {
            foreach (EnclosedMessageTypesCase mappingCase in MappingCases.EnclosedMessageTypes)
            {
                var names = EnclosedMessageTypes.Names(mappingCase.Header);

                Assert.AreEqual(mappingCase.Names.Length, names.Length, "'" + mappingCase.Header + "': number of names");
                for (var i = 0; i < names.Length; i++)
                {
                    Assert.AreEqual(mappingCase.Names[i], names[i], "'" + mappingCase.Header + "': name " + i);
                }
            }
        }

        [TestMethod]
        public void A_missing_header_yields_no_names()
        {
            Assert.AreEqual(0, EnclosedMessageTypes.Names(null).Length);
        }

        [TestMethod]
        public void Subscribed_to_a_type_and_its_base_only_the_copy_for_the_first_subscribed_type_is_designated()
        {
            var subscriptions = Subscriptions("Contracts.ValveEvent", ValveEventTopic, "Contracts.ValveOpened", ValveOpenedTopic);

            Assert.IsTrue(DesignatedCopy.IsDesignated(ValveOpenedTopic, ValveOpenedHeader, subscriptions), "the copy for the concrete type, which comes first in the header");
            Assert.IsFalse(DesignatedCopy.IsDesignated(ValveEventTopic, ValveOpenedHeader, subscriptions), "the copy for the base type");
        }

        [TestMethod]
        public void Subscribed_to_one_of_two_interfaces_the_copy_for_that_interface_is_designated()
        {
            var deviceA = Subscriptions("Contracts.IAlarm", AlarmTopic);
            var deviceB = Subscriptions("Contracts.IAudited", AuditedTopic);

            Assert.IsTrue(DesignatedCopy.IsDesignated(AlarmTopic, ValveOpenedHeader, deviceA), "device A, on the IAlarm copy");
            Assert.IsTrue(DesignatedCopy.IsDesignated(AuditedTopic, ValveOpenedHeader, deviceB), "device B, on the IAudited copy");
        }

        [TestMethod]
        public void Subscribed_to_both_interfaces_the_first_in_header_order_is_designated()
        {
            var subscriptions = Subscriptions("Contracts.IAlarm", AlarmTopic, "Contracts.IAudited", AuditedTopic);

            Assert.IsTrue(DesignatedCopy.IsDesignated(AlarmTopic, ValveOpenedHeader, subscriptions), "IAlarm comes before IAudited");
            Assert.IsFalse(DesignatedCopy.IsDesignated(AuditedTopic, ValveOpenedHeader, subscriptions), "IAudited comes after IAlarm");
        }

        [TestMethod]
        public void A_message_without_the_header_is_processed_as_it_is()
        {
            var subscriptions = Subscriptions("Contracts.ValveEvent", ValveEventTopic);

            Assert.IsTrue(DesignatedCopy.IsDesignated(ValveEventTopic, null, subscriptions));
        }

        [TestMethod]
        public void A_message_on_a_queue_topic_is_processed_as_it_is()
        {
            var subscriptions = Subscriptions("Contracts.ValveEvent", ValveEventTopic);

            Assert.IsTrue(DesignatedCopy.IsDesignated("Gate/01", ValveOpenedHeader, subscriptions));
        }

        [TestMethod]
        public void A_message_none_of_whose_types_is_subscribed_is_processed_as_it_is()
        {
            var subscriptions = Subscriptions("Contracts.PriceChanged", "events/Contracts.PriceChanged");

            Assert.IsTrue(DesignatedCopy.IsDesignated(ValveOpenedTopic, ValveOpenedHeader, subscriptions));
        }

        static Hashtable Subscriptions(params string[] fullNameAndTopicPairs)
        {
            var subscriptions = new Hashtable();
            for (var i = 0; i < fullNameAndTopicPairs.Length; i += 2)
            {
                subscriptions.Add(fullNameAndTopicPairs[i], fullNameAndTopicPairs[i + 1]);
            }

            return subscriptions;
        }
    }
}
