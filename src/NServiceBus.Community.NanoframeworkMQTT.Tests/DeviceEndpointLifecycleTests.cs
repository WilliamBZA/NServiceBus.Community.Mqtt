using System;
using System.Collections;
using System.Threading;
using Contracts;
using Microsoft.Extensions.Logging;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>Starting, stopping, reconnecting and takeover of a device endpoint, against the in-memory broker.</summary>
    [TestClass]
    public class DeviceEndpointLifecycleTests
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

        // ---- start

        [TestMethod]
        public void Start_connects_with_the_session_of_the_queue_and_subscribes_to_it()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.UseCredentials("user", "secret");
            harness.Configuration.SessionExpiry = TimeSpan.FromMinutes(5);
            harness.Configuration.MaximumPacketSize = 2048;

            harness.Start();
            try
            {
                var options = harness.Connection.LastOptions;
                Assert.AreEqual("nsb.Device/01", options.ClientId);
                Assert.AreEqual("broker.test", options.Host);
                Assert.AreEqual(1883, options.Port);
                Assert.AreEqual("user", options.Username);
                Assert.AreEqual("secret", options.Password);
                Assert.AreEqual((uint)300, options.SessionExpirySeconds);
                Assert.AreEqual(2048, options.MaximumPacketSize);
                Assert.AreEqual(60, (int)options.KeepAliveSeconds);

                Assert.IsTrue(harness.Broker.IsSubscribed("nsb.Device/01", "Device/01"), "subscribed to the queue topic");
                Assert.IsTrue(harness.Broker.IsOnline("nsb.Device/01"));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_session_expiry_with_a_fraction_is_rounded_up_to_a_second()
        {
            var harness = new EndpointHarness();
            harness.Configuration.SessionExpiry = TimeSpan.FromMilliseconds(1500);

            harness.Start();
            try
            {
                Assert.AreEqual((uint)2, harness.Connection.LastOptions.SessionExpirySeconds);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Start_subscribes_to_the_handled_events_and_the_explicit_subscriptions()
        {
            var harness = new EndpointHarness();
            harness.Configuration.RegisterHandler(typeof(PriceChanged), new ActionHandler(null));
            harness.Configuration.RegisterHandler(typeof(OpenValve), new ActionHandler(null));
            harness.Configuration.Subscribe(typeof(ValveOpened));

            harness.Start();
            try
            {
                var topics = (string[])harness.Connection.SubscribeCalls[0];
                Assert.AreEqual(3, topics.Length);
                Assert.AreEqual("Device/01", topics[0]);
                Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.ValveOpened"), "explicit");
                Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.PriceChanged"), "handled event");
                Assert.IsFalse(harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.OpenValve"), "a command is not an event");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void The_backlog_of_the_session_is_handled_after_start()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);

            // the device runs once, so that the broker has its session, and is then stopped
            harness.Start();
            harness.Stop();
            Samples.DeliverOpenValve(harness, "m1");
            Samples.DeliverOpenValve(harness, "m2");
            Assert.AreEqual(0, handler.Invocations);

            harness.Start();
            try
            {
                EndpointHarness.WaitFor(() => handler.Invocations == 2, "the backlog to be handled");
                Assert.AreEqual("valve-m1", ((OpenValve)handler.Messages[0]).ValveId, "in the order the broker delivered");
                Assert.AreEqual("valve-m2", ((OpenValve)handler.Messages[1]).ValveId);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void An_unreachable_broker_fails_start_naming_the_host_and_the_port()
        {
            var harness = new EndpointHarness();
            harness.Broker.Reachable = false;

            var message = MessageOf(() => harness.Start());

            Assert.IsNotNull(message);
            Assert.IsTrue(message.StartsWith("InvalidOperationException:"), message);
            Assert.IsTrue(message.IndexOf("broker.test:1883") >= 0, message);
            Assert.IsFalse(harness.Connection.IsConnected);
            Assert.AreEqual(0, harness.Connection.SubscribeCalls.Count, "nothing was subscribed");
            Assert.IsFalse(harness.Broker.IsOnline(harness.ClientId));
        }

        [TestMethod]
        public void Rejected_credentials_fail_start_with_the_host_the_port_and_the_reason()
        {
            var harness = new EndpointHarness();
            harness.Broker.RefuseConnectWith = "Bad user name or password";

            var message = MessageOf(() => harness.Start());

            Assert.IsNotNull(message);
            Assert.IsTrue(message.IndexOf("broker.test:1883") >= 0, message);
            Assert.IsTrue(message.IndexOf("Bad user name or password") >= 0, message);
        }

        [TestMethod]
        public void A_rejected_subscription_fails_start_and_leaves_no_connection()
        {
            var harness = new EndpointHarness();
            harness.Broker.RejectSubscriptionTo = "Device/01";

            var message = MessageOf(() => harness.Start());

            Assert.IsNotNull(message);
            Assert.IsTrue(message.IndexOf("broker.test:1883") >= 0, message);
            Assert.IsTrue(message.IndexOf("Device/01") >= 0, "it names the topic: " + message);
            Assert.IsFalse(harness.Connection.IsConnected, "the connection was closed");
            Assert.AreEqual(1, harness.Connection.Disconnects);
        }

        [TestMethod]
        public void A_failed_start_can_be_tried_again()
        {
            var harness = new EndpointHarness();
            harness.Broker.Reachable = false;
            MessageOf(() => harness.Start());

            harness.Broker.Reachable = true;
            harness.Start();
            try
            {
                Assert.IsTrue(harness.Connection.IsConnected);
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- one message at a time, and stop

        [TestMethod]
        public void Messages_are_handled_one_at_a_time_in_order()
        {
            var harness = new EndpointHarness();
            var release = new ManualResetEvent(false);
            var running = new ManualResetEvent(false);
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                if (((OpenValve)message).ValveId == "valve-m1")
                {
                    running.Set();
                    release.WaitOne();
                }
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);

            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                Samples.DeliverOpenValve(harness, "m2");
                Assert.IsTrue(running.WaitOne(5000, false), "the first handler runs");

                EndpointHarness.Settle();
                Assert.AreEqual(1, handler.Invocations, "the second message waits for the first");

                release.Set();
                EndpointHarness.WaitFor(() => handler.Invocations == 2, "the second message");
                Assert.AreEqual("valve-m1", ((OpenValve)handler.Messages[0]).ValveId);
                Assert.AreEqual("valve-m2", ((OpenValve)handler.Messages[1]).ValveId);
            }
            finally
            {
                release.Set();
                harness.Stop();
            }
        }

        sealed class StopRunner
        {
            readonly DeviceEndpoint endpoint;
            public bool Returned;

            public StopRunner(DeviceEndpoint endpoint)
            {
                this.endpoint = endpoint;
            }

            public void Run()
            {
                endpoint.Stop();
                Returned = true;
            }
        }

        [TestMethod]
        public void Stop_returns_only_after_the_handler_has_returned_and_drains_the_intake()
        {
            var harness = new EndpointHarness();
            var release = new ManualResetEvent(false);
            var running = new ManualResetEvent(false);
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                if (((OpenValve)message).ValveId == "valve-m1")
                {
                    running.Set();
                    release.WaitOne();
                }
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);

            harness.Start();
            Samples.DeliverOpenValve(harness, "m1");
            Samples.DeliverOpenValve(harness, "m2");
            Samples.DeliverOpenValve(harness, "m3");
            Assert.IsTrue(running.WaitOne(5000, false));

            var runner = new StopRunner(harness.Endpoint);
            var thread = new Thread(runner.Run);
            thread.Start();
            try
            {
                EndpointHarness.Settle();
                Assert.IsFalse(runner.Returned, "stop waits for the handler");

                release.Set();
                EndpointHarness.WaitFor(() => runner.Returned, "stop to return");
                Assert.AreEqual(3, handler.Invocations, "the messages that had arrived were processed before stop returned");
            }
            finally
            {
                release.Set();
                thread.Join();
            }
        }

        [TestMethod]
        public void Stop_stops_draining_at_the_timeout_and_logs_how_many_messages_are_lost()
        {
            var harness = new EndpointHarness();
            harness.Configuration.StopDrainTimeout = TimeSpan.Zero;
            var release = new ManualResetEvent(false);
            var running = new ManualResetEvent(false);
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                running.Set();
                release.WaitOne();
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);

            harness.Start();
            Samples.DeliverOpenValve(harness, "m1");
            Samples.DeliverOpenValve(harness, "m2");
            Samples.DeliverOpenValve(harness, "m3");
            Assert.IsTrue(running.WaitOne(5000, false));

            var runner = new StopRunner(harness.Endpoint);
            var thread = new Thread(runner.Run);
            thread.Start();
            EndpointHarness.Settle();
            release.Set();
            EndpointHarness.WaitFor(() => runner.Returned, "stop to return");
            thread.Join();

            Assert.AreEqual(1, handler.Invocations, "only the message in progress finished");
            Assert.IsTrue(harness.Log.Has(LogLevel.Warning, "2 received message(s) that were not processed"), "the log says how many were left");
        }

        [TestMethod]
        public void Stop_keeps_the_session_so_that_a_message_sent_afterwards_waits_for_the_device()
        {
            var harness = new EndpointHarness();
            harness.Configuration.RegisterHandler(typeof(OpenValve), new ActionHandler(null));
            harness.Start();
            harness.Stop();

            Assert.AreEqual(1, harness.Connection.Disconnects, "a normal disconnect");
            Assert.IsTrue(harness.Broker.HasSession(harness.ClientId), "the session is still there");
            Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "Device/01"));
            Assert.IsFalse(harness.Broker.IsOnline(harness.ClientId));

            Samples.DeliverOpenValve(harness, "m1");
            Assert.AreEqual(1, harness.Broker.WaitingFor(harness.ClientId));
        }

        [TestMethod]
        public void A_second_stop_does_nothing()
        {
            var harness = new EndpointHarness();
            harness.Start();

            harness.Stop();
            harness.Stop();

            Assert.AreEqual(1, harness.Connection.Disconnects);
        }

        [TestMethod]
        public void Operations_after_stop_fail_and_publish_nothing()
        {
            var harness = new EndpointHarness();
            harness.Start();
            harness.Stop();
            var options = new SendOptions();
            options.Destination = "Plant";

            Assert.ThrowsException(typeof(InvalidOperationException), () => harness.Endpoint.Send(new OpenValve(), options));
            Assert.ThrowsException(typeof(InvalidOperationException), () => harness.Endpoint.Publish(new PriceChanged()));
            Assert.ThrowsException(typeof(InvalidOperationException), () => harness.Endpoint.Subscribe(typeof(PriceChanged)));
            Assert.ThrowsException(typeof(InvalidOperationException), () => harness.Endpoint.Unsubscribe(typeof(PriceChanged)));

            Assert.AreEqual(0, harness.Broker.PublishedCount("Plant"));
            Assert.AreEqual(0, harness.Connection.PublishAttempts);
        }

        [TestMethod]
        public void Stop_cannot_be_called_from_a_handler()
        {
            var harness = new EndpointHarness();
            string message = null;
            var done = new ManualResetEvent(false);
            var handler = new ActionHandler(delegate(object m, IMessageHandlerContext context)
            {
                message = MessageOf(() => harness.Endpoint.Stop());
                done.Set();
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");

                Assert.IsTrue(done.WaitOne(5000, false));
                Assert.IsNotNull(message);
                Assert.IsTrue(message.StartsWith("InvalidOperationException:"), message);
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- reconnect

        [TestMethod]
        public void A_lost_connection_is_restored_with_a_back_off_that_doubles_to_30_seconds()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                harness.Connection.FailConnects(7, "The broker is down");
                harness.Broker.DropConnection(harness.ClientId);

                EndpointHarness.WaitFor(() => harness.Connection.IsConnected, "the endpoint to reconnect");

                var seconds = harness.Delay.Seconds;
                Assert.AreEqual(8, seconds.Length, "seven failed attempts and one that works");
                var expected = new int[] { 1, 2, 4, 8, 16, 30, 30, 30 };
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.AreEqual(expected[i], seconds[i], "delay " + (i + 1));
                }

                Assert.AreEqual(7, harness.Log.Find(LogLevel.Error, "to reconnect to the MQTT broker at broker.test:1883 failed").Count, "every failed attempt is logged");

                // and the endpoint works again
                Samples.DeliverOpenValve(harness, "after");
                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the message after the reconnect");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void The_back_off_starts_again_at_one_second_after_a_successful_reconnect()
        {
            var harness = new EndpointHarness();
            harness.Start();
            try
            {
                harness.Connection.FailConnects(2, "down");
                harness.Broker.DropConnection(harness.ClientId);
                EndpointHarness.WaitFor(() => harness.Connection.IsConnected, "the first reconnect");

                harness.Broker.DropConnection(harness.ClientId);
                EndpointHarness.WaitFor(() => harness.Delay.Seconds.Length == 4 && harness.Connection.IsConnected, "the second reconnect");

                var seconds = harness.Delay.Seconds;
                Assert.AreEqual(1, seconds[0]);
                Assert.AreEqual(2, seconds[1]);
                Assert.AreEqual(4, seconds[2], "the third attempt of the first outage");
                Assert.AreEqual(1, seconds[3], "the next outage starts again at one second");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void After_a_reconnect_every_subscription_is_applied_again_and_events_are_delivered()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(PriceChanged), handler);
            harness.Start();
            try
            {
                harness.Endpoint.Subscribe(typeof(ValveOpened));

                harness.Broker.DropConnectionAndExpireSession(harness.ClientId);
                // one subscribe at start, one at runtime, and one when the connection is restored
                EndpointHarness.WaitFor(() => harness.Connection.SubscribeCalls.Count == 3, "the endpoint to subscribe again");

                var last = (string[])harness.Connection.SubscribeCalls[2];
                Assert.AreEqual(3, last.Length, "the queue and the two events");
                Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "Device/01"));
                Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.PriceChanged"));
                Assert.IsTrue(harness.Broker.IsSubscribed(harness.ClientId, "events/Contracts.ValveOpened"));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Stop_during_an_outage_ends_the_reconnecting()
        {
            var harness = new EndpointHarness();
            harness.Delay.WaitForInterrupt = true;
            harness.Start();

            harness.Broker.DropConnection(harness.ClientId);
            EndpointHarness.WaitFor(() => harness.Delay.Requested.Count == 1, "the endpoint to wait before it reconnects");

            harness.Stop();

            Assert.AreEqual(1, harness.Connection.Connects, "it never reconnected");
        }

        // ---- takeover

        static void TakeOver(EndpointHarness harness, FakeMqttConnection other)
        {
            var options = new MqttConnectOptions();
            options.ClientId = harness.ClientId;
            options.Host = "broker.test";
            options.Port = 1883;
            options.MaximumPacketSize = 16384;
            options.OperationTimeout = TimeSpan.FromSeconds(1);
            other.Connect(options);
            other.Subscribe(new string[] { harness.QueueTopic });
        }

        [TestMethod]
        public void A_second_device_with_the_same_name_displaces_the_first_which_raises_a_critical_error()
        {
            var harness = new EndpointHarness("Gate_01");
            harness.Start();
            try
            {
                var other = new FakeMqttConnection(harness.Broker);
                TakeOver(harness, other);

                EndpointHarness.WaitFor(() => harness.Criticals.Count == 1, "the critical error");
                var description = ((LogEntry)harness.Criticals[0]).Message;
                Assert.IsTrue(description.IndexOf("Gate_01") >= 0, description);
                Assert.IsTrue(description.IndexOf("Only one consumer per queue is supported") >= 0, description);
                Assert.IsTrue(description.IndexOf("every device needs its own endpoint name") >= 0, description);

                // the second device receives the messages sent to Gate_01 from then on
                var received = new ArrayList();
                other.MessageReceived += delegate(string topic, byte[] payload) { received.Add(topic); };
                Samples.DeliverOpenValve(harness, "m1");
                Assert.AreEqual(1, received.Count);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_displaced_device_does_not_reconnect_and_leaves_the_other_session_alone()
        {
            var harness = new EndpointHarness("Gate_01");
            harness.Start();
            try
            {
                var other = new FakeMqttConnection(harness.Broker);
                TakeOver(harness, other);
                EndpointHarness.WaitFor(() => harness.Criticals.Count == 1, "the critical error");

                EndpointHarness.Settle();

                Assert.AreEqual(1, harness.Connection.Connects, "no attempt to reconnect");
                Assert.AreEqual(0, harness.Delay.Requested.Count, "no back-off was started");
                Assert.IsTrue(other.IsConnected, "the other client's session was not interrupted");
                Assert.AreEqual(0, other.LostConnections.Count);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_displaced_device_fails_sends_and_publishes_saying_it_was_displaced()
        {
            var harness = new EndpointHarness("Gate_01");
            harness.Start();
            try
            {
                var other = new FakeMqttConnection(harness.Broker);
                TakeOver(harness, other);
                EndpointHarness.WaitFor(() => harness.Criticals.Count == 1, "the critical error");
                var options = new SendOptions();
                options.Destination = "Plant";

                var sendMessage = MessageOf(() => harness.Endpoint.Send(new OpenValve(), options));
                var publishMessage = MessageOf(() => harness.Endpoint.Publish(new PriceChanged()));
                var subscribeMessage = MessageOf(() => harness.Endpoint.Subscribe(typeof(PriceChanged)));

                Assert.IsTrue(sendMessage.IndexOf("displaced") >= 0, sendMessage);
                Assert.IsTrue(publishMessage.IndexOf("displaced") >= 0, publishMessage);
                Assert.IsTrue(subscribeMessage.IndexOf("displaced") >= 0, subscribeMessage);
                Assert.AreEqual(0, harness.Connection.PublishAttempts, "nothing was published");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_takeover_drops_the_messages_that_have_arrived_and_logs_how_many()
        {
            var harness = new EndpointHarness("Gate_01");
            var release = new ManualResetEvent(false);
            var running = new ManualResetEvent(false);
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                running.Set();
                release.WaitOne();
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                Assert.IsTrue(running.WaitOne(5000, false));
                Samples.DeliverOpenValve(harness, "m2");
                Samples.DeliverOpenValve(harness, "m3");

                var other = new FakeMqttConnection(harness.Broker);
                TakeOver(harness, other);
                EndpointHarness.WaitFor(() => harness.Criticals.Count == 1, "the critical error");

                Assert.IsTrue(harness.Log.Has(LogLevel.Error, "dropped 2 received message(s)"), "the dropped messages are counted");

                release.Set();
                EndpointHarness.Settle();
                Assert.AreEqual(1, handler.Invocations, "the dropped messages are not handled");
            }
            finally
            {
                release.Set();
                harness.Stop();
            }
        }

        // ---- critical errors

        [TestMethod]
        public void Without_a_callback_a_critical_error_is_logged_at_error_level()
        {
            var harness = new EndpointHarness("Gate_01");
            harness.Configuration.OnCriticalError = null;
            harness.Start();
            try
            {
                TakeOver(harness, new FakeMqttConnection(harness.Broker));

                EndpointHarness.WaitFor(() => harness.Log.Has(LogLevel.Error, "Critical error: Another client took over the session of the endpoint 'Gate_01'"), "the error log");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_failure_of_the_processing_loop_is_a_critical_error_and_the_endpoint_keeps_running()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                if (((OpenValve)message).ValveId == "valve-bad")
                {
                    throw new InvalidOperationException("this one fails");
                }
            });
            harness.Configuration.ImmediateRetries = 0;
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                // the failed message is forwarded to the error queue, which stamps the time of the failure, and the clock fails
                harness.Clock.FailNext = true;
                Samples.DeliverOpenValve(harness, "bad");

                EndpointHarness.WaitFor(() => harness.Criticals.Count == 1, "the critical error");
                var description = ((LogEntry)harness.Criticals[0]).Message;
                Assert.IsTrue(description.IndexOf("processing loop") >= 0, description);
                Assert.IsNotNull(((LogEntry)harness.Criticals[0]).Exception, "the exception is passed on");

                Samples.DeliverOpenValve(harness, "good");
                EndpointHarness.WaitFor(() => handler.Invocations == 2, "the next message");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_critical_error_callback_that_throws_does_not_stop_the_endpoint()
        {
            var harness = new EndpointHarness("Gate_01");
            harness.Configuration.OnCriticalError = delegate(string description, Exception exception) { throw new InvalidOperationException("callback failed"); };
            harness.Start();
            try
            {
                TakeOver(harness, new FakeMqttConnection(harness.Broker));

                EndpointHarness.WaitFor(() => harness.Log.Has(LogLevel.Error, "The critical error callback threw"), "the callback failure to be logged");
            }
            finally
            {
                harness.Stop();
            }
        }
    }
}
