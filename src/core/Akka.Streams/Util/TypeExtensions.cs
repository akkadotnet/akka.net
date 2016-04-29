//-----------------------------------------------------------------------
// <copyright file="TypeExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Reactive.Streams;

namespace Akka.Streams.Util
{
    public static class TypeExtensions
    {
        public static Type GetSubscribedType(this Type type)
        {
            return
                type
                    .GetInterfaces()
                    .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof (ISubscriber<>))
                    .GetGenericArguments()
                    .First();
        }

        public static Type GetPublishedType(this Type type)
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