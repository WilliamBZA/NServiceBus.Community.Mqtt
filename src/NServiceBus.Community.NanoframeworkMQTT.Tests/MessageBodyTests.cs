using System;
using System.Text;
using Contracts;
using Interop;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace BodyTypes
{
    public class Node
    {
        public string Name { get; set; }
        public Node Next { get; set; }
    }

    public class WithFloat
    {
        public float Value { get; set; }
    }

    public class WithByte
    {
        public byte Value { get; set; }
    }

    public class WithGuid
    {
        public Guid Id { get; set; }
    }

    public class WithObject
    {
        public object Anything { get; set; }
    }

    public class WithJaggedArray
    {
        public int[][] Rows { get; set; }
    }

    public class WithNestedUnsupported
    {
        public WithFloat Inner { get; set; }
    }

    public class WithReadOnlyMembers
    {
        public string Text { get; set; }
        public int ReadOnly { get { return 5; } }
        public string Computed { get { return "x"; } }
        public int WriteOnly { set { } }
        public static string Static { get; set; }
    }

    public class NoDefaultConstructor
    {
        public NoDefaultConstructor(int value)
        {
        }

        public int Value { get; set; }
    }

    public abstract class AbstractMessage
    {
        public string Name { get; set; }
    }
}

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    [TestClass]
    public class MessageBodyTests
    {
        // ---- the golden bodies

        [TestMethod]
        public void The_dotnet_body_of_all_member_types_is_read_to_the_sample_values()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(Utf8(WirePayloads.AllMemberTypesPayload.Body), typeof(AllMemberTypes));

            AssertNoDifference(WirePayloads.Difference(WirePayloads.CreateAllMemberTypes(), message));
        }

        [TestMethod]
        public void The_dotnet_body_of_the_array_sample_is_read_to_the_sample_values()
        {
            var message = (ArrayMemberTypes)MessageBody.Deserialize(Utf8(WirePayloads.ArrayMemberTypesDotNetBody), typeof(ArrayMemberTypes));

            AssertNoDifference(WirePayloads.Difference(WirePayloads.CreateArrayMemberTypes(), message));
        }

        [TestMethod]
        public void The_device_body_of_the_array_sample_is_read_to_the_sample_values()
        {
            var message = (ArrayMemberTypes)MessageBody.Deserialize(Utf8(WirePayloads.ArrayMemberTypesDeviceBody), typeof(ArrayMemberTypes));

            AssertNoDifference(WirePayloads.Difference(WirePayloads.CreateArrayMemberTypes(), message));
        }

        [TestMethod]
        public void All_member_types_are_written_as_the_golden_device_body_byte_for_byte()
        {
            var body = MessageBody.Serialize(WirePayloads.CreateAllMemberTypes());

            EnvelopeReaderTests.AssertBytes(Utf8(WirePayloads.AllMemberTypesDeviceBody), body, "AllMemberTypes");
        }

        [TestMethod]
        public void The_array_sample_is_written_as_the_golden_device_body_byte_for_byte()
        {
            var body = MessageBody.Serialize(WirePayloads.CreateArrayMemberTypes());

            EnvelopeReaderTests.AssertBytes(Utf8(WirePayloads.ArrayMemberTypesDeviceBody), body, "ArrayMemberTypes");
        }

        [TestMethod]
        public void The_golden_bodies_that_nservicebus_writes_for_the_command_and_reply_samples_are_read()
        {
            var valve = (OpenValve)MessageBody.Deserialize(Utf8(WirePayloads.Command.Body), typeof(OpenValve));
            Assert.AreEqual("V-1", valve.ValveId);
            Assert.AreEqual(75, valve.Percent);

            var reply = (ValveStatusResponse)MessageBody.Deserialize(Utf8(WirePayloads.SagaReply.Body), typeof(ValveStatusResponse));
            Assert.AreEqual("V-1", reply.ValveId);
            Assert.IsTrue(reply.IsOpen);
            Assert.AreEqual(75, reply.Percent);
        }

        [TestMethod]
        public void A_derived_message_has_the_members_of_its_base_class()
        {
            var opened = (ValveOpened)MessageBody.Deserialize(Utf8(WirePayloads.Event.Body), typeof(ValveOpened));

            Assert.AreEqual("V-1", opened.ValveId, "the member of the base class");
            Assert.AreEqual(100, opened.Percent, "the member of the derived class");

            var written = MessageBody.Serialize(opened);
            Assert.AreEqual("{\"Percent\":100,\"ValveId\":\"V-1\"}", Encoding.UTF8.GetString(written, 0, written.Length));
        }

        // ---- doubles and times

        [TestMethod]
        public void Every_double_is_written_as_the_text_dotnet_writes_for_it()
        {
            foreach (DoubleCase doubleCase in WirePayloads.Doubles)
            {
                Assert.AreEqual(doubleCase.Json, DoubleText.Format(doubleCase.Value), "writing " + doubleCase.Json);
            }
        }

        [TestMethod]
        public void Every_double_text_is_read_back_to_the_exact_value()
        {
            foreach (DoubleCase doubleCase in WirePayloads.Doubles)
            {
                var message = (AllMemberTypes)MessageBody.Deserialize(Utf8("{\"DoubleValue\":" + doubleCase.Json + "}"), typeof(AllMemberTypes));

                Assert.AreEqual(BitConverter.DoubleToInt64Bits(doubleCase.Value), BitConverter.DoubleToInt64Bits(message.DoubleValue), "reading " + doubleCase.Json);
            }
        }

        [TestMethod]
        public void A_double_that_is_not_a_number_or_is_infinite_cannot_be_written()
        {
            Assert.ThrowsException(typeof(ArgumentException), () => DoubleText.Format(double.NaN));
            Assert.ThrowsException(typeof(ArgumentException), () => DoubleText.Format(double.PositiveInfinity));
            Assert.ThrowsException(typeof(ArgumentException), () => DoubleText.Format(double.NegativeInfinity));
        }

        [TestMethod]
        public void A_double_that_overflows_is_rejected_when_read()
        {
            AssertRejected("{\"DoubleValue\":1e400}", typeof(AllMemberTypes), "overflow");
        }

        [TestMethod]
        public void Every_utc_time_is_written_as_the_text_dotnet_writes_for_it()
        {
            foreach (DateTimeCase timeCase in WirePayloads.DateTimes)
            {
                Assert.AreEqual(timeCase.Json, DateTimeText.Format(new DateTime(timeCase.Ticks, DateTimeKind.Utc)), "writing " + timeCase.Json);
            }
        }

        [TestMethod]
        public void Every_utc_time_text_is_read_back_to_the_exact_ticks()
        {
            foreach (DateTimeCase timeCase in WirePayloads.DateTimes)
            {
                var message = (AllMemberTypes)MessageBody.Deserialize(Utf8("{\"Timestamp\":\"" + timeCase.Json + "\"}"), typeof(AllMemberTypes));

                Assert.AreEqual(timeCase.Ticks, message.Timestamp.Ticks, "reading " + timeCase.Json);
            }
        }

        [TestMethod]
        public void Every_time_text_dotnet_may_send_means_the_same_to_the_device()
        {
            foreach (DateTimeParseCase parseCase in WirePayloads.DateTimeParses)
            {
                var body = Utf8("{\"Timestamp\":\"" + parseCase.Json + "\"}");
                if (parseCase.UtcTicks < 0)
                {
                    Assert.ThrowsException(typeof(MessageDeserializationException), () => MessageBody.Deserialize(body, typeof(AllMemberTypes)), "'" + parseCase.Json + "' is not a time");
                }
                else
                {
                    var message = (AllMemberTypes)MessageBody.Deserialize(body, typeof(AllMemberTypes));

                    Assert.AreEqual(parseCase.UtcTicks, message.Timestamp.Ticks, "'" + parseCase.Json + "'");
                }
            }
        }

        [TestMethod]
        public void A_time_before_the_earliest_the_device_can_represent_is_rejected()
        {
            AssertRejected("{\"Timestamp\":\"1600-12-31T23:59:59Z\"}", typeof(AllMemberTypes), "before 1601");
            AssertRejected("{\"Timestamp\":\"0001-01-01T00:00:00Z\"}", typeof(AllMemberTypes), "year 1");
        }

        // ---- text

        [TestMethod]
        public void Text_with_every_kind_of_character_round_trips()
        {
            var texts = new string[]
            {
                "",
                "plain",
                "quote \" backslash \\ slash /",
                "tab\there newline\nthere return\rx backspace\b formfeed\f",
                "\u0001\u0002\u001f",
                "<b>&'+`",
                "caf\u00e9 \u20ac \u65e5\u672c\u8a9e",
                "😀 and 😍 outside the basic plane",
                "{\"looks\":\"like json\"}",
            };

            for (var i = 0; i < texts.Length; i++)
            {
                var message = new AllMemberTypes();
                message.Text = texts[i];
                var body = MessageBody.Serialize(message);
                var read = (AllMemberTypes)MessageBody.Deserialize(body, typeof(AllMemberTypes));

                AssertTextEqual(texts[i], read.Text, "text " + i);
            }
        }

        [TestMethod]
        public void Control_characters_are_escaped_so_that_the_body_is_valid_json()
        {
            var message = new AllMemberTypes();
            message.Text = "a\u0001b\nc\u001fd";

            var body = MessageBody.Serialize(message);

            // the value-type members are always written, so look for the text among them
            Assert.Contains("\"Text\":\"a\\u0001b\\nc\\u001Fd\"", Encoding.UTF8.GetString(body, 0, body.Length), "the escaped text");
        }

        [TestMethod]
        public void Every_json_escape_in_a_body_string_is_read()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(Utf8("{\"Text\":\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u0041\\u00E9\\u20AC\\uD83D\\uDE00\\u0022\"}"), typeof(AllMemberTypes));

            AssertTextEqual("\"\\/\b\f\n\r\tA\u00e9\u20ac😀\"", message.Text, "escapes");
        }

        // ---- members that are missing, null, unknown or in the wrong form

        [TestMethod]
        public void Missing_members_keep_their_default_values()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(Utf8("{}"), typeof(AllMemberTypes));

            Assert.IsNull(message.Text);
            Assert.AreEqual(0, message.Int32Value);
            Assert.IsFalse(message.Flag);
            Assert.IsNull(message.Nested);
            Assert.IsNull(message.Numbers);
        }

        [TestMethod]
        public void Null_is_accepted_for_the_members_that_can_be_null_including_arrays_of_integers()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(
                Utf8("{\"Text\":null,\"Nested\":null,\"Texts\":null,\"Numbers\":null,\"Doubles\":null,\"NestedItems\":null}"),
                typeof(AllMemberTypes));

            Assert.IsNull(message.Text);
            Assert.IsNull(message.Nested);
            Assert.IsNull(message.Texts);
            Assert.IsNull(message.Numbers);
            Assert.IsNull(message.Doubles);
            Assert.IsNull(message.NestedItems);
        }

        [TestMethod]
        public void Null_is_rejected_for_the_members_that_cannot_be_null()
        {
            AssertRejected("{\"Int32Value\":null}", typeof(AllMemberTypes), "int");
            AssertRejected("{\"Int64Value\":null}", typeof(AllMemberTypes), "long");
            AssertRejected("{\"DoubleValue\":null}", typeof(AllMemberTypes), "double");
            AssertRejected("{\"Flag\":null}", typeof(AllMemberTypes), "bool");
            AssertRejected("{\"Timestamp\":null}", typeof(AllMemberTypes), "DateTime");
            AssertRejected("{\"Mode\":null}", typeof(AllMemberTypes), "enum");
            AssertRejected("{\"Numbers\":[1,null]}", typeof(AllMemberTypes), "null in an int array");
        }

        [TestMethod]
        public void A_null_element_of_a_string_or_class_array_is_kept()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(Utf8("{\"Texts\":[\"a\",null],\"NestedItems\":[null,{\"Name\":\"x\"}]}"), typeof(AllMemberTypes));

            Assert.AreEqual(2, message.Texts.Length);
            Assert.IsNull(message.Texts[1]);
            Assert.AreEqual(2, message.NestedItems.Length);
            Assert.IsNull(message.NestedItems[0]);
            Assert.AreEqual("x", message.NestedItems[1].Name);
        }

        [TestMethod]
        public void Unknown_members_of_any_json_type_are_skipped()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(
                Utf8("{\"Extra\":{\"a\":[1,2.5e10,true,null,\"s\",{},[]]},\"Text\":\"kept\",\"Tail\":[[[]]],\"Count\":-12.5E-3}"),
                typeof(AllMemberTypes));

            Assert.AreEqual("kept", message.Text);
        }

        [TestMethod]
        public void Member_names_are_case_sensitive()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(Utf8("{\"text\":\"wrong case\",\"TEXT\":\"wrong case\",\"Text\":\"right\"}"), typeof(AllMemberTypes));

            Assert.AreEqual("right", message.Text);
        }

        [TestMethod]
        public void The_last_of_a_repeated_member_wins()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(Utf8("{\"Int32Value\":1,\"Int32Value\":2}"), typeof(AllMemberTypes));

            Assert.AreEqual(2, message.Int32Value);
        }

        [TestMethod]
        public void Whitespace_is_allowed_between_tokens()
        {
            var message = (AllMemberTypes)MessageBody.Deserialize(Utf8(" \r\n{ \"Int32Value\" :\t5 , \"Numbers\" : [ 1 , 2 ] }\n "), typeof(AllMemberTypes));

            Assert.AreEqual(5, message.Int32Value);
            Assert.AreEqual(2, message.Numbers.Length);
        }

        [TestMethod]
        public void Numbers_of_the_wrong_form_for_an_integer_member_are_rejected()
        {
            AssertRejected("{\"Int32Value\":5.0}", typeof(AllMemberTypes), "5.0 for an int");
            AssertRejected("{\"Int32Value\":5e0}", typeof(AllMemberTypes), "5e0 for an int");
            AssertRejected("{\"Int32Value\":\"5\"}", typeof(AllMemberTypes), "a string for an int");
            AssertRejected("{\"Int64Value\":1.5}", typeof(AllMemberTypes), "1.5 for a long");
            AssertRejected("{\"Mode\":\"Running\"}", typeof(AllMemberTypes), "an enum as a string");
            AssertRejected("{\"Flag\":1}", typeof(AllMemberTypes), "a number for a bool");
            AssertRejected("{\"Flag\":\"true\"}", typeof(AllMemberTypes), "a string for a bool");
            AssertRejected("{\"Text\":5}", typeof(AllMemberTypes), "a number for a string");
            AssertRejected("{\"DoubleValue\":\"1.5\"}", typeof(AllMemberTypes), "a string for a double");
            AssertRejected("{\"Nested\":[]}", typeof(AllMemberTypes), "an array for a class");
            AssertRejected("{\"Numbers\":5}", typeof(AllMemberTypes), "a number for an array");
            AssertRejected("{\"Numbers\":{}}", typeof(AllMemberTypes), "an object for an array");
        }

        [TestMethod]
        public void Integers_are_checked_against_the_range_of_their_type()
        {
            var edges = (AllMemberTypes)MessageBody.Deserialize(Utf8("{\"Int32Value\":2147483647,\"Int64Value\":9223372036854775807}"), typeof(AllMemberTypes));
            Assert.AreEqual(int.MaxValue, edges.Int32Value);
            Assert.AreEqual(long.MaxValue, edges.Int64Value);

            var lows = (AllMemberTypes)MessageBody.Deserialize(Utf8("{\"Int32Value\":-2147483648,\"Int64Value\":-9223372036854775808}"), typeof(AllMemberTypes));
            Assert.AreEqual(int.MinValue, lows.Int32Value);
            Assert.AreEqual(long.MinValue, lows.Int64Value);

            AssertRejected("{\"Int32Value\":2147483648}", typeof(AllMemberTypes), "int max + 1");
            AssertRejected("{\"Int32Value\":-2147483649}", typeof(AllMemberTypes), "int min - 1");
            AssertRejected("{\"Int64Value\":9223372036854775808}", typeof(AllMemberTypes), "long max + 1");
            AssertRejected("{\"Int64Value\":-9223372036854775809}", typeof(AllMemberTypes), "long min - 1");
            AssertRejected("{\"Int64Value\":99999999999999999999}", typeof(AllMemberTypes), "far too large");
            AssertRejected("{\"Int32Value\":01}", typeof(AllMemberTypes), "a leading zero");
            AssertRejected("{\"Int32Value\":-}", typeof(AllMemberTypes), "a lone minus");
        }

        [TestMethod]
        public void A_body_that_is_not_a_json_object_is_rejected()
        {
            AssertRejected("", typeof(AllMemberTypes), "empty");
            AssertRejected("   ", typeof(AllMemberTypes), "whitespace");
            AssertRejected("null", typeof(AllMemberTypes), "null");
            AssertRejected("[]", typeof(AllMemberTypes), "an array");
            AssertRejected("\"text\"", typeof(AllMemberTypes), "a string");
            AssertRejected("5", typeof(AllMemberTypes), "a number");
            AssertRejected("not json", typeof(AllMemberTypes), "garbage");
            AssertRejected("{\"Text\":\"x\"", typeof(AllMemberTypes), "truncated");
            AssertRejected("{\"Text\":\"x\"} trailing", typeof(AllMemberTypes), "trailing data");
            AssertRejected("{\"Text\":\"x\",}", typeof(AllMemberTypes), "trailing comma");
            AssertRejected("{\"Text\" \"x\"}", typeof(AllMemberTypes), "missing colon");
            AssertRejected("{Text:\"x\"}", typeof(AllMemberTypes), "unquoted name");
            AssertRejected("{\"Text\":\"a\u0001b\"}", typeof(AllMemberTypes), "a raw control character");
            AssertRejected("{\"Text\":\"\\x\"}", typeof(AllMemberTypes), "an invalid escape");
            AssertRejected("{\"Text\":\"\\uD83D\"}", typeof(AllMemberTypes), "a lone surrogate");
        }

        [TestMethod]
        public void A_rejection_is_a_message_deserialization_exception_that_names_the_type()
        {
            MessageDeserializationException rejection = null;
            try
            {
                MessageBody.Deserialize(Utf8("{\"Int32Value\":\"x\"}"), typeof(AllMemberTypes));
            }
            catch (MessageDeserializationException exception)
            {
                rejection = exception;
            }

            Assert.IsNotNull(rejection);
            Assert.AreEqual("NServiceBus.MessageDeserializationException", rejection.GetType().FullName);
            Assert.Contains("Contracts.AllMemberTypes", rejection.Message, "names the message type");
            Assert.Contains("Int32Value", rejection.Message, "names the member");
        }

        [TestMethod]
        public void Nesting_that_is_too_deep_is_rejected_instead_of_exhausting_the_stack()
        {
            var body = new StringBuilder();
            for (var i = 0; i < 40; i++)
            {
                body.Append("{\"Name\":\"n\",\"Next\":");
            }

            body.Append("null");
            for (var i = 0; i < 40; i++)
            {
                body.Append('}');
            }

            AssertRejected(body.ToString(), typeof(BodyTypes.Node), "40 levels of a linked list");

            var shallow = new StringBuilder();
            for (var i = 0; i < 10; i++)
            {
                shallow.Append("{\"Name\":\"n" + i + "\",\"Next\":");
            }

            shallow.Append("null");
            for (var i = 0; i < 10; i++)
            {
                shallow.Append('}');
            }

            var node = (BodyTypes.Node)MessageBody.Deserialize(Utf8(shallow.ToString()), typeof(BodyTypes.Node));
            var count = 0;
            for (var current = node; current != null; current = current.Next)
            {
                count++;
            }

            Assert.AreEqual(10, count, "ten levels are fine");
        }

        // ---- the members a type can have

        [TestMethod]
        public void A_type_that_refers_to_itself_is_written_and_read()
        {
            var first = new BodyTypes.Node();
            first.Name = "a";
            first.Next = new BodyTypes.Node();
            first.Next.Name = "b";

            var body = MessageBody.Serialize(first);
            Assert.AreEqual("{\"Name\":\"a\",\"Next\":{\"Name\":\"b\"}}", Encoding.UTF8.GetString(body, 0, body.Length));

            var read = (BodyTypes.Node)MessageBody.Deserialize(body, typeof(BodyTypes.Node));
            Assert.AreEqual("a", read.Name);
            Assert.AreEqual("b", read.Next.Name);
            Assert.IsNull(read.Next.Next);
        }

        [TestMethod]
        public void A_cycle_in_a_message_is_an_error_instead_of_an_endless_loop()
        {
            var node = new BodyTypes.Node();
            node.Name = "loop";
            node.Next = node;

            Assert.ThrowsException(typeof(InvalidOperationException), () => MessageBody.Serialize(node));
        }

        [TestMethod]
        public void Members_that_are_not_read_write_properties_are_not_part_of_the_message()
        {
            var message = new BodyTypes.WithReadOnlyMembers();
            message.Text = "t";

            var body = MessageBody.Serialize(message);

            Assert.AreEqual("{\"Text\":\"t\"}", Encoding.UTF8.GetString(body, 0, body.Length));
        }

        [TestMethod]
        public void A_member_of_a_type_the_device_cannot_exchange_is_an_error_that_names_it()
        {
            AssertNotSupported(typeof(BodyTypes.WithFloat), "BodyTypes.WithFloat.Value");
            AssertNotSupported(typeof(BodyTypes.WithByte), "BodyTypes.WithByte.Value");
            AssertNotSupported(typeof(BodyTypes.WithGuid), "BodyTypes.WithGuid.Id");
            AssertNotSupported(typeof(BodyTypes.WithObject), "BodyTypes.WithObject.Anything");
            AssertNotSupported(typeof(BodyTypes.WithJaggedArray), "BodyTypes.WithJaggedArray.Rows");
            AssertNotSupported(typeof(BodyTypes.WithNestedUnsupported), "BodyTypes.WithFloat.Value");
        }

        [TestMethod]
        public void A_message_type_the_device_cannot_construct_is_an_error_that_names_it()
        {
            AssertNotSupported(typeof(BodyTypes.NoDefaultConstructor), "BodyTypes.NoDefaultConstructor");
            AssertNotSupported(typeof(BodyTypes.AbstractMessage), "BodyTypes.AbstractMessage");
            AssertNotSupported(typeof(string), "System.String");
            AssertNotSupported(typeof(int[]), "System.Int32[]");
        }

        [TestMethod]
        public void Every_member_kind_of_the_samples_is_accepted()
        {
            TypeModels.For(typeof(AllMemberTypes));
            TypeModels.For(typeof(ArrayMemberTypes));
            TypeModels.For(typeof(OpenValve));
            TypeModels.For(typeof(ValveOpened));
            TypeModels.For(typeof(PriceChanged));
            TypeModels.For(typeof(ValveStatusRequest));
            TypeModels.For(typeof(ValveStatusResponse));
            TypeModels.For(typeof(AlwaysFails));
        }

        [TestMethod]
        public void Large_arrays_and_long_strings_round_trip()
        {
            var message = new AllMemberTypes();
            message.Numbers = new int[2000];
            for (var i = 0; i < message.Numbers.Length; i++)
            {
                message.Numbers[i] = i * 31 - 1000;
            }

            var text = new StringBuilder();
            for (var i = 0; i < 3000; i++)
            {
                text.Append((char)('a' + i % 26));
            }

            message.Text = text.ToString();

            var read = (AllMemberTypes)MessageBody.Deserialize(MessageBody.Serialize(message), typeof(AllMemberTypes));

            Assert.AreEqual(3000, read.Text.Length);
            Assert.AreEqual(2000, read.Numbers.Length);
            Assert.AreEqual(1999 * 31 - 1000, read.Numbers[1999]);
        }

        [TestMethod]
        public void The_message_cannot_be_null()
        {
            Assert.ThrowsException(typeof(ArgumentNullException), () => MessageBody.Serialize(null));
            Assert.ThrowsException(typeof(ArgumentNullException), () => MessageBody.Deserialize(null, typeof(AllMemberTypes)));
        }

        // ---- helpers

        static void AssertNoDifference(string difference)
        {
            // nanoFramework's Assert.IsNull prints only the message it is given
            Assert.IsTrue(difference == null, difference);
        }

        static byte[] Utf8(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        static void AssertRejected(string json, Type type, string description)
        {
            var body = Utf8(json);
            Assert.ThrowsException(typeof(MessageDeserializationException), () => MessageBody.Deserialize(body, type), description + " should be rejected");
        }

        static void AssertNotSupported(Type type, string expectedInMessage)
        {
            NotSupportedException failure = null;
            try
            {
                TypeModels.For(type);
            }
            catch (NotSupportedException exception)
            {
                failure = exception;
            }

            Assert.IsNotNull(failure, type.FullName + " should not be supported");
            Assert.Contains(expectedInMessage, failure.Message, type.FullName);
        }

        static void AssertTextEqual(string expected, string actual, string description)
        {
            // compared by their UTF-8 bytes, because a string's characters outside the basic plane cannot be compared char by char
            EnvelopeReaderTests.AssertBytes(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual), description);
        }
    }
}
