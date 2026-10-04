//-----------------------------------------------------------------------
// <copyright file="Internal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Google.Protobuf;

namespace Akka.DistributedData.Internal
{
    /// <summary>
    /// Scheduled signal that asks the replicator to exchange gossip.
    /// </summary>
    [Serializable]
    internal sealed class GossipTick
    {
        /// <summary>
        /// Singleton signal instance.
        /// </summary>
        internal static readonly GossipTick Instance = new();
        private GossipTick() { }
        /// <summary>
        /// Returns the signal name.
        /// </summary>
        /// <returns>The string <c>GossipTick</c>.</returns>
        public override string ToString() => "GossipTick";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [Serializable]
    internal sealed class DeltaPropagationTick
    {
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static DeltaPropagationTick Instance { get; } = new();

        private DeltaPropagationTick() { }
    }

    /// <summary>
    /// Scheduled signal that asks the replicator to prune state for removed nodes.
    /// </summary>
    [Serializable]
    internal class RemovedNodePruningTick
    {
        /// <summary>
        /// Singleton signal instance.
        /// </summary>
        internal static readonly RemovedNodePruningTick Instance = new();
        private RemovedNodePruningTick() { }
        /// <summary>
        /// Returns the signal name.
        /// </summary>
        /// <returns>The string <c>RemovedNodePruningTick</c>.</returns>
        public override string ToString() => "RemovedNodePruningTick";
    }

    /// <summary>
    /// Scheduled signal that asks the replicator to update its local version clock.
    /// </summary>
    [Serializable]
    internal class ClockTick
    {
        /// <summary>
        /// Singleton signal instance.
        /// </summary>
        internal static readonly ClockTick Instance = new();
        private ClockTick() { }
        /// <summary>
        /// Returns the signal name.
        /// </summary>
        /// <returns>The string <c>ClockTick</c>.</returns>
        public override string ToString() => "ClockTick";
    }

    internal interface ISendingSystemUid
    {
        UniqueAddress FromNode { get; }
    }

    internal interface IDestinationSystemUid
    {
        long? ToSystemUid { get; }
    }

    /// <summary>
    /// Internal message carrying a replicated-data write to another replicator.
    /// </summary>
    [Serializable]
    internal sealed class Write : IReplicatorMessage, IEquatable<Write>, ISendingSystemUid
    {
        /// <summary>
        /// Identifier of the replicated-data key being written.
        /// </summary>
        public string Key { get; }
        /// <summary>
        /// Replicated data and pruning metadata sent with the write.
        /// </summary>
        public DataEnvelope Envelope { get; }
        /// <summary>
        /// Address of the node that originated the write, when known.
        /// </summary>
        public UniqueAddress FromNode { get; }
        /// <summary>
        /// Creates a write message.
        /// </summary>
        /// <param name="key">Identifier of the replicated-data key.</param>
        /// <param name="envelope">Data and pruning state to write.</param>
        /// <param name="fromNode">Originating node address, or <see langword="null"/> when not supplied.</param>
        public Write(string key, DataEnvelope envelope, UniqueAddress fromNode = null)
        {
            Key = key;
            Envelope = envelope;
            FromNode = fromNode;
        }

        /// <inheritdoc/>
        public bool Equals(Write other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Key == other.Key && Equals(Envelope, other.Envelope) && Equals(FromNode, other.FromNode);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Write write && Equals(write);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Key != null ? Key.GetHashCode() : 0) * 397) ^ (Envelope != null ? Envelope.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"Write(key={Key}, envelope={Envelope})";
    }

    /// <summary>
    /// Internal acknowledgment that a remote replicator accepted a write.
    /// </summary>
    [Serializable]
    internal sealed class WriteAck : IReplicatorMessage, IEquatable<WriteAck>
    {
        /// <summary>
        /// Singleton acknowledgment instance.
        /// </summary>
        internal static readonly WriteAck Instance = new();

        private WriteAck() { }
        /// <inheritdoc/>
        public bool Equals(WriteAck other) => true;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is WriteAck;
        /// <inheritdoc/>
        public override int GetHashCode() => 1;
        /// <inheritdoc/>
        public override string ToString() => "WriteAck";
    }


