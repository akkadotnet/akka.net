using System;
using Akka.Actor;

namespace Akka.Persistence
{
    public struct Update
    {
        public Update(bool isAwait = false, long replayMax = long.MaxValue) 
            : this()
        {
            IsAwait = isAwait;
            ReplayMax = replayMax;
        }

        public bool IsAwait { get; private set; }
        public long ReplayMax { get; private set; }
    }

    public abstract class PersistentView : ActorBase, IRecovery
    {

        public string PersistenceId { get; private set; }
        public long LastSequenceNr { get; private set; }
        public long SnapshotSequenceNr { get; private set; }
        public string ViewId { get; set; }
        public string SnapshotterId { get; set; }

        public bool IsPersistent
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public bool AutoUpdate
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public TimeSpan AutoUpdateInterval
        {
            get
            {
                throw new NotImplementedException();
            }
        }
        public TimeSpan AutoUpdateReplayMax
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        protected override void PreStart()
        {
            base.PreStart();
        }

        protected override void PreRestart(Exception reason, object message)
        {
            base.PreRestart(reason, message);
        }

        protected override void PostStop()
        {
            base.PostStop();
        }
    }
}