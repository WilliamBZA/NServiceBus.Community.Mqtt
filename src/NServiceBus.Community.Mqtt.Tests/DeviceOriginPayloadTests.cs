namespace NServiceBus.Community.Mqtt.Tests;

using System.Text;
using Faults;
using Hierarchy;
using Interop;
using NServiceBus.Transport.Mqtt;

/// <summary>
/// The golden payloads that a device writes (<c>WirePayloads.DeviceOrigin</c>): the device unit tests produce them byte for byte, and these tests
/// read them as the .NET transport and NServiceBus would, so a device message is understood by a .NET endpoint.
/// </summary>
[TestFixture]
public class DeviceOriginPayloadTests
{
    static IEnumerable<TestCaseData> MessagePayloads() =>
        WirePayloads.DeviceOrigin
            .Where(golden => golden.Message is not null)
            .Select(golden => new TestCaseData(golden).SetArgDisplayNames(golden.Name));

    static Dictionary<string, string> Decode(GoldenPayload golden) =>
        WireFormat.Decode(Encoding.UTF8.GetBytes(golden.Json)).Headers;

    [TestCaseSource(nameof(MessagePayloads))]
    public void The_body_is_read_by_the_nservicebus_serializer_to_the_message_the_device_sent(GoldenPayload golden)
    {
        var decoded = WireFormat.Decode(Encoding.UTF8.GetBytes(golden.Json));

        var message = WirePayloadTests.Deserialize(decoded.Body, golden.Message.GetType());

        // the serializer writes the same text for the message that was read as for the one the device sent
        Assert.That(Encoding.UTF8.GetString(WirePayloadTests.Serialize(message)), Is.EqualTo(Encoding.UTF8.GetString(WirePayloadTests.Serialize(golden.Message))));
    }

    [TestCaseSource(nameof(MessagePayloads))]
    public void The_standard_headers_use_the_names_nservicebus_defines(GoldenPayload golden)
    {
        var headers = Decode(golden);

        Assert.Multiple(() =>
        {
            Assert.That(headers, Does.ContainKey(Headers.MessageId));
            Assert.That(headers, Does.ContainKey(Headers.MessageIntent));
            Assert.That(headers, Does.ContainKey(Headers.EnclosedMessageTypes));
            Assert.That(headers, Does.ContainKey(Headers.ContentType));
            Assert.That(headers, Does.ContainKey(Headers.ConversationId));
            Assert.That(headers, Does.ContainKey(Headers.CorrelationId));
            Assert.That(headers, Does.ContainKey(Headers.OriginatingEndpoint));
            Assert.That(headers[Headers.ContentType], Is.EqualTo("application/json"));
        });
    }

    static IEnumerable<TestCaseData> MessagesTheDeviceWrote() =>
        WirePayloads.DeviceOrigin
            .Where(golden => golden.Message is not null && golden != WirePayloads.DeviceFailedMessage) // a failed message keeps the headers the .NET sender gave it
            .Select(golden => new TestCaseData(golden).SetArgDisplayNames(golden.Name));

    [TestCaseSource(nameof(MessagesTheDeviceWrote))]
    public void The_enclosed_message_types_name_the_message_type_without_an_assembly(GoldenPayload golden)
    {
        var types = Decode(golden)[Headers.EnclosedMessageTypes].Split(';');

        Assert.That(types[0], Is.EqualTo(golden.Message.GetType().FullName).Or.EqualTo(golden.Message.GetType().FullName!.Replace('+', '.')));
        Assert.That(types, Has.All.Not.Contain(","));
    }

    [Test]
    public void The_device_time_is_read_by_nservicebus()
    {
        var headers = Decode(WirePayloads.DeviceCommand);

        var sent = DateTimeOffsetHelper.ToDateTimeOffset(headers[Headers.TimeSent]);

        Assert.Multiple(() =>
        {
            Assert.That(headers[Headers.TimeSent], Is.EqualTo(WirePayloads.DeviceTimeText));
            Assert.That(sent, Is.EqualTo(new DateTimeOffset(2026, 10, 3, 10, 15, 30, TimeSpan.Zero).AddTicks(1234560)));
        });
    }

    [Test]
    public void A_command_sent_outside_a_handler_starts_a_conversation()
    {
        var headers = Decode(WirePayloads.DeviceCommand);

        Assert.Multiple(() =>
        {
            Assert.That(headers[Headers.MessageIntent], Is.EqualTo(nameof(MessageIntent.Send)));
            Assert.That(headers[Headers.CorrelationId], Is.EqualTo(headers[Headers.MessageId]));
            Assert.That(headers[Headers.ConversationId], Is.Not.EqualTo(headers[Headers.MessageId]));
            Assert.That(headers, Does.Not.ContainKey(Headers.RelatedTo));
            Assert.That(headers[Headers.ReplyToAddress], Is.EqualTo("Device_01"));
        });
    }

