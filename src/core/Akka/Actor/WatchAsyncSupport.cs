//-----------------------------------------------------------------------
// <copyright file="WatchAsyncSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Dispatch.SysMsg;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// WatchAsync extensions
    /// </summary>
    public static class WatchAsyncSupport
    {
        /// <summary>
        /// WatchAsync allows non-Akka.NET components to subscribe to DeathWatch notifications for a given <see cref="IActorRef"/>
        /// </summary>
        /// <param name="target">The actor to watch.</param>
        /// <param name="token">Optional - a cancellation token.</param>
        /// <returns><c>true</c> if the target was terminated, <c>false</c> otherwise.</returns>
        /// <exception cref="InvalidOperationException">Thrown if we attempt to watch a</exception>
        public static async Task<bool> WatchAsync(this IActorRef target, CancellationToken token = default)
        {
            if (target is not IInternalActorRef internalActorRef)
                throw new InvalidOperationException(
                    $"{target} is not an {typeof(IInternalActorRef)} and cannot be death-watched");

            var promiseRef = PromiseActorRef.Apply(internalActorRef.Provider, nameof(Watch), token);
            internalActorRef.SendSystemMessage(new Watch(internalActorRef, promiseRef));

            try
            {
                var result = await promiseRef.Result;
                return result switch
                {
                    Terminated terminated => terminated.ActorRef.Path.Equals(target.Path),
                    _ => false
                };
            }
            catch
            {
                // no need to throw here - the returned `false` status does the job just fine
                return false;
            }
            finally
            {
                // need to cleanup DeathWatch afterwards
                internalActorRef.SendSystemMessage(new Unwatch(internalActorRef, promiseRef));
            }
        }
    }
}
