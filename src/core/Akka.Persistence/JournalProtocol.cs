﻿//-----------------------------------------------------------------------
// <copyright file="JournalProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

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

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class DeleteMessagesSuccess : IJournalResponse, IEquatable<DeleteMessagesSuccess>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="toSequenceNr">TBD</param>
        public DeleteMessagesSuccess(long toSequenceNr)
        {
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly long ToSequenceNr;

        /// <inheritdoc/>
        public bool Equals(DeleteMessagesSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return ToSequenceNr == other.ToSequenceNr;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteMessagesSuccess);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return ToSequenceNr.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"DeleteMessagesSuccess<toSequenceNr: {ToSequenceNr}>";
        }
    }

    /// <summary>
    /// Reply message to failed <see cref="DeleteMessages"/> request.
    /// </summary>
    [Serializable]
    public sealed class DeleteMessagesFailure : IJournalResponse, IEquatable<DeleteMessagesFailure>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public DeleteMessagesFailure(Exception cause, long toSequenceNr)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "DeleteMessagesFailure cause exception cannot be null");

            Cause = cause;
            ToSequenceNr = toSequenceNr;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Exception Cause;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly long ToSequenceNr;

        /// <inheritdoc/>
        public bool Equals(DeleteMessagesFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause) && ToSequenceNr == other.ToSequenceNr;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteMessagesFailure);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Cause != null ? Cause.GetHashCode() : 0)*397) ^ ToSequenceNr.GetHashCode();
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"DeleteMessagesFailure<cause: {Cause}, toSequenceNr: {ToSequenceNr}>";
        }
    }

    /// <summary>
    /// Request to delete all persistent messages with sequence numbers up to `toSequenceNr` (inclusive).  
    /// </summary>
    [Serializable]
    public sealed class DeleteMessagesTo : IJournalRequest, IEquatable<DeleteMessagesTo>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <param name="persistentActor">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="persistenceId"/> is undefined.
        /// </exception>
        public DeleteMessagesTo(string persistenceId, long toSequenceNr, IActorRef persistentActor)
        {
            if (string.IsNullOrEmpty(persistenceId)) throw new ArgumentNullException(nameof(persistenceId), "DeleteMessagesTo requires persistence id to be provided");

            PersistenceId = persistenceId;
            ToSequenceNr = toSequenceNr;
            PersistentActor = persistentActor;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PersistenceId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly long ToSequenceNr;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IActorRef PersistentActor;

        /// <inheritdoc/>
        public bool Equals(DeleteMessagesTo other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(PersistenceId, other.PersistenceId) &&
                   ToSequenceNr == other.ToSequenceNr &&
                   Equals(PersistentActor, other.PersistentActor);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as DeleteMessagesTo);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"DeleteMessagesTo<pid: {PersistenceId}, seqNr: {ToSequenceNr}, persistentActor: {PersistentActor}>";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class WriteMessages : IJournalRequest, IEquatable<WriteMessages>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messages">TBD</param>
        /// <param name="persistentActor">TBD</param>
        /// <param name="actorInstanceId">TBD</param>
        public WriteMessages(IEnumerable<IPersistentEnvelope> messages, IActorRef persistentActor,
            int actorInstanceId)
        {
            Messages = messages;
            PersistentActor = persistentActor;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly IEnumerable<IPersistentEnvelope> Messages;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IActorRef PersistentActor;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int ActorInstanceId;

        /// <inheritdoc/>
        public bool Equals(WriteMessages other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(PersistentActor, other.PersistentActor)
                   && Equals(Messages, other.Messages);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessages);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"WriteMessages<actorInstanceId: {ActorInstanceId}, actor: {PersistentActor}>";
        }
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requestor before all subsequent <see cref="WriteMessageSuccess"/> replies.
    /// </summary>
    [Serializable]
    public class WriteMessagesSuccessful : IJournalResponse, IEquatable<WriteMessagesSuccessful>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly WriteMessagesSuccessful Instance = new WriteMessagesSuccessful();

        private WriteMessagesSuccessful() { }

        /// <inheritdoc/>
        public bool Equals(WriteMessagesSuccessful other)
        {
            if (ReferenceEquals(other, null)) return false;

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessagesSuccessful);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "WriteMessagesSuccessful<>";
        }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requestor before all subsequent <see cref="WriteMessageFailure"/> replies.
    /// </summary>
    [Serializable]
    public sealed class WriteMessagesFailed : IJournalResponse, IEquatable<WriteMessagesFailed>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WriteMessagesFailed"/> class.
        /// </summary>
        /// <param name="cause">The cause of the failed <see cref="WriteMessages"/> request.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public WriteMessagesFailed(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "WriteMessagesFailed cause exception cannot be null");

            Cause = cause;
        }

        /// <summary>
        /// The cause of the failed <see cref="WriteMessages"/> request.
        /// </summary>
        public readonly Exception Cause;

        /// <inheritdoc/>
        public bool Equals(WriteMessagesFailed other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessagesFailed);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (Cause != null ? Cause.GetHashCode() : 0);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"WriteMessagesFailed<cause: {Cause}>";
        }
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
    [Serializable]
    public sealed class WriteMessageSuccess : IJournalResponse, IEquatable<WriteMessageSuccess>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistent">TBD</param>
        /// <param name="actorInstanceId">TBD</param>
        public WriteMessageSuccess(IPersistentRepresentation persistent, int actorInstanceId)
        {
            Persistent = persistent;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Successfully written message.
        /// </summary>
        public readonly IPersistentRepresentation Persistent;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int ActorInstanceId;

        /// <inheritdoc/>
        public bool Equals(WriteMessageSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(Persistent, other.Persistent);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessageSuccess);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Persistent != null ? Persistent.GetHashCode() : 0) * 397) ^ ActorInstanceId;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"WriteMessageSuccess<actorInstanceId: {ActorInstanceId}, message: {Persistent}>";
        }
    }

    /// <summary>
    /// Reply message to a rejected <see cref="WriteMessages"/> request. The write of this message was rejected
    /// before it was stored, e.g. because it could not be serialized. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
    [Serializable]
    public sealed class WriteMessageRejected : IJournalResponse, IEquatable<WriteMessageRejected>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistent">TBD</param>
        /// <param name="cause">TBD</param>
        /// <param name="actorInstanceId">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public WriteMessageRejected(IPersistentRepresentation persistent, Exception cause, int actorInstanceId)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "WriteMessageRejected cause exception cannot be null");

            Persistent = persistent;
            Cause = cause;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Message failed to be written.
        /// </summary>
        public readonly IPersistentRepresentation Persistent;

        /// <summary>
        /// The cause of the failure
        /// </summary>
        public readonly Exception Cause;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly int ActorInstanceId;

        /// <inheritdoc/>
        public bool Equals(WriteMessageRejected other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(Persistent, other.Persistent)
                   && Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessageRejected);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"WriteMessageRejected<actorInstanceId: {ActorInstanceId}, message: {Persistent}, cause: {Cause}>";
        }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requestor.
    /// </summary>
    [Serializable]
    public sealed class WriteMessageFailure : IJournalResponse, IEquatable<WriteMessageFailure>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistent">TBD</param>
        /// <param name="cause">TBD</param>
        /// <param name="actorInstanceId">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public WriteMessageFailure(IPersistentRepresentation persistent, Exception cause, int actorInstanceId)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "WriteMessageFailure cause exception cannot be null");

            Persistent = persistent;
            Cause = cause;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// Message failed to be written.
        /// </summary>
        public readonly IPersistentRepresentation Persistent;

        /// <summary>
        /// The cause of the failure
        /// </summary>
        public readonly Exception Cause;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly int ActorInstanceId;

        /// <inheritdoc/>
        public bool Equals(WriteMessageFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(Persistent, other.Persistent)
                   && Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as WriteMessageFailure);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"WriteMessageFailure<actorInstanceId: {ActorInstanceId}, message: {Persistent}, cause: {Cause}>";
        }
    }

    /// <summary>
    /// Reply message to a <see cref="WriteMessages"/> with a non-persistent message.
    /// </summary>
    [Serializable]
    public sealed class LoopMessageSuccess : IJournalResponse, IEquatable<LoopMessageSuccess>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="actorInstanceId">TBD</param>
        public LoopMessageSuccess(object message, int actorInstanceId)
        {
            Message = message;
            ActorInstanceId = actorInstanceId;
        }

        /// <summary>
        /// A looped message.
        /// </summary>
        public readonly object Message;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int ActorInstanceId;

        /// <inheritdoc/>
        public bool Equals(LoopMessageSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ActorInstanceId, other.ActorInstanceId)
                   && Equals(Message, other.Message);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as LoopMessageSuccess);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Message != null ? Message.GetHashCode() : 0) * 397) ^ ActorInstanceId;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"LoopMessageSuccess<actorInstanceId: {ActorInstanceId}, message: {Message}>";
        }
    }

    /// <summary>
    /// Request to replay messages to the <see cref="PersistentActor"/>.
    /// </summary>
    [Serializable]
    public sealed class ReplayMessages : IJournalRequest, IEquatable<ReplayMessages>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayMessages"/> class.
        /// </summary>
        /// <param name="fromSequenceNr">The sequence number where the replay should start.</param>
        /// <param name="toSequenceNr">The sequence number where the replay should end.</param>
        /// <param name="max">The maximum number of messages to be replayed.</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="persistentActor">TBD</param>
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayMessages);
        }

        /// <inheritdoc/>
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
    [Serializable]
    public sealed class ReplayedMessage : IJournalResponse, IEquatable<ReplayedMessage>, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistent">TBD</param>
        public ReplayedMessage(IPersistentRepresentation persistent)
        {
            Persistent = persistent;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly IPersistentRepresentation Persistent;

        /// <inheritdoc/>
        public bool Equals(ReplayedMessage other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Persistent, other.Persistent);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayedMessage);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (Persistent != null ? Persistent.GetHashCode() : 0);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ReplayedMessage<message: {Persistent}>";
        }
    }

    /// <summary>
    /// Reply message to a successful <see cref="ReplayMessages"/> request. This reply is sent 
    /// to the requestor after all <see cref="ReplayedMessage"/> have been sent (if any).
    /// 
    /// It includes the highest stored sequence number of a given persistent actor.
    /// Note that the replay might have been limited to a lower sequence number.
    /// </summary>
    [Serializable]
    public class RecoverySuccess : IJournalResponse, IEquatable<RecoverySuccess>, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="highestSequenceNr">TBD</param>
        public RecoverySuccess(long highestSequenceNr)
        {
            HighestSequenceNr = highestSequenceNr;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly long HighestSequenceNr;

        /// <inheritdoc/>
        public bool Equals(RecoverySuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(HighestSequenceNr, other.HighestSequenceNr);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as RecoverySuccess);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HighestSequenceNr.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"RecoverySuccess<highestSequenceNr: {HighestSequenceNr}>";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class ReplayMessagesFailure : IJournalResponse, IEquatable<ReplayMessagesFailure>, IDeadLetterSuppression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayMessagesFailure"/> class.
        /// </summary>
        /// <param name="cause">The cause of the failure.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public ReplayMessagesFailure(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "ReplayMessagesFailure cause exception cannot be null");

            Cause = cause;
        }

        /// <summary>
        /// The cause of the failure
        /// </summary>
        public readonly Exception Cause;

        /// <inheritdoc/>
        public bool Equals(ReplayMessagesFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayMessagesFailure);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Cause.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ReplayMessagesFailure<cause: {Cause}>";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class ReadHighestSequenceNr : IJournalRequest, IEquatable<ReadHighestSequenceNr>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="fromSequenceNr">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="persistentActor">TBD</param>
        public ReadHighestSequenceNr(long fromSequenceNr, string persistenceId, IActorRef persistentActor)
        {
            FromSequenceNr = fromSequenceNr;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly long FromSequenceNr;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PersistenceId;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly IActorRef PersistentActor;

        /// <inheritdoc/>
        public bool Equals(ReadHighestSequenceNr other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(PersistenceId, other.PersistenceId)
                   && Equals(FromSequenceNr, other.FromSequenceNr)
                   && Equals(PersistentActor, other.PersistentActor);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ReadHighestSequenceNr);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ReadHighestSequenceNr<pid: {PersistenceId}, fromSeqNr: {FromSequenceNr}, actor: {PersistentActor}>";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class ReadHighestSequenceNrSuccess : IEquatable<ReadHighestSequenceNrSuccess>, IComparable<ReadHighestSequenceNrSuccess>
    {

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="highestSequenceNr">TBD</param>
        public ReadHighestSequenceNrSuccess(long highestSequenceNr)
        {
            HighestSequenceNr = highestSequenceNr;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly long HighestSequenceNr;

        /// <inheritdoc/>
        public bool Equals(ReadHighestSequenceNrSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return HighestSequenceNr == other.HighestSequenceNr;
        }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="other">An object to compare with this instance.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has these meanings:
        /// <dl>
        ///   <dt>Less than zero</dt>
        ///   <dd>This instance precedes <paramref name="other" /> in the sort order.</dd>
        ///   <dt>Zero</dt>
        ///   <dd>This instance occurs in the same position in the sort order as <paramref name="other" />.</dd>
        ///   <dt>Greater than zero</dt>
        ///   <dd>This instance follows <paramref name="other" /> in the sort order.</dd>
        /// </dl>
        /// </returns>
        public int CompareTo(ReadHighestSequenceNrSuccess other)
        {
            if (other == null) return 1;
            return other.HighestSequenceNr.CompareTo(HighestSequenceNr);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ReadHighestSequenceNrSuccess);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HighestSequenceNr.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ReadHighestSequenceNrSuccess<nr: {HighestSequenceNr}>";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class ReadHighestSequenceNrFailure : IEquatable<ReadHighestSequenceNrFailure>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadHighestSequenceNrFailure"/> class.
        /// </summary>
        /// <param name="cause">The cause of the failure.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public ReadHighestSequenceNrFailure(Exception cause)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "ReadHighestSequenceNrFailure cause exception cannot be null");

            Cause = cause;
        }

        /// <summary>
        /// The cause of the failure
        /// </summary>
        public readonly Exception Cause;

        /// <inheritdoc/>
        public bool Equals(ReadHighestSequenceNrFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ReadHighestSequenceNrFailure);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Cause.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ReadHighestSequenceNrFailure<cause: {Cause}>";
        }
    }
}

