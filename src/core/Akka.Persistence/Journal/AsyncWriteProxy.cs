//-----------------------------------------------------------------------
// <copyright file="AsyncWriteProxy.cs" company="Akka.NET Project">
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

namespace Akka.Persistence.Journal
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
    /// TBD
    /// </summary>
    public static class AsyncWriteTarget
    {
        #region Internal Messages

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ReplayFailure
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ReplayFailure"/> class.
            /// </summary>
            /// <param name="cause">The cause of the failure</param>
            /// <exception cref="System.ArgumentNullException">
            /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
            /// </exception>
            public ReplayFailure(Exception cause)
            {
                if (cause == null)
                    throw new ArgumentNullException(nameof(cause), "AsyncWriteTarget.ReplayFailure cause exception cannot be null");

                Cause = cause;
            }

            /// <summary>
            /// The cause of the failure
            /// </summary>
            public Exception Cause { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ReplaySuccess : IEquatable<ReplaySuccess>
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="highestSequenceNr">TBD</param>
            public ReplaySuccess(long highestSequenceNr)
            {
                HighestSequenceNr = highestSequenceNr;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public long HighestSequenceNr { get; }

            /// <inheritdoc/>
            public bool Equals(ReplaySuccess other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return HighestSequenceNr == other.HighestSequenceNr;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class WriteMessages
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="messages">TBD</param>
            public WriteMessages(IEnumerable<AtomicWrite> messages)
            {
                Messages = messages.ToArray();
            }

            /// <summary>
            /// TBD
            /// </summary>
            public AtomicWrite[] Messages { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ReplayMessages : IEquatable<ReplayMessages>
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="persistenceId">TBD</param>
            /// <param name="fromSequenceNr">TBD</param>
            /// <param name="toSequenceNr">TBD</param>
            /// <param name="max">TBD</param>
            public ReplayMessages(string persistenceId, long fromSequenceNr, long toSequenceNr, long max)
            {
                PersistenceId = persistenceId;
                FromSequenceNr = fromSequenceNr;
                ToSequenceNr = toSequenceNr;
                Max = max;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public string PersistenceId { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public long FromSequenceNr { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public long ToSequenceNr { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public long Max { get; }

            /// <inheritdoc/>
            public bool Equals(ReplayMessages other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return PersistenceId == other.PersistenceId
                       && FromSequenceNr == other.FromSequenceNr
                       && ToSequenceNr == other.ToSequenceNr
                       && Max == other.Max;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class DeleteMessagesTo : IEquatable<DeleteMessagesTo>
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="persistenceId">TBD</param>
            /// <param name="toSequenceNr">TBD</param>
            public DeleteMessagesTo(string persistenceId, long toSequenceNr)
            {
                PersistenceId = persistenceId;
                ToSequenceNr = toSequenceNr;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public string PersistenceId { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public long ToSequenceNr { get; }

            /// <inheritdoc/>
            public bool Equals(DeleteMessagesTo other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return PersistenceId == other.PersistenceId
                       && ToSequenceNr == other.ToSequenceNr;
            }
        }

        #endregion
    }

    /// <summary>
    /// A journal that delegates actual storage to a target actor. For testing only.
    /// </summary>
    public abstract class AsyncWriteProxy : AsyncWriteJournal, IWithUnboundedStash
    {
        private bool _isInitialized;
        private bool _isInitTimedOut;
        private IActorRef _store;

        /// <summary>
        /// TBD
        /// </summary>
        protected AsyncWriteProxy()
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
        protected internal override bool AroundReceive(Receive receive, object message)
        {
            if (_isInitialized)
            {
                if (!(message is InitTimeout))
                    return base.AroundReceive(receive, message);
            }
            else if (message is SetStore)
            {
                _store = ((SetStore) message).Store;
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

            return _store.Ask<IImmutableList<Exception>>(new AsyncWriteTarget.WriteMessages(messages), Timeout);
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

            return _store.Ask(new AsyncWriteTarget.DeleteMessagesTo(persistenceId, toSequenceNr), Timeout);
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

            _store.Tell(new AsyncWriteTarget.ReplayMessages(persistenceId, fromSequenceNr, toSequenceNr, max), mediator);

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

            return _store.Ask<AsyncWriteTarget.ReplaySuccess>(new AsyncWriteTarget.ReplayMessages(persistenceId, 0, 0, 0), Timeout)
                .ContinueWith(t => t.Result.HighestSequenceNr, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        private Task<T> StoreNotInitialized<T>()
        {
            var promise = new TaskCompletionSource<T>();
            promise.SetException(new TimeoutException("Store not initialized."));
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
            if (message is IPersistentRepresentation) _replayCallback(message as IPersistentRepresentation);
            else if (message is AsyncWriteTarget.ReplaySuccess)
            {
                _replayCompletionPromise.SetResult(new object());
                Context.Stop(Self);
            }
            else if (message is AsyncWriteTarget.ReplayFailure)
            {
                var failure = message as AsyncWriteTarget.ReplayFailure;
                _replayCompletionPromise.SetException(failure.Cause);
                Context.Stop(Self);
            }
            else if (message is ReceiveTimeout)
            {
                var timeoutException = new AsyncReplayTimeoutException($"Replay timed out after {_replayTimeout.TotalSeconds}s of inactivity");
                _replayCompletionPromise.SetException(timeoutException);
                Context.Stop(Self);
            }
            else return false;
            return true;
        }
    }
}
