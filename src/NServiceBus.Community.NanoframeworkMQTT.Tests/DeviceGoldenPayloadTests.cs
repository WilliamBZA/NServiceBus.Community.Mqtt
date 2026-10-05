using System;
using System.Collections;
using System.Text;
using Contracts;
using Interop;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>
    /// The device-origin golden payloads in <c>src/Interop/WirePayloads.cs</c>: what the endpoint itself writes when it sends a command, replies,
    /// publishes an event and forwards a failed message, with the clock and the ids fixed. The .NET unit tests decode the very same text.
    /// </summary>
    [TestClass]
    public class DeviceGoldenPayloadTests
    {
        static string Json(PublishedMessage message)
        {
            return Encoding.UTF8.GetString(message.Payload, 0, message.Payload.Length);
        }

        static EndpointHarness NewHarness()
        {
            // the golden payloads were written by an endpoint with this name, and the harness gives it the fixed clock and the ids id-1, id-2 and so on
            return new EndpointHarness("Device_01");
        }

        [TestMethod]
        public void A_command_sent_outside_a_handler_is_the_golden_device_command()
        {
            var harness = NewHarness();
            harness.Start();
            try
            {
                var command = WirePayloads.CreateOpenValve();
                var options = new SendOptions();
                options.Destination = "Plant";
                harness.Endpoint.Send(command, options);

                var sent = (PublishedMessage)harness.Broker.PublishedTo("Plant")[0];
                Assert.AreEqual(WirePayloads.DeviceCommand.Json, Json(sent));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_reply_to_a_saga_request_is_the_golden_device_reply()
        {
            var harness = NewHarness();
            harness.Configuration.RegisterHandler(typeof(ValveStatusRequest), new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                context.Reply(WirePayloads.CreateValveStatusResponse());
            }));
            harness.Start();
            try
            {
                var id = "a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d";
                var headers = EndpointHarness.DotNetHeaders(id, "Contracts.ValveStatusRequest, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "Send");
                headers[HeaderNames.ConversationId] = "7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10";
                headers[HeaderNames.CorrelationId] = id;
                headers[HeaderNames.OriginatingSagaId] = "3f2e1d0c-9b8a-4796-8574-635241f0e1d2";
                headers[HeaderNames.OriginatingSagaType] = "Plant.ValveSaga, Plant, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
                harness.Deliver(id, headers, EndpointHarness.Utf8("{\"ValveId\":\"V-1\"}"));
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("Plant") == 1, "the reply");

                var sent = (PublishedMessage)harness.Broker.PublishedTo("Plant")[0];
                Assert.AreEqual(WirePayloads.DeviceReply.Json, Json(sent));
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_published_event_is_the_golden_device_event_in_every_copy()
        {
            var harness = NewHarness();
            harness.Start();
            try
            {
                harness.Endpoint.Publish(WirePayloads.CreateValveOpened());

                var topics = new string[] { "events/Contracts.ValveOpened", "events/Contracts.ValveEvent", "events/Contracts.IAlarm", "events/Contracts.IAudited" };
                for (var i = 0; i < topics.Length; i++)
                {
                    var copy = (PublishedMessage)harness.Broker.PublishedTo(topics[i])[0];
                    Assert.AreEqual(WirePayloads.DeviceEvent.Json, Json(copy), topics[i]);
                }
            }
            finally
            {
                harness.Stop();
            }
        }

        [TestMethod]
        public void A_failed_message_is_the_golden_device_failed_message()
        {
            var harness = NewHarness();
            harness.Configuration.RegisterHandler(typeof(OpenValve), new ActionHandler(delegate(object message, IMessageHandlerContext context)
            {
                throw new InvalidOperationException("The valve is stuck.");
            }));
            harness.Start();
            try
            {
                // the very message the .NET transport wrote for the command golden
                harness.Broker.PublishAsForeignClient(harness.QueueTopic, Encoding.UTF8.GetBytes(WirePayloads.Command.Json));
                EndpointHarness.WaitFor(() => harness.Broker.PublishedCount("error") == 1, "the message in the error queue");

                // the stack trace is the runtime's own text, so the test pins it to the golden's before it compares everything else byte for byte
                var failed = WireFormat.Decode(((PublishedMessage)harness.Broker.PublishedTo("error")[0]).Payload);
                Assert.IsTrue(failed.Headers.Contains(HeaderNames.ExceptionStackTrace));
                failed.Headers[HeaderNames.ExceptionStackTrace] = WirePayloads.DeviceFailedStackTrace;
                var payload = WireFormat.Encode(failed.Id, failed.Headers, failed.Body);

                Assert.AreEqual(WirePayloads.DeviceFailedMessage.Json, Encoding.UTF8.GetString(payload, 0, payload.Length));
            }
            finally
            {
                harness.Stop();
            }
        }
    }
}
