using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        public static bool IsNumeric(char x)
        {
            return (x >= '0' && x <= '9');
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

        private static readonly char[] numStrings = new[]
        {
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'
        };
        public static string ToString(long number)
        {
            return number.ToString();
            Span<char> charArr = stackalloc char[19];
            int length = 1;
            long curr = 0;
            long theNum = number;
            do
            {
                curr = theNum % 10;
                charArr[19 - length] = numStrings[theNum % 10];
                theNum = theNum / 10;
            } while (curr != 0);

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
                if (!IsNumeric(str[pos]))
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
    }
}
