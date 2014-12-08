using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    public class AsyncReplayTimeoutException : AkkaException
    {
        public AsyncReplayTimeoutException(string msg)
            : base(msg)
        {
        }
    }

    [Serializable]
    internal sealed class SetStore
    {
        public SetStore(ActorRef store)
        {
            Store = store;
        }

        public ActorRef Store { get; private set; }
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
            else
            {
                Stash.Stash();
            }

            return true;
        }

        public override Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback)
        {
            var mediator = Context.ActorOf(Props.Create(()=>new ReplayMediator(replayCallback, null, Timeout)).WithDeploy(Deploy.Local));
            _store.Tell(new ReplayMessages(fromSequenceNr, toSequenceNr, max, persistenceId, mediator));
            return null;
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteMessagesAsync(IEnumerable<IPersistentRepresentation> messages)
        {
            throw new NotImplementedException();
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            throw new NotImplementedException();
        }
    }

    internal struct ReplayFailure
    {
        public ReplayFailure(Exception cause) : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    internal class ReplaySuccess
    {
        public static readonly ReplaySuccess Instance = new ReplaySuccess();
        private ReplaySuccess()
        {
        }
    }

    internal class ReplayMediator : ActorBase
    {
        private readonly Action<IPersistentRepresentation> _replayCallback;
        private readonly Task<bool> _replayCompletion;
        private readonly TimeSpan _replayTimeout;

        public ReplayMediator(Action<IPersistentRepresentation> replayCallback, Task<bool> replayCompletion, TimeSpan replayTimeout)
        {
            _replayCallback = replayCallback;
            _replayCompletion = replayCompletion;
            _replayTimeout = replayTimeout;

            Context.SetReceiveTimeout(replayTimeout);
        }

        protected override bool Receive(object message)
        {
            if (message is IPersistentRepresentation)
            {
                _replayCallback(message as IPersistentRepresentation);
            }
            else if (message is ReplaySuccess)
            {
            }
            else if (message is ReplayFailure)
            {

            }
            else if (message is ReceiveTimeout)
            {

            }
            else
            {
                return false;
            }

            return true;
        }
    }
}