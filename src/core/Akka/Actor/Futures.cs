using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    ///     Extension method class designed to create Ask support for
    ///     non-ActorRef objects such as <see cref="ActorSelection" />.
    /// </summary>
    public static class Futures
    {
        //when asking from outside of an actor, we need to pass a system, so the FutureActor can register itself there and be resolvable for local and remote calls
        public static Task<object> Ask(this ICanTell self, object message, TimeSpan? timeout = null)
        {
            ActorRefProvider provider = ResolveProvider(self);
            if (provider == null)
                throw new NotSupportedException("Unable to resolve the target Provider");

            ActorRef replyTo = ResolveReplyTo();

            return Ask(self, replyTo, message, provider, timeout);
        }

        public static Task<T> Ask<T>(this ICanTell self, object message, TimeSpan? timeout = null)
        {
            ActorRefProvider provider = ResolveProvider(self);
            if (provider == null)
                throw new NotSupportedException("Unable to resolve the target Provider");

            ActorRef replyTo = ResolveReplyTo();

            var task = Ask(self, replyTo, message, provider, timeout).ContinueWith(t => (T) t.Result);
            task.ConfigureAwait(true);

            return task;
        }

        internal static ActorRef ResolveReplyTo()
        {
            if (ActorCell.Current != null)
                return ActorCell.Current.Self;

            return null;
        }

        internal static ActorRefProvider ResolveProvider(ICanTell self)
        {
            if (ActorCell.Current != null)
                return InternalCurrentActorCellKeeper.Current.SystemImpl.Provider;

            if (self is InternalActorRef)
                return self.AsInstanceOf<InternalActorRef>().Provider;

            if (self is ActorSelection)
                return ResolveProvider(self.AsInstanceOf<ActorSelection>().Anchor);

            return null;
        }

        private static Task<object> Ask(ICanTell self, ActorRef replyTo, object message, ActorRefProvider provider,
            TimeSpan? timeout)
        {
            var result = new TaskCompletionSource<object>(TaskContinuationOptions.AttachedToParent);
            if (timeout.HasValue)
            {
                var cancellationSource = new CancellationTokenSource();
                cancellationSource.Token.Register(() => result.TrySetCanceled());
                cancellationSource.CancelAfter(timeout.Value);
            }

            //create a new tempcontainer path
            ActorPath path = provider.TempPath();
            //callback to unregister from tempcontainer
            Action unregister = () => provider.UnregisterTempActor(path);
            var future = new FutureActorRef(result, replyTo, unregister, path);
            //The future actor needs to be registered in the temp container
            provider.RegisterTempActor(future, path);
            self.Tell(message, future);
            return result.Task;
        }
    }
}