    /// <summary>
    /// Internal negative acknowledgment that a remote replicator did not accept a write.
    /// </summary>
    [Serializable]
    internal sealed class WriteNack : IReplicatorMessage, IEquatable<WriteNack>
    {
        /// <summary>
        /// Singleton negative-acknowledgment instance.
        /// </summary>
        internal static readonly WriteNack Instance = new();

        private WriteNack() { }
        /// <summary>
        /// Compares two write negative acknowledgments.
        /// </summary>
        /// <param name="other">Acknowledgment to compare; all instances of this message type are equal.</param>
        /// <returns><see langword="true"/>.</returns>
        public bool Equals(WriteNack other) => true;
        /// <summary>
        /// Compares this acknowledgment with another object.
        /// </summary>
        /// <param name="obj">Object to compare.</param>
        /// <returns><see langword="true"/> when <paramref name="obj"/> is a <see cref="WriteNack"/>; otherwise, <see langword="false"/>.</returns>
        public override bool Equals(object obj) => obj is WriteNack;
        /// <summary>
        /// Returns the hash code for this acknowledgment.
        /// </summary>
        /// <returns>A constant hash code shared by all instances.</returns>
        public override int GetHashCode() => 1;
        /// <summary>
        /// Returns the message name.
        /// </summary>
        /// <returns>The string <c>WriteNack</c>.</returns>
        public override string ToString() => "WriteNack";
    }

    /// <summary>
    /// Internal request for a replicator to return the current value for a key.
    /// </summary>
    [Serializable]
    internal sealed class Read : IReplicatorMessage, IEquatable<Read>, ISendingSystemUid
    {
        /// <summary>
        /// Identifier of the replicated-data key being read.
        /// </summary>
        public string Key { get; }
        /// <summary>
        /// Address of the node that originated the read, when known.
        /// </summary>
        public UniqueAddress FromNode { get; }
        /// <summary>
        /// Creates a read request.
        /// </summary>
        /// <param name="key">Identifier of the replicated-data key.</param>
        /// <param name="fromNode">Originating node address, or <see langword="null"/> when not supplied.</param>
        public Read(string key, UniqueAddress fromNode = null)
        {
            Key = key;
            FromNode = fromNode;
        }

        /// <inheritdoc/>
        public bool Equals(Read other)
        {
            return other != null && Key == other.Key;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Read read && Equals(read);

        /// <inheritdoc/>
        public override int GetHashCode() => Key?.GetHashCode() ?? 0;

        /// <inheritdoc/>
        public override string ToString() => $"Read(key={Key})";
    }

    /// <summary>
    /// Result of a replicator read, carrying the current data envelope.
    /// </summary>
    [Serializable]
    internal sealed class ReadResult : IReplicatorMessage, IEquatable<ReadResult>, IDeadLetterSuppression
    {
        /// <summary>
        /// Current replicated data and pruning metadata returned by the read.
        /// </summary>
        public DataEnvelope Envelope { get; }
        /// <summary>
        /// Creates a read result.
        /// </summary>
        /// <param name="envelope">Current data and pruning state for the requested key.</param>
        public ReadResult(DataEnvelope envelope)
        {
            Envelope = envelope;
        }

        /// <inheritdoc/>
        public bool Equals(ReadResult other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Envelope, other.Envelope);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ReadResult result && Equals(result);

        /// <inheritdoc/>
        public override int GetHashCode() => Envelope?.GetHashCode() ?? 0;

        /// <inheritdoc/>
        public override string ToString() => $"ReadResult(envelope={Envelope})";
    }

    /// <summary>
    /// Internal message asking a replicator to merge a value observed during a read.
    /// </summary>
    [Serializable]
    internal sealed class ReadRepair : IEquatable<ReadRepair>
    {
        /// <summary>
        /// Identifier of the replicated-data key to repair.
        /// </summary>
        public string Key { get; }
        /// <summary>
        /// Observed replicated data and pruning state to merge.
        /// </summary>
        public DataEnvelope Envelope { get; }
        /// <summary>
        /// Creates a read-repair message.
        /// </summary>
        /// <param name="key">Identifier of the replicated-data key to repair.</param>
        /// <param name="envelope">Observed data and pruning state to merge.</param>
        public ReadRepair(string key, DataEnvelope envelope)
        {
            Key = key;
            Envelope = envelope;
        }

