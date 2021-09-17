using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Util
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// <see cref="Span{T}"/> polyfills that should be deleted once we drop .NET Standard 2.0 support.
    /// </summary>
    internal static class SpanHacks
    {
        /// <summary>
        /// Parses an <see cref="int"/> into a mutable <see cref="Span{T}"/>
        /// </summary>
        /// <param name="i">The integer to convert.</param>
        /// <param name="span">An 11 character span.</param>
        /// <returns>The number of characters actually written to the span.</returns>
        /// <remarks>
        /// <see cref="span"/> MUST BE 11 CHARACTERS in length, as that's the maximum length
        /// of an integer represented as a string.
        /// </remarks>
        public static int AsCharSpan(this int i, Span<char> span)
        {
            if (i == 0)
            {
                span[0] = '0';
                return 1;
            }
            else if (i == int.MinValue)
            {
                // special case - can't be converted back into positive integer
                var negSpan = "-2147483648".AsSpan();
                negSpan.CopyTo(span);
                return 11;
            }

            var negative = false;
            if (i < 0)
            {
                negative = true;
                i = -i;
            }

            // max string length is 10 for integer plus 1 for negative sign
            Span<char> startSpan = stackalloc char[11];
            var count = 0;
            while (i != 0)
            {
                var rem = i % 10;
                startSpan[count++] = (char)((rem > 9) ? (rem - 10) + 'a' : rem + '0');
                i = i / 10;
            }

            if (negative)
            {
                startSpan[count++] = '-';
            }

            // reverse the string
            var b = 0;
            for (var a = count - 1; a >= 0; a--)
            {
                span[b++] = startSpan[a];
            }

            return count;
        }

        /// <summary>
        /// Parses an <see cref="long"/> into a mutable <see cref="Span{T}"/>
        /// </summary>
        /// <param name="i">The long integer to convert.</param>
        /// <param name="span">A 20 character span.</param>
        /// <returns>The number of characters actually written to the span.</returns>
        /// <remarks>
        /// <see cref="span"/> MUST BE 20 CHARACTERS in length, as that's the maximum length
        /// of a long integer represented as a string.
        /// </remarks>
        public static int AsCharSpan(this long i, Span<char> span)
        {
            if (i == 0)
            {
                span[0] = '0';
                return 1;
            }
            else if (i == long.MinValue)
            {
                // special case - can't be converted back into positive integer
                var negSpan = "-9223372036854775808".AsSpan();
                negSpan.CopyTo(span);
                return 11;
            }

            var negative = false;
            if (i < 0)
            {
                negative = true;
                i = -i;
            }

            // max string length is 10 for integer plus 1 for negative sign
            Span<char> startSpan = stackalloc char[20];
            var count = 0;
            while (i != 0)
            {
                var rem = i % 10;
                startSpan[count++] = (char)((rem > 9) ? (rem - 10) + 'a' : rem + '0');
                i = i / 10;
            }

            if (negative)
            {
                startSpan[count++] = '-';
            }

            // reverse the string
            var b = 0;
            for (var a = count - 1; a >= 0; a--)
            {
                span[b++] = startSpan[a];
            }

            return count;

        }

        /// <summary>
        /// Parses an integer from a string.
        /// </summary>
        /// <remarks>
        /// PERFORMS NO INPUT VALIDATION.
        /// </remarks>
        /// <param name="str">The span of input characters.</param>
        /// <returns>An <see cref="int"/>.</returns>
        public static int Parse(ReadOnlySpan<char> str)
        {
            if (TryParse(str, out var i))
                return i;
            throw new FormatException($"[{str.ToString()}] is now a valid numeric format");
        }

        /// <summary>
        /// Parses an integer from a string.
        /// </summary>
        /// <remarks>
        /// PERFORMS NO INPUT VALIDATION.
        /// </remarks>
        /// <param name="str">The span of input characters.</param>
        /// <param name="returnValue">The parsed integer, if any.</param>
        /// <returns>An <see cref="int"/>.</returns>
        public static bool TryParse(ReadOnlySpan<char> str, out int returnValue)
        {
            var pos = 0;
            returnValue = 0;
            var sign = 1;
            if (str[0] == '-')
            {
                sign = -1;
                pos++;
            }

            for (; pos < str.Length; pos++)
            {
                if (!char.IsNumber(str[pos]))
                    return false;
                returnValue = returnValue * 10 + str[pos] - '0';
            }

            returnValue = sign * returnValue;

            return true;
        }

        /// <summary>
        /// Performs <see cref="string.ToLowerInvariant"/> without having to
        /// allocate a new <see cref="string"/> first.
        /// </summary>
        /// <param name="input">The set of characters to be lower-cased</param>
        /// <returns>A new string.</returns>
        public static string ToLowerInvariant(ReadOnlySpan<char> input)
        {
            Span<char> output = stackalloc char[input.Length];
            for (var i = 0; i < input.Length; i++)
            {
                output[i] = char.ToLowerInvariant(input[i]);
            }
            return output.ToString();
        }

        public static int CopySpans(ReadOnlySpan<char> inSpan, Span<char> outSpan, int outIndex)
        {
            for (var i = 0; i < inSpan.Length; i++)
            {
                outSpan[outIndex++] = inSpan[i];
            }

            return inSpan.Length;
        }
    }
}
