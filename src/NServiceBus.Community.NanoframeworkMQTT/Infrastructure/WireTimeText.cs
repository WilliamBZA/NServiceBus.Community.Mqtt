using System;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// The text of the time headers NServiceBus defines. A time is written in the wire time format, <c>yyyy-MM-dd HH:mm:ss:ffffff Z</c> in UTC, which
    /// has to be written by hand because nanoFramework's <see cref="DateTime" /> formatting is limited. A time span is written in the constant
    /// format, <c>[-][d.]hh:mm:ss[.fffffff]</c>, as <c>TimeSpan.ToString("c")</c> does in .NET.
    /// </summary>
    internal static class WireTimeText
    {
        const long TicksPerMicrosecond = 10L;
        const ulong TicksPerSecond = 10000000UL;

        /// <summary>The wire format of a UTC time. Ticks below a microsecond are cut off, as the .NET format does.</summary>
        public static string FormatTime(DateTime utc)
        {
            var text = new StringBuilder();
            DateTimeText.AppendPadded(text, utc.Year, 4);
            text.Append('-');
            DateTimeText.AppendPadded(text, utc.Month, 2);
            text.Append('-');
            DateTimeText.AppendPadded(text, utc.Day, 2);
            text.Append(' ');
            DateTimeText.AppendPadded(text, utc.Hour, 2);
            text.Append(':');
            DateTimeText.AppendPadded(text, utc.Minute, 2);
            text.Append(':');
            DateTimeText.AppendPadded(text, utc.Second, 2);
            text.Append(':');
            DateTimeText.AppendPadded(text, (int)(utc.Ticks % (long)TicksPerSecond / TicksPerMicrosecond), 6);
            text.Append(" Z");
            return text.ToString();
        }

        /// <summary>The constant format of a time span.</summary>
        public static string FormatTimeSpan(TimeSpan value)
        {
            var ticks = value.Ticks;

            // the magnitude as an unsigned number, because the minimum value has no positive counterpart in a long
            var negative = ticks < 0;
            var magnitude = negative ? (ulong)(-(ticks + 1)) + 1UL : (ulong)ticks;

            var fraction = (int)(magnitude % TicksPerSecond);
            var totalSeconds = magnitude / TicksPerSecond;
            var seconds = (int)(totalSeconds % 60UL);
            var totalMinutes = totalSeconds / 60UL;
            var minutes = (int)(totalMinutes % 60UL);
            var totalHours = totalMinutes / 60UL;
            var hours = (int)(totalHours % 24UL);
            var days = (int)(totalHours / 24UL);

            var text = new StringBuilder();
            if (negative)
            {
                text.Append('-');
            }

            if (days != 0)
            {
                text.Append(days.ToString());
                text.Append('.');
            }

            DateTimeText.AppendPadded(text, hours, 2);
            text.Append(':');
            DateTimeText.AppendPadded(text, minutes, 2);
            text.Append(':');
            DateTimeText.AppendPadded(text, seconds, 2);
            if (fraction != 0)
            {
                text.Append('.');
                DateTimeText.AppendPadded(text, fraction, 7);
            }

            return text.ToString();
        }
    }
}
