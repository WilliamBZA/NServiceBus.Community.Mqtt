using System;
using System.Collections;
using System.Threading;
using Contracts;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>Sending, routing, dispatch, type resolution, handler invocation, reply and batched dispatch, against the in-memory broker.</summary>
    [TestClass]
    public class DeviceEndpointMessagingTests
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

        static string Repeat(int count)
        {
            var text = new System.Text.StringBuilder();
            for (var i = 0; i < count; i++)
            {
                text.Append('x');
            }

            return text.ToString();
        }

        static SendOptions To(string destination)
        {
            var options = new SendOptions();
            options.Destination = destination;
            return options;
        }

        static ValveStatusRequest StatusRequest(string valveId)
        {
            var request = new ValveStatusRequest();
            request.ValveId = valveId;
            return request;
        }

        static ValveStatusResponse StatusResponse(string valveId)
        {
            var response = new ValveStatusResponse();
            response.ValveId = valveId;
            response.IsOpen = true;
            response.Percent = 50;
            return response;
        }

        static IHandleMessages SendingTo(string destination)
        {
            return new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Send(StatusRequest("v-1"), To(destination));
            });
        }

        static void DeliverWith(EndpointHarness harness, string id, string types, Hashtable extra)
        {
            var headers = EndpointHarness.DotNetHeaders(id, types, "Send");
            foreach (DictionaryEntry entry in extra)
            {
                headers[entry.Key] = entry.Value;
            }

            harness.Deliver(id, headers, EndpointHarness.Utf8("{\"ValveId\":\"valve-" + id + "\",\"Percent\":50}"));
        }

        // ---- standard headers

        [TestMethod]
        public void Sent_from_a_handler_carries_the_conversation_the_related_to_and_the_devices_reply_to_address()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.RegisterHandler(typeof(OpenValve), SendingTo("Plant"));
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 1, "the command the handler sent");

                var sent = Wire.First(harness, "Plant");
                Assert.AreEqual("id-1", sent.Id);
                Assert.AreEqual("id-1", Wire.Header(sent, HeaderNames.MessageId));
                Assert.AreEqual("Send", Wire.Header(sent, HeaderNames.MessageIntent));
                Assert.AreEqual("Contracts.ValveStatusRequest", Wire.Header(sent, HeaderNames.EnclosedMessageTypes));
                Assert.AreEqual("application/json", Wire.Header(sent, HeaderNames.ContentType));
                Assert.AreEqual("conversation-of-m1", Wire.Header(sent, HeaderNames.ConversationId));
                Assert.AreEqual("m1", Wire.Header(sent, HeaderNames.RelatedTo));
                Assert.AreEqual("correlation-of-m1", Wire.Header(sent, HeaderNames.CorrelationId));
                Assert.AreEqual("Device_01", Wire.Header(sent, HeaderNames.ReplyToAddress));
                Assert.AreEqual("Device_01", Wire.Header(sent, HeaderNames.OriginatingEndpoint));
                Assert.AreEqual(WireTimeText.FormatTime(EndpointHarness.Time), Wire.Header(sent, HeaderNames.TimeSent));
                Assert.AreEqual("{\"ValveId\":\"v-1\"}", Wire.Text(sent.Body));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_message_whose_incoming_message_has_no_correlation_id_is_correlated_to_the_incoming_message_id()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.RegisterHandler(typeof(OpenValve), SendingTo("Plant"));
            harness.Start();
            try
            {
                var headers = EndpointHarness.DotNetHeaders("m1", Samples.OpenValveTypes, "Send");
                headers.Remove(HeaderNames.CorrelationId);
                headers.Remove(HeaderNames.ConversationId);
                harness.Deliver("m1", headers, EndpointHarness.Utf8("{\"ValveId\":\"v\",\"Percent\":1}"));
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 1, "the command the handler sent");

                var sent = Wire.First(harness, "Plant");
                Assert.AreEqual("m1", Wire.Header(sent, HeaderNames.CorrelationId));
                Assert.AreEqual("id-2", Wire.Header(sent, HeaderNames.ConversationId), "a new conversation is started");
                Assert.AreEqual("m1", Wire.Header(sent, HeaderNames.RelatedTo));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Sent_outside_a_handler_starts_a_conversation_and_is_correlated_to_itself()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                harness.Endpoint.Send(new OpenValve(), To("Plant"));

                var sent = Wire.First(harness, "Plant");
                Assert.AreEqual("id-1", Wire.Header(sent, HeaderNames.MessageId));
                Assert.AreEqual("id-2", Wire.Header(sent, HeaderNames.ConversationId), "a new conversation");
                Assert.AreEqual("id-1", Wire.Header(sent, HeaderNames.CorrelationId), "its own message id");
                Assert.IsFalse(sent.Headers.Contains(HeaderNames.RelatedTo), "nothing is related");
                Assert.AreEqual("Device_01", Wire.Header(sent, HeaderNames.ReplyToAddress));
                Assert.AreEqual("Contracts.OpenValve", Wire.Header(sent, HeaderNames.EnclosedMessageTypes));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Headers_the_application_adds_are_sent_unchanged()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                var options = To("Plant");
                options.SetHeader("my.header", "a-😅 \"q\" é");
                options.SetHeader("other", "");
                harness.Endpoint.Send(new OpenValve(), options);

                var sent = Wire.First(harness, "Plant");
                Assert.AreEqual("a-😅 \"q\" é", Wire.Header(sent, "my.header"));
                Assert.AreEqual("", Wire.Header(sent, "other"));
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- routing

        [TestMethod]
        public void A_send_without_a_destination_goes_to_the_route_of_its_type()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.RouteToEndpoint(typeof(OpenValve), "Plant");
            harness.Start();
            try
            {
                harness.Endpoint.Send(new OpenValve());

                Assert.AreEqual(1, harness.Broker.PublishedCount("Plant"));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void An_explicit_destination_wins_over_the_route()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.RouteToEndpoint(typeof(OpenValve), "Plant");
            harness.Start();
            try
            {
                harness.Endpoint.Send(new OpenValve(), To("Plant_Backup"));

                Assert.AreEqual(1, harness.Broker.PublishedCount("Plant/Backup"));
                Assert.AreEqual(0, harness.Broker.PublishedCount("Plant"));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_send_with_no_route_fails_naming_the_type_and_publishes_nothing()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                var message = MessageOf(() => harness.Endpoint.Send(new OpenValve()));

                Assert.IsNotNull(message);
                Assert.IsTrue(message.IndexOf("Contracts.OpenValve") >= 0, message);
                Assert.AreEqual(0, harness.Connection.PublishAttempts);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_destination_is_validated_like_an_endpoint_name()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                var message = MessageOf(() => harness.Endpoint.Send(new OpenValve(), To("gate/#")));

                Assert.IsNotNull(message);
                Assert.IsTrue(message.StartsWith("ArgumentException:"), message);
                Assert.IsTrue(message.IndexOf("gate/#") >= 0, message);
                Assert.AreEqual(0, harness.Connection.PublishAttempts);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void An_event_cannot_be_sent_and_a_command_cannot_be_published()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                var sendEvent = MessageOf(() => harness.Endpoint.Send(new PriceChanged(), To("Plant")));
                var publishCommand = MessageOf(() => harness.Endpoint.Publish(new OpenValve()));

                Assert.IsNotNull(sendEvent);
                Assert.IsTrue(sendEvent.IndexOf("events must be published") >= 0, sendEvent);
                Assert.IsNotNull(publishCommand);
                Assert.IsTrue(publishCommand.IndexOf("Contracts.OpenValve") >= 0, publishCommand);
                Assert.AreEqual(0, harness.Connection.PublishAttempts);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_handler_cannot_reply_with_an_event_or_send_one()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.ImmediateRetries = 0;
            string reply = null;
            string send = null;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                reply = MessageOf(() => context.Reply(new PriceChanged()));
                send = MessageOf(() => context.Send(new PriceChanged(), To("Plant")));
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => handler.Invocations == 1 && send != null, "the handler to run");

                Assert.IsTrue(reply.IndexOf("events must be published") >= 0, reply);
                Assert.IsTrue(send.IndexOf("events must be published") >= 0, send);
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- dispatch

        [TestMethod]
        public void A_send_returns_after_the_broker_acknowledged_it()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                harness.Endpoint.Send(new OpenValve(), To("Plant"));

                Assert.AreEqual(1, harness.Connection.PublishAttempts);
                Assert.AreEqual(1, harness.Broker.PublishedCount("Plant"));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_device_that_is_not_connected_fails_a_send_at_once_naming_the_destination_the_host_and_the_port()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Delay.WaitForInterrupt = true;
            harness.Start();
            try
            {
                harness.Broker.DropConnection(harness.ClientId);
                EndpointHarness.WaitFor(() => harness.Delay.Requested.Count == 1, "the endpoint to notice the outage");

                var message = MessageOf(() => harness.Endpoint.Send(new OpenValve(), To("Plant")));

                Assert.IsNotNull(message);
                Assert.IsTrue(message.IndexOf("Plant") >= 0, message);
                Assert.IsTrue(message.IndexOf("broker.test:1883") >= 0, message);
                Assert.AreEqual(0, harness.Connection.PublishAttempts, "nothing was published, and nothing was buffered");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_broker_that_rejects_a_publish_or_does_not_answer_fails_the_send_naming_the_destination_the_host_and_the_port()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Start();
            try
            {
                harness.Connection.FailPublishes(1, "The broker answered 'Not authorized' (0x87).");
                var rejected = MessageOf(() => harness.Endpoint.Send(new OpenValve(), To("Plant")));

                harness.Connection.FailPublishes(1, "The broker did not acknowledge the publish within 10 s.");
                var timedOut = MessageOf(() => harness.Endpoint.Send(new OpenValve(), To("Plant")));

                Assert.IsNotNull(rejected);
                Assert.IsTrue(rejected.IndexOf("Plant") >= 0 && rejected.IndexOf("broker.test:1883") >= 0 && rejected.IndexOf("Not authorized") >= 0, rejected);
                Assert.IsNotNull(timedOut);
                Assert.IsTrue(timedOut.IndexOf("Plant") >= 0 && timedOut.IndexOf("broker.test:1883") >= 0 && timedOut.IndexOf("did not acknowledge") >= 0, timedOut);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_message_larger_than_the_maximum_packet_size_fails_naming_both_sizes_before_anything_is_published()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.MaximumPacketSize = 200;
            harness.Start();
            try
            {
                var request = StatusRequest(Repeat(400));
                var message = MessageOf(() => harness.Endpoint.Send(request, To("Plant")));

                Assert.IsNotNull(message);
                Assert.IsTrue(message.IndexOf("maximum packet size of 200 bytes") >= 0, message);
                Assert.IsTrue(message.IndexOf("would be an MQTT packet of") >= 0, message);
                Assert.AreEqual(0, harness.Connection.PublishAttempts);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_publish_whose_packet_is_too_large_publishes_none_of_its_copies()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.MaximumPacketSize = 700;
            harness.Start();
            try
            {
                var tooBig = new ValveOpened();
                tooBig.ValveId = Repeat(450);
                var message = MessageOf(() => harness.Endpoint.Publish(tooBig));

                Assert.IsNotNull(message);
                Assert.IsTrue(message.IndexOf("maximum packet size of 700 bytes") >= 0, message);
                Assert.AreEqual(0, harness.Connection.PublishAttempts, "not even the first copy was published");
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- batched dispatch from handlers

        [TestMethod]
        public void A_handler_that_sends_and_then_throws_has_its_message_dispatched_exactly_once_after_the_retry_succeeds()
        {
            var harness = new EndpointHarness("Device_01");
            var attempts = 0;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                attempts++;
                context.Send(StatusRequest("attempt-" + attempts), To("Plant"));
                if (attempts == 1)
                {
                    throw new InvalidOperationException("first attempt fails");
                }
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 1, "the dispatch");
                EndpointHarness.Settle();

                Assert.AreEqual(2, handler.Invocations);
                Assert.AreEqual(1, harness.Broker.PublishedCount("Plant"), "dispatched once");
                Assert.AreEqual("{\"ValveId\":\"attempt-2\"}", Wire.Text(Wire.First(harness, "Plant").Body), "the message of the attempt that succeeded");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Nothing_is_dispatched_when_every_attempt_throws()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.ImmediateRetries = 2;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Send(StatusRequest("v"), To("Plant"));
                throw new InvalidOperationException("always");
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                Assert.AreEqual(3, handler.Invocations);
                Assert.AreEqual(0, harness.Broker.PublishedCount("Plant"));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void The_messages_of_a_handler_are_dispatched_in_the_order_they_were_issued_after_every_handler_has_run()
        {
            var harness = new EndpointHarness("Device_01");
            var second = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Send(StatusRequest("from-second"), To("Plant"));
            });
            var first = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Send(StatusRequest("first-1"), To("Plant"));
                context.Publish(new PriceChanged());
                context.Send(StatusRequest("first-2"), To("Plant"));
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), first);
            harness.Configuration.RegisterHandler(typeof(OpenValve), second);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 3, "the dispatch");

                Assert.AreEqual("{\"ValveId\":\"first-1\"}", Wire.Text(Wire.At(harness, "Plant", 0).Body));
                Assert.AreEqual("{\"ValveId\":\"first-2\"}", Wire.Text(Wire.At(harness, "Plant", 1).Body));
                Assert.AreEqual("{\"ValveId\":\"from-second\"}", Wire.Text(Wire.At(harness, "Plant", 2).Body));

                // in the order they were issued across the whole batch: the publish came between the two sends
                // (the first entry is the message that was delivered to the device)
                var all = harness.Broker.Published;
                Assert.AreEqual("Plant", ((PublishedMessage)all[1]).Topic);
                Assert.AreEqual("events/Contracts.PriceChanged", ((PublishedMessage)all[2]).Topic);
                Assert.AreEqual("Plant", ((PublishedMessage)all[3]).Topic);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_dispatch_failure_counts_as_a_processing_failure_and_the_message_is_handled_again()
        {
            var harness = new EndpointHarness("Device_01");
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Send(StatusRequest("v"), To("Plant"));
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Connection.FailPublishes(1, "The broker answered 'Quota exceeded' (0x97).");
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 1, "the dispatch after the retry");
                EndpointHarness.Settle();

                Assert.AreEqual(2, handler.Invocations, "an immediate retry");
                Assert.AreEqual(0, harness.Broker.PublishedCount("error"));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Messages_that_were_dispatched_before_a_dispatch_failure_are_dispatched_again_by_the_retry()
        {
            var harness = new EndpointHarness("Device_01");
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Send(StatusRequest("one"), To("Plant"));
                context.Send(StatusRequest("two"), To("Plant"));
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            // the first message is dispatched, and the second is rejected
            harness.Connection.FailPublishesAfter(1, 1, "rejected");
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 3, "the retry to dispatch both");

                Assert.AreEqual(2, handler.Invocations);
                Assert.AreEqual("{\"ValveId\":\"one\"}", Wire.Text(Wire.At(harness, "Plant", 0).Body));
                Assert.AreEqual("{\"ValveId\":\"one\"}", Wire.Text(Wire.At(harness, "Plant", 1).Body), "the duplicate");
                Assert.AreEqual("{\"ValveId\":\"two\"}", Wire.Text(Wire.At(harness, "Plant", 2).Body));
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- type resolution and handler invocation

        [TestMethod]
        public void A_type_that_a_dotnet_endpoint_lists_with_its_assembly_is_resolved_by_its_name()
        {
            var harness = new EndpointHarness("Device_01");
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the handler");

                var message = (OpenValve)handler.Messages[0];
                Assert.AreEqual("valve-m1", message.ValveId);
                Assert.AreEqual(50, message.Percent);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_device_that_only_knows_the_base_type_gets_the_base_type_populated_from_the_body()
        {
            var harness = new EndpointHarness("Device_01");
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(ValveEvent), handler);
            harness.Start();
            try
            {
                harness.Deliver("e1", EndpointHarness.DotNetHeaders("e1", Samples.ValveOpenedTypes, "Publish"), EndpointHarness.Utf8(Samples.ValveOpenedBody));
                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the handler");

                var message = handler.Messages[0];
                Assert.AreEqual(typeof(ValveEvent), message.GetType());
                Assert.AreEqual("V-1", ((ValveEvent)message).ValveId);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_handler_for_a_type_and_a_handler_for_its_interface_are_each_invoked_once_in_registration_order()
        {
            var harness = new EndpointHarness("Device_01");
            var order = new ArrayList();
            var forInterface = new ActionHandler(delegate(object message, IMessageHandlerContext context) { order.Add("IAlarm"); });
            var forType = new ActionHandler(delegate(object message, IMessageHandlerContext context) { order.Add("ValveOpened"); });
            harness.Configuration.RegisterHandler(typeof(ValveOpened), forType);
            harness.Configuration.RegisterHandler(typeof(IAlarm), forInterface);
            harness.Start();
            try
            {
                harness.Deliver("e1", EndpointHarness.DotNetHeaders("e1", Samples.ValveOpenedTypes, "Publish"), EndpointHarness.Utf8(Samples.ValveOpenedBody));
                EndpointHarness.WaitFor(() => forType.Invocations == 1 && forInterface.Invocations == 1, "both handlers");
                EndpointHarness.Settle();

                Assert.AreEqual(1, forType.Invocations);
                Assert.AreEqual(1, forInterface.Invocations);
                Assert.AreEqual("ValveOpened", (string)order[0]);
                Assert.AreEqual("IAlarm", (string)order[1]);
                Assert.AreEqual(typeof(ValveOpened), forInterface.Messages[0].GetType(), "the interface handler gets the message that was received");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void The_handler_context_exposes_the_message_id_the_reply_to_address_and_the_headers()
        {
            var harness = new EndpointHarness("Device_01");
            string messageId = null;
            string replyTo = null;
            string custom = null;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                messageId = context.MessageId;
                replyTo = context.ReplyToAddress;
                custom = (string)context.MessageHeaders["custom"];
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                var extra = new Hashtable();
                extra["custom"] = "value";
                DeliverWith(harness, "m1", Samples.OpenValveTypes, extra);
                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the handler");

                Assert.AreEqual("m1", messageId);
                Assert.AreEqual("Plant", replyTo);
                Assert.AreEqual("value", custom);
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- reply

        [TestMethod]
        public void A_reply_goes_to_the_reply_to_address_with_intent_reply_and_the_correlation_headers()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.RegisterHandler(typeof(OpenValve), new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Reply(StatusResponse("v-1"));
            }));
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 1, "the reply");

                var reply = Wire.First(harness, "Plant");
                Assert.AreEqual("Reply", Wire.Header(reply, HeaderNames.MessageIntent));
                Assert.AreEqual("m1", Wire.Header(reply, HeaderNames.RelatedTo));
                Assert.AreEqual("conversation-of-m1", Wire.Header(reply, HeaderNames.ConversationId));
                Assert.AreEqual("correlation-of-m1", Wire.Header(reply, HeaderNames.CorrelationId));
                Assert.AreEqual("Contracts.ValveStatusResponse", Wire.Header(reply, HeaderNames.EnclosedMessageTypes));
                Assert.AreEqual("Device_01", Wire.Header(reply, HeaderNames.ReplyToAddress));
                Assert.IsFalse(reply.Headers.Contains(HeaderNames.SagaId));
                Assert.IsFalse(reply.Headers.Contains(HeaderNames.SagaType));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_reply_to_a_saga_carries_the_saga_id_and_type()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.RegisterHandler(typeof(OpenValve), new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Reply(StatusResponse("v-1"));
            }));
            harness.Start();
            try
            {
                var extra = new Hashtable();
                extra[HeaderNames.OriginatingSagaId] = "saga-1";
                extra[HeaderNames.OriginatingSagaType] = "Plant.ValveSaga, Plant, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
                DeliverWith(harness, "m1", Samples.OpenValveTypes, extra);
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 1, "the reply");

                var reply = Wire.First(harness, "Plant");
                Assert.AreEqual("saga-1", Wire.Header(reply, HeaderNames.SagaId));
                Assert.AreEqual("Plant.ValveSaga, Plant, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", Wire.Header(reply, HeaderNames.SagaType));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_reply_to_a_message_without_a_reply_to_address_fails_the_handler_and_goes_through_recoverability()
        {
            var harness = new EndpointHarness("Device_01");
            harness.Configuration.ImmediateRetries = 1;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Reply(StatusResponse("v-1"));
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                var headers = EndpointHarness.DotNetHeaders("m1", Samples.OpenValveTypes, "Send");
                headers.Remove(HeaderNames.ReplyToAddress);
                harness.Deliver("m1", headers, EndpointHarness.Utf8("{\"ValveId\":\"v\",\"Percent\":1}"));
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                Assert.AreEqual(2, handler.Invocations);
                var failed = Wire.First(harness, "error");
                Assert.IsTrue(Wire.Header(failed, HeaderNames.ExceptionMessage).IndexOf(HeaderNames.ReplyToAddress) >= 0, "the reason names the missing header");
            }
            finally
            {
                harness.Stop();
            }
        }
    }
}
