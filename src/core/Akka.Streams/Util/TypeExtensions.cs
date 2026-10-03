//-----------------------------------------------------------------------
// <copyright file="TypeExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Reactive.Streams;

namespace Akka.Streams.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class TypeExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="type">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Unused by Akka.Streams and reflection-based (the trimmer must keep the type's interfaces); will be removed in 1.7. Obsolete since v1.6.0")]
        public static Type GetSubscribedType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type type)
        {
            return
                type
                    .GetInterfaces()
                    .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof (ISubscriber<>))
                    .GetGenericArguments()
                    .First();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="type">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Unused by Akka.Streams and reflection-based (the trimmer must keep the type's interfaces); will be removed in 1.7. Obsolete since v1.6.0")]
        public static Type GetPublishedType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type type)
        {
            return
                type
                    .GetInterfaces()
                    .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof (IPublisher<>))
                    .GetGenericArguments()
                    .First();
        }
    }
}
