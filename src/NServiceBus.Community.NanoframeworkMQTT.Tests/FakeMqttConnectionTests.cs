using System;
using System.Collections;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>The in-memory broker the device tests are built on has to behave like a broker: these tests are its own.</summary>
    [TestClass]
    public class FakeMqttConnectionTests
    {
        const string ClientId = "nsb.Device/01";

        static MqttConnectOptions Options(string clientId, int maximumPacketSize)
        {
            var options = new MqttConnectOptions();
            options.ClientId = clientId;
            options.Host = "broker.test";
            options.Port = 1883;
            options.MaximumPacketSize = maximumPacketSize;
            options.OperationTimeout = TimeSpan.FromSeconds(1);
            return options;
        }

        sealed class Inbox
        {
            public readonly ArrayList Topics = new ArrayList();

            public Inbox(FakeMqttConnection connection)
            {
                connection.MessageReceived += OnMessage;
            }

            void OnMessage(string topic, byte[] payload)
            {
                Topics.Add(topic);
            }
        }

        [TestMethod]
        public void A_session_keeps_the_messages_sent_while_the_client_is_disconnected()
        {
            var broker = new FakeBroker();
            var first = new FakeMqttConnection(broker);
            first.Connect(Options(ClientId, 16384));
            first.Subscribe(new string[] { "Device/01" });
            first.Disconnect();

            broker.PublishAsForeignClient("Device/01", new byte[] { 1 });
            broker.PublishAsForeignClient("Device/01", new byte[] { 2 });
            Assert.AreEqual(2, broker.WaitingFor(ClientId), "waiting in the session");

            var second = new FakeMqttConnection(broker);
            var inbox = new Inbox(second);
            second.Connect(Options(ClientId, 16384));

            Assert.AreEqual(2, inbox.Topics.Count, "delivered when the session is resumed");
            Assert.AreEqual(0, broker.WaitingFor(ClientId), "nothing is left waiting");
        }

        [TestMethod]
        public void A_session_that_has_expired_keeps_nothing()
        {
            var broker = new FakeBroker();
            var first = new FakeMqttConnection(broker);
            first.Connect(Options(ClientId, 16384));
            first.Subscribe(new string[] { "Device/01" });
            first.Disconnect();
            broker.ExpireSession(ClientId);

            broker.PublishAsForeignClient("Device/01", new byte[] { 1 });

            var second = new FakeMqttConnection(broker);
            var inbox = new Inbox(second);
            second.Connect(Options(ClientId, 16384));

            Assert.AreEqual(0, inbox.Topics.Count);
            Assert.IsFalse(broker.IsSubscribed(ClientId, "Device/01"), "the subscription is gone with the session");
        }

        [TestMethod]
        public void A_takeover_disconnects_the_first_client_with_reason_0x8E()
        {
            var broker = new FakeBroker();
            var first = new FakeMqttConnection(broker);
            var reasons = new ArrayList();
            first.ConnectionLost += delegate(bool takenOver) { reasons.Add(takenOver); };
            first.Connect(Options(ClientId, 16384));

            var second = new FakeMqttConnection(broker);
            second.Connect(Options(ClientId, 16384));

            Assert.AreEqual(1, reasons.Count, "the first client was told once");
            Assert.IsTrue((bool)reasons[0], "it was a takeover");
            Assert.IsFalse(first.IsConnected);
            Assert.IsTrue(second.IsConnected);
        }

        [TestMethod]
        public void A_dropped_connection_is_not_a_takeover()
        {
            var broker = new FakeBroker();
            var connection = new FakeMqttConnection(broker);
            var reasons = new ArrayList();
            connection.ConnectionLost += delegate(bool takenOver) { reasons.Add(takenOver); };
            connection.Connect(Options(ClientId, 16384));

            broker.DropConnection(ClientId);

            Assert.AreEqual(1, reasons.Count);
            Assert.IsFalse((bool)reasons[0]);
        }

        [TestMethod]
        public void A_subscription_receives_a_message_until_it_is_removed()
        {
            var broker = new FakeBroker();
            var connection = new FakeMqttConnection(broker);
            var inbox = new Inbox(connection);
            connection.Connect(Options(ClientId, 16384));
            connection.Subscribe(new string[] { "events/A", "events/B" });

            broker.PublishAsForeignClient("events/A", new byte[0]);
            connection.Unsubscribe(new string[] { "events/A" });
            broker.PublishAsForeignClient("events/A", new byte[0]);
            broker.PublishAsForeignClient("events/B", new byte[0]);

            Assert.AreEqual(2, inbox.Topics.Count);
            Assert.AreEqual("events/A", (string)inbox.Topics[0]);
            Assert.AreEqual("events/B", (string)inbox.Topics[1]);
        }

        [TestMethod]
        public void A_packet_larger_than_the_maximum_the_client_declared_is_not_delivered()
        {
            var broker = new FakeBroker();
            var connection = new FakeMqttConnection(broker);
            var inbox = new Inbox(connection);
            connection.Connect(Options(ClientId, 64));
            connection.Subscribe(new string[] { "t" });

            // a packet with a one byte topic and a payload of 59 bytes is 1 + 1 + 3 + 2 + 1 + 59 = 67 bytes, and 58 bytes of payload make 66
            broker.PublishAsForeignClient("t", new byte[59]);
            broker.PublishAsForeignClient("t", new byte[54]);

            Assert.AreEqual(1, inbox.Topics.Count, "only the packet of 62 bytes arrives");
        }

        [TestMethod]
        public void A_publish_reaches_every_subscriber_and_is_recorded()
        {
            var broker = new FakeBroker();
            var publisher = new FakeMqttConnection(broker);
            publisher.Connect(Options("publisher", 16384));
            var subscriber = new FakeMqttConnection(broker);
            var inbox = new Inbox(subscriber);
            subscriber.Connect(Options(ClientId, 16384));
            subscriber.Subscribe(new string[] { "t" });

            publisher.Publish("t", new byte[] { 7 }, 0);

            Assert.AreEqual(1, inbox.Topics.Count);
            Assert.AreEqual(1, broker.PublishedCount("t"));
            Assert.AreEqual("publisher", ((PublishedMessage)broker.PublishedTo("t")[0]).ClientId);
        }

        [TestMethod]
        public void Injected_failures_are_raised_by_the_next_calls_and_then_stop()
        {
            var broker = new FakeBroker();
            var connection = new FakeMqttConnection(broker);

            connection.FailConnects(1, "connect failed");
            Assert.ThrowsException(typeof(InvalidOperationException), () => connection.Connect(Options(ClientId, 16384)));
            connection.Connect(Options(ClientId, 16384));

            connection.FailPublishes(1, "publish failed");
            Assert.ThrowsException(typeof(InvalidOperationException), () => connection.Publish("t", new byte[0], 0));
            connection.Publish("t", new byte[0], 0);

            broker.RejectSubscriptionTo = "bad";
            Assert.ThrowsException(typeof(InvalidOperationException), () => connection.Subscribe(new string[] { "bad" }));

            broker.Reachable = false;
            var other = new FakeMqttConnection(broker);
            Assert.ThrowsException(typeof(InvalidOperationException), () => other.Connect(Options("other", 16384)));
        }

        [TestMethod]
        public void A_closed_connection_cannot_publish_or_subscribe()
        {
            var broker = new FakeBroker();
            var connection = new FakeMqttConnection(broker);
            connection.Connect(Options(ClientId, 16384));
            connection.Disconnect();

            Assert.ThrowsException(typeof(InvalidOperationException), () => connection.Publish("t", new byte[0], 0));
            Assert.ThrowsException(typeof(InvalidOperationException), () => connection.Subscribe(new string[] { "t" }));
        }
    }
}
