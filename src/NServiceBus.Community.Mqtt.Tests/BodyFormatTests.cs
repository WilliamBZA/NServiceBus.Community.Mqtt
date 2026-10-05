namespace NServiceBus.Community.Mqtt.Tests;

using System.Text;
using System.Text.Json;
using Contracts;
using Interop;

/// <summary>
/// The body cases in <c>src/Interop/WirePayloads.cs</c>: how System.Text.Json, which NServiceBus uses for a message body, writes and reads
/// the member types a device exchanges. The device unit tests check that the device package writes and reads the same text.
/// </summary>
[TestFixture]
public class BodyFormatTests
{
    static IEnumerable<TestCaseData> Doubles() =>
        WirePayloads.Doubles.Select(c => new TestCaseData(c).SetArgDisplayNames(c.Json));

    static IEnumerable<TestCaseData> DateTimes() =>
        WirePayloads.DateTimes.Select(c => new TestCaseData(c).SetArgDisplayNames(c.Json));

    static IEnumerable<TestCaseData> DateTimeParses() =>
        WirePayloads.DateTimeParses.Select(c => new TestCaseData(c).SetArgDisplayNames(c.Json.Length == 0 ? "(empty)" : c.Json));

    [TestCaseSource(nameof(Doubles))]
    public void A_double_is_written_as_the_shortest_text_that_reads_back(DoubleCase doubleCase)
    {
        Assert.That(JsonSerializer.Serialize(doubleCase.Value), Is.EqualTo(doubleCase.Json));
    }

    [TestCaseSource(nameof(Doubles))]
    public void A_double_is_read_back_exactly(DoubleCase doubleCase)
    {
        Assert.That(BitConverter.DoubleToInt64Bits(JsonSerializer.Deserialize<double>(doubleCase.Json)), Is.EqualTo(BitConverter.DoubleToInt64Bits(doubleCase.Value)));
    }

    [TestCaseSource(nameof(DateTimes))]
    public void A_utc_time_is_written_with_a_fraction_only_when_it_has_one(DateTimeCase timeCase)
    {
        var json = JsonSerializer.Serialize(new DateTime(timeCase.Ticks, DateTimeKind.Utc));

        Assert.That(json, Is.EqualTo("\"" + timeCase.Json + "\""));
    }

    [TestCaseSource(nameof(DateTimes))]
    public void A_utc_time_is_read_back_exactly(DateTimeCase timeCase)
    {
        Assert.That(JsonSerializer.Deserialize<DateTime>("\"" + timeCase.Json + "\"").Ticks, Is.EqualTo(timeCase.Ticks));
    }

    [TestCaseSource(nameof(DateTimeParses))]
    public void A_time_text_means_the_same_to_the_device_as_to_dotnet(DateTimeParseCase parseCase)
    {
        if (parseCase.UtcTicks < 0)
        {
            Assert.That(() => JsonSerializer.Deserialize<DateTime>("\"" + parseCase.Json + "\""), Throws.InstanceOf<JsonException>());
            return;
        }

        var parsed = JsonSerializer.Deserialize<DateTime>("\"" + parseCase.Json + "\"");

        // a text without an offset is read as it is, and the device reads it as UTC; one with an offset is converted to local time
        var utcTicks = parsed.Kind == DateTimeKind.Unspecified ? parsed.Ticks : parsed.ToUniversalTime().Ticks;
        Assert.That(utcTicks, Is.EqualTo(parseCase.UtcTicks));
    }

    [Test]
    public void The_dotnet_body_of_the_array_sample_is_the_golden_body()
    {
        Assert.That(Serialize(WirePayloads.CreateArrayMemberTypes()), Is.EqualTo(WirePayloads.ArrayMemberTypesDotNetBody));
    }

    [Test]
    public void The_array_sample_is_read_back_from_its_dotnet_body()
    {
        var message = (ArrayMemberTypes)Deserialize(WirePayloads.ArrayMemberTypesDotNetBody, typeof(ArrayMemberTypes));

        Assert.That(WirePayloads.Difference(WirePayloads.CreateArrayMemberTypes(), message), Is.Null);
    }

    // What the device writes is read by NServiceBus's serializer to the values the device set.
    [Test]
    public void The_device_body_of_the_array_sample_is_read_to_the_sample_values()
    {
        var message = (ArrayMemberTypes)Deserialize(WirePayloads.ArrayMemberTypesDeviceBody, typeof(ArrayMemberTypes));

        Assert.That(WirePayloads.Difference(WirePayloads.CreateArrayMemberTypes(), message), Is.Null);
    }

    [Test]
    public void The_device_body_of_the_all_member_types_sample_is_read_to_the_sample_values()
    {
        var message = (AllMemberTypes)Deserialize(WirePayloads.AllMemberTypesDeviceBody, typeof(AllMemberTypes));

        Assert.That(WirePayloads.Difference(WirePayloads.CreateAllMemberTypes(), message), Is.Null);
    }

    static string Serialize(object message) => Encoding.UTF8.GetString(WirePayloadTests.Serialize(message));

    static object Deserialize(string json, Type type) => WirePayloadTests.Deserialize(Encoding.UTF8.GetBytes(json), type);
}
