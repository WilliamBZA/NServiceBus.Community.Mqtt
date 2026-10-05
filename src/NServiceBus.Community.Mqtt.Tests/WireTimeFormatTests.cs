namespace NServiceBus.Community.Mqtt.Tests;

using System.Globalization;
using Interop;

/// <summary>
/// The wire time and time span cases in <c>src/Interop/WirePayloads.cs</c>: the text a device writes into <c>NServiceBus.TimeSent</c>,
/// <c>NServiceBus.TimeOfFailure</c> and <c>NServiceBus.TimeToBeReceived</c>, read by the helpers NServiceBus itself uses.
/// </summary>
[TestFixture]
public class WireTimeFormatTests
{
    static IEnumerable<TestCaseData> WireTimes() =>
        WirePayloads.WireTimes.Select(c => new TestCaseData(c).SetArgDisplayNames(c.Text));

    static IEnumerable<TestCaseData> TimeSpans() =>
        WirePayloads.TimeSpans.Select(c => new TestCaseData(c).SetArgDisplayNames(c.Text));

    [TestCaseSource(nameof(WireTimes))]
    public void The_wire_time_is_read_by_nservicebus_to_the_utc_time(WireTimeCase timeCase)
    {
        var parsed = DateTimeOffsetHelper.ToDateTimeOffset(timeCase.Text);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.UtcTicks, Is.EqualTo(timeCase.ParsedTicks));
            Assert.That(parsed.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [TestCaseSource(nameof(WireTimes))]
    public void The_wire_time_is_what_nservicebus_writes(WireTimeCase timeCase)
    {
        Assert.That(DateTimeOffsetHelper.ToWireFormattedString(new DateTimeOffset(timeCase.Ticks, TimeSpan.Zero)), Is.EqualTo(timeCase.Text));
    }

    [TestCaseSource(nameof(TimeSpans))]
    public void The_time_span_is_read_by_parse_exact(TimeSpanCase spanCase)
    {
        Assert.That(TimeSpan.ParseExact(spanCase.Text, "c", CultureInfo.InvariantCulture).Ticks, Is.EqualTo(spanCase.Ticks));
    }

    [TestCaseSource(nameof(TimeSpans))]
    public void The_time_span_is_what_the_constant_format_writes(TimeSpanCase spanCase)
    {
        Assert.That(new TimeSpan(spanCase.Ticks).ToString("c", CultureInfo.InvariantCulture), Is.EqualTo(spanCase.Text));
    }
}
