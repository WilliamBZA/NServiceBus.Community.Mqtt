using System;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// Writes a <see cref="double" /> as the shortest text that reads back as the same value, in the form System.Text.Json writes it: plain digits
    /// for decimal exponents from -4 to 16, and <c>d.dddE+XX</c> outside that.
    /// </summary>
    /// <remarks>
    /// nanoFramework's own <c>ToString</c> cannot be used: it keeps about 15 digits, and its <c>E</c> and <c>F</c> formats compute the digits beyond
    /// that inexactly, so a 17-digit result can read back as a neighbouring double. The exact decimal digits of the value are computed here with
    /// integer arithmetic, rounded to 1, 2, 3 ... digits, and the first rounding that <see cref="double.Parse(string)" /> reads back to the same
    /// value is written. That parser is exact (the tests compare it with .NET's for many values), and 17 digits always read back.
    /// </remarks>
    internal static class DoubleText
    {
        const int GuardedDigits = 24; // the exact digits that are generated: more than the 17 a double can need, plus a few to round with

        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("A double that is NaN or infinite has no JSON form.", "value");
            }

            if (value == 0)
            {
                return "0";
            }

            var bits = BitConverter.DoubleToInt64Bits(value);
            var negative = bits < 0;
            var magnitude = negative ? -value : value;

            var biased = (int)((bits >> 52) & 0x7FF);
            var fraction = (ulong)(bits & 0xFFFFFFFFFFFFFL);
            ulong mantissa;
            int binaryExponent;
            if (biased == 0)
            {
                mantissa = fraction; // subnormal
                binaryExponent = -1074;
            }
            else
            {
                mantissa = fraction | (1UL << 52);
                binaryExponent = biased - 1075;
            }

            string exact;
            int exactExponent;
            bool inexact;
            ExactDigits(mantissa, binaryExponent, out exact, out exactExponent, out inexact);

            string digits = null;
            var exponent = 0;
            for (var precision = 1; precision <= 17 && digits == null; precision++)
            {
                string rounded;
                int roundedExponent;
                Round(exact, exactExponent, inexact, precision, out rounded, out roundedExponent);

                if (precision == 17 || double.Parse(Scientific(rounded, roundedExponent)) == magnitude)
                {
                    digits = rounded;
                    exponent = roundedExponent;
                }
            }

            // a rounding that ends in zeros is the same number as a shorter one
            var length = digits.Length;
            while (length > 1 && digits[length - 1] == '0')
            {
                length--;
            }

            digits = digits.Substring(0, length);
            return Layout(digits, exponent, negative);
        }

        // The decimal digits of mantissa * 2^binaryExponent: about 25 of them, rounded down, the decimal exponent of the first one, and whether
        // the digits are not the whole value (so that something non-zero follows them).
        static void ExactDigits(ulong mantissa, int binaryExponent, out string digits, out int decimalExponent, out bool inexact)
        {
            var bitLength = 0;
            for (var rest = mantissa; rest != 0; rest >>= 1)
            {
                bitLength++;
            }

            // the value is in [2^top, 2^(top+1)), so its decimal exponent is the estimate or one more; scaling by 10^scale gives 25 or 26 digits
            var top = binaryExponent + bitLength - 1;
            var product = top * 0.30102999566398119521;
            var estimate = (int)product;
            if (product < estimate)
            {
                estimate--; // a floor, which System.Math would give, but it is a package of its own
            }

            var scale = GuardedDigits - estimate;

            // value * 10^scale = mantissa * 2^(binaryExponent + scale) * 5^scale
            var number = BigUInt.FromUlong(mantissa);
            var shift = binaryExponent + scale;
            inexact = false;

            // a left shift comes before a division, so that nothing is lost twice; a right shift comes after a multiplication
            if (shift >= 0)
            {
                number.ShiftLeft(shift);
            }

            if (scale >= 0)
            {
                number.MultiplyByPowerOfFive(scale);
            }
            else if (number.DivideByPowerOfFive(-scale))
            {
                inexact = true;
            }

            if (shift < 0 && number.ShiftRight(-shift))
            {
                inexact = true;
            }

            digits = number.ToDecimalString();
            decimalExponent = digits.Length - 1 - scale;
        }

        // Rounds the digits to the given number of digits, to nearest, ties to even. 'inexact' says that the digits are the value rounded down.
        static void Round(string digits, int exponent, bool inexact, int precision, out string rounded, out int roundedExponent)
        {
            roundedExponent = exponent;
            var kept = new char[precision];
            for (var i = 0; i < precision; i++)
            {
                kept[i] = digits[i];
            }

            var next = digits[precision];
            var roundUp = false;
            if (next > '5')
            {
                roundUp = true;
            }
            else if (next == '5')
            {
                var beyond = inexact;
                for (var i = precision + 1; i < digits.Length && !beyond; i++)
                {
                    if (digits[i] != '0')
                    {
                        beyond = true;
                    }
                }

                roundUp = beyond || ((kept[precision - 1] - '0') % 2 == 1);
            }

            if (roundUp)
            {
                var index = precision - 1;
                while (index >= 0 && kept[index] == '9')
                {
                    kept[index] = '0';
                    index--;
                }

                if (index >= 0)
                {
                    kept[index]++;
                }
                else
                {
                    // 99...9 became 100...0, one decade up
                    kept[0] = '1';
                    roundedExponent++;
                }
            }

            rounded = new string(kept);
        }

        static string Scientific(string digits, int exponent)
        {
            var text = new StringBuilder();
            text.Append(digits[0]);
            if (digits.Length > 1)
            {
                text.Append('.');
                text.Append(digits.Substring(1));
            }

            text.Append('E');
            text.Append(exponent.ToString());
            return text.ToString();
        }

        // lays out the shortest digits as System.Text.Json does
        static string Layout(string digits, int exponent, bool negative)
        {
            var result = new StringBuilder();
            if (negative)
            {
                result.Append('-');
            }

            if (exponent >= 17 || exponent <= -5)
            {
                result.Append(digits[0]);
                if (digits.Length > 1)
                {
                    result.Append('.');
                    result.Append(digits.Substring(1));
                }

                result.Append('E');
                result.Append(exponent < 0 ? '-' : '+');
                var absolute = exponent < 0 ? -exponent : exponent;
                if (absolute < 10)
                {
                    result.Append('0');
                }

                result.Append(absolute.ToString());
            }
            else if (exponent >= 0)
            {
                if (digits.Length <= exponent + 1)
                {
                    result.Append(digits);
                    result.Append('0', exponent + 1 - digits.Length);
                }
                else
                {
                    result.Append(digits.Substring(0, exponent + 1));
                    result.Append('.');
                    result.Append(digits.Substring(exponent + 1));
                }
            }
            else
            {
                result.Append("0.");
                result.Append('0', -exponent - 1);
                result.Append(digits);
            }

            return result.ToString();
        }
    }
}