    [Test]
    public void The_reply_carries_the_saga_headers_so_that_the_saga_that_sent_the_request_handles_it()
    {
        var headers = Decode(WirePayloads.DeviceReply);

        Assert.Multiple(() =>
        {
            Assert.That(headers[Headers.MessageIntent], Is.EqualTo(nameof(MessageIntent.Reply)));
            Assert.That(headers[Headers.SagaId], Is.EqualTo("3f2e1d0c-9b8a-4796-8574-635241f0e1d2"));
            Assert.That(headers[Headers.SagaType], Is.EqualTo("Plant.ValveSaga, Plant, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));
            Assert.That(headers[Headers.RelatedTo], Is.EqualTo("a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d"));
            Assert.That(headers[Headers.CorrelationId], Is.EqualTo("a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d"));
            Assert.That(headers[Headers.ConversationId], Is.EqualTo("7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10"));
        });
    }

    [Test]
    public void The_reply_matches_the_saga_headers_a_dotnet_reply_has_in_its_golden()
    {
        // a .NET saga's request carries OriginatingSagaId and OriginatingSagaType, which the .NET reply golden also shows
        var dotNetReply = WirePayloadTests.HeadersOf(WirePayloads.SagaReply);
        var deviceReply = Decode(WirePayloads.DeviceReply);

        Assert.Multiple(() =>
        {
            Assert.That(deviceReply[Headers.SagaId], Is.EqualTo(dotNetReply[Headers.OriginatingSagaId]));
            Assert.That(deviceReply[Headers.SagaType], Is.EqualTo(dotNetReply[Headers.OriginatingSagaType]));
        });
    }

    [Test]
    public void The_event_lists_its_types_in_the_order_of_the_hierarchy()
    {
        var types = Decode(WirePayloads.DeviceEvent)[Headers.EnclosedMessageTypes];

        var hierarchy = string.Join(";", EventTypeHierarchy.Enumerate(typeof(Contracts.ValveOpened)).Select(type => type.FullName));

        Assert.Multiple(() =>
        {
            Assert.That(types, Is.EqualTo(hierarchy));
            Assert.That(Decode(WirePayloads.DeviceEvent)[Headers.MessageIntent], Is.EqualTo(nameof(MessageIntent.Publish)));
        });
    }

    [Test]
    public void The_failed_message_has_the_failure_headers_nservicebus_defines()
    {
        var headers = Decode(WirePayloads.DeviceFailedMessage);

        Assert.Multiple(() =>
        {
            Assert.That(headers[FaultsHeaderKeys.FailedQ], Is.EqualTo("Device_01"));
            Assert.That(headers, Does.ContainKey(FaultsHeaderKeys.ExceptionType));
            Assert.That(headers[FaultsHeaderKeys.ExceptionType], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(headers[FaultsHeaderKeys.Message], Is.EqualTo("The valve is stuck."));
            Assert.That(headers, Does.ContainKey(FaultsHeaderKeys.StackTrace));
            Assert.That(headers[Headers.ProcessingEndpoint], Is.EqualTo("Device_01"));
        });
    }

    [Test]
    public void The_failed_message_adds_only_failure_headers_to_the_original_ones()
    {
        var original = WirePayloadTests.HeadersOf(WirePayloads.Command);
        var failed = Decode(WirePayloads.DeviceFailedMessage);

        var added = failed.Keys.Except(original.Keys).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.EqualTo(new[]
            {
                FaultsHeaderKeys.ExceptionType,
                FaultsHeaderKeys.Message,
                FaultsHeaderKeys.StackTrace,
                FaultsHeaderKeys.FailedQ,
                Headers.ProcessingEndpoint,
                FaultsHeaderKeys.TimeOfFailure
            }.OrderBy(key => key, StringComparer.Ordinal).ToArray()));
            foreach (var (key, value) in original)
            {
                Assert.That(failed[key], Is.EqualTo(value), key);
            }
        });
    }

    [Test]
    public void The_time_of_failure_is_read_by_nservicebus()
    {
        var failed = Decode(WirePayloads.DeviceFailedMessage);

        var timeOfFailure = DateTimeOffsetHelper.ToDateTimeOffset(failed[FaultsHeaderKeys.TimeOfFailure]);

        Assert.That(timeOfFailure, Is.EqualTo(new DateTimeOffset(2026, 10, 3, 10, 15, 30, TimeSpan.Zero).AddTicks(1234560)));
    }

    [Test]
    public void The_failed_message_keeps_the_original_body_and_message_id()
    {
        var decoded = WireFormat.Decode(Encoding.UTF8.GetBytes(WirePayloads.DeviceFailedMessage.Json));
        var original = WireFormat.Decode(Encoding.UTF8.GetBytes(WirePayloads.Command.Json));

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Body, Is.EqualTo(original.Body));
            Assert.That(decoded.NativeMessageId, Is.EqualTo(original.NativeMessageId));
            Assert.That(decoded.Headers[Headers.MessageId], Is.EqualTo(original.Headers[Headers.MessageId]));
        });
    }
}
