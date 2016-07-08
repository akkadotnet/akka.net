//-----------------------------------------------------------------------
// <copyright file="Persistent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    [Obsolete("DeleteMessages will be removed.")]
    public interface IWithPersistenceId
    {
        /// <summary>
        /// Identifier of the persistent identity for which messages should be replayed.
        /// </summary>
        string PersistenceId { get; }
    }

    public interface IPersistentIdentity
    {
        /// <summary>
        /// Identifier of the persistent identity for which messages should be replayed.
        /// </summary>
        string PersistenceId { get; }

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
    /// Internal API
    /// 
    /// Marks messages which can then be resequenced by <see cref="AsyncWriteJournal"/>.
    /// 
    /// In essence it is either an <see cref="NonPersistentMessage"/> or <see cref="AtomicWrite"/>
    /// </summary>
    public interface IPersistentEnvelope
    {
        object Payload { get; }
        IActorRef Sender { get; }
        int Size { get; }
    }

    /// <summary>
    /// Message which can be resequenced by <see cref="AsyncWriteJournal"/>, but will not be persisted.
    /// </summary>
    internal sealed class NonPersistentMessage : IPersistentEnvelope
    {
        public NonPersistentMessage(object payload, IActorRef sender)
        {
            Payload = payload;
            Sender = sender;
            Size = 1;
        }

        public object Payload { get; private set; }
        public IActorRef Sender { get; private set; }
        public int Size { get; private set; }
    }

    public sealed class AtomicWrite : IPersistentEnvelope, IMessage
    {
        // This makes the json serializer happy
        internal AtomicWrite() {}

        public AtomicWrite(IPersistentRepresentation @event) : this(ImmutableArray.Create(@event))
        {
        }

        public AtomicWrite(IImmutableList<IPersistentRepresentation> payload)
        {
            if (payload == null)
                throw new ArgumentNullException("payload", "Payload of AtomicWrite must not be null.");

            if (payload.Count == 0)
                throw new ArgumentException("Payload of AtomicWrite must not be empty.", "payload");

            var firstMessage = payload[0];
            if (payload.Count > 1 && !payload.Skip(1).All(m => m.PersistenceId.Equals(firstMessage.PersistenceId)))
                throw new ArgumentException(string.Format("AtomicWrite must contain messages for the same persistenceId, yet difference persistenceIds found: {0}.", payload.Select(m => m.PersistenceId).Distinct()), "payload");

            Payload = payload;
            Sender = ActorRefs.NoSender;
            Size = payload.Count;

            PersistenceId = firstMessage.PersistenceId;
            LowestSequenceNr = firstMessage.SequenceNr; // this assumes they're gapless; they should be (it is only our code creating AWs)
            HighestSequenceNr = payload[payload.Count -1].SequenceNr;
        }

        public object Payload { get; private set; }
        public IActorRef Sender { get; private set; }
        public int Size { get; private set; }

        public string PersistenceId { get; private set; }
        public long LowestSequenceNr { get; private set; }
        public long HighestSequenceNr { get; private set; }

        public bool Equals(AtomicWrite other)
        {
            return Equals(Payload, other.Payload)
                   && Equals(Sender, other.Sender)
                   && Size == other.Size
                   && string.Equals(PersistenceId, other.PersistenceId)
                   && LowestSequenceNr == other.LowestSequenceNr
                   && HighestSequenceNr == other.HighestSequenceNr;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is AtomicWrite && Equals((AtomicWrite) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Payload != null ? Payload.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (Sender != null ? Sender.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ Size;
                hashCode = (hashCode*397) ^ (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ LowestSequenceNr.GetHashCode();
                hashCode = (hashCode*397) ^ HighestSequenceNr.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("AtomicWrite<pid: {0}, lowSeqNr: {1}, highSeqNr: {2}, size: {3}, sender: {4}>", PersistenceId, LowestSequenceNr, HighestSequenceNr, Size, Sender);
        }
    }

    /// <summary>
    /// Representation of a persistent message in the journal plugin API.
    /// 
    /// <see cref="AsyncWriteJournal"/>
    /// <see cref="IAsyncRecovery"/>
    /// </summary>
    public interface IPersistentRepresentation : IMessage
    {
        /// <summary>
        /// This persistent message's payload.
        /// </summary>
        object Payload { get; }

        /// <summary>
        /// Returns the persistent payload's manifest if available.
        /// </summary>
        string Manifest { get; }

        /// <summary>
        /// Persistent id that journals a persistent message.
        /// </summary>
        string PersistenceId { get; }

        /// <summary>
        /// Sequence number of this persistent message.
        /// </summary>
        long SequenceNr { get; }

        /// <summary>
        /// Unique identifier of the writing persistent actor.
        /// Used to detect anomalies with overlapping writes from multiple
        /// persistent actors, which can result in inconsistent replays.
        /// </summary>
        string WriterGuid { get; }

        /// <summary>
        /// Creates a new persistent message with the specified <paramref name="payload"/>.
        /// </summary>
        IPersistentRepresentation WithPayload(object payload);

        /// <summary>
        /// Creates a new persistent message with the specified <paramref name="manifest"/>.
        /// </summary>
        IPersistentRepresentation WithManifest(string manifest);

        /// <summary>
        /// Not used in new records stored with Akka.net v1.5 and above, but
        /// old records may have this as `true` if
        /// it was a non-permanent delete.
        /// </summary>
        bool IsDeleted { get; }

        /// <summary>
        /// Sender of this message
        /// </summary>
        IActorRef Sender { get; }

        /// <summary>
        /// Creates a new deep copy of this message.
        /// </summary>
        IPersistentRepresentation Update(long sequenceNr, string persistenceId, bool isDeleted, IActorRef sender, string writerGuid);
    }

#if SERIALIZATION
    [Serializable]
#endif
    public class Persistent : IPersistentRepresentation, IEquatable<IPersistentRepresentation>
    {
        public static readonly string Undefined = string.Empty;

        public Persistent(object payload, long sequenceNr = 0L, string persistenceId = null, string manifest = null, bool isDeleted = false, IActorRef sender = null, string writerGuid = null)
        {
            Payload = payload;
            SequenceNr = sequenceNr;
            IsDeleted = isDeleted;
            Manifest = manifest ?? Undefined;
            PersistenceId = persistenceId ?? Undefined;
            Sender = sender;
            WriterGuid = writerGuid ?? Undefined;
        }

        public object Payload { get; private set; }
        public string Manifest { get; private set; }
        public string PersistenceId { get; private set; }
        public long SequenceNr { get; private set; }
        public bool IsDeleted { get; private set; }
        public IActorRef Sender { get; private set; }
        public string WriterGuid { get; private set; }

        public IPersistentRepresentation WithPayload(object payload)
        {
            return new Persistent(payload, sequenceNr: SequenceNr, persistenceId: PersistenceId, manifest: Manifest, isDeleted: IsDeleted, sender: Sender, writerGuid: WriterGuid);
        }

        public IPersistentRepresentation WithManifest(string manifest)
        {
            return Manifest == manifest ?
                this :
                new Persistent(payload: Payload, sequenceNr: SequenceNr, persistenceId: PersistenceId, manifest: manifest, isDeleted: IsDeleted, sender: Sender, writerGuid: WriterGuid);
        }

        public IPersistentRepresentation Update(long sequenceNr, string persistenceId, bool isDeleted, IActorRef sender, string writerGuid)
        {
            return new Persistent(payload: Payload, sequenceNr: sequenceNr, persistenceId: persistenceId, manifest: Manifest, isDeleted: isDeleted, sender: sender, writerGuid: writerGuid);
        }

        public bool Equals(IPersistentRepresentation other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Payload, other.Payload)
                   && string.Equals(Manifest, other.Manifest)
                   && string.Equals(PersistenceId, other.PersistenceId)
                   && SequenceNr == other.SequenceNr
                   && IsDeleted == other.IsDeleted
                   && Equals(Sender, other.Sender)
                   && string.Equals(WriterGuid, other.WriterGuid);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as IPersistentRepresentation);
        }

        public bool Equals(Persistent other)
        {
            return Equals(Payload, other.Payload)
                   && string.Equals(Manifest, other.Manifest)
                   && string.Equals(PersistenceId, other.PersistenceId)
                   && SequenceNr == other.SequenceNr
                   && IsDeleted == other.IsDeleted
                   && Equals(Sender, other.Sender)
                   && string.Equals(WriterGuid, other.WriterGuid);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Payload != null ? Payload.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (Manifest != null ? Manifest.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ SequenceNr.GetHashCode();
                hashCode = (hashCode*397) ^ IsDeleted.GetHashCode();
                hashCode = (hashCode*397) ^ (Sender != null ? Sender.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (WriterGuid != null ? WriterGuid.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("Persistent<pid: {0}, seqNr: {1}, deleted: {2}, manifest: {3}, sender: {4}, payload: {5}, writerGuid: {6}>", PersistenceId, SequenceNr, IsDeleted, Manifest, Sender, Payload, WriterGuid);
        }
    }
}

