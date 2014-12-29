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
        public TimeSpan Timeout { get; set; }

        protected AsyncWriteProxy()
        {
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
            var mediator = Context.ActorOf(Props.Create(() => new ReplayMediator(replayCallback, Timeout)).WithDeploy(Deploy.Local));
            return _store.Ask(new AsyncWriteTarget.ReplayMessages(persistenceId, fromSequenceNr, toSequenceNr, max))
                // since we cannot set sender directly in Ask, just forward message to mediator
                // just make sure, that mediator won't call it's Sender inside
                .ContinueWith(t => mediator.Ask(t.Result)
                    .ContinueWith(mediatorResponseTask =>
                    {
                        // wait for mediator response and conditional turn it to Task failure exception
                        if (mediatorResponseTask.Result is ReplayMediator.ReplayCompletionSuccess) ;
                        else if (mediatorResponseTask.Result is ReplayMediator.ReplayCompletionFailure)
                        {
                            throw (mediatorResponseTask.Result as ReplayMediator.ReplayCompletionFailure).Cause;
                        }
                    }));
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
        #region Internal messages

        [Serializable]
        internal sealed class ReplayCompletionSuccess
        {
            public static readonly ReplayCompletionSuccess Instance = new ReplayCompletionSuccess();
            private ReplayCompletionSuccess() { }
        }

        [Serializable]
        internal sealed class ReplayCompletionFailure
        {
            public ReplayCompletionFailure(Exception cause)
            {
                Cause = cause;
            }

            public Exception Cause { get; private set; }
        }

        #endregion

        private readonly Action<IPersistentRepresentation> _replayCallback;
        private readonly TimeSpan _replayTimeout;

        public ReplayMediator(Action<IPersistentRepresentation> replayCallback, TimeSpan replayTimeout)
        {
            _replayCallback = replayCallback;
            _replayTimeout = replayTimeout;

            Context.SetReceiveTimeout(replayTimeout);
        }

        protected override bool Receive(object message)
        {
            if (message is IPersistentRepresentation) _replayCallback(message as IPersistentRepresentation);
            else if (message is AsyncWriteTarget.ReplaySuccess)
            {
                Sender.Tell(ReplayCompletionSuccess.Instance);
                Context.Stop(Self);
            }
            else if (message is AsyncWriteTarget.ReplayFailure)
            {
                var failure = message as AsyncWriteTarget.ReplayFailure;
                Sender.Tell(new ReplayCompletionFailure(failure.Cause));
                Context.Stop(Self);
            }
            else if (message is ReceiveTimeout)
            {
                var timeoutException = new AsyncReplayTimeoutException("Replay timed out after " + _replayTimeout.TotalSeconds + " of inactivity");
                Sender.Tell(new ReplayCompletionFailure(timeoutException));
                Context.Stop(Self);
            }
            else return false;
            return true;
        }
    }
}