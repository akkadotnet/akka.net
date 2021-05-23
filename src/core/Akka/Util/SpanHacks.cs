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
        /// Parses an integer from a string.
        /// </summary>
        /// <remarks>
        /// PERFORMS NO INPUT VALIDATION.
        /// </remarks>
        /// <param name="str">The span of input characters.</param>
        /// <returns>An <see cref="int"/>.</returns>
        public static int Parse(ReadOnlySpan<char> str)
        {
            var pos = 0;
            var returnValue = 0;
            var sign = 1;
            if (str[0] == '-')
            {
                sign = -1;
                pos++;
            }

            bool IsNumeric(char x)
            {
                return (x >= '0' && x <= '9');
            }

            for (; pos < str.Length; pos++)
            {
                if (!IsNumeric(str[pos]))
                    throw new FormatException($"[{str.ToString()}] is now a valid numeric format");
                returnValue = returnValue * 10 + str[pos] - '0';
            }

            return sign * returnValue;
        }
    }
}
