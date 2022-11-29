using System;

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
            throw new FormatException($"[{str.ToString()}] is not a valid numeric format");
        }

        private const char Negative = '-';
        private static readonly char[] Numbers = { '0','1','2','3','4','5','6','7','8','9' };

        /// <summary>
        /// Can replace with int64.TryFormat in later versions of .NET.
        /// </summary>
        /// <param name="i">The integer we want to format into a string.</param>
        /// <param name="startPos">Starting position in the destination span we're going to write from</param>
        /// <param name="span">The span we're going to write our characters into.</param>
        /// <param name="sizeHint">Optional size hint, in order to avoid recalculating it.</param>
        /// <returns></returns>
        public static int TryFormat(long i, int startPos, ref Span<char> span, int sizeHint = 0)
        {
            var index = 0;
            if (i is < 10 and >= 0)
            {
                span[index++] = (char)(i+'0');
                return index;
            }
            var negative = 0;
            if (i < 0)
            {
                negative = 1;
                i = Math.Abs(i);
            }
	
            var targetLength = sizeHint > 0 ? sizeHint : PositiveInt64SizeInCharacters(i, negative);

            while (i > 0)
            {
                i = Math.DivRem(i, 10, out var rem);
                span[startPos + targetLength - index++ - 1] = (char)(rem+'0');
            }
	
            if(negative == 1){
                span[0] = Negative;
                index++;
            }

            return index;
        }

        /// <summary>
        /// How many characters do we need to represent this int as a string?
        /// </summary>
        /// <param name="i">The int.</param>
        /// <returns>Character length.</returns>
        public static int Int64SizeInCharacters(long i)
        {
	        // account for negative characters
            var padding = 0;
            if (i < 0)
            {
                i *= -1;
                padding = 1;
            }

            return PositiveInt64SizeInCharacters(i, padding);
        }
        
        public static int PositiveInt64SizeInCharacters(long i, int padding)
        {
            switch (i)
            {
                case 0:
                    return 1;
                case < 10:
                    return 1 + padding;
                case < 100:
                    return 2 + padding;
                case < 1000:
                    return 3 + padding;
                case < 10000:
                    return 4 + padding;
                case < 100000:
                    return 5 + padding;
                case < 1_000_000:
                    return 6 + padding;
                case < 10_000_000:
                    return 7 + padding;
                case < 100_000_000:
                    return 8 + padding;
                case < 1_000_000_000:
                    return 9 + padding;
                case < 10_000_000_000:
                    return 10 + padding;
                case 100_000_000_000:
                    return 11 + padding;
                default:
                    return (int)Math.Log10(i) + 1 + padding;
            }
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
