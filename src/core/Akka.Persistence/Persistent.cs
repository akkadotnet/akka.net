//-----------------------------------------------------------------------
// <copyright file="Persistent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    public interface IWithPersistenceId
    {
        /// <summary>
        /// Identifier of the persistent identity for which messages should be replayed.
        /// </summary>
        string PersistenceId { get; }
    }

    public interface IPersistentIdentity : IWithPersistenceId
    {
        /// <summary>
        /// Configuration identifier of the journal plugin servicing current persistent actor or view.
        /// When empty, looks in [akka.persistence.journal.plugin] to find configuration entry path.
        /// Otherwise uses string value as an absolute path to the journal configuration entry.
        /// </summary>
        string JournalPluginId { get; }

        /// <summary>
        /// Configuration identifier of the snapshot store plugin servicing current persistent actor or view.
        /// When empty, looks in [akka.persistence.snapshot-store.plugin] to find configuration entry path.
        /// Otherwise uses string value as an absolute path to the snapshot store configuration entry.
        /// </summary>
        string SnapshotPluginId { get; }
    }

    /// <summary>
    /// Marks messages, which can then be resequenced by <see cref="AsyncWriteJournal"/>.
    /// </summary>
    public interface IPersistentEnvelope
    {
        object Payload { get; }
        IActorRef Sender { get; }
    }

    /// <summary>
    /// Message, which can be resequenced by <see cref="AsyncWriteJournal"/>, but won't be persisted.
    /// </summary>
    internal sealed class NonPersistentMessage : IPersistentEnvelope
    {
        public NonPersistentMessage(object payload, IActorRef sender)
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
        public IActorRef Sender { get; private set; }
    }

    /// <summary>
    /// Representation of a persistent message in the journal plugin API.
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
        /// Returns the persistent payload's manifest if available.
        /// </summary>
        string Manifest { get; }

        /// <summary>
        /// Creates a new persistent message with the specified <paramref name="payload"/>.
        /// </summary>
        IPersistentRepresentation WithPayload(object payload);

        /// <summary>
        /// Creates a new persistent message with the specified <paramref name="manifest"/>.
        /// </summary>
        IPersistentRepresentation WithManifest(string manifest);

        /// <summary>
        /// Creates a new deep copy of this message.
        /// </summary>
        IPersistentRepresentation Update(long sequenceNr, string persistenceId, bool isDeleted, IActorRef sender);

        #region Internal API

        IPersistentRepresentation PrepareWrite(IActorRef sender);

        IPersistentRepresentation PrepareWrite(IActorContext context);

        #endregion
    }

    [Serializable]
    public class Persistent : IPersistentRepresentation, IEquatable<IPersistentRepresentation>
    {
        public static readonly string Undefined = string.Empty;

        public Persistent(object payload, long sequenceNr = 0L, string manifest = null, string persistenceId = null, bool isDeleted = false, IActorRef sender = null)
        {
            Payload = payload;
            SequenceNr = sequenceNr;
            IsDeleted = isDeleted;
            Manifest = manifest ?? Undefined;
            PersistenceId = persistenceId ?? Undefined;
            Sender = sender;
        }

        public object Payload { get; private set; }
        public IActorRef Sender { get; private set; }
        public string PersistenceId { get; private set; }
        public bool IsDeleted { get; private set; }
        public long SequenceNr { get; private set; }
        public string Manifest { get; private set; }

        public IPersistentRepresentation WithPayload(object payload)
        {
            return new Persistent(payload, sequenceNr: SequenceNr, manifest: Manifest, persistenceId: PersistenceId, isDeleted: IsDeleted, sender: Sender);
        }

        public IPersistentRepresentation WithManifest(string manifest)
        {
            return Manifest == manifest ?
                this :
                new Persistent(payload: Payload, sequenceNr: SequenceNr, manifest: manifest, persistenceId: PersistenceId, isDeleted: IsDeleted, sender: Sender);
        }

        public IPersistentRepresentation Update(long sequenceNr, string persistenceId, bool isDeleted, IActorRef sender)
        {
            return new Persistent(payload: Payload, sequenceNr: sequenceNr, manifest: Manifest, persistenceId: persistenceId, isDeleted: isDeleted, sender: sender);
        }

        public IPersistentRepresentation PrepareWrite(IActorRef sender)
        {
            return new Persistent(payload: Payload, sequenceNr: SequenceNr, manifest: Manifest, persistenceId: PersistenceId, isDeleted: IsDeleted, sender: sender);
        }

        public IPersistentRepresentation PrepareWrite(IActorContext context)
        {
            return PrepareWrite(Sender is FutureActorRef ? context.System.DeadLetters : Sender);
        }

        public bool Equals(IPersistentRepresentation other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(PersistenceId, other.PersistenceId)
                   && Equals(SequenceNr, other.SequenceNr)
                   && Equals(IsDeleted, other.IsDeleted)
                   && Equals(Manifest, other.Manifest)
                   && Equals(Sender, other.Sender)
                   && Equals(Payload, other.Payload);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as IPersistentRepresentation);
        }

        protected bool Equals(Persistent other)
        {
            return Equals(Payload, other.Payload) && Equals(Sender, other.Sender) && string.Equals(PersistenceId, other.PersistenceId) && IsDeleted == other.IsDeleted && SequenceNr == other.SequenceNr && string.Equals(Manifest, other.Manifest);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Payload != null ? Payload.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Sender != null ? Sender.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ IsDeleted.GetHashCode();
                hashCode = (hashCode * 397) ^ SequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ (Manifest != null ? Manifest.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("Persistent<pid: {0}, seqNr: {1}, deleted: {2}, manifest: {3}, sender: {4}, payload: {5}>", PersistenceId, SequenceNr, IsDeleted, Manifest, Sender, Payload);
        }
    }
}

