namespace NServiceBus.Community.Mqtt.Tests;

using System.Text;
using Interop;
using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
using NServiceBus.Serialization;
using NServiceBus.Settings;
using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;

/// <summary>
/// The golden wire payloads in <c>src/Interop/WirePayloads.cs</c>. The payloads that the .NET transport produces are checked here byte for byte,
/// and the device unit tests decode the very same text. If the .NET encoding changes, these tests fail and the golden has to be updated on purpose,
/// which in turn fails the device tests until the device reads the new form.
/// </summary>
[TestFixture]
public class WirePayloadTests
{
    static IEnumerable<TestCaseData> DotNetOrigin() =>
        WirePayloads.DotNetOrigin.Select(golden => new TestCaseData(golden).SetArgDisplayNames(golden.Name));

    [TestCaseSource(nameof(DotNetOrigin))]
    public void Encode_produces_the_golden_payload_byte_for_byte(GoldenPayload golden)
    {
        var payload = WireFormat.Encode(new OutgoingMessage(golden.Id, HeadersOf(golden), BodyOf(golden)));

        Assert.That(Encoding.UTF8.GetString(payload), Is.EqualTo(golden.Json));
    }

    [TestCaseSource(nameof(DotNetOrigin))]
    public void The_golden_body_is_what_the_nservicebus_serializer_writes(GoldenPayload golden)
    {
        Assert.That(Encoding.UTF8.GetString(BodyOf(golden)), Is.EqualTo(golden.Body));
    }

    [TestCaseSource(nameof(DotNetOrigin))]
    public void Decode_reads_back_the_ids_headers_and_body_of_the_golden_payload(GoldenPayload golden)
    {
        var decoded = WireFormat.Decode(Encoding.UTF8.GetBytes(golden.Json));

        Assert.Multiple(() =>
        {
            Assert.That(decoded.NativeMessageId, Is.EqualTo(golden.Id));
            Assert.That(decoded.Headers, Is.EqualTo(HeadersOf(golden)));
            Assert.That(Encoding.UTF8.GetString(decoded.Body), Is.EqualTo(golden.Body));
        });
    }

    static IEnumerable<TestCaseData> DecodeCases() =>
        WirePayloads.DecodeCases.Select(golden => new TestCaseData(golden).SetArgDisplayNames(golden.Name));

    static IEnumerable<TestCaseData> InvalidPayloads() =>
        WirePayloads.InvalidPayloads.Select(invalid => new TestCaseData(invalid).SetArgDisplayNames(invalid.Name));

    // These are the forms a device reader has to accept as well. A case that .NET refused would mean the device accepts what .NET does not.
    [TestCaseSource(nameof(DecodeCases))]
    public void Decode_accepts_the_hand_written_payload(GoldenPayload golden)
    {
        var decoded = WireFormat.Decode(Encoding.UTF8.GetBytes(golden.Json));

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Headers, Is.EqualTo(HeadersOf(golden)));
            Assert.That(Encoding.UTF8.GetString(decoded.Body), Is.EqualTo(golden.Body));
        });
    }

    static IEnumerable<TestCaseData> DeviceOrigin() =>
        WirePayloads.DeviceOrigin.Select(golden => new TestCaseData(golden).SetArgDisplayNames(golden.Name));

    // The payloads the device writes are read by the .NET transport as they are, with raw UTF-8 and only the escapes the device uses.
    [TestCaseSource(nameof(DeviceOrigin))]
    public void Decode_reads_the_device_origin_golden_payload(GoldenPayload golden)
    {
        var decoded = WireFormat.Decode(Encoding.UTF8.GetBytes(golden.Json));

        Assert.Multiple(() =>
        {
            Assert.That(decoded.NativeMessageId, Is.EqualTo(golden.Id));
            Assert.That(decoded.Headers, Is.EqualTo(HeadersOf(golden)));
            Assert.That(Encoding.UTF8.GetString(decoded.Body), Is.EqualTo(golden.Body));
        });
    }

    [TestCaseSource(nameof(InvalidPayloads))]
    public void Decode_rejects_the_invalid_payload(InvalidPayload invalid)
    {
        Assert.That(() => WireFormat.Decode(Encoding.UTF8.GetBytes(invalid.Json)), Throws.TypeOf<MessageDecodeException>());
    }

    [Test]
    public void The_golden_payloads_are_valid_utf8_with_no_byte_order_mark()
    {
        foreach (var golden in WirePayloads.DotNetOrigin)
        {
            Assert.That(Encoding.UTF8.GetBytes(golden.Json).Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), golden.Name);
        }
    }

    [Test]
    public void The_all_member_types_golden_body_deserializes_to_the_sample_values()
    {
        var golden = WirePayloads.AllMemberTypesPayload;

        var message = (Contracts.AllMemberTypes)Deserialize(Encoding.UTF8.GetBytes(golden.Body), typeof(Contracts.AllMemberTypes));

        Assert.That(WirePayloads.Difference(WirePayloads.CreateAllMemberTypes(), message), Is.Null);
    }

    internal static Dictionary<string, string> HeadersOf(GoldenPayload golden)
    {
        var headers = new Dictionary<string, string>();
        for (var i = 0; i < golden.Headers.Length; i += 2)
        {
            headers[golden.Headers[i]] = golden.Headers[i + 1];
        }

        return headers;
    }

    static IMessageSerializer Serializer()
    {
        var definition = new SystemJsonSerializer();
        var factory = definition.Configure(new SettingsHolder());
        return factory(new MessageMapper());
    }

    internal static byte[] Serialize(object message)
    {
        using var stream = new MemoryStream();
        Serializer().Serialize(message, stream);
        return stream.ToArray();
    }

    internal static object Deserialize(byte[] body, Type type) =>
        Serializer().Deserialize(body, [type])[0];

    static byte[] BodyOf(GoldenPayload golden) => golden.Message is null ? [] : Serialize(golden.Message);
}
