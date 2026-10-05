using System;
using System.Collections;
using System.Threading;
using Contracts;
using Microsoft.Extensions.Logging;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>Immediate retries, forwarding to the error queue, and the messages that skip the retries or are not messages at all.</summary>
    [TestClass]
    public class DeviceEndpointRecoverabilityTests
    {
        static ActionHandler AlwaysThrowing()
        {
            return new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                throw new InvalidOperationException("boom");
            });
        }

        // ---- immediate retries

        [TestMethod]
        public void A_handler_that_succeeds_on_the_third_attempt_is_invoked_three_times_and_nothing_goes_to_the_error_queue()
        {
            var harness = new EndpointHarness();
            var handler = new CountingHandler(2);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => handler.Invocations == 3, "three attempts");
                EndpointHarness.Settle();

                Assert.AreEqual(3, handler.Invocations, "no fourth attempt");
                Assert.AreEqual(0, harness.Broker.PublishedCount("error"));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_header_change_in_one_attempt_does_not_reach_the_next()
        {
            var harness = new EndpointHarness();
            var attempts = 0;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                attempts++;
                context.MessageHeaders["x"] = "changed";
                context.MessageHeaders["added"] = "by attempt " + attempts;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("first attempt fails");
                }
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                var headers = EndpointHarness.DotNetHeaders("m1", Samples.OpenValveTypes, "Send");
                headers["x"] = "original";
                harness.Deliver("m1", headers, EndpointHarness.Utf8("{\"ValveId\":\"v\",\"Percent\":1}"));
                EndpointHarness.WaitFor(() => handler.Invocations == 2, "the second attempt");

                var seen = handler.HeadersSeen;
                Assert.AreEqual("original", (string)((Hashtable)seen[0])["x"]);
                Assert.AreEqual("original", (string)((Hashtable)seen[1])["x"], "the second attempt sees the original value");
                Assert.IsFalse(((Hashtable)seen[1]).Contains("added"), "and nothing the first attempt added");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Every_attempt_gets_a_message_deserialized_again()
        {
            var harness = new EndpointHarness();
            var messages = new ArrayList();
            var valveIdsOnEntry = new ArrayList();
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                messages.Add(message);
                valveIdsOnEntry.Add(((OpenValve)message).ValveId);
                ((OpenValve)message).ValveId = "changed by the handler";
                if (messages.Count == 1)
                {
                    throw new InvalidOperationException("first attempt fails");
                }
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => handler.Invocations == 2, "the second attempt");

                Assert.IsFalse(object.ReferenceEquals(messages[0], messages[1]), "a new instance");
                Assert.AreEqual("valve-m1", (string)valveIdsOnEntry[1], "and what the first attempt changed did not leak");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void With_retries_turned_off_the_handler_is_invoked_once_and_the_message_goes_to_the_error_queue()
        {
            var harness = new EndpointHarness();
            harness.Configuration.ImmediateRetries = 0;
            var handler = AlwaysThrowing();
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                Assert.AreEqual(1, handler.Invocations);
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- the error queue

        [TestMethod]
        public void An_always_failing_handler_is_invoked_six_times_and_the_message_arrives_in_the_error_queue_with_the_failure_headers()
        {
            var harness = new EndpointHarness("Device_01");
            var handler = AlwaysThrowing();
            harness.Configuration.RegisterHandler(typeof(AlwaysFails), handler);
            harness.Start();
            try
            {
                Samples.DeliverAlwaysFails(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                Assert.AreEqual(6, handler.Invocations, "one attempt and five immediate retries");

                var failed = Wire.First(harness, "error");
                Assert.AreEqual("m1", failed.Id, "the original message id");
                Assert.AreEqual("m1", Wire.Header(failed, HeaderNames.MessageId));
                Assert.AreEqual("{\"Reason\":\"because\"}", Wire.Text(failed.Body), "the original body");
                Assert.AreEqual(Samples.AlwaysFailsTypes, Wire.Header(failed, HeaderNames.EnclosedMessageTypes), "the original headers");
                Assert.AreEqual("conversation-of-m1", Wire.Header(failed, HeaderNames.ConversationId));

                Assert.AreEqual("Device_01", Wire.Header(failed, HeaderNames.FailedQ));
                Assert.AreEqual("Device_01", Wire.Header(failed, HeaderNames.ProcessingEndpoint));
                Assert.AreEqual(WireTimeText.FormatTime(EndpointHarness.Time), Wire.Header(failed, HeaderNames.TimeOfFailure));
                Assert.AreEqual("System.InvalidOperationException", Wire.Header(failed, HeaderNames.ExceptionType));
                Assert.AreEqual("boom", Wire.Header(failed, HeaderNames.ExceptionMessage));
                Assert.IsTrue(failed.Headers.Contains(HeaderNames.ExceptionStackTrace), "a stack trace header, even if the runtime has little to say");
                Assert.IsFalse(failed.Headers.Contains(HeaderNames.InnerExceptionType), "there is no inner exception");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void The_type_of_an_inner_exception_is_added_when_there_is_one()
        {
            var harness = new EndpointHarness();
            harness.Configuration.ImmediateRetries = 0;
            harness.Configuration.RegisterHandler(typeof(OpenValve), new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                throw new InvalidOperationException("outer", new ArgumentException("inner"));
            }));
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                var failed = Wire.First(harness, "error");
                Assert.AreEqual("System.ArgumentException", Wire.Header(failed, HeaderNames.InnerExceptionType));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_failed_message_goes_to_the_configured_error_queue()
        {
            var harness = new EndpointHarness();
            harness.Configuration.ImmediateRetries = 0;
            harness.Configuration.ErrorQueue = "plant_errors";
            harness.Configuration.RegisterHandler(typeof(OpenValve), AlwaysThrowing());
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("plant/errors") == 1, "the message in the error queue");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Processing_continues_with_the_next_message_after_one_was_forwarded()
        {
            var harness = new EndpointHarness();
            harness.Configuration.ImmediateRetries = 1;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                if (((OpenValve)message).ValveId == "valve-bad")
                {
                    throw new InvalidOperationException("bad");
                }
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "bad");
                Samples.DeliverOpenValve(harness, "good");
                EndpointHarness.WaitFor(() => handler.Invocations == 3, "the good message");

                Assert.AreEqual(1, harness.Broker.PublishedCount("error"));
                Assert.AreEqual("valve-good", ((OpenValve)handler.Messages[2]).ValveId);
            }
            finally
            {
                harness.Stop();
            }
        }

        // ---- forwarding is retried

        [TestMethod]
        public void A_message_that_cannot_be_forwarded_raises_a_critical_error_and_is_forwarded_again_with_a_back_off_that_doubles()
        {
            var harness = new EndpointHarness();
            harness.Configuration.ImmediateRetries = 0;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                if (((OpenValve)message).ValveId == "valve-bad")
                {
                    throw new InvalidOperationException("bad");
                }
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            // the error queue is unreachable for the first three tries
            harness.Connection.FailPublishes(3, "The broker is unreachable.");
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "bad");
                Samples.DeliverOpenValve(harness, "good");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");
                EndpointHarness.WaitFor(() => handler.Invocations == 2, "the message after it");

                Assert.AreEqual(3, harness.Criticals.Count, "a critical error for every failed try");
                var description = ((LogEntry)harness.Criticals[0]).Message;
                Assert.IsTrue(description.IndexOf("'bad'") >= 0, "it names the message id: " + description);
                Assert.IsTrue(description.IndexOf("'error'") >= 0, "it names the error queue: " + description);

                var seconds = harness.Delay.Seconds;
                Assert.AreEqual(3, seconds.Length);
                Assert.AreEqual(1, seconds[0]);
                Assert.AreEqual(2, seconds[1]);
                Assert.AreEqual(4, seconds[2]);
                Assert.AreEqual(1, harness.Broker.PublishedCount("error"), "forwarded once");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_message_is_forwarded_once_the_device_has_reconnected_after_the_broker_was_gone()
        {
            var harness = new EndpointHarness();
            harness.Configuration.ImmediateRetries = 0;
            var handler = new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                // the broker goes away while the message is being handled
                harness.Broker.DropConnection(harness.ClientId);
                throw new InvalidOperationException("the broker is gone");
            });
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                Samples.DeliverOpenValve(harness, "m1");

                EndpointHarness.WaitFor(() => harness.Criticals.Count >= 1, "a critical error");
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue once the broker is back");

                Assert.AreEqual("m1", Wire.First(harness, "error").Id);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void Stopping_during_the_outage_loses_the_message_and_logs_its_id()
        {
            var harness = new EndpointHarness();
            harness.Configuration.ImmediateRetries = 0;
            harness.Delay.WaitForInterrupt = true;
            harness.Configuration.RegisterHandler(typeof(OpenValve), AlwaysThrowing());
            harness.Connection.FailPublishes(1000, "The broker is unreachable.");
            harness.Start();

            Samples.DeliverOpenValve(harness, "m1");
            EndpointHarness.WaitFor(() => harness.Criticals.Count == 1, "the critical error");

            harness.Stop();

            Assert.IsTrue(harness.Log.Has(LogLevel.Error, "stopped before the message 'm1' could be forwarded"), "the loss is logged with the message id");
            Assert.AreEqual(0, harness.Broker.PublishedCount("error"));
        }

        // ---- messages that are not messages, and messages that cannot succeed

        [TestMethod]
        public void A_payload_that_is_not_a_message_is_logged_with_its_topic_and_discarded()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                harness.Broker.PublishAsForeignClient(harness.QueueTopic, EndpointHarness.Utf8("this is not a message"));
                Samples.DeliverOpenValve(harness, "m1");
                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the valid message");

                Assert.IsTrue(harness.Log.Has(LogLevel.Error, "topic 'Device/01'"), "an error naming the topic is logged");
                Assert.AreEqual(0, harness.Broker.PublishedCount("error"), "a payload that is not a message does not go to the error queue");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_message_of_an_unknown_type_goes_to_the_error_queue_after_a_single_attempt_naming_the_types()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                var headers = EndpointHarness.DotNetHeaders("m1", "Contracts.Unknown, Other, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "Send");
                harness.Deliver("m1", headers, EndpointHarness.Utf8("{}"));
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                Assert.AreEqual(0, handler.Invocations);
                var failed = Wire.First(harness, "error");
                Assert.AreEqual("NServiceBus.MessageDeserializationException", Wire.Header(failed, HeaderNames.ExceptionType));
                Assert.IsTrue(Wire.Header(failed, HeaderNames.ExceptionMessage).IndexOf("Contracts.Unknown") >= 0, "the reason names the enclosed types");
                Assert.AreEqual("{}", Wire.Text(failed.Body));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_message_without_enclosed_message_types_goes_to_the_error_queue_at_once()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                var headers = EndpointHarness.DotNetHeaders("m1", Samples.OpenValveTypes, "Send");
                headers.Remove(HeaderNames.EnclosedMessageTypes);
                harness.Deliver("m1", headers, EndpointHarness.Utf8("{}"));
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                Assert.AreEqual(0, handler.Invocations);
                var failed = Wire.First(harness, "error");
                Assert.AreEqual("NServiceBus.MessageDeserializationException", Wire.Header(failed, HeaderNames.ExceptionType));
                Assert.IsTrue(Wire.Header(failed, HeaderNames.ExceptionMessage).IndexOf(HeaderNames.EnclosedMessageTypes) >= 0);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_message_whose_content_type_is_not_json_goes_to_the_error_queue_at_once()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                var headers = EndpointHarness.DotNetHeaders("m1", Samples.OpenValveTypes, "Send");
                headers[HeaderNames.ContentType] = "application/xml";
                harness.Deliver("m1", headers, EndpointHarness.Utf8("<OpenValve />"));
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                Assert.AreEqual(0, handler.Invocations);
                var failed = Wire.First(harness, "error");
                Assert.IsTrue(Wire.Header(failed, HeaderNames.ExceptionMessage).IndexOf("application/xml") >= 0);
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_json_content_type_with_a_charset_is_json()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                var headers = EndpointHarness.DotNetHeaders("m1", Samples.OpenValveTypes, "Send");
                headers[HeaderNames.ContentType] = "application/json; charset=utf-8";
                harness.Deliver("m1", headers, EndpointHarness.Utf8("{\"ValveId\":\"v\",\"Percent\":1}"));
                EndpointHarness.WaitFor(() => handler.Invocations == 1, "the handler");
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_message_with_a_malformed_body_goes_to_the_error_queue_after_a_single_attempt()
        {
            var harness = new EndpointHarness();
            var handler = new ActionHandler(null);
            harness.Configuration.RegisterHandler(typeof(OpenValve), handler);
            harness.Start();
            try
            {
                harness.Deliver("m1", EndpointHarness.DotNetHeaders("m1", Samples.OpenValveTypes, "Send"), EndpointHarness.Utf8("{\"ValveId\": not json"));
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                Assert.AreEqual(0, handler.Invocations);
                var failed = Wire.First(harness, "error");
                Assert.AreEqual("NServiceBus.MessageDeserializationException", Wire.Header(failed, HeaderNames.ExceptionType));
                Assert.IsTrue(Wire.Header(failed, HeaderNames.ExceptionMessage).IndexOf("Contracts.OpenValve") >= 0, "the reason names the type");
            }
            finally
            {
                harness.Stop();
            }
        }
    }

    /// <summary>A handler that throws on its first attempts of every message, and then succeeds.</summary>
    internal sealed class CountingHandler : IHandleMessages
    {
        readonly int failuresPerMessage;
        readonly Hashtable attemptsByMessage = new Hashtable();
        int invocations;

        public CountingHandler(int failuresPerMessage)
        {
            this.failuresPerMessage = failuresPerMessage;
        }

        public int Invocations
        {
            get
            {
                lock (attemptsByMessage)
                {
                    return invocations;
                }
            }
        }

        public void Handle(object message, IMessageHandlerContext context)
        {
            int attempt;
            lock (attemptsByMessage)
            {
                invocations++;
                attempt = attemptsByMessage.Contains(context.MessageId) ? (int)attemptsByMessage[context.MessageId] + 1 : 1;
                attemptsByMessage[context.MessageId] = attempt;
            }

            if (attempt <= failuresPerMessage)
            {
                throw new InvalidOperationException("attempt " + attempt + " fails");
            }
        }
    }
}
