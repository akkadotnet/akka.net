using System;
using System.Collections.Generic;
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

    public abstract class Recovery : ActorBase, IRecovery, ISnapshotter, WithUnboundedStash
    {
        private Exception _recoveryFailureCause;
        private Envelope _recoveryFailureMessage;
        private long _lastSequenceNr = 0L;
        private IPersistentRepresentation _currentPersistent;

        protected internal List<IResequencable> ProcessorBatch;

        public IStash Stash { get; set; }

        protected Recovery()
        {
            var persistenceExt = Context.System.GetExtension<Persistence>();
            if (persistenceExt == null)
            {
                throw new ArgumentException(string.Format("Couldn't intantiate {0} class, because current context ActorSystem has no Persistence extension initalized", 
                    GetType().FullName));
            }

            PersistenceId = persistenceExt.PersistenceId(Self);
            ProcessorBatch = new List<IResequencable>();
        }

        public string SnapshoterId { get; private set; }
        public string PersistenceId { get; private set; }
        public long LastSequenceNr { get { return _lastSequenceNr; } }
        public long SnapshotSequenceNr { get { return _lastSequenceNr; }}

        internal ActorRef Journal { get; private set; }

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

        protected internal void WithCurrentPersistent(IPersistentRepresentation persistent, Action<Receive, IPersistentRepresentation> body)
        {
            try
            {
                _currentPersistent = persistent;
                UpdateLastSequenceNr(persistent);
                body(Receive, persistent);
            }
            finally
            {
                _currentPersistent = null;
            }
        }

        internal void InternalUnhandled(object message)
        {
            Unhandled(message);
        }

        internal void UpdateLastSequenceNr(IPersistentRepresentation persistent)
        {
            throw new NotImplementedException();
        }
        internal void UpdateLastSequenceNr(long sequenceNumber)
        {
            throw new NotImplementedException();
        }

        internal void OnReplayFailure(Receive receive, bool shouldAwait, Exception cause)
        {
            throw new NotImplementedException();
        }

        internal void OnReplaySuccess(Receive receive, bool shouldAwait)
        {
            throw new NotImplementedException();
        }

        protected IState RecoveryStarted(long replayMax)
        {
            return new RecoveryStartedState(this, replayMax);
        }
    }

}