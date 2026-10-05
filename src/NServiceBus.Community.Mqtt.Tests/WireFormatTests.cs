namespace NServiceBus.Community.Mqtt.Tests;

using System.Text;
using System.Text.Json;
using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;

[TestFixture]
public class WireFormatTests
{
    [Test]
    public void Should_round_trip_unicode_headers()
    {
        var headers = new Dictionary<string, string>
        {
            [Headers.MessageId] = "id-1",
            ["MyHeader"] = "MyValue",
            ["a-😅-B7"] = "a-😍-b",
            ["日本語"] = "ünï"
        };

        var decoded = WireFormat.Decode(WireFormat.Encode(new OutgoingMessage("id-1", headers, new byte[] { 1, 2, 3 })));

        Assert.That(decoded.Headers, Is.EqualTo(headers));
    }

    [Test]
    public void Should_round_trip_a_binary_body()
    {
        var body = new byte[] { 0, 255, 1, 2 };

        var decoded = WireFormat.Decode(WireFormat.Encode(new OutgoingMessage("id-1", WithMessageId("id-1"), body)));

        Assert.That(decoded.Body, Is.EqualTo(body));
    }

    [Test]
    public void Should_round_trip_every_byte_value()
    {
        var body = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        var decoded = WireFormat.Decode(WireFormat.Encode(new OutgoingMessage("id-1", WithMessageId("id-1"), body)));

        Assert.That(decoded.Body, Is.EqualTo(body));
    }

