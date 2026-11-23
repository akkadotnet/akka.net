//-----------------------------------------------------------------------
// <copyright file="ArteryRefCaches.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Text;
using Akka.Actor;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Local refs by the numeric id carried in the envelope header, so an inbound message whose
    /// recipient id matches is delivered without decoding, parsing or resolving its recipient path.
    /// One per transport, direct-mapped with a fixed capacity: a slot holds the last ref stored under
    /// any id with the same low bits, and a lookup hits only when the slot's ref has exactly the
    /// requested id. A slot is a single reference write, so readers and writers need no locking; a
    /// lost race only costs a miss.
    ///
    /// <para>
    /// Filled when a local ref is written as the sender of an outbound envelope, so the reply to it
    /// hits on arrival, and when an inbound recipient was resolved by path. Actor UIDs are unique
    /// within a system, so a hit is always the actor the id names. A ref whose actor has stopped
    /// stays valid to Tell: its mailbox is closed and the message goes to dead letters, as it would
    /// through path resolution.
    /// </para>
    /// </summary>
    internal sealed class ArteryLocalRefCache
    {
        /// <summary>Number of slots; a power of two.</summary>
        public const int Capacity = 4096;

        private const long Mask = Capacity - 1;

        private readonly IInternalActorRef?[] _slots = new IInternalActorRef?[Capacity];

        /// <summary>
        /// The id a local ref carries on the wire: its actor UID; for an Ask future, whose path has no
        /// uid, its negative /temp child id (never cached here); otherwise 0.
        /// </summary>
        public static long IdOf(IInternalActorRef local)
        {
            var uid = local.Path.Uid;
            return uid != 0 ? uid : local is MinimalActorRef minimal ? minimal.WireId : 0;
        }

        /// <summary>The cached ref with exactly this id, or <see langword="null"/>.</summary>
        public IInternalActorRef? Get(long id)
        {
            if (id <= 0)
                return null;

            var slot = _slots[id & Mask];
            return slot is not null && IdOf(slot) == id ? slot : null;
        }

        /// <summary>
        /// Stores <paramref name="local"/> under <paramref name="id"/>, which must be its
        /// <see cref="IdOf"/>. Only refs backed by an actor cell are stored: a stopped actor's ref
        /// dead-letters by itself, whereas a function ref (a stream's stage actor) keeps accepting
        /// messages after it is removed and must be found through its parent each time.
        /// </summary>
        public void Put(long id, IInternalActorRef local)
        {
            if (id <= 0 || local is not ActorRefWithCell)
                return;

            ref var slot = ref _slots[id & Mask];
            if (!ReferenceEquals(slot, local))
                slot = local;
        }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Receive-side cache of remote senders: (origin system UID, sender id) to the
    /// <see cref="RemoteActorRef"/> built for it last time, so a repeat sender needs no new ref. One
    /// per inbound lane, and one per connection for the inline (inbound-lanes = 1) path, so each
    /// instance is used by one thread at a time and needs no locking. Direct-mapped like
    /// <see cref="ArteryLocalRefCache"/>.
    /// </summary>
    internal sealed class ArteryRemoteSenderCache
    {
        /// <summary>Number of entries; a power of two.</summary>
        public const int Capacity = 1024;

        private const long Mask = Capacity - 1;

        private struct Entry
        {
            public long OriginUid;
            public long Id;
            public IActorRef? Ref;
        }

        private readonly Entry[] _entries = new Entry[Capacity];

        /// <summary>The ref cached for this sender, or <see langword="null"/>.</summary>
        public IActorRef? Get(long originUid, long id)
        {
            if (id == 0)
                return null;

            ref var entry = ref _entries[id & Mask];
            return entry.Id == id && entry.OriginUid == originUid ? entry.Ref : null;
        }

        /// <summary>Caches <paramref name="sender"/> for (<paramref name="originUid"/>, <paramref name="id"/>).</summary>
        public void Put(long originUid, long id, IActorRef sender)
        {
            ref var entry = ref _entries[id & Mask];
            entry.OriginUid = originUid;
            entry.Id = id;
            entry.Ref = sender;
        }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Manifest strings decoded on one connection or inbound lane, so a repeated manifest is not
    /// decoded into a new string for every message. Used by one thread at a time. Only ASCII literals
    /// hit; anything else is decoded as before.
    /// </summary>
    internal sealed class ArteryManifestCache
    {
        private readonly string?[] _slots = new string?[8];

        /// <summary>The same result as <see cref="ArteryEnvelopeDecoded.TryGetManifest"/>.</summary>
        public bool TryGetManifest(in ArteryEnvelopeDecoded decoded, out string manifest)
        {
            var bytes = decoded.ManifestKind == ArteryTagKind.Literal ? decoded.GetManifestLiteralBytes() : default;
            if (bytes.IsEmpty || !bytes.IsSingleSegment)
                return decoded.TryGetManifest(out manifest);

            var span = bytes.First.Span;
            ref var slot = ref _slots[(span.Length + span[span.Length - 1]) & 7];
            if (slot is null || !AsciiEquals(span, slot))
                slot = Encoding.UTF8.GetString(span);
            manifest = slot;
            return true;
        }

        private static bool AsciiEquals(ReadOnlySpan<byte> bytes, string value)
        {
            if (bytes.Length != value.Length)
                return false;
            for (var i = 0; i < bytes.Length; i++)
                if (bytes[i] >= 0x80 || bytes[i] != value[i])
                    return false;
            return true;
        }
    }
}
