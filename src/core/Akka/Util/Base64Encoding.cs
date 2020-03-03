//-----------------------------------------------------------------------
// <copyright file="Base64Encoding.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        public static string Base64Encode(this long value) => Base64Encode(value, new StringBuilder()).ToString();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <param name="sb">TBD</param>
        /// <returns>TBD</returns>
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

