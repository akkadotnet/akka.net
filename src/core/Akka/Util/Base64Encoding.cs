//-----------------------------------------------------------------------
// <copyright file="Base64Encoding.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        /// <param name="prefix"></param>
        /// <returns>TBD</returns>
        public static string Base64Encode(this long value, string prefix = null){
            Span<char> encodedBytes = stackalloc char[12 + prefix?.Length ?? 0];            
            var writeIndex = 0;

            if(prefix != null){
               for(; writeIndex < prefix.Length; writeIndex++)
                    encodedBytes[writeIndex] = prefix[writeIndex];
            }

            var next = value;
            do
            {		
                var index = (int)(next & 63);
                encodedBytes[writeIndex]= Base64Chars[index];
                next = next >> 6;	
                writeIndex++;
            } while (next != 0);

            unsafe
            {
                fixed (char* p1 = encodedBytes.Slice(0, writeIndex)) {
                    return new string(p1);
                }		
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

