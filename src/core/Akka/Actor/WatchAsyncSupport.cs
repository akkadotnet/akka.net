//-----------------------------------------------------------------------
// <copyright file="WatchAsyncSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// WatchAsync extensions
    /// </summary>
    public static class WatchAsyncSupport
    {
        // public static Task WatchAsync(this IActorRef target)
        // {
        //     if (target is IInternalActorRef internalActorRef)
        //     {
        //         var promiseRef = PromiseActorRef.Apply(internalActorRef.Provider, )
        //     }
        //
        //     throw new InvalidOperationException(
        //         $"{target} is not an {typeof(IInternalActorRef)} and cannot be death-watched");
        // }
    }
}