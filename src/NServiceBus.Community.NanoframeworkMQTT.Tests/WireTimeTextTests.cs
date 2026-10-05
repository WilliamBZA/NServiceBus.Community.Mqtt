using System;
using Interop;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    /// <summary>
    /// The wire time and time span cases in <c>src/Interop/WirePayloads.cs</c>. The .NET unit tests check the same strings with NServiceBus's
    /// <c>DateTimeOffsetHelper</c> and <c>TimeSpan.ParseExact</c>.
    /// </summary>
    [TestClass]
    public class WireTimeTextTests
    {
        [TestMethod]
        public void Every_time_is_written_as_the_golden_wire_time()
        {
            foreach (WireTimeCase timeCase in WirePayloads.WireTimes)
            {
                Assert.AreEqual(timeCase.Text, WireTimeText.FormatTime(new DateTime(timeCase.Ticks, DateTimeKind.Utc)), "ticks " + timeCase.Ticks);
            }
        }

        [TestMethod]
        public void Every_time_span_is_written_as_the_golden_constant_format()
        {
            foreach (TimeSpanCase spanCase in WirePayloads.TimeSpans)
            {
                Assert.AreEqual(spanCase.Text, WireTimeText.FormatTimeSpan(new TimeSpan(spanCase.Ticks)), "ticks " + spanCase.Ticks);
            }
        }
    }
}