        /// <inheritdoc/>
        public bool Equals(ReadRepair other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Key, other.Key) && Equals(Envelope, other.Envelope);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ReadRepair repair && Equals(repair);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Key?.GetHashCode() ?? 0) * 397) ^ (Envelope?.GetHashCode() ?? 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"ReadRepair(key={Key}, envelope={Envelope})";
    }

    /// <summary>
    /// Internal acknowledgment sent after processing a read repair.
    /// </summary>
    [Serializable]
    internal sealed class ReadRepairAck
    {
        /// <summary>
        /// Singleton acknowledgment instance.
        /// </summary>
        public static readonly ReadRepairAck Instance = new();

        private ReadRepairAck() { }

        /// <inheritdoc/>
        public override string ToString() => "ReadRepairAck";
    }

    /// <summary>
    /// Replicated value together with pruning metadata and delta-version tracking.
    /// </summary>
    [Serializable]
    public sealed class DataEnvelope : IEquatable<DataEnvelope>, IReplicatorMessage
    {
        /// <summary>
        /// Gets a tombstone envelope representing deleted data.
        /// </summary>
        public static DataEnvelope DeletedEnvelope => new(DeletedData.Instance);
        /// <summary>
        /// Replicated data carried by this envelope.
        /// </summary>
        public IReplicatedData Data { get; }
        /// <summary>
        /// Pruning state tracked for removed node addresses.
        /// </summary>
        public ImmutableDictionary<UniqueAddress, IPruningState> Pruning { get; }

        public VersionVector DeltaVersions { get; }
        /// <summary>
        /// Creates an envelope around replicated data.
        /// </summary>
        /// <param name="data">The replicated data value.</param>
        /// <param name="pruning">Pruning state by removed node, or <see langword="null"/> for an empty map.</param>
        /// <param name="deltaVersions">Per-node delta version tracking, or <see langword="null"/> for an empty version vector.</param>
        internal DataEnvelope(IReplicatedData data, ImmutableDictionary<UniqueAddress, IPruningState> pruning = null, VersionVector deltaVersions = null)
        {
            Data = data;
            Pruning = pruning ?? ImmutableDictionary<UniqueAddress, IPruningState>.Empty;
            DeltaVersions = deltaVersions ?? VersionVector.Empty;
        }

        internal DataEnvelope WithData(IReplicatedData data) => new(data, Pruning, DeltaVersions);

        internal DataEnvelope WithPruning(ImmutableDictionary<UniqueAddress, IPruningState> pruning) => new(Data, pruning, DeltaVersions);

        internal DataEnvelope WithDeltaVersions(VersionVector deltaVersions) => new(Data, Pruning, deltaVersions);

