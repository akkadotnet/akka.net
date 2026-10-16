//-----------------------------------------------------------------------
// <copyright file="ActorRefCompression.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Serialization.V2;

namespace Akka.Remote.Artery
{
    [AkkaSerializable(Manifest = "ARAdv")]
    internal sealed record ActorRefTableAdvertisement(
        [property: AkkaField(1)] UniqueAddress From,
        [property: AkkaField(2)] long OriginUid,
        [property: AkkaField(3)] byte Version,
        [property: AkkaField(4)] string[] Paths) : IArteryControlMessage;

    [AkkaSerializable(Manifest = "ARAck")]
    internal sealed record ActorRefTableAck(
        [property: AkkaField(1)] UniqueAddress From,
        [property: AkkaField(2)] byte Version) : IArteryControlMessage;

    internal sealed record CompressedActorRef(string Path, IActorRef? Ref);

    /// <summary>A literal path or a table-owned resolved ref; both fit in one reference slot.</summary>
    internal readonly struct InboundActorRef
    {
        private readonly object? _value;

        internal InboundActorRef(string? path) => _value = path;
        internal InboundActorRef(CompressedActorRef actorRef) => _value = actorRef;
        internal string? Path => _value is CompressedActorRef entry ? entry.Path : (string?)_value;
        internal IActorRef? Resolved => (_value as CompressedActorRef)?.Ref;
    }

    internal delegate bool ActorRefDecoder(in ArteryEnvelopeDecoded envelope, out InboundActorRef sender, out InboundActorRef recipient);

    /// <summary>Owned immutable entries. Building a table is cold; lookup never formats a path.</summary>
    internal sealed class ActorRefTable
    {
        private readonly CompressedActorRef[] _entries;
        private readonly Dictionary<IActorRef, int> _indices;

