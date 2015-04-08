//-----------------------------------------------------------------------
// <copyright file="JournalProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence
{
    public sealed class DeleteMessages
    {
        public DeleteMessages(IEnumerable<IPersistentEnvelope> messageIds, bool isPermanent, IActorRef requestor)
        {
            MessageIds = messageIds;
            IsPermanent = isPermanent;
            Requestor = requestor;
        }

        public IEnumerable<IPersistentEnvelope> MessageIds { get; private set; }
        public bool IsPermanent { get; private set; }
        public IActorRef Requestor { get; private set; }
    }

    public sealed class DeleteMessagesSuccess
    {
        public DeleteMessagesSuccess(IEnumerable<IPersistentEnvelope> messageIds)
        {
            MessageIds = messageIds;
        }

        public IEnumerable<IPersistentEnvelope> MessageIds { get; private set; }
    }

    /// <summary>
    /// Reply message to failed <see cref="DeleteMessages"/> request.
    /// </summary>
    public sealed class DeleteMessagesFailure
    {
        public DeleteMessagesFailure(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "DeleteMessagesFailure cause exception cannot be null");

            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    /// <summary>
    /// Request to delete all persistent messages with sequence numbers up to `toSequenceNr` (inclusive).  
    /// </summary>
    public sealed class DeleteMessagesTo : IEquatable<DeleteMessagesTo>
    {
        public DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            PersistenceId = persistenceId;
            ToSequenceNr = toSequenceNr;
            IsPermanent = isPermanent;
        }

        public string PersistenceId { get; private set; }
        public long ToSequenceNr { get; private set; }

        /// <summary>
        /// If false, the persistent messages are marked as deleted in the journal, 
        /// otherwise they are permanently deleted from the journal.
        /// </summary>
        public bool IsPermanent { get; private set; }

        public bool Equals(DeleteMessagesTo other)
        {
            if (other == null) return false;
            return PersistenceId == other.PersistenceId &&
                ToSequenceNr == other.ToSequenceNr &&
                IsPermanent == other.IsPermanent;
        }
    }

    internal sealed class WriteConfirmationsFailure
    {
        public WriteConfirmationsFailure(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "WriteConfirmationsFailure cause exception cannot be null");

            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    public sealed class WriteMessages
    {
        public WriteMessages(IEnumerable<IPersistentEnvelope> messages, IActorRef persistentActor,
            int actorInstanceId)
        {
            Messages = messages;
            PersistentActor = persistentActor;
            ActorInstanceId = actorInstanceId;
        }

        public IEnumerable<IPersistentEnvelope> Messages { get; private set; }
        public IActorRef PersistentActor { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requestor before all subsequent <see cref="WriteMessageSuccess"/> replies.
    /// </summary>
    [Serializable]
    public class WriteMessagesSuccessful : IEquatable<WriteMessagesSuccessful>
    {
        public static readonly WriteMessagesSuccessful Instance = new WriteMessagesSuccessful();

        private WriteMessagesSuccessful() { }

        public bool Equals(WriteMessagesSuccessful other)
        {
            return true;
        }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requestor before all subsequent <see cref="WriteMessageFailure"/> replies.
    /// </summary>
    public sealed class WriteMessagesFailed
    {
        public WriteMessagesFailed(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "WriteMessagesFailed cause exception cannot be null");

            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
    public sealed class WriteMessageSuccess
    {
        public WriteMessageSuccess(IPersistentRepresentation persistent, int actorInstanceId)
        {
            Persistent = persistent;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Successfully written message.
        /// </summary>
        public IPersistentRepresentation Persistent { get; private set; }

        public int ActorInstanceId { get; private set; }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
    public sealed class WriteMessageFailure
    {
        public WriteMessageFailure(IPersistentRepresentation persistent, Exception cause, int actorInstanceId)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "WriteMessageFailure cause exception cannot be null");

            Persistent = persistent;
            Cause = cause;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Message failed to be written.
        /// </summary>
        public IPersistentRepresentation Persistent { get; private set; }

        /// <summary>
        /// Failure cause.
        /// </summary>
        public Exception Cause { get; private set; }

        public int ActorInstanceId { get; private set; }
    }

    public sealed class LoopMessage
    {
        public LoopMessage(object message, IActorRef persistentActor, int actorInstanceId)
        {
            Message = message;
            PersistentActor = persistentActor;
            ActorInstanceId = actorInstanceId;
        }

        public object Message { get; private set; }
        public IActorRef PersistentActor { get; private set; }
        public int ActorInstanceId { get; private set; }
    }

    /// <summary>
    /// Reply message to a <see cref="WriteMessages"/> with a non-persistent message.
    /// </summary>
    public sealed class LoopMessageSuccess
    {
        public LoopMessageSuccess(object message, int actorInstanceId)
        {
            Message = message;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// A looped message.
        /// </summary>
        public object Message { get; private set; }

        public int ActorInstanceId { get; private set; }
    }

    /// <summary>
    /// Request to replay messages to the <see cref="PersistentActor"/>.
    /// </summary>
    public sealed class ReplayMessages : IEquatable<ReplayMessages>
    {
        public ReplayMessages(long fromSequenceNr, long toSequenceNr, long max, string persistenceId,
            IActorRef persistentActor, bool replayDeleted = false)
        {
            FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            Max = max;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
            ReplayDeleted = replayDeleted;
        }

        /// <summary>
        /// Inclusive lower sequence number bound where a replay should start.
        /// </summary>
        public long FromSequenceNr { get; private set; }

        /// <summary>
        /// Inclusive upper sequence number bound where a replay should end.
        /// </summary>
        public long ToSequenceNr { get; private set; }

        /// <summary>
        /// Maximum number of messages to be replayed.
        /// </summary>
        public long Max { get; private set; }

        /// <summary>
        /// Requesting persistent actor identifier.
        /// </summary>
        public string PersistenceId { get; private set; }

        /// <summary>
        /// Requesting persistent actor.
        /// </summary>
        public IActorRef PersistentActor { get; private set; }

        /// <summary>
        /// If true, message marked as deleted shall be replayed.
        /// </summary>
        public bool ReplayDeleted { get; private set; }

        public bool Equals(ReplayMessages other)
        {
            if (other == null) return false;
            return PersistenceId == other.PersistenceId
                   && PersistentActor == other.PersistentActor
                   && FromSequenceNr == other.FromSequenceNr
                   && ToSequenceNr == other.ToSequenceNr
                   && Max == other.Max
                   && ReplayDeleted == other.ReplayDeleted;
        }
    }

    /// <summary>
    /// Reply message to a <see cref="ReplayMessages"/> request. A separate reply is sent to the requestor for each replayed message.
    /// </summary>
    public sealed class ReplayedMessage
    {
        public ReplayedMessage(IPersistentRepresentation persistent)
        {
            Persistent = persistent;
        }

        public IPersistentRepresentation Persistent { get; private set; }
    }

    /// <summary>
    /// Reply message to a successful <see cref="ReplayMessages"/> request. This reply is sent 
    /// to the requestor after all <see cref="ReplayedMessage"/> have been sent (if any).
    /// </summary>
    public class ReplayMessagesSuccess : IEquatable<ReplayMessagesSuccess>
    {
        public static readonly ReplayMessagesSuccess Instance = new ReplayMessagesSuccess();

        private ReplayMessagesSuccess() { }

        public bool Equals(ReplayMessagesSuccess other)
        {
            return true;
        }
    }

    public sealed class ReplayMessagesFailure
    {
        public ReplayMessagesFailure(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "ReplayMessagesFailure cause exception cannot be null");

            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    public sealed class ReadHighestSequenceNr : IEquatable<ReadHighestSequenceNr>
    {
        public ReadHighestSequenceNr(long fromSequenceNr, string persistenceId, IActorRef persistentActor)
        {
            FromSequenceNr = fromSequenceNr;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
        }

        public long FromSequenceNr { get; private set; }

        public string PersistenceId { get; private set; }

        public IActorRef PersistentActor { get; private set; }

        public bool Equals(ReadHighestSequenceNr other)
        {
            if (other == null) return false;
            return PersistenceId == other.PersistenceId
                   && FromSequenceNr == other.FromSequenceNr
                   && PersistentActor == other.PersistentActor;
        }
    }

    public sealed class ReadHighestSequenceNrSuccess : IEquatable<ReadHighestSequenceNrSuccess>, IComparable<ReadHighestSequenceNrSuccess>
    {

        public ReadHighestSequenceNrSuccess(long highestSequenceNr)
        {
            HighestSequenceNr = highestSequenceNr;
        }

        public long HighestSequenceNr { get; private set; }

        public bool Equals(ReadHighestSequenceNrSuccess other)
        {
            return HighestSequenceNr == other.HighestSequenceNr;
        }

        public int CompareTo(ReadHighestSequenceNrSuccess other)
        {
            if (other == null) return 1;
            return other.HighestSequenceNr.CompareTo(HighestSequenceNr);
        }

        public override bool Equals(object obj)
        {
            var other = obj as ReadHighestSequenceNrSuccess;
            return other != null && Equals(other);
        }

        public override int GetHashCode()
        {
            return HighestSequenceNr.GetHashCode();
        }
    }

    public sealed class ReadHighestSequenceNrFailure
    {
        public ReadHighestSequenceNrFailure(Exception cause)
        {
            if (cause == null) 
                throw new ArgumentNullException("cause", "ReadHighestSequenceNrFailure cause exception cannot be null");

            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }
}
