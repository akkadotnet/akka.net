using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    [Serializable]
    public class AsyncReplayTimeoutException : AkkaException
    {
        public AsyncReplayTimeoutException(string msg)
            : base(msg)
        {
        }
    }

    [Serializable]
    public sealed class SetStore
    {
        public SetStore(ActorRef store)
        {
            Store = store;
        }

        public ActorRef Store { get; private set; }
    }

    public static class AsyncWriteTarget
    {

        #region Internal Messages

        [Serializable]
        public sealed class ReplayFailure
        {
            public ReplayFailure(Exception cause)
            {
                Cause = cause;
            }

            public Exception Cause { get; private set; }
        }

        [Serializable]
        public sealed class ReplaySuccess
        {
            public static readonly ReplaySuccess Instance = new ReplaySuccess();

            private ReplaySuccess()
            {
            }
        }

        [Serializable]
        public sealed class WriteMessages
        {
            public WriteMessages(IEnumerable<IPersistentRepresentation> messages)
            {
                Messages = messages;
            }

            public IEnumerable<IPersistentRepresentation> Messages { get; private set; }
        }

        [Serializable]
        public sealed class ReplayMessages : IWithPersistenceId
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
        }

        [Serializable]
        public sealed class ReadHighestSequenceNr : IWithPersistenceId
        {
            public ReadHighestSequenceNr(string persistenceId, long fromSequenceNr)
            {
                PersistenceId = persistenceId;
                FromSequenceNr = fromSequenceNr;
            }

            public string PersistenceId { get; private set; }
            public long FromSequenceNr { get; private set; }
        }

        [Serializable]
        public sealed class DeleteMessagesTo
        {
            public DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent)
            {
                PersistenceId = persistenceId;
                ToSequenceNr = toSequenceNr;
                IsPermanent = isPermanent;
            }

            public string PersistenceId { get; private set; }
            public long ToSequenceNr { get; private set; }
            public bool IsPermanent { get; private set; }
        }

        #endregion
    }

    public abstract class AsyncWriteProxy : AsyncWriteJournal, WithUnboundedStash
    {
        private readonly Receive _initialized;
        private ActorRef _store;

        public IStash Stash { get; set; }
        public TimeSpan Timeout { get; private set; }

        protected AsyncWriteProxy()
        {
            //TODO: turn into configurable value
            Timeout = TimeSpan.FromSeconds(5);
            _initialized = base.Receive;
        }

        protected override bool Receive(object message)
        {
            if (message is SetStore)
            {
                var setStore = message as SetStore;
                _store = setStore.Store;
                Stash.UnstashAll();
                Context.Become(_initialized);
            }
            else Stash.Stash();

            return true;
        }

        public override Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback)
        {
            var replayCompletionPromise = new TaskCompletionSource<object>();
            var mediator = Context.ActorOf(Props.Create(() => new ReplayMediator(replayCallback, replayCompletionPromise, Timeout)).WithDeploy(Deploy.Local));

            _store.Tell(new AsyncWriteTarget.ReplayMessages(persistenceId, fromSequenceNr, toSequenceNr, max), mediator);

            return replayCompletionPromise.Task;
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return _store.Ask(new AsyncWriteTarget.ReadHighestSequenceNr(persistenceId, fromSequenceNr))
                .ContinueWith(t => (long)t.Result);
        }

        protected override Task WriteMessagesAsync(IEnumerable<IPersistentRepresentation> messages)
        {
            return _store.Ask(new AsyncWriteTarget.WriteMessages(messages));
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            return _store.Ask(new AsyncWriteTarget.DeleteMessagesTo(persistenceId, toSequenceNr, isPermanent));
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
                var timeoutException = new AsyncReplayTimeoutException("Replay timed out after " + _replayTimeout.TotalSeconds + " of inactivity");
                _replayCompletionPromise.SetException(timeoutException);
                Context.Stop(Self);
            }
            else return false;
            return true;
        }
    }
}