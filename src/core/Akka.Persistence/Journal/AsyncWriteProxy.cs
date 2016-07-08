//-----------------------------------------------------------------------
// <copyright file="AsyncWriteProxy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
#if SERIALIZATION
    [Serializable]
#endif
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

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class SetStore
    {
        public SetStore(IActorRef store)
        {
            if (store == null)
                throw new ArgumentNullException("store", "SetStore requires non-null reference to store actor");

            Store = store;
        }

        public readonly IActorRef Store;
    }

    public static class AsyncWriteTarget
    {

        #region Internal Messages

#if SERIALIZATION
        [Serializable]
#endif
        public sealed class ReplayFailure
        {
            public ReplayFailure(Exception cause)
            {
                if (cause == null)
                    throw new ArgumentNullException("cause", "AsyncWriteTarget.ReplayFailure cause exception cannot be null");

                Cause = cause;
            }

            public Exception Cause { get; private set; }
        }

#if SERIALIZATION
        [Serializable]
#endif
        public sealed class ReplaySuccess : IEquatable<ReplaySuccess>
        {
            public ReplaySuccess(long highestSequenceNr)
            {
                HighestSequenceNr = highestSequenceNr;
            }

            public long HighestSequenceNr { get; private set; }
            public bool Equals(ReplaySuccess other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return HighestSequenceNr == other.HighestSequenceNr;
            }
        }

#if SERIALIZATION
        [Serializable]
#endif
        public sealed class WriteMessages
        {
            public WriteMessages(IEnumerable<AtomicWrite> messages)
            {
                Messages = messages.ToArray();
            }

            public AtomicWrite[] Messages { get; private set; }
        }

#if SERIALIZATION
        [Serializable]
#endif
        public sealed class ReplayMessages : IEquatable<ReplayMessages>
        {
            public ReplayMessages(string persistenceId, long fromSequenceNr, long toSequenceNr, long max)
            {
                PersistenceId = persistenceId;
                FromSequenceNr = fromSequenceNr;
                ToSequenceNr = toSequenceNr;
                Max = max;
            }

            public string PersistenceId { get; private set; }
            public long FromSequenceNr { get; private set; }
            public long ToSequenceNr { get; private set; }
            public long Max { get; private set; }
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

#if SERIALIZATION
        [Serializable]
#endif
        public sealed class DeleteMessagesTo : IEquatable<DeleteMessagesTo>
        {
            public DeleteMessagesTo(string persistenceId, long toSequenceNr)
            {
                PersistenceId = persistenceId;
                ToSequenceNr = toSequenceNr;
            }

            public string PersistenceId { get; private set; }
            public long ToSequenceNr { get; private set; }
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

        protected AsyncWriteProxy()
        {
            _isInitialized = false;
            _isInitTimedOut = false;
            _store = null;
        }

        public abstract TimeSpan Timeout { get; }

        public override void AroundPreStart()
        {
            Context.System.Scheduler.ScheduleTellOnce(Timeout, Self, InitTimeout.Instance, Self);
            base.AroundPreStart();
        }

        protected override bool AroundReceive(Receive receive, object message)
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

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            if (_store == null)
                return StoreNotInitialized<IImmutableList<Exception>>();

            return _store.Ask<IImmutableList<Exception>>(new AsyncWriteTarget.WriteMessages(messages), Timeout);
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            if (_store == null)
                return StoreNotInitialized<object>();

            return _store.Ask(new AsyncWriteTarget.DeleteMessagesTo(persistenceId, toSequenceNr), Timeout);
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            if (_store == null)
                return StoreNotInitialized<object>();

            var replayCompletionPromise = new TaskCompletionSource<object>();
            var mediator = context.ActorOf(Props.Create(() => new ReplayMediator(recoveryCallback, replayCompletionPromise, Timeout)).WithDeploy(Deploy.Local));

            _store.Tell(new AsyncWriteTarget.ReplayMessages(persistenceId, fromSequenceNr, toSequenceNr, max), mediator);

            return replayCompletionPromise.Task;
        }

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
            promise.SetException(new TimeoutException("Store not intialized."));
            return promise.Task;
        }

        public IStash Stash { get; set; }

        // sent to self only
        public class InitTimeout
        {
            private InitTimeout() { }
            private static readonly InitTimeout _instance = new InitTimeout();

            public static InitTimeout Instance
            {
                get
                {
                    return _instance;
                }
            }
        }
    }

    internal class ReplayMediator : ActorBase
    {
        private readonly Action<IPersistentRepresentation> _replayCallback;
        private readonly TaskCompletionSource<object> _replayCompletionPromise;
        private readonly TimeSpan _replayTimeout;

        public ReplayMediator(Action<IPersistentRepresentation> replayCallback, TaskCompletionSource<object> replayCompletionPromise, TimeSpan replayTimeout)
        {
            _replayCallback = replayCallback;
            _replayCompletionPromise = replayCompletionPromise;
            _replayTimeout = replayTimeout;

            Context.SetReceiveTimeout(replayTimeout);
        }

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
                var timeoutException = new AsyncReplayTimeoutException("Replay timed out after " + _replayTimeout.TotalSeconds + "s of inactivity");
                _replayCompletionPromise.SetException(timeoutException);
                Context.Stop(Self);
            }
            else return false;
            return true;
        }
    }
}

