//-----------------------------------------------------------------------
// <copyright file="AsyncWriteProxyEx.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using System.Runtime.Serialization;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Persistence;
using System.Threading;
using Akka.Util.Internal;
using Akka.Actor.Internal;

namespace Akka.Cluster.Sharding.Tests
{
    /// <summary>
    /// This exception is thrown when the replay inactivity exceeds a specified timeout.
    /// </summary>
    [Serializable]
    public class AsyncReplayTimeoutException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncReplayTimeoutException"/> class.
        /// </summary>
        public AsyncReplayTimeoutException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncReplayTimeoutException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public AsyncReplayTimeoutException(string message)
            : base(message)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncReplayTimeoutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AsyncReplayTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class SetStore
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="store">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="store"/> is undefined.
        /// </exception>
        public SetStore(IActorRef store)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store), "SetStore requires non-null reference to store actor");

            Store = store;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly IActorRef Store;
    }

    /// <summary>
    /// A journal that delegates actual storage to a target actor. For testing only.
    /// </summary>
    public abstract class AsyncWriteProxyEx : AsyncWriteJournal, IWithUnboundedStash
    {
        private bool _isInitialized;
        private bool _isInitTimedOut;
        private IActorRef _store;

        /// <summary>
        /// TBD
        /// </summary>
        protected AsyncWriteProxyEx()
        {
            _isInitialized = false;
            _isInitTimedOut = false;
            _store = null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract TimeSpan Timeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override void AroundPreStart()
        {
            Context.System.Scheduler.ScheduleTellOnce(Timeout, Self, InitTimeout.Instance, Self);
            base.AroundPreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool AroundReceive(Receive receive, object message)
        {
            if (_isInitialized)
            {
                if (!(message is InitTimeout))
                    return base.AroundReceive(receive, message);
            }
            else if (message is SetStore msg)
            {
                _store = msg.Store;
                Stash.UnstashAll();
                _isInitialized = true;
            }
            else if (message is InitTimeout)
            {
                _isInitTimedOut = true;
                Stash.UnstashAll(); // will trigger appropriate failures
            }
            else if (_isInitTimedOut)
            {
                return base.AroundReceive(receive, message);
            }
            else Stash.Stash();
            return true;
        }



        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messages">TBD</param>
        /// <exception cref="TimeoutException">
        /// This exception is thrown when the store has not been initialized.
        /// </exception>
        /// <returns>TBD</returns>
        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            if (_store == null)
                return StoreNotInitialized<IImmutableList<Exception>>();

            return _store.AskEx<object>(sender => new WriteMessages(messages, sender, 1), Timeout)
                .ContinueWith(r =>
                {
                    if (r.IsCanceled)
                        return (IImmutableList<Exception>)messages.Select(i => (Exception)new TimeoutException()).ToImmutableList();
                    if (r.IsFaulted)
                        return messages.Select(i => (Exception)r.Exception).ToImmutableList();

                    if (r.Result is WriteMessageSuccess wms)
                    {
                        return messages.Select(i => (Exception)null).ToImmutableList();
                    }
                    if (r.Result is WriteMessageFailure wmf)
                    {
                        return messages.Select(i => wmf.Cause).ToImmutableList();
                    }
                    return null;
                }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <exception cref="TimeoutException">
        /// This exception is thrown when the store has not been initialized.
        /// </exception>
        /// <returns>TBD</returns>
        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            if (_store == null)
                return StoreNotInitialized<object>();

            var result = new TaskCompletionSource<object>();

            _store.AskEx(sender => new DeleteMessagesTo(persistenceId, toSequenceNr, sender), Timeout).ContinueWith(r =>
            {
                if (r.IsFaulted)
                    result.TrySetException(r.Exception);
                else if (r.IsCanceled)
                    result.TrySetException(new TimeoutException());
                else
                    result.TrySetResult(true);
            }, TaskContinuationOptions.ExecuteSynchronously);

            return result.Task;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="recoveryCallback">TBD</param>
        /// <exception cref="TimeoutException">
        /// This exception is thrown when the store has not been initialized.
        /// </exception>
        /// <returns>TBD</returns>
        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            if (_store == null)
                return StoreNotInitialized<object>();

            var replayCompletionPromise = new TaskCompletionSource<object>();
            var mediator = context.ActorOf(Props.Create(() => new ReplayMediator(recoveryCallback, replayCompletionPromise, Timeout)).WithDeploy(Deploy.Local));

            _store.Tell(new ReplayMessages(fromSequenceNr, toSequenceNr, max, persistenceId, mediator), mediator);

            return replayCompletionPromise.Task;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <exception cref="TimeoutException">
        /// This exception is thrown when the store has not been initialized.
        /// </exception>
        /// <returns>TBD</returns>
        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            if (_store == null)
                return StoreNotInitialized<long>();

            var result = new TaskCompletionSource<long>();

            _store.AskEx<object>(sender => new ReplayMessages(0, 0, 0, persistenceId, sender), Timeout)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        result.TrySetException(t.Exception);
                    else if (t.IsCanceled)
                        result.TrySetException(new TimeoutException());
                    else if (t.Result is RecoverySuccess rs)
                        result.TrySetResult(rs.HighestSequenceNr);
                    else
                        result.TrySetException(new InvalidOperationException());
                }, TaskContinuationOptions.ExecuteSynchronously);
            return result.Task;
        }

        private Task<T> StoreNotInitialized<T>()
        {
            var promise = new TaskCompletionSource<T>();
            promise.SetException(new TimeoutException("Store not intialized."));
            return promise.Task;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IStash Stash { get; set; }

        // sent to self only
        /// <summary>
        /// TBD
        /// </summary>
        public class InitTimeout
        {
            private InitTimeout() { }
            private static readonly InitTimeout _instance = new InitTimeout();

            /// <summary>
            /// TBD
            /// </summary>
            public static InitTimeout Instance
            {
                get
                {
                    return _instance;
                }
            }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class ReplayMediator : ActorBase
    {
        private readonly Action<IPersistentRepresentation> _replayCallback;
        private readonly TaskCompletionSource<object> _replayCompletionPromise;
        private readonly TimeSpan _replayTimeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="replayCallback">TBD</param>
        /// <param name="replayCompletionPromise">TBD</param>
        /// <param name="replayTimeout">TBD</param>
        public ReplayMediator(Action<IPersistentRepresentation> replayCallback, TaskCompletionSource<object> replayCompletionPromise, TimeSpan replayTimeout)
        {
            _replayCallback = replayCallback;
            _replayCompletionPromise = replayCompletionPromise;
            _replayTimeout = replayTimeout;

            Context.SetReceiveTimeout(replayTimeout);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <exception cref="AsyncReplayTimeoutException">
        /// This exception is thrown when the replay timed out due to inactivity.
        /// </exception>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case ReplayedMessage rm:
                    //rm.Persistent
                    _replayCallback(rm.Persistent);
                    return true;
                case RecoverySuccess _:
                    _replayCompletionPromise.SetResult(new object());
                    Context.Stop(Self);
                    return true;
                case ReplayMessagesFailure failure:
                    _replayCompletionPromise.SetException(failure.Cause);
                    Context.Stop(Self);
                    return true;
                case ReceiveTimeout _:
                    var timeoutException = new AsyncReplayTimeoutException($"Replay timed out after {_replayTimeout.TotalSeconds}s of inactivity");
                    _replayCompletionPromise.SetException(timeoutException);
                    Context.Stop(Self);
                    return true;
            }
            return false;
        }
    }

    internal static class FuturesEx
    {
        public static Task<object> AskEx(this ICanTell self, Func<IActorRef, object> messageFactory, TimeSpan? timeout = null)
        {
            return self.AskEx<object>(messageFactory, timeout, CancellationToken.None);
        }

        public static Task<object> AskEx(this ICanTell self, Func<IActorRef, object> messageFactory, CancellationToken cancellationToken)
        {
            return self.AskEx<object>(messageFactory, null, cancellationToken);
        }

        public static Task<object> AskEx(this ICanTell self, Func<IActorRef, object> messageFactory, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            return self.AskEx<object>(messageFactory, timeout, cancellationToken);
        }

        public static Task<T> AskEx<T>(this ICanTell self, Func<IActorRef, object> messageFactory, TimeSpan? timeout = null)
        {
            return self.AskEx<T>(messageFactory, timeout, CancellationToken.None);
        }

        public static Task<T> AskEx<T>(this ICanTell self, Func<IActorRef, object> messageFactory, CancellationToken cancellationToken)
        {
            return self.AskEx<T>(messageFactory, null, cancellationToken);
        }

        public static async Task<T> AskEx<T>(this ICanTell self, Func<IActorRef, object> messageFactory, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            IActorRefProvider provider = ResolveProvider(self);
            if (provider == null)
                throw new ArgumentException("Unable to resolve the target Provider", nameof(self));

            return (T)await AskEx(self, messageFactory, provider, timeout, cancellationToken);
        }
        internal static IActorRefProvider ResolveProvider(ICanTell self)
        {
            if (InternalCurrentActorCellKeeper.Current != null)
                return InternalCurrentActorCellKeeper.Current.SystemImpl.Provider;

            if (self is IInternalActorRef iar)
                return iar.Provider;

            if (self is ActorSelection asel)
                return ResolveProvider(asel.Anchor);

            return null;
        }

        private static async Task<object> AskEx(ICanTell self, Func<IActorRef, object> messageFactory, IActorRefProvider provider, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<object>();

            CancellationTokenSource timeoutCancellation = null;
            timeout = timeout ?? provider.Settings.AskTimeout;
            var ctrList = new List<CancellationTokenRegistration>(2);

            if (timeout != Timeout.InfiniteTimeSpan && timeout.Value > default(TimeSpan))
            {
                timeoutCancellation = new CancellationTokenSource();

                ctrList.Add(timeoutCancellation.Token.Register(() =>
                {
                    result.TrySetException(new AskTimeoutException($"Timeout after {timeout} seconds"));
                }));

                timeoutCancellation.CancelAfter(timeout.Value);
            }

            if (cancellationToken.CanBeCanceled)
            {
                ctrList.Add(cancellationToken.Register(() => result.TrySetCanceled()));
            }

            //create a new tempcontainer path
            ActorPath path = provider.TempPath();

            var future = new FutureActorRef(result, () => { }, path);
            //The future actor needs to be registered in the temp container
            provider.RegisterTempActor(future, path);

            self.Tell(messageFactory(future), future);

            try
            {
                return await result.Task;
            }
            finally
            {
                //callback to unregister from tempcontainer

                provider.UnregisterTempActor(path);

                for (var i = 0; i < ctrList.Count; i++)
                {
                    ctrList[i].Dispose();
                }

                if (timeoutCancellation != null)
                {
                    timeoutCancellation.Dispose();
                }
            }
        }
    }
}