        internal DataEnvelope WithoutDeltaVersions() =>
            DeltaVersions.IsEmpty
                ? this
                : new DataEnvelope(Data, Pruning);
        /// <summary>
        /// Removes the delta-version entry for a removed node.
        /// </summary>
        /// <param name="from">Address of the removed node.</param>
        /// <returns>A version vector without that node entry.</returns>
        private VersionVector CleanedDeltaVersions(UniqueAddress from) => DeltaVersions.PruningCleanup(from);
        /// <summary>
        /// Checks whether the contained data requires pruning for a removed node.
        /// </summary>
        /// <param name="removedNode">Address of the removed node.</param>
        /// <returns><see langword="true"/> if the data implements removed-node pruning and reports work for this node.</returns>
        internal bool NeedPruningFrom(UniqueAddress removedNode)
        {
            return Data is IRemovedNodePruning r && r.NeedPruningFrom(removedNode);
        }
        /// <summary>
        /// Records that pruning has started for a removed node.
        /// </summary>
        /// <param name="removed">Address of the removed node.</param>
        /// <param name="owner">Address whose state will absorb the removed node state.</param>
        /// <returns>A new envelope with initialized pruning state for the removed node.</returns>
        internal DataEnvelope InitRemovedNodePruning(UniqueAddress removed, UniqueAddress owner) =>
            new(Data, Pruning.SetItem(removed, new PruningInitialized(owner, ImmutableHashSet<Address>.Empty)));
        /// <summary>
        /// Applies pruning for a removed node when its pruning state is initialized.
        /// </summary>
        /// <param name="from">Address of the removed node.</param>
        /// <param name="pruningPerformed">Pruning state to store after pruning.</param>
        /// <exception cref="ArgumentException">No pruning entry exists for <paramref name="from"/> when the data supports removed-node pruning.</exception>
        /// <returns>A new pruned envelope when initialized pruning is applied; otherwise, this instance.</returns>
        internal DataEnvelope Prune(UniqueAddress from, PruningPerformed pruningPerformed)
        {
            if (Data is IRemovedNodePruning dataWithRemovedNodePruning)
            {
                if (!Pruning.TryGetValue(from, out var state))
                    throw new ArgumentException($"Can't prune {@from} since it's not found in DataEnvelope");

                if (state is PruningInitialized initialized)
                {
                    var prunedData = dataWithRemovedNodePruning.Prune(from, initialized.Owner);
                    return new DataEnvelope(data: prunedData, pruning: Pruning.SetItem(from, pruningPerformed), deltaVersions: CleanedDeltaVersions(from));
                }
            }
            return this;
        }
        /// <summary>
        /// Merges another envelope, combining data, pruning state, and delta-version tracking.
        /// </summary>
        /// <param name="other">Envelope whose state is merged with this instance.</param>
        /// <returns>The merged envelope, or the deleted-data tombstone if the other envelope contains deleted data.</returns>
        internal DataEnvelope Merge(DataEnvelope other)
        {
            if (other.Data is DeletedData) return DeletedEnvelope;

            var mergedPrunning = other.Pruning.ToBuilder();
            foreach (var entry in this.Pruning)
            {
                if (mergedPrunning.TryGetValue(entry.Key, out var state))
                    mergedPrunning[entry.Key] = entry.Value.Merge(state);
                else
                    mergedPrunning[entry.Key] = entry.Value;
            }

            var currentTime = DateTime.UtcNow;
            var filteredMergedPruning = mergedPrunning.Count == 0
                ? mergedPrunning.ToImmutable()
                : mergedPrunning
                    .Where(entry =>
                    {
                        var performed = entry.Value as PruningPerformed;
                        return !performed?.IsObsolete(currentTime) ?? true;
                    })
                    .ToImmutableDictionary();

            // cleanup and merge DeltaVersions
            var removedNodes = filteredMergedPruning.Keys.ToArray();
            var cleanedDeltaVersions = removedNodes.Aggregate(DeltaVersions, (acc, node) => acc.PruningCleanup(node));
            var cleanedOtherDeltaVersions = removedNodes.Aggregate(other.DeltaVersions, (acc, node) => acc.PruningCleanup(node));
            var mergedDeltaVersions = cleanedDeltaVersions.Merge(cleanedOtherDeltaVersions);

            // cleanup both sides before merging, `merge(otherData: ReplicatedData)` will cleanup other.data
            return new DataEnvelope(
                    data: Cleaned(Data, filteredMergedPruning),
                    pruning: filteredMergedPruning,
                    deltaVersions: mergedDeltaVersions)
                .Merge(other.Data);
        }
        /// <summary>
        /// Merges replicated data into this envelope after applying its pruning metadata.
        /// </summary>
        /// <param name="otherData">Replicated data or delta to merge.</param>
        /// <returns>A new envelope containing the merged data and this envelope metadata.</returns>
        /// <exception cref="ArgumentException">The incoming value is a delta but the current data does not support delta merging.</exception>
        internal DataEnvelope Merge(IReplicatedData otherData)
        {
            if (otherData is DeletedData) return DeletedEnvelope;

            var cleanedData = Cleaned(otherData, Pruning);
            IReplicatedData mergedData;
            if (cleanedData is IReplicatedDelta d)
            {
                var delta = Data as IDeltaReplicatedData ?? throw new ArgumentException($"Expected {nameof(IDeltaReplicatedData)} but got '{Data}' instead.");

                mergedData = delta.MergeDelta(d);
            }
            else mergedData = Data.Merge(cleanedData);

            return new DataEnvelope(mergedData, Pruning, DeltaVersions);
        }

