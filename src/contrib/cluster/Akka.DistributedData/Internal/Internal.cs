//-----------------------------------------------------------------------
// <copyright file="Internal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Cluster;
using Akka.Event;
using Google.ProtocolBuffers;

namespace Akka.DistributedData.Internal
{
    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class GossipTick
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal static readonly GossipTick Instance = new GossipTick();
        private GossipTick() { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "GossipTick";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal class RemovedNodePruningTick
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal static readonly RemovedNodePruningTick Instance = new RemovedNodePruningTick();
        private RemovedNodePruningTick() { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "RemovedNodePruningTick";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal class ClockTick
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal static readonly ClockTick Instance = new ClockTick();
        private ClockTick() { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "ClockTick";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Write : IReplicatorMessage, IEquatable<Write>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public string Key { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public DataEnvelope Envelope { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="envelope">TBD</param>
        public Write(string key, DataEnvelope envelope)
        {
            Key = key;
            Envelope = envelope;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(Write other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Key == other.Key && Equals(Envelope, other.Envelope);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is Write && Equals((Write)obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Key != null ? Key.GetHashCode() : 0) * 397) ^ (Envelope != null ? Envelope.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"Write(key={Key}, envelope={Envelope})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class WriteAck : IReplicatorMessage, IEquatable<WriteAck>
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal static readonly WriteAck Instance = new WriteAck();

        private WriteAck() { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(WriteAck other) => true;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is WriteAck;
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode() => 1;
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "WriteAck";
    }


    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class WriteNack : IReplicatorMessage, IEquatable<WriteNack>
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal static readonly WriteNack Instance = new WriteNack();

        private WriteNack() { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(WriteNack other) => true;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is WriteNack;
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode() => 1;
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "WriteNack";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Read : IReplicatorMessage, IEquatable<Read>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="key">TBD</param>
        public Read(string key)
        {
            Key = key;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(Read other)
        {
            return other != null && Key == other.Key;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is Read && Equals((Read)obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode() => Key?.GetHashCode() ?? 0;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"Read(key={Key})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class ReadResult : IReplicatorMessage, IEquatable<ReadResult>, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        public DataEnvelope Envelope { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        public ReadResult(DataEnvelope envelope)
        {
            Envelope = envelope;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(ReadResult other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Envelope, other.Envelope);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is ReadResult && Equals((ReadResult)obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode() => Envelope?.GetHashCode() ?? 0;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"ReadResult(envelope={Envelope})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class ReadRepair : IEquatable<ReadRepair>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public string Key { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public DataEnvelope Envelope { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="envelope">TBD</param>
        public ReadRepair(string key, DataEnvelope envelope)
        {
            Key = key;
            Envelope = envelope;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(ReadRepair other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Key, other.Key) && Equals(Envelope, other.Envelope);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is ReadRepair && Equals((ReadRepair)obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Key?.GetHashCode() ?? 0) * 397) ^ (Envelope?.GetHashCode() ?? 0);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"ReadRepair(key={Key}, envelope={Envelope})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class ReadRepairAck
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly ReadRepairAck Instance = new ReadRepairAck();

        private ReadRepairAck() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"ReadRepairAck";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class DataEnvelope : IEquatable<DataEnvelope>, IReplicatorMessage
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal static DataEnvelope DeletedEnvelope => new DataEnvelope(DeletedData.Instance);

        /// <summary>
        /// TBD
        /// </summary>
        internal IReplicatedData Data { get; }
        /// <summary>
        /// TBD
        /// </summary>
        internal IImmutableDictionary<UniqueAddress, PruningState> Pruning { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="data">TBD</param>
        internal DataEnvelope(IReplicatedData data) : this(data, ImmutableDictionary<UniqueAddress, PruningState>.Empty)
        { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="data">TBD</param>
        /// <param name="pruning">TBD</param>
        internal DataEnvelope(IReplicatedData data, IImmutableDictionary<UniqueAddress, PruningState> pruning)
        {
            Data = data;
            Pruning = pruning;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <returns>TBD</returns>
        internal bool NeedPruningFrom(UniqueAddress removedNode)
        {
            var r = Data as IRemovedNodePruning;
            return r != null && r.NeedPruningFrom(removedNode);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removed">TBD</param>
        /// <param name="owner">TBD</param>
        /// <returns>TBD</returns>
        internal DataEnvelope InitRemovedNodePruning(UniqueAddress removed, UniqueAddress owner) =>
            new DataEnvelope(Data, Pruning.Add(removed, new PruningState(owner, new PruningInitialized(ImmutableHashSet<Address>.Empty))));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="otherData">TBD</param>
        /// <returns>TBD</returns>
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
                return ((IRemovedNodePruning)c).PruningCleanup(kvp.Key);
            return c;
        });

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(DataEnvelope other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            if (!Equals(Data, other.Data)) return false;
            if (Pruning.Count != other.Pruning.Count) return false;

            foreach (var entry in Pruning)
            {
                if (!Equals(entry.Value, other.Pruning[entry.Key])) return false;
            }

            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is DataEnvelope && Equals((DataEnvelope)obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Data != null ? Data.GetHashCode() : 0) * 397) ^ (Pruning != null ? Pruning.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            var sb = new StringBuilder("{");
            if (Pruning != null)
                foreach (var entry in Pruning)
                {
                    sb.Append(entry.Key).Append("->").Append(entry.Value).Append(",");
                }
            sb.Append('}');

            return $"DataEnvelope(data={Data}, prunning={sb})";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <returns>TBD</returns>
    [Serializable]
    internal sealed class DeletedData : IReplicatedData<DeletedData>, IEquatable<DeletedData>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static readonly DeletedData Instance = new DeletedData();

        private DeletedData() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public DeletedData Merge(DeletedData other) => this;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IReplicatedData Merge(IReplicatedData other) => Merge((DeletedData)other);
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public bool Equals(DeletedData other) => true;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is DeletedData;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode() => 1;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "DeletedData";
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Status : IReplicatorMessage, IEquatable<Status>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableDictionary<string, ByteString> Digests { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public int Chunk { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public int TotalChunks { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="digests">TBD</param>
        /// <param name="chunk">TBD</param>
        /// <param name="totalChunks">TBD</param>
        public Status(IImmutableDictionary<string, ByteString> digests, int chunk, int totalChunks)
        {
            Digests = digests;
            Chunk = chunk;
            TotalChunks = totalChunks;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(Status other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.Chunk.Equals(Chunk) && other.TotalChunks.Equals(TotalChunks) && Digests.SequenceEqual(other.Digests);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is Status && Equals((Status)obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Digests != null ? Digests.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Chunk;
                hashCode = (hashCode * 397) ^ TotalChunks;
                return hashCode;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            var sb = new StringBuilder("{");
            if (Digests != null)
                foreach (var entry in Digests)
                {
                    sb.Append(entry.Key).Append("->").Append(entry.Value).Append(",");
                }
            sb.Append('}');

            return $"Status(chunk={Chunk}, totalChunks={TotalChunks}, digest={sb})";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Gossip : IReplicatorMessage, IEquatable<Gossip>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableDictionary<string, DataEnvelope> UpdatedData { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public bool SendBack { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="updatedData">TBD</param>
        /// <param name="sendBack">TBD</param>
        public Gossip(IImmutableDictionary<string, DataEnvelope> updatedData, bool sendBack)
        {
            UpdatedData = updatedData;
            SendBack = sendBack;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(Gossip other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.SendBack.Equals(SendBack) && UpdatedData.SequenceEqual(other.UpdatedData);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is Gossip && Equals((Gossip)obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((UpdatedData != null ? UpdatedData.GetHashCode() : 0) * 397) ^ SendBack.GetHashCode();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            var sb = new StringBuilder("{");
            if (UpdatedData != null)
                foreach (var entry in UpdatedData)
                {
                    sb.Append(entry.Key).Append("->").Append(entry.Value).Append(",");
                }
            sb.Append('}');

            return $"Gossip(sendBack={SendBack}, updatedData={sb})";
        }
    }
}
