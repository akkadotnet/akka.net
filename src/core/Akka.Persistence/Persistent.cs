//-----------------------------------------------------------------------
// <copyright file="Persistent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Persistence.Journal;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    /// <summary>
    /// TBD
    /// </summary>
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
        /// <summary>
        /// TBD
        /// </summary>
        object Payload { get; }

        /// <summary>
        /// TBD
        /// </summary>
        IActorRef Sender { get; }

        /// <summary>
        /// TBD
        /// </summary>
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

        public object Payload { get; }

        public IActorRef Sender { get; }

        public int Size { get; }
    }

    /// <summary>
    /// Represents a single atomic write to an Akka.Persistence journal.
    /// </summary>
    public sealed class AtomicWrite : IPersistentEnvelope, IMessage
    {
        /// <summary>
        /// INTERNAL API. This makes the json serializer happy
        /// </summary>
        internal AtomicWrite() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AtomicWrite"/> class.
        /// </summary>
        /// <param name="event">TBD</param>
        public AtomicWrite(IPersistentRepresentation @event) : this(ImmutableArray.Create(@event))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AtomicWrite"/> class.
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="payload"/> is empty
        /// or the specified <paramref name="payload"/> contains messages from different <see cref="IPersistentRepresentation.PersistenceId"/>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="payload"/> is undefined.
        /// </exception>
        public AtomicWrite(IImmutableList<IPersistentRepresentation> payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload), "Payload of AtomicWrite must not be null.");

            if (payload.Count == 0)
                throw new ArgumentException("Payload of AtomicWrite must not be empty.", nameof(payload));

            var firstMessage = payload[0];
            if (payload.Count > 1 && !payload.Skip(1).All(m => m.PersistenceId.Equals(firstMessage.PersistenceId)))
                throw new ArgumentException($"AtomicWrite must contain messages for the same persistenceId, yet difference persistenceIds found: {payload.Select(m => m.PersistenceId).Distinct()}.", nameof(payload));

            Payload = payload;
            Sender = ActorRefs.NoSender;
            Size = payload.Count;

            PersistenceId = firstMessage.PersistenceId;
            LowestSequenceNr = firstMessage.SequenceNr; // this assumes they're gapless; they should be (it is only our code creating AWs)
            HighestSequenceNr = payload[payload.Count - 1].SequenceNr;
        }

        /// <summary>
        /// This persistent message's payload.
        /// </summary>
        public object Payload { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Sender { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int Size { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public string PersistenceId { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public long LowestSequenceNr { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public long HighestSequenceNr { get; }

        /// <inheritdoc/>
        public bool Equals(AtomicWrite other)
        {
            return Equals(Payload, other.Payload)
                   && Equals(Sender, other.Sender)
                   && Size == other.Size
                   && string.Equals(PersistenceId, other.PersistenceId)
                   && LowestSequenceNr == other.LowestSequenceNr
                   && HighestSequenceNr == other.HighestSequenceNr;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is AtomicWrite && Equals((AtomicWrite)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Payload != null ? Payload.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Sender != null ? Sender.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Size;
                hashCode = (hashCode * 397) ^ (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ LowestSequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ HighestSequenceNr.GetHashCode();
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString() 
            => $"AtomicWrite<pid: {PersistenceId}, lowSeqNr: {LowestSequenceNr}, highSeqNr: {HighestSequenceNr}, size: {Size}, sender: {Sender}>";
    }

    /// <summary>
    /// Representation of a persistent message in the journal plugin API.
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
        /// <param name="payload">This persistent message's payload.</param>
        /// <returns>TBD</returns>
        IPersistentRepresentation WithPayload(object payload);

        /// <summary>
        /// Creates a new persistent message with the specified <paramref name="manifest"/>.
        /// </summary>
        /// <param name="manifest">The persistent payload's manifest.</param>
        /// <returns>TBD</returns>
        IPersistentRepresentation WithManifest(string manifest);

        /// <summary>
        /// Not used in new records stored with Akka.net v1.1 and above, but
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
        /// <param name="sequenceNr">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="isDeleted">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="writerGuid">TBD</param>
        /// <returns>TBD</returns>
        IPersistentRepresentation Update(long sequenceNr, string persistenceId, bool isDeleted, IActorRef sender, string writerGuid);
    }

    /// <summary>
    /// INTERNAL API.
    /// </summary>
    [InternalApi]
    [Serializable]
    public class Persistent : IPersistentRepresentation, IEquatable<IPersistentRepresentation>
    {
        /// <summary>
        /// Plugin API: value of an undefined persistenceId or manifest.
        /// </summary>
        public static string Undefined { get; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Persistent"/> class.
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="manifest">TBD</param>
        /// <param name="isDeleted">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="writerGuid">TBD</param>
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

        /// <inheritdoc />
        public object Payload { get; }

        /// <inheritdoc />
        public string Manifest { get; }

        /// <inheritdoc />
        public string PersistenceId { get; }

        /// <inheritdoc />
        public long SequenceNr { get; }

        /// <inheritdoc />
        public bool IsDeleted { get; }

        /// <inheritdoc />
        public IActorRef Sender { get; }

        /// <inheritdoc />
        public string WriterGuid { get; }

        /// <inheritdoc />
        public IPersistentRepresentation WithPayload(object payload)
        {
            return new Persistent(payload, sequenceNr: SequenceNr, persistenceId: PersistenceId, manifest: Manifest, isDeleted: IsDeleted, sender: Sender, writerGuid: WriterGuid);
        }

        /// <inheritdoc />
        public IPersistentRepresentation WithManifest(string manifest)
        {
            return Manifest == manifest ?
                this :
                new Persistent(payload: Payload, sequenceNr: SequenceNr, persistenceId: PersistenceId, manifest: manifest, isDeleted: IsDeleted, sender: Sender, writerGuid: WriterGuid);
        }

        /// <inheritdoc />
        public IPersistentRepresentation Update(long sequenceNr, string persistenceId, bool isDeleted, IActorRef sender, string writerGuid)
        {
            return new Persistent(payload: Payload, sequenceNr: sequenceNr, persistenceId: persistenceId, manifest: Manifest, isDeleted: isDeleted, sender: sender, writerGuid: writerGuid);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as IPersistentRepresentation);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Payload != null ? Payload.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Manifest != null ? Manifest.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ SequenceNr.GetHashCode();
                hashCode = (hashCode * 397) ^ IsDeleted.GetHashCode();
                hashCode = (hashCode * 397) ^ (Sender != null ? Sender.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (WriterGuid != null ? WriterGuid.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString() 
            => $"Persistent<pid: {PersistenceId}, seqNr: {SequenceNr}, deleted: {IsDeleted}, manifest: {Manifest}, sender: {Sender}, payload: {Payload}, writerGuid: {WriterGuid}>";
    }
}
