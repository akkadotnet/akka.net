//-----------------------------------------------------------------------
// <copyright file="Base64Encoding.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text;
using Akka.Annotations;
using Microsoft.Extensions.ObjectPool;

namespace Akka.Util
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Used to help reduce memory allocations.
    /// </summary>
    internal static class PooledObject
    {
        public static readonly ObjectPool<StringBuilder> StringBuilderPool =
            new DefaultObjectPoolProvider().CreateStringBuilderPool(512, 2048);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
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
        public static string Base64Encode(this long value) => Base64Encode(value, PooledObject.StringBuilderPool.Get()).ToString();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <param name="sb">TBD</param>
        /// <returns>TBD</returns>
        public static string Base64Encode(this long value, StringBuilder sb)
        {
            try
            {
                var next = value;
                do
                {
                    var index = (int)(next & 63);
                    sb.Append(Base64Chars[index]);
                    next = next >> 6;
                } while (next != 0);

                return sb.ToString();
            }
            finally
            {
                PooledObject.StringBuilderPool.Return(sb);
            }
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

