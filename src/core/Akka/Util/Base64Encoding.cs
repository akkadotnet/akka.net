//-----------------------------------------------------------------------
// <copyright file="Base64Encoding.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class Base64Encoding
    {
        /// <summary>
        /// TBD
        /// </summary>
        public const string Base64Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+~";

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public static string Base64Encode(this long value)
        {
            return Base64Encode(value, "");
        }

        internal static string Base64Encode(this long value, string prefix)
        {
            // 11 is the number of characters it takes to represent long.MaxValue
            // so we will never need a larger size for encoding longs
            Span<char> sb = stackalloc char[11 + prefix?.Length ?? 0];
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
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <param name="sb">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Do not use. Pass in prefix as a string instead.")]
        public static StringBuilder Base64Encode(this long value, StringBuilder sb)
        {
            var next = value;
            do
            {
                var index = (int)(next & 63);
                sb.Append(Base64Chars[index]);
                next = next >> 6;
            } while (next != 0);
            return sb;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public static string Base64Encode(this string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            return System.Convert.ToBase64String(bytes);
        }
    }
}

