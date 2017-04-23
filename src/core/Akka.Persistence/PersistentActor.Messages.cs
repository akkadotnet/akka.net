//-----------------------------------------------------------------------
// <copyright file="PersistentActor.Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence
{
    /// <summary>
    /// Sent to a <see cref="PersistentActor"/> when the journal replay has been finished.
    /// </summary>
    [Serializable]
    public sealed class RecoveryCompleted
    {
        /// <summary>
        /// The singleton instance of <see cref="RecoveryCompleted"/>.
        /// </summary>
        public static RecoveryCompleted Instance { get; } = new RecoveryCompleted();

        private RecoveryCompleted() {}

        public override bool Equals(object obj)
        {
            return obj is RecoveryCompleted;
        }
    }

    /// <summary>
    /// Recovery mode configuration object to be return in <see cref="PersistentActor.get_Recovery()"/>
    /// 
    /// By default recovers from latest snashot replays through to the last available event (last sequenceNr).
    /// 
    /// Recovery will start from a snapshot if the persistent actor has previously saved one or more snapshots
    /// and at least one of these snapshots matches the specified <see cref="FromSnapshot"/> criteria.
    /// Otherwise, recovery will start from scratch by replaying all stored events.
    /// 
    /// If recovery starts from a snapshot, the <see cref="PersistentActor"/> is offered that snapshot with a
    /// <see cref="SnapshotOffer"/> message, followed by replayed messages, if any, that are younger than the snapshot, up to the
    /// specified upper sequence number bound (<see cref="ToSequenceNr"/>).
    /// </summary>
    [Serializable]
    public sealed class Recovery
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static Recovery Default { get; } = new Recovery(SnapshotSelectionCriteria.Latest);

        /// <summary>
        /// Convenience method for skipping recovery in <see cref="PersistentActor"/>.
        /// </summary>
        public static Recovery None { get; } = new Recovery(SnapshotSelectionCriteria.Latest, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="Recovery"/> class.
        /// </summary>
        public Recovery() : this(SnapshotSelectionCriteria.Latest)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Recovery"/> class.
        /// </summary>
        /// <param name="fromSnapshot">Criteria for selecting a saved snapshot from which recovery should start.</param>
        public Recovery(SnapshotSelectionCriteria fromSnapshot) : this(fromSnapshot, long.MaxValue)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Recovery"/> class.
        /// </summary>
        /// <param name="fromSnapshot">Criteria for selecting a saved snapshot from which recovery should start.</param>
        /// <param name="toSequenceNr">Upper, inclusive sequence number bound for recovery. Default is no upper bound.</param>
        public Recovery(SnapshotSelectionCriteria fromSnapshot, long toSequenceNr) : this(fromSnapshot, toSequenceNr, long.MaxValue)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Recovery"/> class.
        /// </summary>
        /// <param name="fromSnapshot">Criteria for selecting a saved snapshot from which recovery should start.</param>
        /// <param name="toSequenceNr">Upper, inclusive sequence number bound for recovery. Default is no upper bound.</param>
        /// <param name="replayMax">Maximum number of messages to replay. Default is no limit.</param>
        public Recovery(SnapshotSelectionCriteria fromSnapshot = null, long toSequenceNr = long.MaxValue, long replayMax = long.MaxValue)
        {
            FromSnapshot = fromSnapshot != null ? fromSnapshot : SnapshotSelectionCriteria.Latest;
            ToSequenceNr = toSequenceNr;
            ReplayMax = replayMax;
        }

        /// <summary>
        /// Criteria for selecting a saved snapshot from which recovery should start. Default is latest (= youngest) snapshot.
        /// </summary>
        public SnapshotSelectionCriteria FromSnapshot { get; }

        /// <summary>
        /// Upper, inclusive sequence number bound for recovery. Default is no upper bound.
        /// </summary>
        public long ToSequenceNr { get; }

        /// <summary>
        /// Maximum number of messages to replay. Default is no limit.
        /// </summary>
        public long ReplayMax { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class RecoveryTimedOutException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RecoveryTimedOutException"/> class.
        /// </summary>
        public RecoveryTimedOutException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecoveryTimedOutException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public RecoveryTimedOutException(string message, Exception cause = null) : base(message, cause)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecoveryTimedOutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        public RecoveryTimedOutException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// This defines how to handle the current received message which failed to stash, when the size
    /// of the Stash exceeding the capacity of the Stash.
    /// </summary>
    public interface IStashOverflowStrategy { }

    /// <summary>
    /// Discard the message to <see cref="DeadLetterActorRef"/>
    /// </summary>
    public sealed class DiscardToDeadLetterStrategy : IStashOverflowStrategy
    {
        /// <summary>
        /// The singleton instance of <see cref="DiscardToDeadLetterStrategy"/>.
        /// </summary>
        public static DiscardToDeadLetterStrategy Instance { get; } = new DiscardToDeadLetterStrategy();

        private DiscardToDeadLetterStrategy() { }
    }

    /// <summary>
    /// Throw <see cref="StashOverflowException"/>, hence the persistent actor will start recovery
    /// if guarded by default supervisor strategy.
    /// Be careful if used together with <see cref="Eventsourced.Persist{TEvent}(TEvent,Action{TEvent})"/>
    /// or <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent},Action{TEvent})"/>
    /// or has many messages needed to replay.
    /// </summary>
    public sealed class ThrowOverflowExceptionStrategy : IStashOverflowStrategy
    {
        /// <summary>
        /// The singleton instance of <see cref="ThrowOverflowExceptionStrategy"/>.
        /// </summary>
        public static ThrowOverflowExceptionStrategy Instance { get; } = new ThrowOverflowExceptionStrategy();

        private ThrowOverflowExceptionStrategy() { }
    }

    /// <summary>
    /// Reply to sender with predefined response, and discard the received message silently.
    /// </summary>
    public sealed class ReplyToStrategy : IStashOverflowStrategy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReplyToStrategy"/> class.
        /// </summary>
        /// <param name="response">The message replying to sender with.</param>
        public ReplyToStrategy(object response)
        {
            Response = response;
        }

        /// <summary>
        /// The message replying to sender with.
        /// </summary>
        public object Response { get; }
    }

    /// <summary>
    /// Implement this interface in order to configure the <see cref="IStashOverflowStrategy"/>
    /// for the internal stash of the persistent actor.
    /// An instance of this class must be instantiable using a no-args constructor.
    /// </summary>
    public interface IStashOverflowStrategyConfigurator
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
        IStashOverflowStrategy Create(Config config);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ThrowExceptionConfigurator : IStashOverflowStrategyConfigurator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowExceptionConfigurator"/> class.
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
        public IStashOverflowStrategy Create(Config config)
        {
            return ThrowOverflowExceptionStrategy.Instance;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class DiscardConfigurator : IStashOverflowStrategyConfigurator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DiscardConfigurator"/> class.
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
        public IStashOverflowStrategy Create(Config config)
        {
            return DiscardToDeadLetterStrategy.Instance;
        }
    }
}
