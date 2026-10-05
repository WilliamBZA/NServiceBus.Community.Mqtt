using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>
    /// The size of a QoS 1 MQTT 5 PUBLISH packet, against sizes worked out by hand from the packet layout: one byte of fixed header, the remaining
    /// length as a variable byte integer, a two byte topic length and the topic, a two byte packet identifier, the length of the properties as a variable byte
    /// integer, the properties, and the payload.
    /// </summary>
    [TestClass]
    public class MqttPacketTests
    {
        [TestMethod]
        public void A_short_topic_with_no_payload_is_8_bytes()
        {
            // 1 + 1 (remaining length) + [2 + 1 (topic) + 2 (packet identifier) + 1 (no properties) + 0]
            Assert.AreEqual(8L, MqttPacket.PublishSize("a", 0, 0));
        }

        [TestMethod]
        public void A_topic_and_a_payload_are_added_to_the_size()
        {
            // 1 + 1 + [2 + 5 + 2 + 1 + 100] = 112
            Assert.AreEqual(112L, MqttPacket.PublishSize("Plant", 100, 0));
        }

        [TestMethod]
        public void The_size_of_a_topic_is_counted_in_utf8_bytes()
        {
            // the topic 'é' is two bytes: 1 + 1 + [2 + 2 + 2 + 1 + 0] = 9
            Assert.AreEqual(9L, MqttPacket.PublishSize("é", 0, 0));

            // and a topic of a character outside the basic plane is four bytes: 1 + 1 + [2 + 4 + 2 + 1 + 0] = 11
            Assert.AreEqual(11L, MqttPacket.PublishSize("😅", 0, 0));
        }

        [TestMethod]
        public void A_message_expiry_adds_a_property_of_five_bytes()
        {
            // the property identifier and four bytes of the interval: 1 + 1 + [2 + 1 + 2 + 1 + 5 + 0] = 13
            Assert.AreEqual(13L, MqttPacket.PublishSize("a", 0, 30));
        }

        [TestMethod]
        public void The_remaining_length_takes_one_byte_up_to_127()
        {
            // remaining length 6 + payload; 127 is the largest value of one byte: 1 + 1 + 127
            Assert.AreEqual(129L, MqttPacket.PublishSize("a", 121, 0));
        }

        [TestMethod]
        public void The_remaining_length_takes_two_bytes_from_128_to_16383()
        {
            // 128 needs two bytes: 1 + 2 + 128
            Assert.AreEqual(131L, MqttPacket.PublishSize("a", 122, 0));

            // 16383 is the largest value of two bytes: 1 + 2 + 16383
            Assert.AreEqual(16386L, MqttPacket.PublishSize("a", 16377, 0));
        }

        [TestMethod]
        public void The_remaining_length_takes_three_bytes_from_16384_to_2097151()
        {
            // 16384 needs three bytes: 1 + 3 + 16384
            Assert.AreEqual(16388L, MqttPacket.PublishSize("a", 16378, 0));

            // 2097151 is the largest value of three bytes: 1 + 3 + 2097151
            Assert.AreEqual(2097155L, MqttPacket.PublishSize("a", 2097145, 0));
        }

        [TestMethod]
        public void The_remaining_length_takes_four_bytes_from_2097152()
        {
            // 1 + 4 + 2097152
            Assert.AreEqual(2097157L, MqttPacket.PublishSize("a", 2097146, 0));
        }

        [TestMethod]
        public void A_long_topic_moves_the_boundary()
        {
            // a topic of 100 characters and a payload of 20 bytes: remaining 2 + 100 + 2 + 1 + 20 = 125, total 1 + 1 + 125 = 127
            var topic = new string('t', 100);
            Assert.AreEqual(127L, MqttPacket.PublishSize(topic, 20, 0));

            // and 22 bytes of payload make the remaining length 127, and 23 make it 128, which needs a second byte
            Assert.AreEqual(1 + 1 + 127L, MqttPacket.PublishSize(topic, 22, 0));
            Assert.AreEqual(1 + 2 + 128L, MqttPacket.PublishSize(topic, 23, 0));
        }

        [TestMethod]
        public void The_variable_byte_length_has_its_boundaries_at_128_16384_and_2097152()
        {
            Assert.AreEqual(1, MqttPacket.VariableByteLength(0));
            Assert.AreEqual(1, MqttPacket.VariableByteLength(127));
            Assert.AreEqual(2, MqttPacket.VariableByteLength(128));
            Assert.AreEqual(2, MqttPacket.VariableByteLength(16383));
            Assert.AreEqual(3, MqttPacket.VariableByteLength(16384));
            Assert.AreEqual(3, MqttPacket.VariableByteLength(2097151));
            Assert.AreEqual(4, MqttPacket.VariableByteLength(2097152));
            Assert.AreEqual(4, MqttPacket.VariableByteLength(268435455));
        }
    }
}
