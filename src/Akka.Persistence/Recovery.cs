using System;
using Akka.Actor;

namespace Akka.Persistence
{
    public interface IRecovery
    {
        string PersistenceId { get; }
        long LastSequenceNr { get; }
        long SnapshotSequenceNr { get; }
    }

    public struct Recover
    {
        public Recover(SnapshotSelectionCriteria fromSnapshot, long toSequenceNr = long.MaxValue, long replayMax = long.MaxValue) 
            : this()
        {
            FromSnapshot = fromSnapshot;
            ToSequenceNr = toSequenceNr;
            ReplayMax = replayMax;
        }

        public SnapshotSelectionCriteria FromSnapshot { get; private set; }
        public long ToSequenceNr { get; private set; }
        public long ReplayMax { get; private set; }
    }

    public abstract class Recovery : ActorBase, IRecovery, ISnapshotter, IStash
    {
        private Exception _recoveryFailureCause;
        private Envelope _recoveryFailureMessage;
        private long _lastSequenceNr = 0L;
        private Persistent _currentPersistent;

        protected Recovery()
        {
            var persistenceExt = Context.System.GetExtension<Persistence>();
            if (persistenceExt == null)
            {
                throw new ArgumentException(string.Format("Couldn't intantiate {0} class, because current context ActorSystem has no Persistence extension initalized", 
                    GetType().FullName));
            }

            PersistenceId = persistenceExt.PersistenceId(Self);
        }

        public string SnapshoterId { get; private set; }
        public string PersistenceId { get; private set; }
        public long LastSequenceNr { get { return _lastSequenceNr; } }
        public long SnapshotSequenceNr { get { return _lastSequenceNr; }}
        public void LoadSnapshot(string persistenceId, SnapshotSelectionCriteria criteria, long toSequenceNr)
        {
            throw new NotImplementedException();
        }

        public void SaveSnapshot(object snapshot)
        {
            throw new NotImplementedException();
        }

        public void DeleteSnapshot(long sequenceNr, long timestamp)
        {
            throw new NotImplementedException();
        }

        public void DeleteSnapshots(SnapshotSelectionCriteria criteria)
        {
            throw new NotImplementedException();
        }

        public void Stash()
        {
            throw new NotImplementedException();
        }

        public void Unstash()
        {
            throw new NotImplementedException();
        }

        public void UnstashAll()
        {
            throw new NotImplementedException();
        }

        public void UnstashAll(Func<Envelope, bool> predicate)
        {
            throw new NotImplementedException();
        }

        protected void WithCurrentPersistent(Persistent persistent, Action<Persistent> body)
        {
            try
            {
                _currentPersistent = persistent;
                UpdateLastSequenceNr(persistent);
                body(persistent);
            }
            finally
            {
                _currentPersistent = null;
            }
        }

        private void UpdateLastSequenceNr(Persistent persistent)
        {
            throw new NotImplementedException();
        }
    }

    internal abstract class State
    {
        private readonly Action<object> _unhandled;
        private readonly Action<Persistent, Action<Persistent>> _withCurrentPersistent;

        protected State(Action<object> unhandled, Action<Persistent, Action<Persistent>> withCurrentPersistent)
        {
            _unhandled = unhandled;
            _withCurrentPersistent = withCurrentPersistent;
        }

        public abstract void AroundReceive(Receive receive, object message);

        protected void Process(Receive receive, object message)
        {
            var handled = receive(message);
            if (!handled)
            {
                _unhandled(message);
            }
        }

        protected void ProcessPersistent(Receive receive, Persistent persistent)
        {
            _withCurrentPersistent(persistent, p => Process(receive, persistent));
        }

        protected void RecordFailure(Exception cause)
        {
            throw new NotImplementedException();
        }
    }

    internal class RecoveryPendingState : State
    {
        public RecoveryPendingState(Action<object> unhandled, Action<Persistent, Action<Persistent>> withCurrentPersistent) 
            : base(unhandled, withCurrentPersistent)
        {
        }

        public override void AroundReceive(Receive receive, object message)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return "recovery pending";
        }
    }
}