        private IReplicatedData Cleaned(IReplicatedData c, IImmutableDictionary<UniqueAddress, IPruningState> p) => p.Aggregate(c, (acc, kvp) =>
        {
            // Cleanup has to chain through the accumulator: applying it repeatedly to the
            // original input can leave later removed-node markers behind after the first rewrite.
            if (acc is IRemovedNodePruning pruning
                && kvp.Value is PruningPerformed
                && pruning.NeedPruningFrom(kvp.Key))
                return pruning.PruningCleanup(kvp.Key);
            return acc;
        });
        /// <summary>
        /// Adds a node address to each tracked pruning state.
        /// </summary>
        /// <param name="node">Address observed by the pruning process.</param>
        /// <returns>A new envelope if any pruning state changes; otherwise, this instance.</returns>
        internal DataEnvelope AddSeen(Address node)
        {
            var changed = false;
            var newRemovedNodePruning = Pruning.Select(kvp =>
            {
                var newPruningState = kvp.Value.AddSeen(node);
                changed = !ReferenceEquals(newPruningState, kvp.Value) || changed;
                return new KeyValuePair<UniqueAddress, IPruningState>(kvp.Key, newPruningState);
            }).ToImmutableDictionary();

            return changed ? new DataEnvelope(Data, newRemovedNodePruning) : this;
        }
        /// <summary>
        /// Compares the data, pruning states, and delta versions in two envelopes.
        /// </summary>
        /// <param name="other">Envelope to compare with this instance.</param>
        /// <returns><see langword="true"/> if all compared state is equal; otherwise, <see langword="false"/>.</returns>
        public bool Equals(DataEnvelope other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            if (!Data.Equals(other.Data)) return false;
            if (Pruning.Count != other.Pruning.Count) return false;
            if (!DeltaVersions.Equals(other.DeltaVersions)) return false;

            foreach (var entry in Pruning)
            {
                //"it's possible that one node that begins pruning may"
                //"have different data than another node that hasn't started"
                if (other.Pruning.TryGetValue(entry.Key, out var state))
                {
                    if (!Equals(entry.Value, state))
                        return false;
                }
                else
                    return false;
            }

            return true;
        }
        /// <summary>
        /// Compares this envelope with another object.
        /// </summary>
        /// <param name="obj">Object to compare.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="DataEnvelope"/>; otherwise, <see langword="false"/>.</returns>
        public override bool Equals(object obj) => obj is DataEnvelope envelope && Equals(envelope);
        /// <summary>
        /// Returns a hash code based on the data, pruning states, and delta versions.
        /// </summary>
        /// <returns>A hash code for this envelope.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var seed =  ((Data != null ? Data.GetHashCode() : 0) * 397)
                            ^ (DeltaVersions != null ? DeltaVersions.GetHashCode() : 0);

                foreach (var p in Pruning)
                {
                    seed *= p.Key.GetHashCode() ^ p.Value.GetHashCode();
                }

                return seed;
            }
        }
        /// <summary>
        /// Formats the data and tracked pruning state for diagnostics.
        /// </summary>
        /// <returns>A string representation of this envelope.</returns>
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
    /// INTERNAL API
    ///
    /// Placeholder used to represent deleted data that has not yet been pruned or is permanently tombstoned.
    /// </summary>
    [Serializable]
    internal sealed class DeletedData : IReplicatedData<DeletedData>, IEquatable<DeletedData>, IReplicatedDataSerialization
    {
        public static readonly DeletedData Instance = new();

        private DeletedData() { }

        /// <inheritdoc cref="IReplicatedData{T}"/>
        public DeletedData Merge(DeletedData other) => this;

        /// <inheritdoc cref="IReplicatedData{T}"/>
        public IReplicatedData Merge(IReplicatedData other) => Merge((DeletedData)other);
        /// <inheritdoc/>
        public bool Equals(DeletedData other) => true;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DeletedData;

        /// <inheritdoc/>
        public override int GetHashCode() => 1;

        /// <inheritdoc/>
        public override string ToString() => "DeletedData";
    }