        internal ActorRefTable(byte version, CompressedActorRef[] entries)
        {
            if (version > 127 || entries.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(version));
            Version = version;
            _entries = (CompressedActorRef[])entries.Clone();
            _indices = new Dictionary<IActorRef, int>(entries.Length);
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Ref is { } actorRef)
                    _indices[actorRef] = i;
            }
        }

        internal byte Version { get; }
        internal bool TryCompress(IActorRef actorRef, out int index) => _indices.TryGetValue(actorRef, out index);
        internal string[] Paths() => _entries.Select(entry => entry.Path).ToArray();
        internal bool SamePaths(ActorRefTable other) =>
            _entries.Select(entry => entry.Path).SequenceEqual(other._entries.Select(entry => entry.Path));
        internal bool SameEntries(CompressedActorRef[] entries) => _entries.SequenceEqual(entries);

        internal bool TryGet(int index, out InboundActorRef actorRef)
        {
            if ((uint)index < (uint)_entries.Length)
            {
                actorRef = new InboundActorRef(_entries[index]);
                return true;
            }
            actorRef = default;
            return false;
        }
    }

    /// <summary>
    /// Experimental, association-incarnation-owned compression. One sampled counter chooses refs;
    /// published tables are readable before advertisement. ACKs only retire old tables and stop retries.
    /// Cold mutations share a gate; frame decoding reads an immutable snapshot without locking.
    /// </summary>
    internal sealed class ActorRefCompression
    {
        private readonly object _gate = new();
        private readonly int _maximum;
        private readonly Dictionary<IActorRef, (CompressedActorRef Entry, long Count)> _counts = new();
        private ActorRefTable[] _inbound = Array.Empty<ActorRefTable>();
        private ActorRefTable? _outbound;
        private ActorRefTable? _pending;
        private int _observations;
        private byte _version;
        private int _lastUsedVersion = -1;

        internal ActorRefCompression(long peerUid, int maximum)
        {
            if (maximum < 1 || maximum > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            PeerUid = peerUid;
            _maximum = maximum;
        }

        internal long PeerUid { get; }
        internal ActorRefTable? Outbound => Volatile.Read(ref _outbound);
        internal bool HasPendingAdvertisement => Volatile.Read(ref _pending) is not null;

        internal static bool IsCacheable(string path, IActorRef actorRef) =>
            actorRef is not MinimalActorRef && actorRef.Path.Uid != ActorCell.UndefinedUid &&
            !actorRef.Path.Elements.Contains("temp") &&
            ActorPath.TryParse(path, out var parsed) && parsed.Uid == actorRef.Path.Uid;

        internal void Observe(InboundActorRef sender, InboundActorRef recipient, Func<string, IActorRef> resolve)
        {
            if ((Interlocked.Increment(ref _observations) & 63) != 0)
                return;
            ObserveOne(sender, resolve);
            ObserveOne(recipient, resolve);
        }

        private void ObserveOne(InboundActorRef value, Func<string, IActorRef> resolve)
        {
            if (value.Path is not { } path)
                return;
            var actorRef = value.Resolved ?? resolve(path);
            if (value.Resolved is null && !IsCacheable(path, actorRef))
                return;
            lock (_gate)
            {
                if (_counts.TryGetValue(actorRef, out var previous))
                {
                    _counts[actorRef] = (previous.Entry, previous.Count == long.MaxValue ? long.MaxValue : previous.Count + 1);
                    return;
                }
                if (_counts.Count >= Math.Min(ushort.MaxValue, _maximum * 4))
                {
                    IActorRef? least = null;
                    var minimum = long.MaxValue;
                    foreach (var pair in _counts)
                    {
                        if (least is null || pair.Value.Count < minimum)
                        {
                            least = pair.Key;
                            minimum = pair.Value.Count;
                        }
                    }
                    _counts.Remove(least!);
                }
                _counts.Add(actorRef, (new CompressedActorRef(path, actorRef), 1));
            }
        }

        internal ActorRefTable? Advertise()
        {
            lock (_gate)
            {
                if (_pending is not null)
                    return _pending;
                var entries = _counts.OrderByDescending(pair => pair.Value.Count)
                    .Take(_maximum).Select(pair => pair.Value.Entry).ToArray();
                if (entries.Length == 0)
                    return null;
                if (_inbound.Length > 0 && _inbound[0].SameEntries(entries))
                {
                    _counts.Clear();
                    return null;
                }
                // Fail closed in the spike instead of reusing an ambiguous eight-bit generation.
                // Production needs a demonstrated stale-frame/version-reuse bound before wrapping.
                if (_version == 127)
                    return null;
                var table = new ActorRefTable(++_version, entries);
                Volatile.Write(ref _inbound, new[] { table }.Concat(_inbound.Take(4)).ToArray());
                Volatile.Write(ref _pending, table);
                _counts.Clear();
                return table;
            }
        }

        internal void Confirm(byte version)
        {
            lock (_gate)
            {
                if (_pending?.Version != version)
                    return;
                Volatile.Write(ref _pending, null);
                Volatile.Write(ref _inbound, _inbound.Take(4).ToArray());
            }
        }

        internal bool TryResolve(byte version, int index, out InboundActorRef actorRef)
        {
            foreach (var table in Volatile.Read(ref _inbound))
            {
                if (table.Version == version)
                    return table.TryGet(index, out actorRef);
            }
            actorRef = default;
            return false;
        }

        internal bool TryInstall(ActorRefTable table)
        {
            while (true)
            {
                var current = Outbound;
                if (current is not null)
                {
                    if (current.Version > table.Version)
                        return false;
                    if (current.Version == table.Version)
                        return current.SamePaths(table);
                }
                if (ReferenceEquals(Interlocked.CompareExchange(ref _outbound, table, current), current))
                    return true;
            }
        }

        // Publish only once per generation, never allocate an event for every warm frame.
        internal bool MarkUsed(byte version)
        {
            var previous = Volatile.Read(ref _lastUsedVersion);
            return previous < version && Interlocked.CompareExchange(ref _lastUsedVersion, version, previous) == previous;
        }
    }

    internal sealed record ActorRefCompressionUsed(long OriginUid, byte Version);

    internal sealed record RefOutboundEnvelope(object Message, IActorRef? Sender, RemoteActorRef Recipient, Address LocalAddress) : IOutboundEnvelope
    {
        public string? SenderPath => Sender?.Path.ToSerializationFormatWithAddress(LocalAddress);
        public string RecipientPath => Recipient.Path.ToSerializationFormatWithAddress(Recipient.Path.Address);
        public bool IsControl { get; } = Message is IArteryControlMessage;
    }

    internal sealed record RefInboundEnvelope(object Message, InboundActorRef Sender, InboundActorRef Recipient,
        long OriginUid, int SerializerId, string Manifest) : IInboundEnvelope
    {
        public string? SenderPath => Sender.Path;
        public string? RecipientPath => Recipient.Path;
        public bool IsControl { get; } = Message is IArteryControlMessage;
    }
}
