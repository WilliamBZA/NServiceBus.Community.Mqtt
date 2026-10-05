using System;
using System.Collections;
using System.Text;
using Interop;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    [TestClass]
    public class EnvelopeReaderTests
    {
        [TestMethod]
        public void Every_dotnet_origin_golden_payload_decodes_to_the_expected_id_headers_and_body()
        {
            foreach (GoldenPayload golden in WirePayloads.DotNetOrigin)
            {
                AssertDecodes(golden);
            }
        }

        [TestMethod]
        public void Every_hand_written_payload_decodes_to_the_expected_id_headers_and_body()
        {
            foreach (GoldenPayload golden in WirePayloads.DecodeCases)
            {
                AssertDecodes(golden);
            }
        }

        [TestMethod]
        public void Every_invalid_payload_is_rejected()
        {
            foreach (InvalidPayload invalid in WirePayloads.InvalidPayloads)
            {
                var payload = Encoding.UTF8.GetBytes(invalid.Json);
                Assert.ThrowsException(typeof(MessageDecodeException), () => WireFormat.Decode(payload), invalid.Name);
            }
        }

        [TestMethod]
        public void A_null_payload_is_rejected()
        {
            Assert.ThrowsException(typeof(MessageDecodeException), () => WireFormat.Decode(null));
        }

        [TestMethod]
        public void A_rejection_says_what_is_wrong_and_where()
        {
            MessageDecodeException rejection = null;
            try
            {
                WireFormat.Decode(Encoding.UTF8.GetBytes("{\"Id\":\"x\",\"Headers\":null,\"Body\":\"\"}"));
            }
            catch (MessageDecodeException exception)
            {
                rejection = exception;
            }

            Assert.IsNotNull(rejection);
            Assert.Contains("headers", rejection.Message, "names the part that is wrong");
        }

        [TestMethod]
        public void A_surrogate_pair_escape_decodes_to_the_four_byte_utf8_character()
        {
            var envelope = WireFormat.Decode(Encoding.UTF8.GetBytes("{\"Id\":null,\"Headers\":{\"k\":\"\\uD83D\\uDE05\"},\"Body\":\"\"}"));

            var value = (string)envelope.Headers["k"];

            AssertBytes(new byte[] { 0xF0, 0x9F, 0x98, 0x85 }, Encoding.UTF8.GetBytes(value), "the UTF-8 bytes of U+1F605");
        }

        [TestMethod]
        public void A_nul_character_ends_the_string_it_is_in()
        {
            // A known platform limit, which the README states: a nanoFramework string is NUL-terminated, so it cannot hold U+0000.
            var envelope = WireFormat.Decode(Encoding.UTF8.GetBytes("{\"Id\":null,\"Headers\":{\"k\":\"a\\u0000b\"},\"Body\":\"\"}"));

            Assert.AreEqual("a", (string)envelope.Headers["k"]);
        }

        [TestMethod]
        public void An_empty_headers_object_decodes_to_no_headers()
        {
            var envelope = WireFormat.Decode(Encoding.UTF8.GetBytes("{\"Id\":\"m\",\"Headers\":{},\"Body\":\"e30=\"}"));

            Assert.AreEqual(0, envelope.Headers.Count);
            Assert.AreEqual("m", envelope.Id);
        }

        [TestMethod]
        public void A_byte_order_mark_is_rejected_as_it_is_by_the_dotnet_transport()
        {
            var payload = Encoding.UTF8.GetBytes("﻿{\"Id\":null,\"Headers\":{},\"Body\":\"\"}");
            Assert.AreEqual((byte)0xEF, payload[0], "the test builds a payload that starts with a byte order mark");

            Assert.ThrowsException(typeof(MessageDecodeException), () => WireFormat.Decode(payload));
        }

        [TestMethod]
        public void A_string_that_is_not_well_formed_utf8_is_rejected()
        {
            // each of these bytes sits inside the header value
            byte[][] invalidSequences = new byte[][]
            {
                new byte[] { 0xC0, 0x80 },             // overlong encoding of NUL
                new byte[] { 0xC1, 0xBF },             // overlong encoding of U+007F
                new byte[] { 0xE0, 0x80, 0x80 },       // overlong encoding of NUL
                new byte[] { 0xED, 0xA0, 0x80 },       // a surrogate encoded as UTF-8
                new byte[] { 0xF4, 0x90, 0x80, 0x80 }, // above U+10FFFF
                new byte[] { 0xF5, 0x80, 0x80, 0x80 }, // a lead byte that no valid sequence starts with
                new byte[] { 0xFF },                   // never valid
                new byte[] { 0x80 },                   // a continuation byte on its own
                new byte[] { 0xC3 },                   // a sequence cut off by the closing quote
                new byte[] { 0xE2, 0x82 },             // a sequence cut off by the closing quote
            };

            for (var i = 0; i < invalidSequences.Length; i++)
            {
                var payload = WithHeaderValueBytes(invalidSequences[i]);
                Assert.ThrowsException(typeof(MessageDecodeException), () => WireFormat.Decode(payload), "invalid sequence " + i);
            }
        }

        [TestMethod]
        public void Well_formed_utf8_of_every_length_is_accepted_as_it_is()
        {
            // U+00E9 (2 bytes), U+20AC (3 bytes), U+1F605 (4 bytes), and the edges of each range
            byte[][] validSequences = new byte[][]
            {
                new byte[] { 0xC3, 0xA9 },
                new byte[] { 0xE2, 0x82, 0xAC },
                new byte[] { 0xF0, 0x9F, 0x98, 0x85 },
                new byte[] { 0xC2, 0x80 },             // U+0080
                new byte[] { 0xDF, 0xBF },             // U+07FF
                new byte[] { 0xE0, 0xA0, 0x80 },       // U+0800
                new byte[] { 0xED, 0x9F, 0xBF },       // U+D7FF
                new byte[] { 0xEE, 0x80, 0x80 },       // U+E000
                new byte[] { 0xEF, 0xBF, 0xBF },       // U+FFFF
                new byte[] { 0xF0, 0x90, 0x80, 0x80 }, // U+10000
                new byte[] { 0xF4, 0x8F, 0xBF, 0xBF }, // U+10FFFF
            };

            for (var i = 0; i < validSequences.Length; i++)
            {
                var envelope = WireFormat.Decode(WithHeaderValueBytes(validSequences[i]));

                AssertBytes(validSequences[i], Encoding.UTF8.GetBytes((string)envelope.Headers["k"]), "valid sequence " + i);
            }
        }

        [TestMethod]
        public void A_large_header_and_body_decode()
        {
            var body = new byte[5000];
            for (var i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i * 7);
            }

            var builder = new ByteBuffer(16);
            builder.AppendAscii("{\"Id\":\"big\",\"Headers\":{\"k\":\"");
            for (var i = 0; i < 3000; i++)
            {
                builder.Append((byte)('a' + (i % 26)));
            }

            builder.AppendAscii("\"},\"Body\":\"");
            Base64.Encode(body, builder);
            builder.AppendAscii("\"}");

            var envelope = WireFormat.Decode(builder.ToArray());

            Assert.AreEqual(3000, ((string)envelope.Headers["k"]).Length);
            AssertBytes(body, envelope.Body, "the body");
        }

        [TestMethod]
        public void Base64_round_trips_every_length_and_every_byte_value()
        {
            for (var length = 0; length < 70; length++)
            {
                var data = new byte[length];
                for (var i = 0; i < length; i++)
                {
                    data[i] = (byte)(i * 37 + length);
                }

                var encoded = new ByteBuffer(16);
                Base64.Encode(data, encoded);
                var text = encoded.ToArray();
                var decoded = Base64.Decode(text, 0, text.Length);

                Assert.IsNotNull(decoded, "length " + length);
                AssertBytes(data, decoded, "length " + length);
            }

            var all = new byte[256];
            for (var i = 0; i < all.Length; i++)
            {
                all[i] = (byte)i;
            }

            var allEncoded = new ByteBuffer(16);
            Base64.Encode(all, allEncoded);
            var allText = allEncoded.ToArray();
            AssertBytes(all, Base64.Decode(allText, 0, allText.Length), "every byte value");
        }

        [TestMethod]
        public void Base64_matches_the_known_encodings()
        {
            AssertBase64("", "");
            AssertBase64("f", "Zg==");
            AssertBase64("fo", "Zm8=");
            AssertBase64("foo", "Zm9v");
            AssertBase64("foob", "Zm9vYg==");
            AssertBase64("fooba", "Zm9vYmE=");
            AssertBase64("foobar", "Zm9vYmFy");
        }

        static void AssertBase64(string text, string expected)
        {
            var encoded = new ByteBuffer(16);
            Base64.Encode(Encoding.UTF8.GetBytes(text), encoded);

            var bytes = encoded.ToArray();
            Assert.AreEqual(expected, Encoding.UTF8.GetString(bytes, 0, bytes.Length), "encoding '" + text + "'");
        }

        static byte[] WithHeaderValueBytes(byte[] valueBytes)
        {
            var payload = new ByteBuffer(64);
            payload.AppendAscii("{\"Id\":null,\"Headers\":{\"k\":\"");
            payload.Append(valueBytes);
            payload.AppendAscii("\"},\"Body\":\"\"}");
            return payload.ToArray();
        }

        static void AssertDecodes(GoldenPayload golden)
        {
            var envelope = WireFormat.Decode(Encoding.UTF8.GetBytes(golden.Json));

            AreEqualOrNull(golden.Id, envelope.Id, golden.Name + ": id");
            Assert.AreEqual(golden.Headers.Length / 2, envelope.Headers.Count, golden.Name + ": number of headers");
            for (var i = 0; i < golden.Headers.Length; i += 2)
            {
                var key = golden.Headers[i];
                Assert.IsTrue(envelope.Headers.Contains(key), golden.Name + ": header '" + key + "' is present");
                AreEqualOrNull(golden.Headers[i + 1], (string)envelope.Headers[key], golden.Name + ": header '" + key + "'");
            }

            var body = Encoding.UTF8.GetBytes(golden.Body);
            AssertBytes(body, envelope.Body, golden.Name + ": body");
        }

        // nanoFramework's Assert.AreEqual fails when both strings are null
        static void AreEqualOrNull(string expected, string actual, string description)
        {
            if (expected == null)
            {
                Assert.IsNull(actual, description);
            }
            else
            {
                Assert.AreEqual(expected, actual, description);
            }
        }

        internal static void AssertBytes(byte[] expected, byte[] actual, string description)
        {
            Assert.IsNotNull(actual, description + " is null");
            Assert.AreEqual(expected.Length, actual.Length, description + ": length");
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], actual[i], description + ": byte " + i);
            }
        }
    }
}
