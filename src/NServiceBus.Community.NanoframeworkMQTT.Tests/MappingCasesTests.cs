using System;
using Interop;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>
    /// Runs the cases in <c>src/Interop/MappingCases.cs</c> against the device package. The .NET unit tests run the same cases against
    /// <c>NServiceBus.Community.Mqtt</c>, so a rule that drifts on either side fails a test.
    /// </summary>
    [TestClass]
    public class MappingCasesTests
    {
        [TestMethod]
        public void Addresses_map_to_the_expected_topic_or_are_rejected()
        {
            foreach (AddressCase mappingCase in MappingCases.Addresses)
            {
                var address = mappingCase.Address;
                if (mappingCase.Topic == null)
                {
                    Assert.ThrowsException(typeof(ArgumentException), () => MqttAddress.ToTopic(address), "address '" + address + "' should be rejected");
                }
                else
                {
                    Assert.AreEqual(mappingCase.Topic, MqttAddress.ToTopic(address), "address '" + address + "'");
                }
            }
        }

        [TestMethod]
        public void Queue_topics_map_to_the_expected_client_id_or_are_rejected()
        {
            foreach (ClientIdCase mappingCase in MappingCases.ClientIds)
            {
                var topic = mappingCase.Topic;
                if (mappingCase.ClientId == null)
                {
                    Assert.ThrowsException(typeof(ArgumentException), () => MqttClientId.ForQueue(topic), "topic '" + topic + "' should be rejected");
                }
                else
                {
                    Assert.AreEqual(mappingCase.ClientId, MqttClientId.ForQueue(topic), "topic '" + topic + "'");
                }
            }
        }

        [TestMethod]
        public void The_rejection_names_the_address()
        {
            ArgumentException rejection = null;
            try
            {
                MqttAddress.ToTopic("gate/#");
            }
            catch (ArgumentException exception)
            {
                rejection = exception;
            }

            Assert.IsNotNull(rejection, "The address should have been rejected.");
            Assert.Contains("gate/#", rejection.Message, "the message names the address");
            Assert.Contains("'+' or '#'", rejection.Message, "the message explains the rule");
        }
    }
}
