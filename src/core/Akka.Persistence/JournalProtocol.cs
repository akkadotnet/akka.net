//-----------------------------------------------------------------------
// <copyright file="JournalProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    /// Marker interface for internal journal messages
    /// </summary>
    public interface IJournalMessage : IPersistenceMessage { }

    /// <summary>
    /// Internal journal command
    /// </summary>
    public interface IJournalRequest : IJournalMessage { }
    
    /// <summary>
    /// Internal journal acknowledgement
    /// </summary>
    public interface IJournalResponse : IJournalMessage { }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class DeleteMessagesSuccess : IJournalResponse, IEquatable<DeleteMessagesSuccess>
    {
        public DeleteMessagesSuccess(long toSequenceNr)
        {
            ToSequenceNr = toSequenceNr;
        }

        public readonly long ToSequenceNr;

        public bool Equals(DeleteMessagesSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return ToSequenceNr == other.ToSequenceNr;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteMessagesSuccess);
        }

        public override int GetHashCode()
        {
            return ToSequenceNr.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("DeleteMessagesSuccess<toSequenceNr: {0}>", ToSequenceNr);
        }
    }

    /// <summary>
    /// Reply message to failed <see cref="DeleteMessages"/> request.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class DeleteMessagesFailure : IJournalResponse, IEquatable<DeleteMessagesFailure>
    {
        public DeleteMessagesFailure(Exception cause, long toSequenceNr)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "DeleteMessagesFailure cause exception cannot be null");

            Cause = cause;
            ToSequenceNr = toSequenceNr;
        }

        public readonly Exception Cause;
        public readonly long ToSequenceNr;

        public bool Equals(DeleteMessagesFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && ToSequenceNr == other.ToSequenceNr;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteMessagesFailure);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Cause != null ? Cause.GetHashCode() : 0)*397) ^ ToSequenceNr.GetHashCode();
            }
        }

        public override string ToString()
        {
            return string.Format("DeleteMessagesFailure<cause: {0}, toSequenceNr: {1}>", Cause, ToSequenceNr);
        }
    }

    /// <summary>
    /// Request to delete all persistent messages with sequence numbers up to `toSequenceNr` (inclusive).  
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class DeleteMessagesTo : IJournalRequest, IEquatable<DeleteMessagesTo>
    {
        public DeleteMessagesTo(string persistenceId, long toSequenceNr, IActorRef persistentActor)
        {
            if (string.IsNullOrEmpty(persistenceId)) throw new ArgumentNullException("persistenceId", "DeleteMessagesTo requires persistence id to be provided");

            PersistenceId = persistenceId;
            ToSequenceNr = toSequenceNr;
            PersistentActor = persistentActor;
        }

        public readonly string PersistenceId;
        public readonly long ToSequenceNr;
        public readonly IActorRef PersistentActor;

        public bool Equals(DeleteMessagesTo other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(PersistenceId, other.PersistenceId) &&
                   ToSequenceNr == other.ToSequenceNr &&
                   Equals(PersistentActor, other.PersistentActor);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteMessagesTo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ToSequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ (PersistentActor != null ? PersistentActor.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("DeleteMessagesTo<pid: {0}, seqNr: {1}, persistentActor: {2}>", PersistenceId, ToSequenceNr, PersistentActor);
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class WriteMessages : IJournalRequest, IEquatable<WriteMessages>
    {
        public WriteMessages(IEnumerable<IPersistentEnvelope> messages, IActorRef persistentActor,
            int actorInstanceId)
        {
            Messages = messages;
            PersistentActor = persistentActor;
            ActorInstanceId = actorInstanceId;
        }

        public readonly IEnumerable<IPersistentEnvelope> Messages;
        public readonly IActorRef PersistentActor;
        public readonly int ActorInstanceId;

        public bool Equals(WriteMessages other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(PersistentActor, other.PersistentActor)
                   && Equals(Messages, other.Messages);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessages);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Messages != null ? Messages.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (PersistentActor != null ? PersistentActor.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ActorInstanceId;
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("WriteMessages<actorInstanceId: {0}, actor: {1}>", ActorInstanceId, PersistentActor);
        }
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requestor before all subsequent <see cref="WriteMessageSuccess"/> replies.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public class WriteMessagesSuccessful : IJournalResponse, IEquatable<WriteMessagesSuccessful>
    {
        public static readonly WriteMessagesSuccessful Instance = new WriteMessagesSuccessful();

        private WriteMessagesSuccessful() { }

        public bool Equals(WriteMessagesSuccessful other)
        {
            if (ReferenceEquals(other, null)) return false;

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessagesSuccessful);
        }

        public override string ToString()
        {
            return "WriteMessagesSuccessful<>";
        }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requestor before all subsequent <see cref="WriteMessageFailure"/> replies.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class WriteMessagesFailed : IJournalResponse, IEquatable<WriteMessagesFailed>
    {
        public WriteMessagesFailed(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "WriteMessagesFailed cause exception cannot be null");

            Cause = cause;
        }

        public readonly Exception Cause;

        public bool Equals(WriteMessagesFailed other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessagesFailed);
        }

        public override int GetHashCode()
        {
            return (Cause != null ? Cause.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return string.Format("WriteMessagesFailed<cause: {0}>", Cause);
        }
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class WriteMessageSuccess : IJournalResponse, IEquatable<WriteMessageSuccess>
    {
        public WriteMessageSuccess(IPersistentRepresentation persistent, int actorInstanceId)
        {
            Persistent = persistent;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Successfully written message.
        /// </summary>
        public readonly IPersistentRepresentation Persistent;
        public readonly int ActorInstanceId;

        public bool Equals(WriteMessageSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(Persistent, other.Persistent);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessageSuccess);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Persistent != null ? Persistent.GetHashCode() : 0) * 397) ^ ActorInstanceId;
            }
        }

        public override string ToString()
        {
            return string.Format("WriteMessageSuccess<actorInstanceId: {0}, message: {1}>", ActorInstanceId, Persistent);
        }
    }

    /// <summary>
    /// Reply message to a rejected <see cref="WriteMessages"/> request. The write of this message was rejected
    /// before it was stored, e.g. because it could not be serialized. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class WriteMessageRejected : IJournalResponse, IEquatable<WriteMessageRejected>
    {
        public WriteMessageRejected(IPersistentRepresentation persistent, Exception cause, int actorInstanceId)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "WriteMessageRejected cause exception cannot be null");

            Persistent = persistent;
            Cause = cause;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Message failed to be written.
        /// </summary>
        public readonly IPersistentRepresentation Persistent;

        /// <summary>
        /// Failure cause.
        /// </summary>
        public readonly Exception Cause;

        public readonly int ActorInstanceId;

        public bool Equals(WriteMessageRejected other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(Persistent, other.Persistent)
                   && Equals(Cause, other.Cause);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessageRejected);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Cause != null ? Cause.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ActorInstanceId;
                hashCode = (hashCode * 397) ^ (Persistent != null ? Persistent.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("WriteMessageRejected<actorInstanceId: {0}, message: {1}, cause: {2}>", ActorInstanceId, Persistent, Cause);
        }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class WriteMessageFailure : IJournalResponse, IEquatable<WriteMessageFailure>
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
        public readonly IPersistentRepresentation Persistent;

        /// <summary>
        /// Failure cause.
        /// </summary>
        public readonly Exception Cause;

        public readonly int ActorInstanceId;

        public bool Equals(WriteMessageFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(Persistent, other.Persistent)
                   && Equals(Cause, other.Cause);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessageFailure);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Cause != null ? Cause.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ActorInstanceId;
                hashCode = (hashCode * 397) ^ (Persistent != null ? Persistent.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("WriteMessageFailure<actorInstanceId: {0}, message: {1}, cause: {2}>", ActorInstanceId, Persistent, Cause);
        }
    }

    /// <summary>
    /// Reply message to a <see cref="WriteMessages"/> with a non-persistent message.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class LoopMessageSuccess : IJournalResponse, IEquatable<LoopMessageSuccess>
    {
        public LoopMessageSuccess(object message, int actorInstanceId)
        {
            Message = message;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// A looped message.
        /// </summary>
        public readonly object Message;
        public readonly int ActorInstanceId;

        public bool Equals(LoopMessageSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(Message, other.Message);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as LoopMessageSuccess);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Message != null ? Message.GetHashCode() : 0) * 397) ^ ActorInstanceId;
            }
        }

        public override string ToString()
        {
            return string.Format("LoopMessageSuccess<actorInstanceId: {0}, message: {1}>", ActorInstanceId, Message);
        }
    }

    /// <summary>
    /// Request to replay messages to the <see cref="PersistentActor"/>.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class ReplayMessages : IJournalRequest, IEquatable<ReplayMessages>
    {
        public ReplayMessages(long fromSequenceNr, long toSequenceNr, long max, string persistenceId,
            IActorRef persistentActor)
        {
            FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            Max = max;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
        }

        /// <summary>
        /// Inclusive lower sequence number bound where a replay should start.
        /// </summary>
        public readonly long FromSequenceNr;

        /// <summary>
        /// Inclusive upper sequence number bound where a replay should end.
        /// </summary>
        public readonly long ToSequenceNr;

        /// <summary>
        /// Maximum number of messages to be replayed.
        /// </summary>
        public readonly long Max;

        /// <summary>
        /// Requesting persistent actor identifier.
        /// </summary>
        public readonly string PersistenceId;

        /// <summary>
        /// Requesting persistent actor.
        /// </summary>
        public readonly IActorRef PersistentActor;

        public bool Equals(ReplayMessages other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(PersistenceId, other.PersistenceId)
                   && Equals(PersistentActor, other.PersistentActor)
                   && Equals(FromSequenceNr, other.FromSequenceNr)
                   && Equals(ToSequenceNr, other.ToSequenceNr)
                   && Equals(Max, other.Max);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayMessages);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = FromSequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ ToSequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ Max.GetHashCode();
                hashCode = (hashCode * 397) ^ (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (PersistentActor != null ? PersistentActor.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Reply message to a <see cref="ReplayMessages"/> request. A separate reply is sent to the requestor for each replayed message.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class ReplayedMessage : IJournalResponse, IEquatable<ReplayedMessage>
    {
        public ReplayedMessage(IPersistentRepresentation persistent)
        {
            Persistent = persistent;
        }

        public readonly IPersistentRepresentation Persistent;

        public bool Equals(ReplayedMessage other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Persistent, other.Persistent);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayedMessage);
        }

        public override int GetHashCode()
        {
            return (Persistent != null ? Persistent.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return string.Format("ReplayedMessage<message: {0}>", Persistent);
        }
    }

    /// <summary>
    /// Reply message to a successful <see cref="ReplayMessages"/> request. This reply is sent 
    /// to the requestor after all <see cref="ReplayedMessage"/> have been sent (if any).
    /// 
    /// It includes the highest stored sequence number of a given persistent actor.
    /// Note that the replay might have been limited to a lower sequence number.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public class RecoverySuccess : IJournalResponse, IEquatable<RecoverySuccess>
    {
        public RecoverySuccess(long highestSequenceNr)
        {
            HighestSequenceNr = highestSequenceNr;
        }

        public readonly long HighestSequenceNr;

        public bool Equals(RecoverySuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(HighestSequenceNr, other.HighestSequenceNr);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as RecoverySuccess);
        }

        public override int GetHashCode()
        {
            return HighestSequenceNr.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("RecoverySuccess<highestSequenceNr: {0}>", HighestSequenceNr);
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class ReplayMessagesFailure : IJournalResponse, IEquatable<ReplayMessagesFailure>
    {
        public ReplayMessagesFailure(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "ReplayMessagesFailure cause exception cannot be null");

            Cause = cause;
        }

        public readonly Exception Cause;

        public bool Equals(ReplayMessagesFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayMessagesFailure);
        }

        public override int GetHashCode()
        {
            return Cause.GetHashCode();
        }
        
        public override string ToString()
        {
            return string.Format("ReplayMessagesFailure<cause: {0}>", Cause);
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class ReadHighestSequenceNr : IEquatable<ReadHighestSequenceNr>
    {
        public ReadHighestSequenceNr(long fromSequenceNr, string persistenceId, IActorRef persistentActor)
        {
            FromSequenceNr = fromSequenceNr;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
        }

        public readonly long FromSequenceNr;

        public readonly string PersistenceId;

        public readonly IActorRef PersistentActor;

        public bool Equals(ReadHighestSequenceNr other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(PersistenceId, other.PersistenceId)
                   && Equals(FromSequenceNr, other.FromSequenceNr)
                   && Equals(PersistentActor, other.PersistentActor);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReadHighestSequenceNr);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = FromSequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (PersistentActor != null ? PersistentActor.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("ReadHighestSequenceNr<pid: {0}, fromSeqNr: {1}, actor: {2}>", PersistenceId, FromSequenceNr, PersistentActor);
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class ReadHighestSequenceNrSuccess : IEquatable<ReadHighestSequenceNrSuccess>, IComparable<ReadHighestSequenceNrSuccess>
    {

        public ReadHighestSequenceNrSuccess(long highestSequenceNr)
        {
            HighestSequenceNr = highestSequenceNr;
        }

        public readonly long HighestSequenceNr;

        public bool Equals(ReadHighestSequenceNrSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return HighestSequenceNr == other.HighestSequenceNr;
        }

        public int CompareTo(ReadHighestSequenceNrSuccess other)
        {
            if (other == null) return 1;
            return other.HighestSequenceNr.CompareTo(HighestSequenceNr);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReadHighestSequenceNrSuccess);
        }

        public override int GetHashCode()
        {
            return HighestSequenceNr.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("ReadHighestSequenceNrSuccess<nr: {0}>", HighestSequenceNr);
        }
    }

#if SERIALIZATION
    [Serializable]
#endif
    public sealed class ReadHighestSequenceNrFailure : IEquatable<ReadHighestSequenceNrFailure>
    {
        public ReadHighestSequenceNrFailure(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException("cause", "ReadHighestSequenceNrFailure cause exception cannot be null");

            Cause = cause;
        }

        public readonly Exception Cause;

        public bool Equals(ReadHighestSequenceNrFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReadHighestSequenceNrFailure);
        }

        public override int GetHashCode()
        {
            return Cause.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("ReadHighestSequenceNrFailure<cause: {0}>", Cause);
        }
    }
}

