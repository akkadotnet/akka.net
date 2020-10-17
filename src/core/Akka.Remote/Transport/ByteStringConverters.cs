// //-----------------------------------------------------------------------
// // <copyright file="ByteStringConverters.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

// //-----------------------------------------------------------------------
// // <copyright file="ByteStringConverters.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

// //-----------------------------------------------------------------------
// // <copyright file="ByteStringConverters.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Linq.Expressions;
using System.Reflection;
using Google.Protobuf;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// Helper methods for converting between
    /// <see cref="N:Google.Protobuf"/>
    /// and
    /// <see cref="N:Akka.IO"/>
    /// ByteStrings 
    /// </summary>
    public static class ByteStringConverters
    {
        /// <summary>
        /// Get the byte array from a ByteString
        /// </summary>
        internal static readonly Func<Google.Protobuf.ByteString, byte[]> _getByteArrayUnsafeFunc = build();
        

        private static Func<Google.Protobuf.ByteString, byte[]> build()
        {
            //See if we can 'cheat' and grab the byte array directly.
            //If for some reason we can't (CAS in Framework, change in protobuf)
            //We instead fall-back to simple version which allocates
            //but works.
            try
            {
                var p = Expression.Parameter(typeof(ByteString));
                return Expression.Lambda<Func<ByteString, byte[]>>(Expression.Field(p,
                        typeof(ByteString).GetField("bytes",
                            BindingFlags.NonPublic | BindingFlags.Instance)), p)
                    .Compile();
            }
            catch (Exception e)
            {
                return s => s.ToByteArray();
            }
            
        }
    }
}