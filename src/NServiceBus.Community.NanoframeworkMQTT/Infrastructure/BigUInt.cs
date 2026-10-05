using System;
using System.Text;

namespace NServiceBus.Community.NanoframeworkMQTT.Infrastructure
{
    /// <summary>
    /// An unsigned integer of any size, with just the operations that writing a double as exact decimal digits needs: multiply and divide by a
    /// number that fits in 32 bits, shifts, and conversion to decimal text.
    /// </summary>
    internal sealed class BigUInt
    {
        uint[] words;
        int count;

        BigUInt(uint[] words, int count)
        {
            this.words = words;
            this.count = count;
        }

        public static BigUInt FromUlong(ulong value)
        {
            var words = new uint[4];
            words[0] = (uint)value;
            words[1] = (uint)(value >> 32);
            var count = words[1] != 0 ? 2 : (words[0] != 0 ? 1 : 0);
            return new BigUInt(words, count);
        }

        public bool IsZero
        {
            get { return count == 0; }
        }

        /// <summary>Multiplies by 5 to the power <paramref name="exponent" />.</summary>
        public void MultiplyByPowerOfFive(int exponent)
        {
            while (exponent >= 13)
            {
                MultiplyBy(1220703125u); // 5^13, the largest power of 5 that fits in 32 bits
                exponent -= 13;
            }

            if (exponent > 0)
            {
                MultiplyBy(PowerOfFive(exponent));
            }
        }

        /// <summary>Divides by 5 to the power <paramref name="exponent" />, rounding down. Returns whether anything was lost, that is whether the division was not exact.</summary>
        public bool DivideByPowerOfFive(int exponent)
        {
            var inexact = false;
            while (exponent >= 13)
            {
                if (DivideBy(1220703125u) != 0)
                {
                    inexact = true;
                }

                exponent -= 13;
            }

            if (exponent > 0 && DivideBy(PowerOfFive(exponent)) != 0)
            {
                inexact = true;
            }

            return inexact;
        }

        public void ShiftLeft(int bits)
        {
            if (count == 0 || bits == 0)
            {
                return;
            }

            var wordShift = bits / 32;
            var bitShift = bits % 32;
            var shifted = new uint[count + wordShift + 1];
            for (var i = 0; i < count; i++)
            {
                var value = (ulong)words[i] << bitShift;
                shifted[i + wordShift] |= (uint)value;
                shifted[i + wordShift + 1] |= (uint)(value >> 32);
            }

            words = shifted;
            count = shifted.Length;
            Trim();
        }

        /// <summary>Shifts right, rounding down. Returns whether any bit that was set was shifted out.</summary>
        public bool ShiftRight(int bits)
        {
            if (count == 0 || bits == 0)
            {
                return false;
            }

            var wordShift = bits / 32;
            var bitShift = bits % 32;
            var lost = false;

            for (var i = 0; i < wordShift && i < count; i++)
            {
                if (words[i] != 0)
                {
                    lost = true;
                }
            }

            if (wordShift >= count)
            {
                count = 0;
                return lost;
            }

            if (bitShift != 0 && (words[wordShift] & ((1u << bitShift) - 1)) != 0)
            {
                lost = true;
            }

            var shifted = new uint[count - wordShift];
            for (var i = wordShift; i < count; i++)
            {
                var value = (ulong)words[i] >> bitShift;
                if (i + 1 < count)
                {
                    value |= (ulong)words[i + 1] << (32 - bitShift);
                }

                shifted[i - wordShift] = (uint)value;
            }

            words = shifted;
            count = shifted.Length;
            Trim();
            return lost;
        }

        /// <summary>The decimal digits of the value, without leading zeros ("0" for zero). The value is used up.</summary>
        public string ToDecimalString()
        {
            if (count == 0)
            {
                return "0";
            }

            // nine decimal digits at a time, least significant first
            var chunks = new uint[count * 10 / 9 + 2];
            var chunkCount = 0;
            while (count > 0)
            {
                chunks[chunkCount++] = DivideBy(1000000000u);
            }

            var text = new StringBuilder();
            text.Append(chunks[chunkCount - 1].ToString());
            for (var i = chunkCount - 2; i >= 0; i--)
            {
                var digits = chunks[i].ToString();
                text.Append('0', 9 - digits.Length);
                text.Append(digits);
            }

            return text.ToString();
        }

        void MultiplyBy(uint factor)
        {
            ulong carry = 0;
            for (var i = 0; i < count; i++)
            {
                var product = (ulong)words[i] * factor + carry;
                words[i] = (uint)product;
                carry = product >> 32;
            }

            if (carry != 0)
            {
                if (count == words.Length)
                {
                    var grown = new uint[words.Length * 2];
                    Array.Copy(words, grown, count);
                    words = grown;
                }

                words[count++] = (uint)carry;
            }
        }

        // divides in place, rounding down, and returns the remainder
        uint DivideBy(uint divisor)
        {
            ulong remainder = 0;
            for (var i = count - 1; i >= 0; i--)
            {
                var current = (remainder << 32) | words[i];
                words[i] = (uint)(current / divisor);
                remainder = current % divisor;
            }

            Trim();
            return (uint)remainder;
        }

        void Trim()
        {
            while (count > 0 && words[count - 1] == 0)
            {
                count--;
            }
        }

        static uint PowerOfFive(int exponent)
        {
            uint result = 1;
            for (var i = 0; i < exponent; i++)
            {
                result *= 5;
            }

            return result;
        }
    }
}
