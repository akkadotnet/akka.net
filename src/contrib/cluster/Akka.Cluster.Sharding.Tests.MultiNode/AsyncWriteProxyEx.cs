//-----------------------------------------------------------------------
// <copyright file="AsyncWriteProxyEx.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Persistence;
using Akka.Persistence.Journal;

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

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncReplayTimeoutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AsyncReplayTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Message that supplies the actor used by the proxy as its journal store.
    /// </summary>
    [Serializable]
    public sealed class SetStore
    {
        /// <summary>
        /// Creates a message containing the target journal-store actor.
        /// </summary>
        /// <param name="store">The actor that handles journal protocol messages.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="store"/> is undefined.
        /// </exception>
        public SetStore(IActorRef store) =>
            Store = store ?? throw new ArgumentNullException(nameof(store), "SetStore requires non-null reference to store actor");

        /// <summary>
        /// The journal-store actor supplied to the proxy.
        /// </summary>
        public readonly IActorRef Store;
    }

    /// <summary>
    /// A journal that delegates actual storage to a target actor. For testing only.
    /// </summary>
    public abstract class AsyncWriteProxyEx : AsyncWriteJournal, IWithUnboundedStash, IWithTimers
    {
        private const string InitTimeoutTimerKey = nameof(InitTimeoutTimerKey);
        
        private class InitTimeout
        {
            public static readonly InitTimeout Instance = new();
            private InitTimeout() { }
        }

        private bool _isInitialized;
        private bool _isInitTimedOut;
        private IActorRef _store;

        /// <summary>
        /// Initializes the journal proxy and prepares a timeout while it waits for its store actor.
        /// </summary>
        protected AsyncWriteProxyEx()
        {
            _isInitialized = false;
            _isInitTimedOut = false;
            _store = null;
        }

        /// <summary>
        /// The maximum wait before the proxy stops stashing messages for store initialization.
        /// </summary>
        public abstract TimeSpan Timeout { get; }

        /// <summary>
        /// Starts the initialization timeout before the journal's base startup logic runs.
        /// </summary>
        public override void AroundPreStart()
        {
            Timers.StartSingleTimer(InitTimeoutTimerKey, InitTimeout.Instance, Timeout, Self);
            base.AroundPreStart();
        }

        /// <summary>
        /// Stashes messages until a store is supplied or the initialization timeout expires, then delegates normal handling.
        /// </summary>
        /// <param name="receive">The journal's receive handler.</param>
        /// <param name="message">The incoming message to process or stash.</param>
        /// <returns><c>true</c> when initialization handling consumes the message; otherwise, the base journal result.</returns>
        protected internal override bool AroundReceive(Receive receive, object message)
        {
            if (_isInitialized)
            {
                if (message is not InitTimeout)
                    return base.AroundReceive(receive, message);
            }
            else switch (message)
            {
                case SetStore msg:
                    _store = msg.Store;
                    Stash.UnstashAll();
                    _isInitialized = true;
                    break;
                case InitTimeout:
                    _isInitTimedOut = true;
                    Stash.UnstashAll(); // will trigger appropriate failures
                    break;
                default:
                {
                    if (_isInitTimedOut)
                    {
                        return base.AroundReceive(receive, message);
                    }
                    else Stash.Stash();

                    break;
                }
            }
            return true;
        }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            var trueMsgs = messages.ToArray();
            
            if (_store == null)
                return StoreNotInitialized<IImmutableList<Exception>>();

            return _store.Ask<object>(sender => new WriteMessages(trueMsgs, sender, 1), Timeout, cancellationToken)
                .ContinueWith(r =>
                {
                    if (r.IsCanceled)
                        return (IImmutableList<Exception>)trueMsgs.Select(_ => (Exception)new TimeoutException()).ToImmutableList();
                    if (r.IsFaulted)
                        return trueMsgs.Select(_ => (Exception)r.Exception).ToImmutableList();

                    return r.Result switch
                    {
                        WriteMessageSuccess wms => trueMsgs.Select(_ => (Exception)null).ToImmutableList(),
                        WriteMessageFailure wmf => trueMsgs.Select(_ => wmf.Cause).ToImmutableList(),
                        _ => null
                    };
                }, TaskContinuationOptions.ExecuteSynchronously);
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
        {
            if (_store == null)
                return StoreNotInitialized<object>();

            var result = new TaskCompletionSource<object>();

            _store.Ask<object>(sender => new DeleteMessagesTo(persistenceId, toSequenceNr, sender), Timeout, cancellationToken).ContinueWith(r =>
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
        /// Replays messages from the configured store through a mediator that invokes the recovery callback and completes the replay task.
        /// </summary>
        /// <param name="context">The actor context used to create a local replay mediator.</param>
        /// <param name="persistenceId">The persistence identifier being recovered.</param>
        /// <param name="fromSequenceNr">The first sequence number to replay.</param>
        /// <param name="toSequenceNr">The last sequence number to replay.</param>
        /// <param name="max">The maximum number of messages to replay.</param>
        /// <param name="recoveryCallback">The callback invoked for each replayed persistent representation.</param>
        /// <exception cref="TimeoutException">
        /// This exception is thrown when the store has not been initialized.
        /// </exception>
        /// <returns>A task that completes when the store reports replay completion or failure.</returns>
        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            if (_store == null)
                return StoreNotInitialized<object>();

            var replayCompletionPromise = new TaskCompletionSource<object>();
            var mediator = context.ActorOf(Props.Create(() => new ReplayMediator(recoveryCallback, replayCompletionPromise, Timeout)).WithDeploy(Deploy.Local));

            _store.Tell(new ReplayMessages(fromSequenceNr, toSequenceNr, max, persistenceId, mediator), mediator);

            return replayCompletionPromise.Task;
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            if (_store == null)
                return StoreNotInitialized<long>();

            var result = new TaskCompletionSource<long>();

            _store.Ask<object>(sender => new ReplayMessages(0, 0, 0, persistenceId, sender), Timeout, cancellationToken)
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
        /// The stash used while waiting for the store actor to be set.
        /// </summary>
        public IStash Stash { get; set; }

        public ITimerScheduler Timers { get; set; }
    }

    /// <summary>
    /// Receives replay messages from the journal store, invokes the recovery callback, and completes or times out the replay request.
    /// </summary>
    internal class ReplayMediator : ActorBase
    {
        private readonly Action<IPersistentRepresentation> _replayCallback;
        private readonly TaskCompletionSource<object> _replayCompletionPromise;
        private readonly TimeSpan _replayTimeout;

        /// <summary>
        /// Creates a mediator for one journal replay request.
        /// </summary>
        /// <param name="replayCallback">The callback invoked for each replayed persistent representation.</param>
        /// <param name="replayCompletionPromise">The task completion source completed when replay succeeds or fails.</param>
        /// <param name="replayTimeout">The inactivity timeout for receiving replay messages.</param>
        public ReplayMediator(Action<IPersistentRepresentation> replayCallback, TaskCompletionSource<object> replayCompletionPromise, TimeSpan replayTimeout)
        {
            _replayCallback = replayCallback;
            _replayCompletionPromise = replayCompletionPromise;
            _replayTimeout = replayTimeout;

            Context.SetReceiveTimeout(replayTimeout);
        }

        /// <summary>
        /// Processes replayed records, completion or failure notifications, and replay inactivity timeouts.
        /// </summary>
        /// <param name="message">A replay protocol message or receive-timeout notification.</param>
        /// <exception cref="AsyncReplayTimeoutException">
        /// This exception is thrown when the replay timed out due to inactivity.
        /// </exception>
        /// <returns><c>true</c> for replay protocol messages and timeouts handled here; otherwise, <c>false</c>.</returns>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case ReplayedMessage rm:
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
}
