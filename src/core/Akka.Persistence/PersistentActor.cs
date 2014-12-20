using System;
using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    /// Sent to a <see cref="PersistentActor"/> if a journal fails to write a persistent message. 
    /// If not handled, an <see cref="ActorKilledException"/> is thrown by that persistent actor.
    /// </summary>
    [Serializable]
    public struct PersistenceFailure
    {
        public PersistenceFailure(object payload, long sequenceNr, Exception cause) : this()
        {
            Payload = payload;
            SequenceNr = sequenceNr;
            Cause = cause;
        }

        /// <summary>
        /// Payload of the persistent message.
        /// </summary>
        public object Payload { get; private set; }
        
        /// <summary>
        /// Sequence number of the persistent message.
        /// </summary>
        public long SequenceNr { get; private set; }

        /// <summary>
        /// Failure cause.
        /// </summary>
        public Exception Cause { get; private set; }
    }

    /// <summary>
    /// Sent to a <see cref="PersistentActor"/> if a journal fails to replay messages or fetch that 
    /// persistent actor's highest sequence number. If not handled, the actor will be stopped.
    /// </summary>
    [Serializable]
    public struct RecoveryFailure
    {
        public RecoveryFailure(Exception cause) : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    [Serializable]
    public sealed class RecoveryCompleted
    {
        public static readonly RecoveryCompleted Instance = new RecoveryCompleted();
        private RecoveryCompleted(){}
    }

    /// <summary>
    /// Instructs a <see cref="PersistentActor"/> to recover itself. Recovery will start from the first previously saved snapshot
    /// matching provided <see cref="FromSnapshot"/> selection criteria, if any. Otherwise it will replay all journaled messages.
    /// 
    /// If recovery starts from a snapshot, the <see cref="PersistentActor"/> is offered with that snapshot wrapped in 
    /// <see cref="SnapshotOffer"/> message, followed by replayed messages, if any, that are younger than the snapshot, up to the
    /// specified upper sequence number bound (<see cref="ToSequenceNr"/>).
    /// </summary>
    [Serializable]
    public struct Recover
    {
        public Recover(SnapshotSelectionCriteria fromSnapshot, long toSequenceNr = long.MaxValue, long replayMax = long.MaxValue)
            : this()
        {
            FromSnapshot = fromSnapshot;
            ToSequenceNr = toSequenceNr;
            ReplayMax = replayMax;
        }

        /// <summary>
        /// Criteria for selecting a saved snapshot from which recovery should start. Default is del youngest snapshot.
        /// </summary>
        public SnapshotSelectionCriteria FromSnapshot { get; private set; }

        /// <summary>
        /// Upper, inclusive sequence number bound. Default is no upper bound.
        /// </summary>
        public long ToSequenceNr { get; private set; }

        /// <summary>
        /// Maximum number of messages to replay. Default is no limit.
        /// </summary>
        public long ReplayMax { get; private set; }
    }

    /// <summary>
    /// Persistent actor - can be used to implement command or eventsourcing.
    /// </summary>
    public abstract class PersistentActor : Eventsourced
    {
        protected override bool Receive(object message)
        {
            return ReceiveCommand(message);
        }
    }

}