    /// <summary>
    /// Gossip status message containing key digests and optional chunk and system identifiers.
    /// </summary>
    [Serializable]
    internal sealed class Status : IReplicatorMessage, IEquatable<Status>, IDestinationSystemUid
    {
        /// <summary>
        /// Digest values indexed by replicated-data key.
        /// </summary>
        public IImmutableDictionary<string, ByteString> Digests { get; }
        /// <summary>
        /// Zero-based index of this status chunk.
        /// </summary>
        public int Chunk { get; }
        /// <summary>
        /// Total number of chunks in the status exchange.
        /// </summary>
        public int TotalChunks { get; }
        /// <summary>
        /// Destination actor-system UID, when specified.
        /// </summary>
        public long? ToSystemUid { get; }
        /// <summary>
        /// Originating actor-system UID, when supplied.
        /// </summary>
        public long? FromSystemUid { get; }
        /// <summary>
        /// Creates a status message.
        /// </summary>
        /// <param name="digests">Digest for each included key.</param>
        /// <param name="chunk">Zero-based index of this chunk.</param>
        /// <param name="totalChunks">Number of chunks in the exchange.</param>
        /// <param name="toSystemUid">Destination actor-system UID, or <see langword="null"/> if not specified.</param>
        /// <param name="fromSystemUid">Originating actor-system UID, or <see langword="null"/> if not specified.</param>
        public Status(IImmutableDictionary<string, ByteString> digests, int chunk, int totalChunks, long? toSystemUid = null, long? fromSystemUid = null)
        {
            Digests = digests;
            Chunk = chunk;
            TotalChunks = totalChunks;
            ToSystemUid = toSystemUid;
            FromSystemUid = fromSystemUid;
        }

        /// <inheritdoc/>
        public bool Equals(Status other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.Chunk.Equals(Chunk)
                && other.TotalChunks.Equals(TotalChunks)
                && Digests.SequenceEqual(other.Digests)
                && ToSystemUid.Equals(other.ToSystemUid)
                && FromSystemUid.Equals(other.FromSystemUid);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Status status && Equals(status);

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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
    /// Gossip message carrying updated data envelopes between replicators.
    /// </summary>
    [Serializable]
    internal sealed class Gossip : IReplicatorMessage, IEquatable<Gossip>, IDestinationSystemUid
    {
        /// <summary>
        /// Updated data envelopes indexed by replicated-data key.
        /// </summary>
        public IImmutableDictionary<string, DataEnvelope> UpdatedData { get; }
        /// <summary>
        /// Whether the receiver should send its differing data back in a reply.
        /// </summary>
        public bool SendBack { get; }
        /// <summary>
        /// Destination actor-system UID, when specified.
        /// </summary>
        public long? ToSystemUid { get; }
        /// <summary>
        /// Originating actor-system UID, when supplied.
        /// </summary>
        public long? FromSystemUid { get; }
        /// <summary>
        /// Creates a gossip message.
        /// </summary>
        /// <param name="updatedData">Data envelopes to merge, indexed by key.</param>
        /// <param name="sendBack">Whether the receiver should reply with differing data.</param>
        /// <param name="toSystemUid">Destination actor-system UID, or <see langword="null"/> if not specified.</param>
        /// <param name="fromSystemUid">Originating actor-system UID, or <see langword="null"/> if not specified.</param>
        public Gossip(IImmutableDictionary<string, DataEnvelope> updatedData, bool sendBack, long? toSystemUid = null, long? fromSystemUid = null)
        {
            UpdatedData = updatedData;
            SendBack = sendBack;
            ToSystemUid = toSystemUid;
            FromSystemUid = fromSystemUid;
        }

        /// <inheritdoc/>
        public bool Equals(Gossip other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.SendBack.Equals(SendBack)
                && UpdatedData.SequenceEqual(other.UpdatedData)
                && ToSystemUid.Equals(other.ToSystemUid)
                && FromSystemUid.Equals(other.FromSystemUid);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Gossip gossip && Equals(gossip);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((UpdatedData != null ? UpdatedData.GetHashCode() : 0) * 397) ^ SendBack.GetHashCode();
            }
        }

        /// <inheritdoc/>
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

    public sealed class Delta : IEquatable<Delta>
    {
        public DataEnvelope DataEnvelope { get; }
        public long FromSeqNr { get; }
        public long ToSeqNr { get; }

        public Delta(DataEnvelope dataEnvelope, long fromSeqNr, long toSeqNr)
        {
            DataEnvelope = dataEnvelope;
            FromSeqNr = fromSeqNr;
            ToSeqNr = toSeqNr;
        }

        public bool RequiresCausalDeliveryOfDeltas => DataEnvelope.Data is IRequireCausualDeliveryOfDeltas;

        public bool Equals(Delta other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(DataEnvelope, other.DataEnvelope) && FromSeqNr == other.FromSeqNr && ToSeqNr == other.ToSeqNr;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Delta delta && Equals(delta);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = DataEnvelope.GetHashCode();
                hashCode = (hashCode * 397) ^ FromSeqNr.GetHashCode();
                hashCode = (hashCode * 397) ^ ToSeqNr.GetHashCode();
                return hashCode;
            }
        }
    }

