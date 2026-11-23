//-----------------------------------------------------------------------
// <copyright file="Base64Encoding.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;

namespace Akka.Util
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class Base64Encoding
    {
        private const string Base64Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+~";

        /// <summary>
        /// Encodes a 64-bit integer value as a custom base64 string using Akka.NET's encoding alphabet.
        /// </summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>The base64-encoded string representation of the value.</returns>
        public static string Base64Encode(this long value)
        {
            return Base64Encode(value, "");
        }

        internal static string Base64Encode(this long value, string prefix)
        {
            // 11 is the number of characters it takes to represent long.MaxValue
            // so we will never need a larger size for encoding longs
            Span<char> sb = stackalloc char[11 + (prefix?.Length ?? 0)];
            var spanIndex = 0;
            if (!string.IsNullOrWhiteSpace(prefix) && prefix.Length > 0)
            {
                prefix.AsSpan().CopyTo(sb);
                spanIndex = prefix.Length;
            }

            var next = value;
            do
            {
                var index = (int)(next & 63);
                sb[spanIndex++] = Base64Chars[index];
                next = next >> 6;
            } while (next != 0);
            return sb.Slice(0, spanIndex).ToString();
        }

        /// <summary>
        /// Decodes a name produced by <see cref="Base64Encode(long)"/> back into its value. Only
        /// canonical encodings are accepted (the exact string <see cref="Base64Encode(long)"/> would
        /// produce for the decoded non-negative value), so the mapping between names and values is a
        /// bijection.
        /// </summary>
        /// <param name="name">The encoded name.</param>
        /// <param name="value">The decoded value, or -1 when <paramref name="name"/> is not canonical.</param>
        /// <returns><c>true</c> if <paramref name="name"/> is the canonical encoding of a non-negative <see cref="long"/>.</returns>
        internal static bool TryBase64DecodeLong(ReadOnlySpan<char> name, out long value)
        {
            value = -1;
            // 11 characters carry 66 bits; a non-negative long needs at most 63.
            if (name.Length == 0 || name.Length > 11)
                return false;

            long result = 0;
            for (var i = 0; i < name.Length; i++)
            {
                var digit = DigitOf(name[i]);
                if (digit < 0)
                    return false;

                // The encoder never emits a zero most-significant digit, except for the value 0 itself.
                if (i == name.Length - 1 && digit == 0 && name.Length > 1)
                    return false;

                // The 11th digit sits at bit 60, so only 3 bits (60..62) remain for a non-negative long.
                if (i == 10 && digit > 7)
                    return false;

                result |= (long)digit << (6 * i);
            }

            value = result;
            return true;
        }

        private static int DigitOf(char c)
        {
            if (c >= 'a' && c <= 'z') return c - 'a';
            if (c >= 'A' && c <= 'Z') return c - 'A' + 26;
            if (c >= '0' && c <= '9') return c - '0' + 52;
            if (c == '+') return 62;
            if (c == '~') return 63;
            return -1;
        }

        /// <summary>
        /// Encodes a string as a standard base64 string using UTF-8 encoding.
        /// </summary>
        /// <param name="s">The string to encode.</param>
        /// <returns>The base64-encoded string.</returns>
        public static string Base64Encode(this string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            return System.Convert.ToBase64String(bytes);
        }
    }
}

