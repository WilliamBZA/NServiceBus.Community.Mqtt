using System;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// The ISO 8601 text of a <see cref="DateTime" /> as System.Text.Json writes and reads it. A device has no local time, so a time is
    /// written as UTC with a <c>Z</c>, and a text without an offset is read as UTC. nanoFramework's own parsing reads <c>.1</c> as one tick
    /// instead of a tenth of a second, and ignores an offset.
    /// </summary>
    internal static class DateTimeText
    {
        const long TicksPerSecond = 10000000L;
        const long TicksPerMinute = 600000000L;

        /// <summary>The text of a UTC time: seconds, a fraction without trailing zeros when there is one, and <c>Z</c>.</summary>
        public static string Format(DateTime value)
        {
            var text = new StringBuilder();
            AppendPadded(text, value.Year, 4);
            text.Append('-');
            AppendPadded(text, value.Month, 2);
            text.Append('-');
            AppendPadded(text, value.Day, 2);
            text.Append('T');
            AppendPadded(text, value.Hour, 2);
            text.Append(':');
            AppendPadded(text, value.Minute, 2);
            text.Append(':');
            AppendPadded(text, value.Second, 2);

            var fraction = value.Ticks % TicksPerSecond;
            if (fraction != 0)
            {
                var digits = fraction.ToString();
                text.Append('.');
                text.Append('0', 7 - digits.Length);

                var length = digits.Length;
                while (digits[length - 1] == '0')
                {
                    length--;
                }

                text.Append(digits.Substring(0, length));
            }

            text.Append('Z');
            return text.ToString();
        }

        /// <summary>
        /// Reads <c>yyyy-MM-dd</c>, optionally followed by <c>Thh:mm</c>, <c>:ss</c> and a fraction of any length (digits after the seventh are
        /// dropped), then optionally <c>Z</c> or an offset <c>+hh:mm</c> or <c>-hh:mm</c>. The result is UTC.
        /// </summary>
        public static bool TryParse(string text, out DateTime value)
        {
            value = default(DateTime);
            var index = 0;
            int year, month, day, hour = 0, minute = 0, second = 0;

            if (!ReadNumber(text, ref index, 4, out year) || !Read(text, ref index, '-')
                || !ReadNumber(text, ref index, 2, out month) || !Read(text, ref index, '-')
                || !ReadNumber(text, ref index, 2, out day))
            {
                return false;
            }

            long fraction = 0;
            long offset = 0;

            if (index < text.Length)
            {
                if (!Read(text, ref index, 'T') || !ReadNumber(text, ref index, 2, out hour) || !Read(text, ref index, ':') || !ReadNumber(text, ref index, 2, out minute))
                {
                    return false;
                }

                if (index < text.Length && text[index] == ':')
                {
                    index++;
                    if (!ReadNumber(text, ref index, 2, out second))
                    {
                        return false;
                    }

                    if (index < text.Length && text[index] == '.')
                    {
                        index++;
                        var digits = 0;
                        while (index < text.Length && text[index] >= '0' && text[index] <= '9')
                        {
                            if (digits < 7)
                            {
                                fraction = fraction * 10 + (text[index] - '0');
                            }

                            digits++;
                            index++;
                        }

                        // a point with no digits after it is accepted, as System.Text.Json accepts it
                        for (; digits < 7; digits++)
                        {
                            fraction *= 10;
                        }
                    }
                }

                if (index < text.Length)
                {
                    if (text[index] == 'Z')
                    {
                        index++;
                    }
                    else if (text[index] == '+' || text[index] == '-')
                    {
                        var sign = text[index] == '-' ? -1 : 1;
                        index++;
                        int offsetHours, offsetMinutes;
                        if (!ReadNumber(text, ref index, 2, out offsetHours) || !Read(text, ref index, ':') || !ReadNumber(text, ref index, 2, out offsetMinutes) || offsetHours > 23 || offsetMinutes > 59)
                        {
                            return false;
                        }

                        offset = sign * (offsetHours * 60L + offsetMinutes) * TicksPerMinute;
                    }
                }

                if (index != text.Length)
                {
                    return false;
                }
            }

            if (month < 1 || month > 12 || day < 1 || day > DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
            {
                return false;
            }

            try
            {
                // the constructor with a kind needs the ticks, so the whole seconds come from the date and time constructor
                var wholeSeconds = new DateTime(year, month, day, hour, minute, second);
                value = new DateTime(wholeSeconds.Ticks + fraction - offset, DateTimeKind.Utc);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                // before the earliest time nanoFramework can represent
                return false;
            }
        }

        static int DaysInMonth(int year, int month)
        {
            switch (month)
            {
                case 2:
                    return (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)) ? 29 : 28;
                case 4:
                case 6:
                case 9:
                case 11:
                    return 30;
                default:
                    return 31;
            }
        }

        static bool ReadNumber(string text, ref int index, int length, out int value)
        {
            value = 0;
            if (index + length > text.Length)
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                var digit = text[index + i];
                if (digit < '0' || digit > '9')
                {
                    return false;
                }

                value = value * 10 + (digit - '0');
            }

            index += length;
            return true;
        }

        static bool Read(string text, ref int index, char expected)
        {
            if (index < text.Length && text[index] == expected)
            {
                index++;
                return true;
            }

            return false;
        }

        internal static void AppendPadded(StringBuilder text, int value, int width)
        {
            var digits = value.ToString();
            text.Append('0', width - digits.Length);
            text.Append(digits);
        }
    }
}
