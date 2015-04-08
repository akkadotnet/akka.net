//-----------------------------------------------------------------------
// <copyright file="GracefulStopSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    /// Returns a <see cref="Task"/> that will be completed with success when existing messages
    /// of the target actor have been processed and the actor has been terminated.
    /// 
    /// Useful when you need to wait for termination or compose ordered termination of several actors,
    /// which should only be done outside of the <see cref="ActorSystem"/> as blocking inside <see cref="ActorBase"/> is discouraged.
    /// 
    /// <remarks><c>IMPORTANT:</c> the actor being terminated and its supervisor being informed of the availability of the deceased actor's name
    /// are two distinct operations, which do not obey any reliable ordering.</remarks>
    /// 
    /// If the target actor isn't terminated within the timeout the <see cref="Task"/> is complted with failure.
    /// 
    /// If you want to invoke specialized stopping logic on your target actor instead of <see cref="PoisonPill"/>, you can pass your stop command as a parameter:
    /// <code>
    ///     GracefulStop(someChild, timeout, MyStopGracefullyMessage).ContinueWith(r => {
    ///         // Do something after someChild starts being stopped.
    ///     });
    /// </code>
    /// </summary>
    public static class GracefulStopSupport
    {
        public static Task<bool> GracefulStop(this IActorRef target, TimeSpan timeout)
        {
            return GracefulStop(target, timeout, PoisonPill.Instance);
        }

        public static Task<bool> GracefulStop(this IActorRef target, TimeSpan timeout, object stopMessage)
        {
            var internalTarget = target.AsInstanceOf<IInternalActorRef>();
            if (internalTarget.IsTerminated) return Task.Run(() => true);

            var provider = Futures.ResolveProvider(target);
            var promise = new TaskCompletionSource<object>();

            //set up the timeout
            var cancellationSource = new CancellationTokenSource();
            cancellationSource.Token.Register(() => promise.TrySetCanceled());
            cancellationSource.CancelAfter(timeout);

            //create a new tempcontainer path
            var path = provider.TempPath();
            //callback to unregister from tempcontainer
            Action unregister = () => provider.UnregisterTempActor(path);

            var fref = new FutureActorRef(promise, unregister, path);
            internalTarget.Tell(new Watch(internalTarget, fref));
            target.Tell(stopMessage, ActorRefs.NoSender);
            return promise.Task.ContinueWith(t =>
            {
                var returnResult = false;
                PatternMatch.Match(t.Result)
                    .With<Terminated>(terminated =>
                    {
                        returnResult = (terminated.ActorRef.Path.Equals(target.Path));
                    })
                    .Default(m =>
                    {
                        returnResult = false;
                    });

                internalTarget.Tell(new Unwatch(target, fref));
                return returnResult;
            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
        }
    }
}
