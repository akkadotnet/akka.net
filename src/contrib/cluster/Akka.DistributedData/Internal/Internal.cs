//-----------------------------------------------------------------------
// <copyright file="Internal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Akka.IO;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster;
using Akka.Event;

namespace Akka.DistributedData.Internal
{
    [Serializable]
    internal sealed class GossipTick
    {
        internal static readonly GossipTick Instance = new GossipTick();

        private GossipTick() { }
    }

    [Serializable]
    internal class RemovedNodePruningTick
    {
        internal static readonly RemovedNodePruningTick Instance = new RemovedNodePruningTick();

        private RemovedNodePruningTick() { }
    }

    [Serializable]
    internal class ClockTick
    {
        internal static readonly ClockTick Instance = new ClockTick();

        private ClockTick() { }
    }

    [Serializable]
    internal sealed class Write : IReplicatorMessage, IEquatable<Write>
    {
        public string Key { get; }
        public DataEnvelope Envelope { get; }

        public Write(string key, DataEnvelope envelope)
        {
            Key = key;
            Envelope = envelope;
        }

        public bool Equals(Write other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Key == other.Key && Equals(Envelope, other.Envelope);
        }

        public override bool Equals(object obj) => obj is Write && Equals((Write)obj);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Key != null ? Key.GetHashCode() : 0)*397) ^ (Envelope != null ? Envelope.GetHashCode() : 0);
            }
        }
    }

    [Serializable]
    internal sealed class WriteAck : IReplicatorMessage, IEquatable<WriteAck>
    {
        internal static readonly WriteAck Instance = new WriteAck();

        private WriteAck() { }
        public bool Equals(WriteAck other)
        {
            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is WriteAck;
        }

        public override int GetHashCode()
        {
            return 1;
        }
    }

    [Serializable]
    internal sealed class Read : IReplicatorMessage, IEquatable<Read>
    {
        public string Key { get; }

        public Read(string key)
        {
            Key = key;
        }

        public bool Equals(Read other)
        {
            return other != null && Key == other.Key;
        }

        public override bool Equals(object obj) => obj is Read && Equals((Read) obj);

        public override int GetHashCode() => Key?.GetHashCode() ?? 0;
    }

    [Serializable]
    internal sealed class ReadResult : IReplicatorMessage, IEquatable<ReadResult>, IDeadLetterSuppression
    {
        public DataEnvelope Envelope { get; }

        public ReadResult(DataEnvelope envelope)
        {
            Envelope = envelope;
        }

        public bool Equals(ReadResult other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Envelope, other.Envelope);
        }

        public override bool Equals(object obj) => obj is ReadResult && Equals((ReadResult) obj);

        public override int GetHashCode() => Envelope?.GetHashCode() ?? 0;
    }

    [Serializable]
    internal sealed class ReadRepair : IEquatable<ReadRepair>
    {
        public string Key { get; }
        public DataEnvelope Envelope { get; }

        public ReadRepair(string key, DataEnvelope envelope)
        {
            Key = key;
            Envelope = envelope;
        }

        public bool Equals(ReadRepair other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Key, other.Key) && Equals(Envelope, other.Envelope);
        }

        public override bool Equals(object obj) => obj is ReadRepair && Equals((ReadRepair) obj);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Key?.GetHashCode() ?? 0)*397) ^ (Envelope?.GetHashCode() ?? 0);
            }
        }
    }

    [Serializable]
    internal sealed class ReadRepairAck
    {
        public static readonly ReadRepairAck Instance = new ReadRepairAck();

        private ReadRepairAck() { }
    }

    [Serializable]
    internal sealed class DataEnvelope : IEquatable<DataEnvelope>, IReplicatorMessage
    {
        internal static DataEnvelope DeletedEnvelope => new DataEnvelope(DeletedData.Instance);

        internal IReplicatedData Data { get; }
        internal IImmutableDictionary<UniqueAddress, PruningState> Pruning { get; }

        internal DataEnvelope(IReplicatedData data) : this(data, ImmutableDictionary<UniqueAddress, PruningState>.Empty)
        { }

        internal DataEnvelope(IReplicatedData data, IImmutableDictionary<UniqueAddress, PruningState> pruning)
        {
            Data = data;
            Pruning = pruning;
        }

        internal bool NeedPruningFrom(UniqueAddress removedNode)
        {
            var r = Data as IRemovedNodePruning;
            return r != null && r.NeedPruningFrom(removedNode);
        }

        internal DataEnvelope InitRemovedNodePruning(UniqueAddress removed, UniqueAddress owner) => 
            new DataEnvelope(Data, Pruning.Add(removed, new PruningState(owner, new PruningInitialized(ImmutableHashSet<Address>.Empty))));

        internal DataEnvelope Prune(UniqueAddress from)
        {
            var r = Data as IRemovedNodePruning;
            if (r != null)
            {
                if (!Pruning.ContainsKey(from))
                    throw new ArgumentException($"Can't prune {@from} since it's not there");

                var to = Pruning[from].Owner;
                var prunedData = r.Prune(from, to);
                return new DataEnvelope(prunedData, Pruning.SetItem(from, new PruningState(to, PruningPerformed.Instance)));
            }
            return this;
        }

        internal DataEnvelope Merge(DataEnvelope other)
        {
            if (other.Data is DeletedData) return DeletedEnvelope;
            else
            {
                var mergedRemovedNodePruning = other.Pruning;
                foreach (var kvp in Pruning)
                {
                    PruningState value;
                    var contains = mergedRemovedNodePruning.TryGetValue(kvp.Key, out value);
                    mergedRemovedNodePruning = mergedRemovedNodePruning.SetItem(kvp.Key, !contains ? kvp.Value : value.Merge(kvp.Value));
                }
                var envelope = new DataEnvelope(Cleaned(Data, mergedRemovedNodePruning), mergedRemovedNodePruning);
                return envelope.Merge(other.Data);
            }
        }

        internal DataEnvelope Merge(IReplicatedData otherData)
        {
            if (otherData is DeletedData)
                return DeletedEnvelope;

            var data = Data.Merge(Cleaned(otherData, Pruning));
            return new DataEnvelope(data, Pruning);
        }

        private IReplicatedData Cleaned(IReplicatedData c, IImmutableDictionary<UniqueAddress, PruningState> p) => p.Aggregate(c, (state, kvp) =>
        {
            if (c is IRemovedNodePruning 
                && kvp.Value.Phase is PruningPerformed
                && ((IRemovedNodePruning)c).NeedPruningFrom(kvp.Key))
                return ((IRemovedNodePruning) c).PruningCleanup(kvp.Key);
            return c;
        });

        internal DataEnvelope AddSeen(Address node)
        {
            var changed = false;
            var newRemovedNodePruning = Pruning.Select(kvp =>
            {
                var newPruningState = kvp.Value.AddSeen(node);
                changed = !ReferenceEquals(newPruningState, kvp.Value) || changed;
                return new KeyValuePair<UniqueAddress, PruningState>(kvp.Key, newPruningState);
            }).ToImmutableDictionary();

            return changed ? new DataEnvelope(Data, newRemovedNodePruning) : this;
        }

        public bool Equals(DataEnvelope other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            var pruningCountsEqual = Pruning.Count == other.Pruning.Count;
            var elementsEqual = !Pruning.Except(other.Pruning).Any();
            return Data.Equals(other.Data) && pruningCountsEqual && elementsEqual;
        }

        public override bool Equals(object obj) => obj is DataEnvelope && Equals((DataEnvelope) obj);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Data != null ? Data.GetHashCode() : 0)*397) ^ (Pruning != null ? Pruning.GetHashCode() : 0);
            }
        }
    }

    [Serializable]
    public sealed class DeletedData : IReplicatedData<DeletedData>, IEquatable<DeletedData>
    {
        public static readonly DeletedData Instance = new DeletedData();

        private DeletedData() { }

        public DeletedData Merge(DeletedData other) => this;

        public IReplicatedData Merge(IReplicatedData other) => Merge((DeletedData) other);
        public bool Equals(DeletedData other)
        {
            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is DeletedData;
        }

        public override int GetHashCode()
        {
            return 1;
        }
    }
    
    [Serializable]
    internal sealed class Status : IReplicatorMessage, IEquatable<Status>
    {
        public IImmutableDictionary<string, ByteString> Digests { get; }
        public int Chunk { get; }
        public int TotChunks { get; }

        public Status(IImmutableDictionary<string, ByteString> digests, int chunk, int totChunks)
        {
            Digests = digests;
            Chunk = chunk;
            TotChunks = totChunks;
        }

        public bool Equals(Status other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return other.Chunk.Equals(Chunk) && other.TotChunks.Equals(TotChunks) && Digests.SequenceEqual(other.Digests);
        }

        public override bool Equals(object obj) => obj is Status && Equals((Status) obj);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Digests != null ? Digests.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ Chunk;
                hashCode = (hashCode*397) ^ TotChunks;
                return hashCode;
            }
        }
    }

    [Serializable]
    internal sealed class Gossip : IReplicatorMessage, IEquatable<Gossip>
    {
        public IImmutableDictionary<string, DataEnvelope> UpdatedData { get; }
        public bool SendBack { get; }

        public Gossip(IImmutableDictionary<string, DataEnvelope> updatedData, bool sendBack)
        {
            UpdatedData = updatedData;
            SendBack = sendBack;
        }

        public bool Equals(Gossip other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.SendBack.Equals(SendBack) && UpdatedData.SequenceEqual(other.UpdatedData);
        }

        public override bool Equals(object obj) => obj is Gossip && Equals((Gossip) obj);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((UpdatedData != null ? UpdatedData.GetHashCode() : 0) * 397) ^ SendBack.GetHashCode();
            }
        }

    }
}