    [Test]
    public void Should_round_trip_an_empty_body_without_treating_it_as_a_poison_message()
    {
        var decoded = WireFormat.Decode(WireFormat.Encode(new OutgoingMessage("id-1", WithMessageId("id-1"), ReadOnlyMemory<byte>.Empty)));

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Body, Is.Empty);
            Assert.That(decoded.NativeMessageId, Is.EqualTo("id-1"));
        });
    }

    [Test]
    public void Should_use_the_message_id_header_as_the_native_message_id()
    {
        var decoded = WireFormat.Decode(WireFormat.Encode(new OutgoingMessage("outgoing-id", WithMessageId("header-id"), new byte[] { 1 })));

        Assert.That(decoded.NativeMessageId, Is.EqualTo("header-id"));
    }

    [Test]
    public void Should_keep_the_json_format_of_1x()
    {
        var message = new OutgoingMessage("id-1", new Dictionary<string, string> { [Headers.MessageId] = "id-1", ["a"] = "b" }, new byte[] { 0, 1, 2, 3 });

        var payload = Encoding.UTF8.GetString(WireFormat.Encode(message));

        Assert.That(payload, Is.EqualTo("""{"Id":"id-1","Headers":{"NServiceBus.MessageId":"id-1","a":"b"},"Body":"AAECAw=="}"""));
    }

    [Test]
    public void Should_produce_the_same_bytes_as_the_1x_serializer_call()
    {
        var headers = new Dictionary<string, string> { [Headers.MessageId] = "id-1", ["a-😅-B7"] = "a-😍-b" };
        var body = new byte[] { 0, 255, 1, 2 };

        var oneDotX = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new MessageWrapper { Id = "id-1", Headers = headers, Body = body }));

        Assert.That(WireFormat.Encode(new OutgoingMessage("id-1", headers, body)), Is.EqualTo(oneDotX));
    }

    [Test]
    public void Should_decode_a_payload_written_by_1x()
    {
        var payload = Encoding.UTF8.GetBytes("""{"Id":"id-1","Headers":{"NServiceBus.MessageId":"id-1","a":"b"},"Body":"AAECAw=="}""");

        var decoded = WireFormat.Decode(payload);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.NativeMessageId, Is.EqualTo("id-1"));
            Assert.That(decoded.Headers, Is.EqualTo(new Dictionary<string, string> { [Headers.MessageId] = "id-1", ["a"] = "b" }));
            Assert.That(decoded.Body, Is.EqualTo(new byte[] { 0, 1, 2, 3 }));
        });
    }

    [Test]
    public void Should_fill_in_a_missing_message_id_header_from_the_outgoing_message_id()
    {
        var decoded = WireFormat.Decode(WireFormat.Encode(new OutgoingMessage("outgoing-id", new Dictionary<string, string> { ["a"] = "b" }, new byte[] { 1 })));

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Headers[Headers.MessageId], Is.EqualTo("outgoing-id"));
            Assert.That(decoded.Headers["a"], Is.EqualTo("b"));
        });
    }

    [Test]
    public void Should_fill_in_an_empty_message_id_header_from_the_outgoing_message_id()
    {
        var decoded = WireFormat.Decode(WireFormat.Encode(new OutgoingMessage("outgoing-id", new Dictionary<string, string> { [Headers.MessageId] = string.Empty }, new byte[] { 1 })));

        Assert.That(decoded.Headers[Headers.MessageId], Is.EqualTo("outgoing-id"));
    }

    [Test]
    public void Should_keep_an_existing_message_id_header()
    {
        var decoded = WireFormat.Decode(WireFormat.Encode(new OutgoingMessage("outgoing-id", WithMessageId("original"), new byte[] { 1 })));

        Assert.That(decoded.Headers[Headers.MessageId], Is.EqualTo("original"));
    }

    [Test]
    public void Should_not_change_the_callers_headers_when_it_fills_in_the_message_id()
    {
        var headers = new Dictionary<string, string> { ["a"] = "b" };

        WireFormat.Encode(new OutgoingMessage("outgoing-id", headers, new byte[] { 1 }));

        Assert.That(headers, Is.EqualTo(new Dictionary<string, string> { ["a"] = "b" }));
    }

    [Test]
    public void Should_assign_a_message_id_when_the_payload_has_none()
    {
        var payload = Encoding.UTF8.GetBytes("""{"Headers":{"a":"b"},"Body":"AQ=="}""");

        var decoded = WireFormat.Decode(payload);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.NativeMessageId, Is.Not.Empty);
            Assert.That(Guid.TryParse(decoded.NativeMessageId, out _), Is.True);
            Assert.That(decoded.Headers[Headers.MessageId], Is.EqualTo(decoded.NativeMessageId), "the handler sees the same ID in the headers");
        });
    }

    [Test]
    public void Should_return_new_instances_for_every_decode_of_the_same_payload()
    {
        var payload = WireFormat.Encode(new OutgoingMessage("id-1", WithMessageId("id-1"), new byte[] { 1, 2, 3 }));

        var first = WireFormat.Decode(payload);
        first.Headers["added"] = "x";
        first.Headers[Headers.MessageId] = "changed";
        first.Body[0] = 99;
        var second = WireFormat.Decode(payload);

        Assert.Multiple(() =>
        {
            Assert.That(second.Headers, Is.EqualTo(WithMessageId("id-1")));
            Assert.That(second.Body, Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [TestCase("")]
    [TestCase("garbage")]
    [TestCase("not json at all {")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("42")]
    [TestCase("{}")]
    [TestCase("""{"Headers":{"a":"b"}}""")]
    [TestCase("""{"Body":"AQ=="}""")]
    [TestCase("""{"Headers":null,"Body":"AQ=="}""")]
    [TestCase("""{"Headers":{},"Body":null}""")]
    [TestCase("""{"Headers":{},"Body":"!!! not base64"}""")]
    [TestCase("""{"Headers":["a"],"Body":"AQ=="}""")]
    [TestCase("""{"Headers":{"a":"b"},"Body":"AQ==""")]
    public void Should_report_a_garbage_payload_as_a_decode_failure(string payload)
    {
        Assert.Throws<MessageDecodeException>(() => WireFormat.Decode(Encoding.UTF8.GetBytes(payload)));
    }

    [Test]
    public void Should_report_random_bytes_as_a_decode_failure()
    {
        var random = new byte[] { 0xFF, 0xFE, 0x00, 0x9C, 0x80, 0x7F, 0x01 };

        Assert.Throws<MessageDecodeException>(() => WireFormat.Decode(random));
    }

    [Test]
    public void Should_keep_the_underlying_parse_error_for_a_malformed_payload()
    {
        var exception = Assert.Throws<MessageDecodeException>(() => WireFormat.Decode("garbage"u8));

        Assert.That(exception!.InnerException, Is.InstanceOf<JsonException>());
    }

    static Dictionary<string, string> WithMessageId(string messageId) => new() { [Headers.MessageId] = messageId };
}
