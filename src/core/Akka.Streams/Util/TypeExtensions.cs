//-----------------------------------------------------------------------
// <copyright file="TypeExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        public static Type GetSubscribedType(this Type type)
        {
            return
                type
                    .GetInterfaces()
                    .Single(i => i.GetTypeInfo().IsGenericType && i.GetGenericTypeDefinition() == typeof (ISubscriber<>))
                    .GetGenericArguments()
                    .First();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="type">TBD</param>
        /// <returns>TBD</returns>
        public static Type GetPublishedType(this Type type)
        {
            return
                type
                    .GetInterfaces()
                    .Single(i => i.GetTypeInfo().IsGenericType && i.GetGenericTypeDefinition() == typeof (IPublisher<>))
                    .GetGenericArguments()
                    .First();
        }
    }
}
