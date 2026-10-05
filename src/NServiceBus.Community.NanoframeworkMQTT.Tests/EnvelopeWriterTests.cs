using System;
using System.Collections;
using System.Text;
using Interop;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    [TestClass]
    public class EnvelopeWriterTests
    {
        [TestMethod]
        public void Every_device_origin_golden_payload_is_produced_byte_for_byte()
        {
            foreach (GoldenPayload golden in WirePayloads.DeviceOrigin)
            {
                var payload = WireFormat.Encode(golden.Id, HeadersOf(golden), Encoding.UTF8.GetBytes(golden.Body));

                var expected = Encoding.UTF8.GetBytes(golden.Json);
                EnvelopeReaderTests.AssertBytes(expected, payload, golden.Name);
            }
        }

        [TestMethod]
        public void The_header_order_does_not_depend_on_the_order_they_were_added_in()
        {
            var forwards = new Hashtable();
            forwards.Add("a", "1");
            forwards.Add("b", "2");
            forwards.Add("c", "3");
            var backwards = new Hashtable();
            backwards.Add("c", "3");
            backwards.Add("b", "2");
            backwards.Add("a", "1");

            var first = WireFormat.Encode("id", forwards, new byte[0]);
            var second = WireFormat.Encode("id", backwards, new byte[0]);

            EnvelopeReaderTests.AssertBytes(first, second, "same headers");
            Assert.AreEqual("{\"Id\":\"id\",\"Headers\":{\"a\":\"1\",\"b\":\"2\",\"c\":\"3\"},\"Body\":\"\"}", Encoding.UTF8.GetString(first, 0, first.Length));
        }

        [TestMethod]
        public void A_null_id_is_written_as_json_null()
        {
            var payload = WireFormat.Encode(null, new Hashtable(), new byte[0]);

            Assert.AreEqual("{\"Id\":null,\"Headers\":{},\"Body\":\"\"}", Encoding.UTF8.GetString(payload, 0, payload.Length));
        }

        [TestMethod]
        public void A_null_header_value_is_written_as_json_null()
        {
            var headers = new Hashtable();
            headers.Add("k", null);

            var payload = WireFormat.Encode("id", headers, new byte[0]);

            Assert.AreEqual("{\"Id\":\"id\",\"Headers\":{\"k\":null},\"Body\":\"\"}", Encoding.UTF8.GetString(payload, 0, payload.Length));
        }

        [TestMethod]
        public void Only_the_quote_the_backslash_and_control_characters_are_escaped()
        {
            var headers = new Hashtable();
            headers.Add("k", "/<>&+'`\u007fé");

            var payload = WireFormat.Encode("id", headers, new byte[0]);

            Assert.AreEqual("{\"Id\":\"id\",\"Headers\":{\"k\":\"/<>&+'`\u007fé\"},\"Body\":\"\"}", Encoding.UTF8.GetString(payload, 0, payload.Length));
        }

        [TestMethod]
        public void Every_control_character_is_escaped_and_read_back()
        {
            // from U+0001: a nanoFramework string ends at U+0000, so it cannot hold one
            var value = new StringBuilder();
            for (var code = 1; code < 0x20; code++)
            {
                value.Append((char)code);
            }

            var headers = new Hashtable();
            headers.Add("k", value.ToString());

            var payload = WireFormat.Encode("id", headers, new byte[0]);

            var text = Encoding.UTF8.GetString(payload, 0, payload.Length);
            Assert.AreEqual(
                "{\"Id\":\"id\",\"Headers\":{\"k\":\"\\u0001\\u0002\\u0003\\u0004\\u0005\\u0006\\u0007\\b\\t\\n\\u000B\\f\\r\\u000E\\u000F\\u0010\\u0011\\u0012\\u0013\\u0014\\u0015\\u0016\\u0017\\u0018\\u0019\\u001A\\u001B\\u001C\\u001D\\u001E\\u001F\"},\"Body\":\"\"}",
                text);

            var decoded = WireFormat.Decode(payload);
            Assert.AreEqual(value.ToString(), (string)decoded.Headers["k"], "read back");
        }

        [TestMethod]
        public void What_the_writer_writes_the_reader_reads_for_every_dotnet_origin_golden()
        {
            foreach (GoldenPayload golden in WirePayloads.DotNetOrigin)
            {
                var body = Encoding.UTF8.GetBytes(golden.Body);

                var decoded = WireFormat.Decode(WireFormat.Encode(golden.Id, HeadersOf(golden), body));

                Assert.AreEqual(golden.Id, decoded.Id, golden.Name + ": id");
                Assert.AreEqual(golden.Headers.Length / 2, decoded.Headers.Count, golden.Name + ": number of headers");
                for (var i = 0; i < golden.Headers.Length; i += 2)
                {
                    Assert.AreEqual(golden.Headers[i + 1], (string)decoded.Headers[golden.Headers[i]], golden.Name + ": header '" + golden.Headers[i] + "'");
                }

                EnvelopeReaderTests.AssertBytes(body, decoded.Body, golden.Name + ": body");
            }
        }

        [TestMethod]
        public void A_binary_body_with_every_byte_value_round_trips()
        {
            var body = new byte[256];
            for (var i = 0; i < body.Length; i++)
            {
                body[i] = (byte)i;
            }

            var decoded = WireFormat.Decode(WireFormat.Encode("id", new Hashtable(), body));

            EnvelopeReaderTests.AssertBytes(body, decoded.Body, "body");
        }

        [TestMethod]
        public void Characters_outside_the_basic_plane_are_written_as_raw_utf8_and_read_back()
        {
            var headers = new Hashtable();
            headers.Add("a-😅-B7", "a-😍-b");

            var payload = WireFormat.Encode("id", headers, new byte[0]);

            // the four UTF-8 bytes of U+1F605 are in the payload as they are
            var found = false;
            for (var i = 0; i + 3 < payload.Length; i++)
            {
                if (payload[i] == 0xF0 && payload[i + 1] == 0x9F && payload[i + 2] == 0x98 && payload[i + 3] == 0x85)
                {
                    found = true;
                }
            }

            Assert.IsTrue(found, "the raw bytes of U+1F605");
            Assert.AreEqual("a-😍-b", (string)WireFormat.Decode(payload).Headers["a-😅-B7"]);
        }

        [TestMethod]
        public void A_missing_headers_table_or_body_is_an_argument_error()
        {
            Assert.ThrowsException(typeof(ArgumentNullException), () => WireFormat.Encode("id", null, new byte[0]));
            Assert.ThrowsException(typeof(ArgumentNullException), () => WireFormat.Encode("id", new Hashtable(), null));
        }

        static Hashtable HeadersOf(GoldenPayload golden)
        {
            var headers = new Hashtable();
            for (var i = 0; i < golden.Headers.Length; i += 2)
            {
                headers.Add(golden.Headers[i], golden.Headers[i + 1]);
            }

            return headers;
        }
    }
}
