using System;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    public interface IWithPersistenceId
    {
        string PersistenceId { get; }
    }

    /// <summary>
    /// Marks messages, which can then be resequenced by <see cref="AsyncWriteJournal"/>.
    /// </summary>
    public interface IPersistentEnvelope
    {
        object Payload { get; }
        ActorRef Sender { get; }
    }

    /// <summary>
    /// Message, which can be resequenced by <see cref="AsyncWriteJournal"/>, but won't be persisted.
    /// </summary>
    internal sealed class NonPersistentMessage : IPersistentEnvelope
    {
        public NonPersistentMessage(object payload, ActorRef sender)
        {
            Payload = payload;
            Sender = sender;
        }

        /// <summary>
        /// Message's payload.
        /// </summary>
        public object Payload { get; private set; }

        /// <summary>
        /// Sender of this message.
        /// </summary>
        public ActorRef Sender { get; private set; }
    }

    /// <summary>
    /// Representation of a persitent message in the journal plugin API.
    /// </summary>
    public interface IPersistentRepresentation : IPersistentEnvelope, IWithPersistenceId, IMessage
    {
        /// <summary>
        /// True if this message is marked as deleted.
        /// </summary>
        bool IsDeleted { get; }

        /// <summary>
        /// Sequence number of this persistent message.
        /// </summary>
        long SequenceNr { get; }

        /// <summary>
        /// Creates a new persistent message with the specified <paramref name="payload"/>.
        /// </summary>
        IPersistentRepresentation WithPayload(object payload);

        /// <summary>
        /// Creates a new deep copy of this message.
        /// </summary>
        IPersistentRepresentation Update(long sequenceNr, string persistenceId, bool isDeleted, ActorRef sender);

        #region Internal API

        IPersistentRepresentation PrepareWrite(ActorRef sender);

        IPersistentRepresentation PrepareWrite(IActorContext context);

        #endregion
    }

    [Serializable]
    public class Persistent : IPersistentRepresentation
    {
        public Persistent(object payload, long sequenceNr = 0L, string persistenceId = null, bool isDeleted = false, ActorRef sender = null)
        {
            Payload = payload;
            SequenceNr = sequenceNr;
            IsDeleted = isDeleted;
            PersistenceId = persistenceId ?? string.Empty;
            Sender = sender;
        }

        public object Payload { get; private set; }
        public ActorRef Sender { get; private set; }
        public string PersistenceId { get; private set; }
        public bool IsDeleted { get; private set; }
        public long SequenceNr { get; private set; }

        public IPersistentRepresentation WithPayload(object payload)
        {
            return new Persistent(payload, SequenceNr, PersistenceId, IsDeleted, Sender);
        }

        public IPersistentRepresentation Update(long sequenceNr, string persistenceId, bool isDeleted, ActorRef sender)
        {
            return new Persistent(Payload, sequenceNr, persistenceId, isDeleted, sender);
        }

        public IPersistentRepresentation PrepareWrite(ActorRef sender)
        {
            return new Persistent(Payload, SequenceNr, PersistenceId, IsDeleted, sender);
        }

        public IPersistentRepresentation PrepareWrite(IActorContext context)
        {
            return PrepareWrite(Sender is FutureActorRef ? context.System.DeadLetters : Sender);
        }
    }
}