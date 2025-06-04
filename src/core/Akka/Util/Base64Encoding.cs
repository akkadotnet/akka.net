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