    public sealed class DeltaPropagation : IReplicatorMessage, IEquatable<DeltaPropagation>, ISendingSystemUid
    {
        private sealed class NoDelta : IDeltaReplicatedData<IReplicatedData, IReplicatedDelta>, IRequireCausualDeliveryOfDeltas
        {
            public static readonly NoDelta Instance = new();
            private NoDelta() { }

            IReplicatedDelta IDeltaReplicatedData.Delta => Delta;
            public IReplicatedDelta Delta => null;
            public IDeltaReplicatedData Zero => this;

            IReplicatedData IReplicatedData<IReplicatedData>.Merge(IReplicatedData other) => Merge(other);
            public IReplicatedData Merge(IReplicatedData other) => this;
            IReplicatedData IDeltaReplicatedData<IReplicatedData, IReplicatedDelta>.MergeDelta(IReplicatedDelta delta) => MergeDelta(delta);
            public IReplicatedData ResetDelta() => this;
            public IReplicatedData MergeDelta(IReplicatedDelta delta) => this;
        }
        /// <summary>
        /// When a DeltaReplicatedData returns `null` from <see cref="Delta"/> it must still be
        /// treated as a delta that increase the version counter in <see cref="DeltaPropagationSelector"/>`.
        /// Otherwise a later delta might be applied before the full state gossip is received
        /// and thereby violating <see cref="IRequireCausualDeliveryOfDeltas"/>.
        ///
        /// This is used as a placeholder for such `null` delta. It's filtered out
        /// in <see cref="DeltaPropagationSelector.CreateDeltaPropagation"/>, i.e. never sent to the other replicas.
        /// </summary>
        public static readonly IReplicatedDelta NoDeltaPlaceholder = NoDelta.Instance;

        public UniqueAddress FromNode { get; }
        public bool ShouldReply { get; }
        public ImmutableDictionary<string, Delta> Deltas { get; }

        public DeltaPropagation(UniqueAddress fromNode, bool shouldReply, ImmutableDictionary<string, Delta> deltas)
        {
            FromNode = fromNode;
            ShouldReply = shouldReply;
            Deltas = deltas;
        }

        public bool Equals(DeltaPropagation other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            if (!Equals(FromNode, other.FromNode) || !ShouldReply == other.ShouldReply || Deltas.Count != other.Deltas.Count)
                return false;

            foreach (var entry in Deltas)
            {
                if (!Equals(other.Deltas.GetValueOrDefault(entry.Key), entry.Value)) return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is DeltaPropagation propagation && Equals(propagation);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (FromNode != null ? FromNode.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ShouldReply.GetHashCode();
                hashCode = (hashCode * 397) ^ Deltas.GetHashCode();
                return hashCode;
            }
        }
    }

    public sealed class DeltaNack : IReplicatorMessage, IDeadLetterSuppression, IEquatable<DeltaNack>
    {
        public static readonly DeltaNack Instance = new();
        private DeltaNack() { }
        public bool Equals(DeltaNack other) => true;
        public override bool Equals(object obj) => obj is DeltaNack;
        public override int GetHashCode() => nameof(DeltaNack).GetHashCode();
    }
}
