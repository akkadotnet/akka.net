//-----------------------------------------------------------------------
// <copyright file="GracefulStopSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Dispatch.SysMsg;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// GracefulStop extensions.
    /// </summary>
    public static class GracefulStopSupport
    {
        /// <summary>
        /// Returns a <see cref="Task"/> that will be completed with success when existing messages
        /// of the target actor have been processed and the actor has been terminated.
        /// 
        /// Useful when you need to wait for termination or compose ordered termination of several actors,
        /// which should only be done outside of the <see cref="ActorSystem"/> as blocking inside <see cref="ActorBase"/> is discouraged.
        /// 
        /// <remarks><c>IMPORTANT:</c> the actor being terminated and its supervisor being informed of the availability of the deceased actor's name
        /// are two distinct operations, which do not obey any reliable ordering.</remarks>
        /// 
        /// If the target actor isn't terminated within the timeout the <see cref="Task"/> is completed with failure.
        /// 
        /// If you want to invoke specialized stopping logic on your target actor instead of <see cref="PoisonPill"/>, you can pass your stop command as a parameter:
        /// <code>
        ///     GracefulStop(someChild, timeout, MyStopGracefullyMessage).ContinueWith(r => {
        ///         // Do something after someChild starts being stopped.
        ///     });
        /// </code>
        /// </summary>
        /// <param name="target">The actor to be terminated.</param>
        /// <param name="timeout">The amount of time we're going to wait for the actor to terminate.</param>
        /// <returns>A <see cref="Task"/> that will return <c>true</c> if the <see cref="target"/> shuts down within <see cref="timeout"/></returns>
        public static Task<bool> GracefulStop(this IActorRef target, TimeSpan timeout)
        {
            return GracefulStop(target, timeout, PoisonPill.Instance);
        }

        /// <summary>
        /// Returns a <see cref="Task"/> that will be completed with success when existing messages
        /// of the target actor have been processed and the actor has been terminated.
        /// 
        /// Useful when you need to wait for termination or compose ordered termination of several actors,
        /// which should only be done outside of the <see cref="ActorSystem"/> as blocking inside <see cref="ActorBase"/> is discouraged.
        /// 
        /// <remarks><c>IMPORTANT:</c> the actor being terminated and its supervisor being informed of the availability of the deceased actor's name
        /// are two distinct operations, which do not obey any reliable ordering.</remarks>
        /// 
        /// If the target actor isn't terminated within the timeout the <see cref="Task"/> is completed with failure.
        /// 
        /// If you want to invoke specialized stopping logic on your target actor instead of <see cref="PoisonPill"/>, you can pass your stop command as a parameter:
        /// <code>
        ///     GracefulStop(someChild, timeout, MyStopGracefullyMessage).ContinueWith(r => {
        ///         // Do something after someChild starts being stopped.
        ///     });
        /// </code>
        /// </summary>
        /// <param name="target">The actor to be terminated.</param>
        /// <param name="timeout">The amount of time we're going to wait for the actor to terminate.</param>
        /// <param name="stopMessage">A custom message to use to shutdown <see cref="target"/> - by default the other overload uses <see cref="PoisonPill"/>.</param>
        /// <exception cref="TaskCanceledException">
        /// This exception is thrown if the underlying task is <see cref="TaskStatus.Canceled"/>.
        /// </exception>
        /// <returns>A <see cref="Task"/> that will return <c>true</c> if the <see cref="target"/> shuts down within <see cref="timeout"/></returns>
        public static Task<bool> GracefulStop(this IActorRef target, TimeSpan timeout, object stopMessage)
        {
            var internalTarget = target.AsInstanceOf<IInternalActorRef>();

            var promiseRef = PromiseActorRef.Apply(internalTarget.Provider, timeout, target, stopMessage.GetType().Name);
            internalTarget.SendSystemMessage(new Watch(internalTarget, promiseRef));
            target.Tell(stopMessage, ActorRefs.NoSender);
            return promiseRef.Result.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    switch (t.Result)
                    {
                        case Terminated terminated:
                            return terminated.ActorRef.Path.Equals(target.Path);
                        default:
                            internalTarget.SendSystemMessage(new Unwatch(internalTarget, promiseRef));
                            return false;
                        
                    }
                }
                
                internalTarget.SendSystemMessage(new Unwatch(internalTarget, promiseRef));
                if (t.Status == TaskStatus.Canceled)
                    throw new TaskCanceledException();
                
                throw t.Exception;
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}

