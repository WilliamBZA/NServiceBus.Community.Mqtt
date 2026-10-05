using System;
using Contracts;
using nanoFramework.TestFramework;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    [TestClass]
    public class DeviceEndpointConfigurationTests
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

        static void AssertRejects(Check action, string expectedType, string expectedText)
        {
            var message = MessageOf(action);
            Assert.IsNotNull(message, "an exception was expected");
            Assert.IsTrue(message.StartsWith(expectedType + ":"), "expected " + expectedType + " but got " + message);
            Assert.IsTrue(message.IndexOf(expectedText) >= 0, "'" + message + "' should contain '" + expectedText + "'");
        }

        [TestMethod]
        public void The_defaults_are_the_documented_ones()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            Assert.AreEqual(1883, configuration.Port);
            Assert.AreEqual(TimeSpan.FromDays(7).Ticks, configuration.SessionExpiry.Ticks);
            Assert.AreEqual(5, configuration.ImmediateRetries);
            Assert.AreEqual("error", configuration.ErrorQueue);
            Assert.AreEqual(16384, configuration.MaximumPacketSize);
            Assert.AreEqual(TimeSpan.FromSeconds(10).Ticks, configuration.DispatchTimeout.Ticks);
            Assert.AreEqual(TimeSpan.FromSeconds(10).Ticks, configuration.StopDrainTimeout.Ticks);
            Assert.IsNotNull(configuration.Logger, "the default logger writes to the debug output");
            Assert.IsNull(configuration.OnCriticalError);
            Assert.IsNull(configuration.Username);
        }

        [TestMethod]
        public void An_endpoint_name_that_is_not_an_address_is_rejected_and_named()
        {
            AssertRejects(() => new DeviceEndpointConfiguration("gate/#", "broker.test"), "ArgumentException", "gate/#");
            AssertRejects(() => new DeviceEndpointConfiguration("gate+1", "broker.test"), "ArgumentException", "gate+1");
            AssertRejects(() => new DeviceEndpointConfiguration("$SYS", "broker.test"), "ArgumentException", "$SYS");
            AssertRejects(() => new DeviceEndpointConfiguration("", "broker.test"), "ArgumentException", "empty");
            AssertRejects(() => new DeviceEndpointConfiguration("   ", "broker.test"), "ArgumentException", "empty");
            AssertRejects(() => new DeviceEndpointConfiguration(null, "broker.test"), "ArgumentException", "empty");
        }

        [TestMethod]
        public void The_error_message_for_a_wildcard_explains_the_rule()
        {
            var message = MessageOf(() => new DeviceEndpointConfiguration("gate/#", "broker.test"));

            Assert.IsTrue(message.IndexOf("cannot contain '+' or '#' and cannot start with '$'") >= 0, message);
        }

        [TestMethod]
        public void A_port_outside_1_to_65535_is_rejected_and_named()
        {
            AssertRejects(() => new DeviceEndpointConfiguration("Gate_01", "broker.test", 0), "ArgumentOutOfRangeException", "0");
            AssertRejects(() => new DeviceEndpointConfiguration("Gate_01", "broker.test", 70000), "ArgumentOutOfRangeException", "70000");
            AssertRejects(() => new DeviceEndpointConfiguration("Gate_01", "broker.test", -1), "ArgumentOutOfRangeException", "-1");

            Assert.AreEqual(1, new DeviceEndpointConfiguration("Gate_01", "broker.test", 1).Port);
            Assert.AreEqual(65535, new DeviceEndpointConfiguration("Gate_01", "broker.test", 65535).Port);
        }

        [TestMethod]
        public void An_empty_host_is_rejected()
        {
            AssertRejects(() => new DeviceEndpointConfiguration("Gate_01", ""), "ArgumentException", "host");
            AssertRejects(() => new DeviceEndpointConfiguration("Gate_01", "  "), "ArgumentException", "host");
            AssertRejects(() => new DeviceEndpointConfiguration("Gate_01", null), "ArgumentException", "host");
        }

        [TestMethod]
        public void The_session_expiry_is_from_one_second_to_the_largest_interval_mqtt_can_express()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            configuration.SessionExpiry = TimeSpan.FromSeconds(1);
            Assert.AreEqual(TimeSpan.FromSeconds(1).Ticks, configuration.SessionExpiry.Ticks);
            configuration.SessionExpiry = TimeSpan.FromSeconds(4294967295L);
            Assert.AreEqual(TimeSpan.FromSeconds(4294967295L).Ticks, configuration.SessionExpiry.Ticks);

            AssertRejects(() => configuration.SessionExpiry = TimeSpan.Zero, "ArgumentOutOfRangeException", "session expiry");
            AssertRejects(() => configuration.SessionExpiry = TimeSpan.FromMilliseconds(999), "ArgumentOutOfRangeException", "session expiry");
            AssertRejects(() => configuration.SessionExpiry = TimeSpan.FromSeconds(-1), "ArgumentOutOfRangeException", "session expiry");
            AssertRejects(() => configuration.SessionExpiry = TimeSpan.FromSeconds(4294967296L), "ArgumentOutOfRangeException", "session expiry");
            Assert.AreEqual(TimeSpan.FromSeconds(4294967295L).Ticks, configuration.SessionExpiry.Ticks, "a rejected value changes nothing");
        }

        [TestMethod]
        public void Immediate_retries_cannot_be_negative_and_zero_turns_them_off()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            configuration.ImmediateRetries = 0;
            Assert.AreEqual(0, configuration.ImmediateRetries);
            AssertRejects(() => configuration.ImmediateRetries = -1, "ArgumentOutOfRangeException", "immediate retries");
            Assert.AreEqual(0, configuration.ImmediateRetries);
        }

        [TestMethod]
        public void The_maximum_packet_size_must_be_a_size_mqtt_can_express()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            configuration.MaximumPacketSize = 1;
            configuration.MaximumPacketSize = 268435455;
            AssertRejects(() => configuration.MaximumPacketSize = 0, "ArgumentOutOfRangeException", "maximum packet size");
            AssertRejects(() => configuration.MaximumPacketSize = -5, "ArgumentOutOfRangeException", "maximum packet size");
            AssertRejects(() => configuration.MaximumPacketSize = 268435456, "ArgumentOutOfRangeException", "maximum packet size");
        }

        [TestMethod]
        public void The_timeouts_are_checked()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            AssertRejects(() => configuration.DispatchTimeout = TimeSpan.Zero, "ArgumentOutOfRangeException", "dispatch timeout");
            AssertRejects(() => configuration.DispatchTimeout = TimeSpan.FromSeconds(-1), "ArgumentOutOfRangeException", "dispatch timeout");
            AssertRejects(() => configuration.StopDrainTimeout = TimeSpan.FromSeconds(-1), "ArgumentOutOfRangeException", "stop drain timeout");

            configuration.StopDrainTimeout = TimeSpan.Zero;
            Assert.AreEqual(0L, configuration.StopDrainTimeout.Ticks, "zero means to stop after the message in progress");
        }

        [TestMethod]
        public void Credentials_need_a_username_and_a_password()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            AssertRejects(() => configuration.UseCredentials("", "secret"), "ArgumentException", "username");
            AssertRejects(() => configuration.UseCredentials(null, "secret"), "ArgumentException", "username");
            AssertRejects(() => configuration.UseCredentials("user", null), "ArgumentNullException", "password");

            configuration.UseCredentials("user", "");
            Assert.AreEqual("user", configuration.Username);
            Assert.AreEqual("", configuration.Password);
        }

        [TestMethod]
        public void The_error_queue_is_validated_like_an_address()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            AssertRejects(() => configuration.ErrorQueue = "bad#queue", "ArgumentException", "bad#queue");
            AssertRejects(() => configuration.ErrorQueue = "", "ArgumentException", "empty");
            configuration.ErrorQueue = "audit_errors";
            Assert.AreEqual("audit_errors", configuration.ErrorQueue);
        }

        [TestMethod]
        public void A_route_needs_a_valid_destination_and_a_type()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            AssertRejects(() => configuration.RouteToEndpoint(typeof(OpenValve), "bad+destination"), "ArgumentException", "bad+destination");
            AssertRejects(() => configuration.RouteToEndpoint(null, "Plant"), "ArgumentNullException", "messageType");
            configuration.RouteToEndpoint(typeof(OpenValve), "Plant");
        }

        [TestMethod]
        public void A_handler_needs_a_type_and_an_instance()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            AssertRejects(() => configuration.RegisterHandler(null, new ActionHandler(null)), "ArgumentNullException", "messageType");
            AssertRejects(() => configuration.RegisterHandler(typeof(OpenValve), null), "ArgumentNullException", "handler");
        }

        [TestMethod]
        public void Only_an_event_type_can_be_subscribed_to()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            AssertRejects(() => configuration.Subscribe(typeof(OpenValve)), "ArgumentException", "Contracts.OpenValve");
            AssertRejects(() => configuration.Subscribe(null), "ArgumentNullException", "eventType");
            configuration.Subscribe(typeof(PriceChanged));
        }

        [TestMethod]
        public void A_logger_is_required()
        {
            var configuration = new DeviceEndpointConfiguration("Gate_01", "broker.test");

            AssertRejects(() => configuration.Logger = null, "ArgumentNullException", "value");
        }

        [TestMethod]
        public void An_option_header_that_the_endpoint_sets_itself_is_rejected()
        {
            var options = new SendOptions();

            AssertRejects(() => options.SetHeader("NServiceBus.MessageId", "mine"), "ArgumentException", "NServiceBus.MessageId");
            AssertRejects(() => options.SetHeader("", "x"), "ArgumentException", "name");
            options.SetHeader("my-header", "x");
        }
    }
}
