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
    public interface IJournalMessage : IPersistenceMessage
    {
        /// <summary>
        /// Used to correlate journal requests with responses.
        /// </summary>
        object CorrelationId { get; }
    }

    /// <summary>
    /// Internal journal command
    /// </summary>
    public interface IJournalRequest : IJournalMessage { }

    /// <summary>
    /// Internal journal acknowledgement
    /// </summary>
    public interface IJournalResponse : IJournalMessage { }

    /// <summary>
    /// Internal journal acknowledgement - response for a failed operation.
    /// </summary>
    public interface IJournalFailure : IJournalResponse
    {
        /// <summary>
        /// Failure cause.
        /// </summary>
        Exception Cause { get; }
    }

    /// <summary>
    /// Reply message to a successful <see cref="Eventsourced.DeleteMessages"/> request.
    /// </summary>
    [Serializable]
    public sealed class DeleteMessagesSuccess : IEquatable<DeleteMessagesSuccess>, IJournalResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteMessagesSuccess"/> class.
        /// </summary>
        /// <param name="toSequenceNr">Inclusive upper sequence number bound where a replay should end.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        public DeleteMessagesSuccess(long toSequenceNr, object correlationId = null)
        {
            ToSequenceNr = toSequenceNr;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Inclusive upper sequence number bound where a replay should end.
        /// </summary>
        public long ToSequenceNr { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteMessagesSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return ToSequenceNr == other.ToSequenceNr && Equals(CorrelationId, other.CorrelationId);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as DeleteMessagesSuccess);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (ToSequenceNr.GetHashCode() * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"DeleteMessagesSuccess<toSequenceNr: {ToSequenceNr}>";
    }

    /// <summary>
    /// Reply message to failed <see cref="Eventsourced.DeleteMessages"/> request.
    /// </summary>
    [Serializable]
    public sealed class DeleteMessagesFailure : IEquatable<DeleteMessagesFailure>, IJournalFailure
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteMessagesFailure"/> class.
        /// </summary>
        /// <param name="cause">Failure cause.</param>
        /// <param name="toSequenceNr">Inclusive upper sequence number bound where a replay should end.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public DeleteMessagesFailure(Exception cause, long toSequenceNr, object correlationId)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "DeleteMessagesFailure cause exception cannot be null");

            Cause = cause;
            ToSequenceNr = toSequenceNr;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Failure cause.
        /// </summary>
        public Exception Cause { get; }

        /// <summary>
        /// Inclusive upper sequence number bound where a replay should end.
        /// </summary>
        public long ToSequenceNr { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteMessagesFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId) && Equals(Cause, other.Cause) && ToSequenceNr == other.ToSequenceNr;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as DeleteMessagesFailure);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Cause != null ? Cause.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ToSequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                return hashCode;
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"DeleteMessagesFailure<cause: {Cause}, toSequenceNr: {ToSequenceNr}>";
    }

    /// <summary>
    /// Request to delete all persistent messages with sequence numbers up to `toSequenceNr` (inclusive).  
    /// </summary>
    [Serializable]
    public sealed class DeleteMessagesTo : IJournalRequest, IEquatable<DeleteMessagesTo>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteMessagesTo"/> class.
        /// </summary>
        /// <param name="persistenceId">Requesting persistent actor id.</param>
        /// <param name="toSequenceNr">Sequence number where replay should end (inclusive).</param>
        /// <param name="persistentActor">Requesting persistent actor.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="persistenceId"/> is undefined.
        /// </exception>
        public DeleteMessagesTo(string persistenceId, long toSequenceNr, IActorRef persistentActor, object correlationId = null)
        {
            if (string.IsNullOrEmpty(persistenceId))
                throw new ArgumentNullException(nameof(persistenceId), "DeleteMessagesTo requires persistence id to be provided");

            PersistenceId = persistenceId;
            ToSequenceNr = toSequenceNr;
            PersistentActor = persistentActor;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Requesting persistent actor id.
        /// </summary>
        public string PersistenceId { get; }

        /// <summary>
        /// Sequence number where replay should end (inclusive).
        /// </summary>
        public long ToSequenceNr { get; }

        /// <summary>
        /// Requesting persistent actor.
        /// </summary>
        public IActorRef PersistentActor { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(DeleteMessagesTo other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(PersistenceId, other.PersistenceId) &&
                   Equals(CorrelationId, other.CorrelationId) &&
                   ToSequenceNr == other.ToSequenceNr &&
                   Equals(PersistentActor, other.PersistentActor);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as DeleteMessagesTo);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ToSequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ (PersistentActor != null ? PersistentActor.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                return hashCode;
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"DeleteMessagesTo<pid: {PersistenceId}, seqNr: {ToSequenceNr}, persistentActor: {PersistentActor}>";
    }

    /// <summary>
    /// Request to write messages.
    /// </summary>
    [Serializable]
    public sealed class WriteMessages : IJournalRequest, INoSerializationVerificationNeeded, IEquatable<WriteMessages>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WriteMessages"/> class.
        /// </summary>
        /// <param name="messages">Messages to be written.</param>
        /// <param name="persistentActor">Write requester.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        public WriteMessages(IEnumerable<IPersistentEnvelope> messages, IActorRef persistentActor, object correlationId = null)
        {
            Messages = messages;
            PersistentActor = persistentActor;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Messages to be written.
        /// </summary>
        public IEnumerable<IPersistentEnvelope> Messages { get; }

        /// <summary>
        /// Write requester.
        /// </summary>
        public IActorRef PersistentActor { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(WriteMessages other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(PersistentActor, other.PersistentActor)
                   && Equals(Messages, other.Messages);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WriteMessages);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Messages != null ? Messages.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (PersistentActor != null ? PersistentActor.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                return hashCode;
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"WriteMessages<actor: {PersistentActor}>";
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requester before all subsequent <see cref="WriteMessageSuccess"/> replies.
    /// </summary>
    [Serializable]
    public sealed class WriteMessagesSuccessful : IJournalResponse, IEquatable<WriteMessagesSuccessful>
    {
        /// <summary>
        /// The singleton instance of <see cref="WriteMessagesSuccessful"/>.
        /// </summary>
        public static WriteMessagesSuccessful Instance { get; } = new WriteMessagesSuccessful(null);

        public WriteMessagesSuccessful(object correlationId)
        {
            CorrelationId = correlationId;
        }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        public bool Equals(WriteMessagesSuccessful other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(CorrelationId, other.CorrelationId);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is WriteMessagesSuccessful && Equals((WriteMessagesSuccessful)obj);
        }

        public override int GetHashCode()
        {
            return (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
        }
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. This reply is sent 
    /// to the requester before all subsequent <see cref="WriteMessageFailure"/> replies.
    /// </summary>
    [Serializable]
    public sealed class WriteMessagesFailed : IJournalFailure, IEquatable<WriteMessagesFailed>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WriteMessagesFailed"/> class.
        /// </summary>
        /// <param name="cause">The cause of the failed <see cref="WriteMessages"/> request.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public WriteMessagesFailed(Exception cause, object correlationId = null)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "WriteMessagesFailed cause exception cannot be null");

            Cause = cause;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// The cause of the failed <see cref="WriteMessages"/> request.
        /// </summary>
        public Exception Cause { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(WriteMessagesFailed other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId) && Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WriteMessagesFailed);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Cause != null ? Cause.GetHashCode() : 0) * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"WriteMessagesFailed<cause: {Cause}>";
    }

    /// <summary>
    /// Reply message to a successful <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requester.
    /// </summary>
    [Serializable]
    public sealed class WriteMessageSuccess : IJournalResponse, IEquatable<WriteMessageSuccess>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WriteMessageSuccess"/> class.
        /// </summary>
        /// <param name="persistent">Successfully written message.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        public WriteMessageSuccess(IPersistentRepresentation persistent, object correlationId = null)
        {
            Persistent = persistent;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Successfully written message.
        /// </summary>
        public IPersistentRepresentation Persistent { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(WriteMessageSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(Persistent, other.Persistent);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WriteMessageSuccess);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Persistent != null ? Persistent.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                return hashCode;
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"WriteMessageSuccess<message: {Persistent}>";
    }

    /// <summary>
    /// Reply message to a rejected <see cref="WriteMessages"/> request. The write of this message was rejected
    /// before it was stored, e.g. because it could not be serialized. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requester.
    /// </summary>
    [Serializable]
    public sealed class WriteMessageRejected : IJournalFailure, INoSerializationVerificationNeeded, IEquatable<WriteMessageRejected>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WriteMessageRejected"/> class.
        /// </summary>
        /// <param name="persistent">Message rejected to be written.</param>
        /// <param name="cause">Failure cause.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public WriteMessageRejected(IPersistentRepresentation persistent, Exception cause, object correlationId = null)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "WriteMessageRejected cause exception cannot be null");

            Persistent = persistent;
            Cause = cause;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Message failed to be written.
        /// </summary>
        public IPersistentRepresentation Persistent { get; }

        /// <summary>
        /// The cause of the failure
        /// </summary>
        public Exception Cause { get; }
        
        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(WriteMessageRejected other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(Persistent, other.Persistent)
                   && Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WriteMessageRejected);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Persistent != null ? Persistent.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                return hashCode;
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"WriteMessageRejected<message: {Persistent}, cause: {Cause}>";
    }

    /// <summary>
    /// Reply message to a failed <see cref="WriteMessages"/> request. For each contained 
    /// <see cref="IPersistentRepresentation"/> message in the request, a separate reply is sent to the requester.
    /// </summary>
    [Serializable]
    public sealed class WriteMessageFailure : IJournalFailure, INoSerializationVerificationNeeded, IEquatable<WriteMessageFailure>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WriteMessageFailure"/> class.
        /// </summary>
        /// <param name="persistent">Message failed to be written.</param>
        /// <param name="cause">Failure cause.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public WriteMessageFailure(IPersistentRepresentation persistent, Exception cause,  object correlationId = null)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "WriteMessageFailure cause exception cannot be null");

            Persistent = persistent;
            Cause = cause;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Message failed to be written.
        /// </summary>
        public IPersistentRepresentation Persistent { get; }

        /// <summary>
        /// The cause of the failure
        /// </summary>
        public Exception Cause { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(WriteMessageFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(Persistent, other.Persistent)
                   && Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WriteMessageFailure);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Persistent != null ? Persistent.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Cause != null ? Cause.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                return hashCode;
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"WriteMessageFailure<message: {Persistent}, cause: {Cause}>";
    }

    /// <summary>
    /// Reply message to a <see cref="WriteMessages"/> with a non-persistent message.
    /// </summary>
    [Serializable]
    public sealed class LoopMessageSuccess : IJournalResponse, INoSerializationVerificationNeeded, IEquatable<LoopMessageSuccess>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LoopMessageSuccess"/> class.
        /// </summary>
        /// <param name="message">A looped message.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        public LoopMessageSuccess(object message, object correlationId = null)
        {
            Message = message;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// A looped message.
        /// </summary>
        public object Message { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(LoopMessageSuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(Message, other.Message);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as LoopMessageSuccess);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Message != null ? Message.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"LoopMessageSuccess<message: {Message}>";
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
        /// <param name="fromSequenceNr">Sequence number where replay should start (inclusive).</param>
        /// <param name="toSequenceNr">Sequence number where replay should end (inclusive).</param>
        /// <param name="max">Maximum number of messages to be replayed.</param>
        /// <param name="persistenceId">Requesting persistent actor identifier.</param>
        /// <param name="persistentActor">Requesting persistent actor.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        public ReplayMessages(long fromSequenceNr, long toSequenceNr, long max, string persistenceId, IActorRef persistentActor, object correlationId = null)
        {
            FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            Max = max;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Inclusive lower sequence number bound where a replay should start.
        /// </summary>
        public long FromSequenceNr { get; }

        /// <summary>
        /// Inclusive upper sequence number bound where a replay should end.
        /// </summary>
        public long ToSequenceNr { get; }

        /// <summary>
        /// Maximum number of messages to be replayed.
        /// </summary>
        public long Max { get; }

        /// <summary>
        /// Requesting persistent actor identifier.
        /// </summary>
        public string PersistenceId { get; }

        /// <summary>
        /// Requesting persistent actor.
        /// </summary>
        public IActorRef PersistentActor { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(ReplayMessages other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(PersistenceId, other.PersistenceId)
                   && Equals(PersistentActor, other.PersistentActor)
                   && Equals(FromSequenceNr, other.FromSequenceNr)
                   && Equals(ToSequenceNr, other.ToSequenceNr)
                   && Equals(Max, other.Max);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as ReplayMessages);

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
                hashCode = (hashCode * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
                return hashCode;
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"ReplayMessages<fromSequenceNr: {FromSequenceNr}, toSequenceNr: {ToSequenceNr}, max: {Max}, persistenceId: {PersistenceId}>";
    }

    /// <summary>
    /// Reply message to a <see cref="ReplayMessages"/> request. A separate reply is sent to the requester for each replayed message.
    /// </summary>
    [Serializable]
    public sealed class ReplayedMessage : IJournalResponse, IDeadLetterSuppression, INoSerializationVerificationNeeded, IEquatable<ReplayedMessage>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayedMessage"/> class.
        /// </summary>
        /// <param name="persistent">Replayed message.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        public ReplayedMessage(IPersistentRepresentation persistent, object correlationId = null)
        {
            Persistent = persistent;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Replayed message.
        /// </summary>
        public IPersistentRepresentation Persistent { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(ReplayedMessage other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(Persistent, other.Persistent);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as ReplayedMessage);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Persistent != null ? Persistent.GetHashCode() : 0) * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"ReplayedMessage<message: {Persistent}>";
    }

    /// <summary>
    /// Reply message to a successful <see cref="ReplayMessages"/> request. This reply is sent 
    /// to the requester after all <see cref="ReplayedMessage"/> have been sent (if any).
    /// 
    /// It includes the highest stored sequence number of a given persistent actor.
    /// Note that the replay might have been limited to a lower sequence number.
    /// </summary>
    [Serializable]
    public sealed class RecoverySuccess : IJournalResponse, IDeadLetterSuppression, IEquatable<RecoverySuccess>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RecoverySuccess"/> class.
        /// </summary>
        /// <param name="highestSequenceNr">Highest stored sequence number.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        public RecoverySuccess(long highestSequenceNr, object correlationId = null)
        {
            HighestSequenceNr = highestSequenceNr;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// Highest stored sequence number.
        /// </summary>
        public long HighestSequenceNr { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(RecoverySuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(HighestSequenceNr, other.HighestSequenceNr);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as RecoverySuccess);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (HighestSequenceNr.GetHashCode() * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"RecoverySuccess<highestSequenceNr: {HighestSequenceNr}>";
    }

    /// <summary>
    /// Reply message to a failed <see cref="ReplayMessages"/> request. This reply is sent to the requester
    /// if a replay could not be successfully completed.
    /// </summary>
    [Serializable]
    public sealed class ReplayMessagesFailure : IJournalFailure, IDeadLetterSuppression, IEquatable<ReplayMessagesFailure>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayMessagesFailure"/> class.
        /// </summary>
        /// <param name="cause">The cause of the failure.</param>
        /// <param name="correlationId">
        /// Unique identifier used to correlate <see cref="IJournalRequest"/> with <see cref="IJournalResponse"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public ReplayMessagesFailure(Exception cause, object correlationId = null)
        {
            if (cause == null)
                throw new ArgumentNullException(nameof(cause), "ReplayMessagesFailure cause exception cannot be null");

            Cause = cause;
            CorrelationId = correlationId;
        }

        /// <summary>
        /// The cause of the failure
        /// </summary>
        public Exception Cause { get; }

        /// <inheritdoc/>
        public object CorrelationId { get; }

        /// <inheritdoc/>
        public bool Equals(ReplayMessagesFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CorrelationId, other.CorrelationId)
                   && Equals(Cause, other.Cause);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as ReplayMessagesFailure);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Cause != null ? Cause.GetHashCode() : 0) * 397) ^ (CorrelationId != null ? CorrelationId.GetHashCode() : 0);
            }
        }


        /// <inheritdoc/>
        public override string ToString() => $"ReplayMessagesFailure<cause: {Cause}>";
    }
}
