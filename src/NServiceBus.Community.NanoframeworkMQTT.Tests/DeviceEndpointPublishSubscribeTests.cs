using System;
using System.Collections;
using Contracts;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>Publishing along the hierarchy, subscribing, unsubscribing, auto-subscribe and the one-copy rule, against the in-memory broker.</summary>
    [TestClass]
    public class DeviceEndpointPublishSubscribeTests
    {
        static string MessageOf(Check action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                return exception.GetType().Name + ": " + exception.Message;
            }

            return null;
        }

        static ValveOpened Opened()
        {
            var opened = new ValveOpened();
            opened.ValveId = "V-1";
            opened.Percent = 100;
            return opened;
        }

        // ---- publish

        [TestMethod]
        public void A_publish_makes_one_copy_for_every_type_in_the_hierarchy_with_the_same_message_id_and_headers()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                harness.Endpoint.Publish(Opened());

                var topics = new string[] { "events/Contracts.ValveOpened", "events/Contracts.ValveEvent", "events/Contracts.IAlarm", "events/Contracts.IAudited" };
                var published = harness.Broker.Published;
                Assert.AreEqual(4, published.Count);
                for (var i = 0; i < topics.Length; i++)
                {
                    Assert.AreEqual(topics[i], ((PublishedMessage)published[i]).Topic, "copy " + i + " is in the order of the hierarchy");
                }

                var first = Wire.First(harness, topics[0]);
                Assert.AreEqual("id-1", first.Id);
                Assert.AreEqual("Publish", Wire.Header(first, HeaderNames.MessageIntent));
                Assert.AreEqual("Contracts.ValveOpened;Contracts.ValveEvent;Contracts.IAlarm;Contracts.IAudited", Wire.Header(first, HeaderNames.EnclosedMessageTypes));

                var firstPayload = ((PublishedMessage)published[0]).Payload;
                for (var i = 1; i < topics.Length; i++)
                {
                    var payload = ((PublishedMessage)published[i]).Payload;
                    Assert.AreEqual(firstPayload.Length, payload.Length, "copy " + i);
                    for (var j = 0; j < payload.Length; j++)
                    {
                        Assert.AreEqual(firstPayload[j], payload[j], "byte " + j + " of copy " + i);
                    }
                }
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void The_first_copy_that_fails_stops_the_publish_and_the_error_names_its_topic()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                // the first copy works, the second one is rejected
                harness.Connection.FailPublishesAfter(1, 1, "The broker answered 'Quota exceeded' (0x97).");

                var message = MessageOf(() => harness.Endpoint.Publish(Opened()));

                Assert.IsNotNull(message);
                Assert.IsTrue(message.IndexOf("events/Contracts.ValveEvent") >= 0, "it names the topic that failed: " + message);
                Assert.IsTrue(message.IndexOf("broker.test:1883") >= 0, message);
                Assert.AreEqual(1, harness.Broker.Published.Count, "the later copies were not published");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_publish_from_a_handler_has_the_conversation_of_the_incoming_message()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.RegisterHandler(typeof(OpenValve), new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Publish(Opened());
            }));
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("events/Contracts.IAudited") == 1, "the last copy");

                var copy = Wire.First(harness, "events/Contracts.ValveOpened");
                Assert.AreEqual("conversation-of-m1", Wire.Header(copy, HeaderNames.ConversationId));
                Assert.AreEqual("m1", Wire.Header(copy, HeaderNames.RelatedTo));
                Assert.AreEqual("Publish", Wire.Header(copy, HeaderNames.MessageIntent));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_device_subscribed_to_an_interface_or_a_base_class_receives_the_event_another_device_publishes()
        {
            var broker = new FakeBroker();
            var publisher = new EndpointHarness("Publisher_01", broker);
            var alarms = new EndpointHarness("Alarms_01", broker);
            var bases = new EndpointHarness("Bases_01", broker);
            var alarmHandler = new ActionHandler(null);
            var baseHandler = new ActionHandler(null);
            alarms.Configuration.Subscribe(typeof(IAlarm));
            alarms.Configuration.RegisterHandler(typeof(ValveOpened), alarmHandler);
            alarms.Configuration.Unsubscribe(typeof(ValveOpened));
            bases.Configuration.RegisterHandler(typeof(ValveEvent), baseHandler);

            publisher.Start();
            alarms.Start();
            bases.Start();
            try
            {
                publisher.Endpoint.Publish(Opened());

                EndpointHarness.WaitFor(() => alarmHandler.Invocations == 1 && baseHandler.Invocations == 1, "both subscribers");
                EndpointHarness.Settle();
                Assert.AreEqual(1, alarmHandler.Invocations);
                Assert.AreEqual(1, baseHandler.Invocations);
                Assert.AreEqual("V-1", ((ValveEvent)baseHandler.Messages[0]).ValveId);
            }
            finally
            {
                bases.Stop();
                alarms.Stop();
                publisher.Stop();
            }
        }

        // ---- subscribe and unsubscribe

        [TestMethod]
        public void A_handler_without_an_explicit_subscription_gets_the_event()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(PriceChanged), handler);
            harness.Start();
            try
            {
                Samples.PublishPriceChanged(harness.Broker, "p1");

                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the handler");
                Assert.AreEqual("sku-1", ((PriceChanged)handler.Messages[0]).Sku);
                Assert.AreEqual(9.5, ((PriceChanged)handler.Messages[0]).Price);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Subscribing_at_runtime_waits_for_the_broker_and_delivers_the_event_from_then_on()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(PriceChanged), handler);
            harness.Configuration.Unsubscribe(typeof(PriceChanged));
            harness.Start();
            try
            {
                Samples.PublishPriceChanged(harness.Broker, "before");
                EndpointHarness.Settle();
                Assert.AreEqual(0, handler.Invocations, "not subscribed yet");

                harness.Endpoint.Subscribe(typeof(PriceChanged));
                Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.PriceChanged"), "subscribed when the call returns");

                Samples.PublishPriceChanged(harness.Broker, "after");
                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the handler");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Unsubscribing_stops_the_events_of_that_type_and_leaves_the_other_subscriptions_alone()
        {
            var broker = new FakeBroker();
            var device = new EndpointHarness("Device_01", broker);
            var other = new EndpointHarness("Device_02", broker);
            var deviceHandler = new ActionHandler(null);
            var otherHandler = new ActionHandler(null);
            var openedHandler = new ActionHandler(null);
            device.Configuration.RegisterHandler(typeof(PriceChanged), deviceHandler);
            device.Configuration.RegisterHandler(typeof(ValveOpened), openedHandler);
            other.Configuration.RegisterHandler(typeof(PriceChanged), otherHandler);
            device.Start();
            other.Start();
            try
            {
                device.Endpoint.Unsubscribe(typeof(PriceChanged));
                Assert.IsFalse(broker.IsSubscribed(device.ClientId, "events/Contracts.PriceChanged"));

                Samples.PublishPriceChanged(broker, "p1");
                Samples.PublishValveOpened(broker, "v1");

                EndpointHarness.WaitFor(() => otherHandler.Invocations == 1 && openedHandler.Invocations == 1, "the other subscriptions");
                EndpointHarness.Settle();
                Assert.AreEqual(0, deviceHandler.Invocations, "the device no longer receives it");
                Assert.AreEqual(1, otherHandler.Invocations, "another subscribed endpoint still does");
                Assert.AreEqual(1, openedHandler.Invocations, "the device's other subscription is unaffected");
            }
            finally
            {
                other.Stop();
                device.Stop();
            }
        }

        [TestMethod]
        public void Only_an_event_type_can_be_subscribed_to_at_runtime()
        {
            var harness = new EndpointHarness();
            harness.Start();
            try
            {
                var message = MessageOf(() => harness.Endpoint.Subscribe(typeof(OpenValve)));

                Assert.IsNotNull(message);
                Assert.IsTrue(message.StartsWith("ArgumentException:"), message);
                Assert.IsTrue(message.IndexOf("Contracts.OpenValve") >= 0, message);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_subscription_that_the_broker_rejects_fails_naming_the_event_and_is_not_kept()
        {
            var harness = new EndpointHarness();
            harness.Broker.RejectSubscriptionTo = "events/Contracts.PriceChanged";
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(PriceChanged), handler);
            harness.Configuration.Unsubscribe(typeof(PriceChanged));
            harness.Start();
            try
            {
                var message = MessageOf(() => harness.Endpoint.Subscribe(typeof(PriceChanged)));

                Assert.IsNotNull(message);
                Assert.IsTrue(message.IndexOf("Contracts.PriceChanged") >= 0, message);
                Assert.IsTrue(message.IndexOf("broker.test:1883") >= 0, message);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Subscriptions_made_at_runtime_survive_a_reconnect()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(PriceChanged), handler);
            harness.Configuration.Unsubscribe(typeof(PriceChanged));
            harness.Start();
            try
            {
                harness.Endpoint.Subscribe(typeof(PriceChanged));
                harness.Broker.DropConnectionAndExpireSession(harness.ClientId);
                EndpointHarness.WaitFor(() => harness.Connection.IsConnected && harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.PriceChanged"), "the endpoint to subscribe again");

                Samples.PublishPriceChanged(harness.Broker, "after");
                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the event published after the reconnect");
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- one copy

        [TestMethod]
        public void A_device_subscribed_to_a_type_and_its_base_processes_the_event_once()
        {
            var harness = new EndpointHarness();
            var openedHandler = new ActionHandler(null);
            var baseHandler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(ValveOpened), openedHandler);
            harness.Configuration.RegisterHandler(typeof(ValveEvent), baseHandler);
            harness.Start();
            try
            {
                Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.ValveOpened"));
                Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.ValveEvent"));

                Samples.PublishValveOpened(harness.Broker, "e1");

                EndpointHarness.WaitFor(() => openedHandler.Invocations >= 1, "the handler");
                EndpointHarness.Settle();
                Assert.AreEqual(1, openedHandler.Invocations, "the event is processed once");
                Assert.AreEqual(1, baseHandler.Invocations, "and the base handler is invoked once for it");
                Assert.IsTrue(harness.Log.Has(Microsoft.Extensions.Logging.LogLevel.Debug, "Dropped a copy of event 'e1'"), "the other copy was dropped");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Two_devices_subscribed_to_two_different_interfaces_each_process_the_event_once()
        {
            var broker = new FakeBroker();
            var a = new EndpointHarness("Device_A", broker);
            var b = new EndpointHarness("Device_B", broker);
            var handlerA = new ActionHandler(null);
            var handlerB = new ActionHandler(null);
            a.Configuration.RegisterHandler(typeof(ValveOpened), handlerA);
            a.Configuration.Unsubscribe(typeof(ValveOpened));
            a.Configuration.Subscribe(typeof(IAlarm));
            b.Configuration.RegisterHandler(typeof(ValveOpened), handlerB);
            b.Configuration.Unsubscribe(typeof(ValveOpened));
            b.Configuration.Subscribe(typeof(IAudited));
            a.Start();
            b.Start();
            try
            {
                Samples.PublishValveOpened(broker, "e1");

                EndpointHarness.WaitFor(() => handlerA.Invocations == 1 && handlerB.Invocations == 1, "both devices");
                EndpointHarness.Settle();
                Assert.AreEqual(1, handlerA.Invocations);
                Assert.AreEqual(1, handlerB.Invocations);
            }
            finally
            {
                b.Stop();
                a.Stop();
            }
        }

        [TestMethod]
        public void A_message_on_the_queue_with_enclosed_types_is_processed_as_it_is()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(ValveOpened), handler);
            harness.Start();
            try
            {
                harness.Deliver("e1", EndpointHarness.DotNetHeaders("e1", Samples.ValveOpenedTypes, "Publish"), EndpointHarness.Utf8(Samples.ValveOpenedBody));

                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the handler");
            }
            finally
            {
                harness.Stop();
            }
        }
    }
}
