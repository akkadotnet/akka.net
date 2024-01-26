//-----------------------------------------------------------------------
// <copyright file="ORSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Cluster;
using Akka.Util.Internal;

namespace Akka.DistributedData
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IORSetKey
    {
        Type SetType { get; }
    }

    [Serializable]
    public sealed class ORSetKey<T> : Key<ORSet<T>>, IORSetKey
    {
        public ORSetKey(string id) : base(id) { }
        public Type SetType { get; } = typeof(T);
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Used for serialization help.
    /// </summary>
    internal interface IORSet
    {
        ORSet.IAddDeltaOperation ToAddDeltaOperation();

        ORSet.IRemoveDeltaOperation ToRemoveDeltaOperation();

        ORSet.IFullStateDeltaOperation ToFullStateDeltaOperation();

        Type SetType { get; }
    }


    public static class ORSet
    {
        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IDeltaOperation
        {
            IORSet UnderlyingSerialization { get; }
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IAddDeltaOperation : IDeltaOperation { }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IRemoveDeltaOperation : IDeltaOperation { }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IDeltaGroupOperation
        {
            IEnumerable<IReplicatedData> OperationsSerialization { get; }
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IFullStateDeltaOperation : IDeltaOperation { }

        public static ORSet<T> Create<T>(UniqueAddress node, T element) =>
            ORSet<T>.Empty.Add(node, element);

        public static ORSet<T> Create<T>(params KeyValuePair<UniqueAddress, T>[] elements) =>
            elements.Aggregate(ORSet<T>.Empty, (set, kv) => set.Add(kv.Key, kv.Value));

        public static ORSet<T> Create<T>(IEnumerable<KeyValuePair<UniqueAddress, T>> elements) =>
            elements.Aggregate(ORSet<T>.Empty, (set, kv) => set.Add(kv.Key, kv.Value));

        /// <summary>
        /// INTERNAL API
        /// Subtract the <paramref name="vvector"/> from the <paramref name="dot"/>.
        /// What this means is that any (node, version) pair in
        /// <paramref name="dot"/> that is &lt;= an entry in <paramref name="vvector"/> is removed from <paramref name="dot"/>.
        /// Example [{a, 3}, {b, 2}, {d, 14}, {g, 22}] -
        ///         [{a, 4}, {b, 1}, {c, 1}, {d, 14}, {e, 5}, {f, 2}] =
        ///         [{b, 2}, {g, 22}]
        /// </summary>
        internal static VersionVector SubtractDots(VersionVector dot, VersionVector vvector)
        {
            if (dot.IsEmpty) return VersionVector.Empty;

            if (dot is SingleVersionVector single)
            {
                // if dot is dominated by version vector, drop it
                return vvector.VersionAt(single.Node) >= single.Version ? VersionVector.Empty : single;
            }

            if (dot is MultiVersionVector multi)
            {
                var acc = ImmutableDictionary<UniqueAddress, long>.Empty.ToBuilder();
                foreach (var pair in multi.Versions)
                {
                    var v2 = vvector.VersionAt(pair.Key);
                    if (v2 < pair.Value) acc.Add(pair);
                }

                return VersionVector.Create(acc.ToImmutable());
            }

            throw new NotSupportedException("Cannot subtract dots from provided version vector");
        }
    }

    /// <summary>
    /// Implements a 'Observed Remove Set' CRDT, also called a 'OR-Set'.
    /// Elements can be added and removed any number of times. Concurrent add wins
    /// over remove.
    /// 
    /// It is not implemented as in the paper
    /// <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">A comprehensive study of Convergent and Commutative Replicated Data Types</a>.
    /// This is more space efficient and doesn't accumulate garbage for removed elements.
    /// It is described in the paper
    /// <a href="https://hal.inria.fr/file/index/docid/738680/filename/RR-8083.pdf">An optimized conflict-free replicated set</a>
    /// The implementation is inspired by the Riak DT <a href="https://github.com/basho/riak_dt/blob/develop/src/riak_dt_orswot.erl">
    /// riak_dt_orswot</a>.
    /// 
    /// The ORSet has a version vector that is incremented when an element is added to
    /// the set. The `node -&gt; count` pair for that increment is stored against the
    /// element as its "birth dot". Every time the element is re-added to the set,
    /// its "birth dot" is updated to that of the `node -&gt; count` version vector entry
    /// resulting from the add. When an element is removed, we simply drop it, no tombstones.
    /// 
    /// When an element exists in replica A and not replica B, is it because A added
    /// it and B has not yet seen that, or that B removed it and A has not yet seen that?
    /// In this implementation we compare the `dot` of the present element to the version vector
    /// in the Set it is absent from. If the element dot is not "seen" by the Set version vector,
    /// that means the other set has yet to see this add, and the item is in the merged
    /// Set. If the Set version vector dominates the dot, that means the other Set has removed this
    /// element already, and the item is not in the merged Set.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public sealed class ORSet<T> :
        FastMerge<ORSet<T>>,
        IORSet,
        IReplicatedDataSerialization,
        IRemovedNodePruning<ORSet<T>>,
        IEquatable<ORSet<T>>,
        IEnumerable<T>,
        IDeltaReplicatedData<ORSet<T>, ORSet<T>.IDeltaOperation>
    {
        public static readonly ORSet<T> Empty = new();

        internal readonly ImmutableDictionary<T, VersionVector> ElementsMap;

        /// <summary>
        /// Needs to be internal for serialization purposes.
        /// </summary>
        internal readonly VersionVector VersionVector;

        internal static ImmutableDictionary<T, VersionVector> MergeCommonKeys(IEnumerable<T> commonKeys, ORSet<T> lhs, ORSet<T> rhs) => commonKeys.Aggregate(ImmutableDictionary<T, VersionVector>.Empty, (acc, k) =>
        {
            var l = lhs.ElementsMap[k];
            var r = rhs.ElementsMap[k];

            if (l is SingleVersionVector lhsDots1)
            {
                if (r is SingleVersionVector rhsDots1)
                {
                    if (lhsDots1.Node == rhsDots1.Node && lhsDots1.Version == rhsDots1.Version)
                    {
                        return acc.SetItem(k, lhsDots1);
                    }
                    else
                    {
                        var lhsKeep = ORSet.SubtractDots(lhsDots1, rhs.VersionVector);
                        var rhsKeep = ORSet.SubtractDots(rhsDots1, lhs.VersionVector);
                        var merged = lhsKeep.Merge(rhsKeep);
                        return merged.IsEmpty ? acc : acc.SetItem(k, merged);
                    }
                }
                else
                {
                    var rhsDots = (MultiVersionVector)r;
                    var commonDots = rhsDots.Versions
                        .Where(kv => lhsDots1.Version == kv.Value && lhsDots1.Node == kv.Key)
                        .ToImmutableDictionary();
                    var commonDotKeys = commonDots.Keys.ToImmutableArray();
                    var lhsUnique = commonDotKeys.Length != 0 ? VersionVector.Empty : lhsDots1;
                    var rhsUniqueDots = rhsDots.Versions.RemoveRange(commonDotKeys);
                    var lhsKeep = ORSet.SubtractDots(lhsUnique, rhs.VersionVector);
                    var rhsKeep = ORSet.SubtractDots(new MultiVersionVector(rhsUniqueDots), lhs.VersionVector);
                    var merged = lhsKeep.Merge(rhsKeep).Merge(new MultiVersionVector(commonDots));

                    return merged.IsEmpty ? acc : acc.SetItem(k, merged);
                }
            }
            else
            {
                var lhsDots = (MultiVersionVector)l;
                if (r is SingleVersionVector rhsDots1)
                {
                    var commonDots = lhsDots.Versions
                        .Where(kv => kv.Value == rhsDots1.Version && kv.Key == rhsDots1.Node)
                        .ToImmutableDictionary();
                    var commonDotKeys = commonDots.Keys.ToImmutableArray();
                    var lhsUniqueDots = lhsDots.Versions.RemoveRange(commonDotKeys);
                    var rhsUnique = commonDotKeys.IsEmpty ? rhsDots1 : VersionVector.Empty;
                    var lhsKeep = ORSet.SubtractDots(VersionVector.Create(lhsUniqueDots), rhs.VersionVector);
                    var rhsKeep = ORSet.SubtractDots(rhsUnique, lhs.VersionVector);
                    var merged = lhsKeep.Merge(rhsKeep).Merge(VersionVector.Create(commonDots));
                    return merged.IsEmpty ? acc : acc.SetItem(k, merged);
                }
                else
                {
                    var rhsDots = (MultiVersionVector)r;
                    var commonDots = rhsDots.Versions
                        .Where(kv =>
                        {
                            return rhsDots.Versions.TryGetValue(kv.Key, out var v) && v == kv.Value;
                        }).ToImmutableDictionary();
                    var commonDotKeys = commonDots.Keys.ToImmutableArray();
                    var lhsUniqueDots = lhsDots.Versions.RemoveRange(commonDotKeys);
                    var rhsUniqueDots = rhsDots.Versions.RemoveRange(commonDotKeys);
                    var lhsKeep = ORSet.SubtractDots(VersionVector.Create(lhsUniqueDots), rhs.VersionVector);
                    var rhsKeep = ORSet.SubtractDots(VersionVector.Create(rhsUniqueDots), lhs.VersionVector);
                    var merged = lhsKeep.Merge(rhsKeep).Merge(VersionVector.Create(commonDots));
                    return merged.IsEmpty ? acc : acc.SetItem(k, merged);
                }
            }
        });

        public ORSet() : this(ImmutableDictionary<T, VersionVector>.Empty, VersionVector.Empty, null)
        {
        }

        public ORSet(ImmutableDictionary<T, VersionVector> elementsMap, VersionVector versionVector)
            : this(elementsMap, versionVector, null)
        {
        }

        internal ORSet(T element, VersionVector vector, VersionVector versionVector, IDeltaOperation delta)
            : this(
                ImmutableDictionary.CreateRange(new[] { new KeyValuePair<T, VersionVector>(element, vector) }),
                versionVector,
                delta)
        {
        }

        internal ORSet(ImmutableDictionary<T, VersionVector> elementsMap, VersionVector versionVector, IDeltaOperation delta)
        {
            ElementsMap = elementsMap;
            VersionVector = versionVector;
            _syncRoot = delta;
        }

        public IImmutableSet<T> Elements => ElementsMap.Keys.ToImmutableHashSet();

        public bool Contains(T elem) => ElementsMap.ContainsKey(elem);

        public bool IsEmpty => ElementsMap.Count == 0;

        public int Count => ElementsMap.Count;

        /// <summary>
        /// Adds an element to the set
        /// </summary>
        public ORSet<T> Add(Cluster.Cluster cluster, T element) => Add(cluster.SelfUniqueAddress, element);

        /// <summary>
        /// Adds an element to the set
        /// </summary>
        public ORSet<T> Add(UniqueAddress node, T element)
        {
            var newVersionVector = VersionVector.Increment(node);
            var newDot = VersionVector.Create(node, newVersionVector.VersionAt(node));
            IDeltaOperation newDelta = new AddDeltaOperation(new ORSet<T>(element, newDot, newDot, null));
            if (Delta != null)
            {
                newDelta = (IDeltaOperation)Delta.Merge(newDelta);
            }

            return AssignAncestor(new ORSet<T>(ElementsMap.SetItem(element, newDot), newVersionVector, newDelta));
        }

        /// <summary>
        /// Removes an element from the set.
        /// </summary>
        public ORSet<T> Remove(Cluster.Cluster node, T element) =>
            Remove(node.SelfUniqueAddress, element);

        /// <summary>
        /// Removes an element from the set.
        /// </summary>
        public ORSet<T> Remove(UniqueAddress node, T element)
        {
            var deltaDot = VersionVector.Create(node, VersionVector.VersionAt(node));
            IDeltaOperation newDelta = new RemoveDeltaOperation(new ORSet<T>(element, deltaDot, VersionVector, null));
            if (Delta != null)
            {
                newDelta = (IDeltaOperation)Delta.Merge(newDelta);
            }

            return AssignAncestor(new ORSet<T>(ElementsMap.Remove(element), VersionVector, newDelta));
        }

        /// <summary>
        /// Removes all elements from the set, but keeps the history.
        /// This has the same result as using <see cref="Remove(Akka.Cluster.Cluster,T)"/> for each
        /// element, but it is more efficient.
        /// </summary>
        public ORSet<T> Clear(Akka.Cluster.Cluster node) => Clear(node.SelfUniqueAddress);

        /// <summary>
        /// Removes all elements from the set, but keeps the history.
        /// This has the same result as using <see cref="Remove(UniqueAddress,T)"/> for each
        /// element, but it is more efficient.
        /// </summary>
        public ORSet<T> Clear(UniqueAddress node)
        {
            var newFullState = new ORSet<T>(ImmutableDictionary<T, VersionVector>.Empty, VersionVector);
            IDeltaOperation newDelta = new FullStateDeltaOperation(newFullState);
            if (Delta != null) newDelta = (IDeltaOperation)Delta.Merge(newDelta);

            return AssignAncestor(new ORSet<T>(ImmutableDictionary<T, VersionVector>.Empty, VersionVector, newDelta));
        }

        /// <summary>
        /// When element is in this Set but not in that Set:
        /// Compare the "birth dot" of the present element to the version vector in the Set it is absent from.
        /// If the element dot is not "seen" by other Set version vector, that means the other set has yet to
        /// see this add, and the element is to be in the merged Set.
        /// If the other Set version vector dominates the dot, that means the other Set has removed
        /// the element already, and the element is not to be in the merged Set.
        /// 
        /// When element in both this Set and in that Set:
        /// Some dots may still need to be shed. If this Set has dots that the other Set does not have,
        /// and the other Set version vector dominates those dots, then we need to drop those dots.
        /// Keep only common dots, and dots that are not dominated by the other sides version vector
        /// </summary>
        public override ORSet<T> Merge(ORSet<T> other)
        {
            if (ReferenceEquals(this, other) || other.IsAncestorOf(this)) return ClearAncestor();
            else if (IsAncestorOf(other)) return other.ClearAncestor();
            else return DryMerge(other, addDeltaOp: false);
        }

        private ORSet<T> DryMerge(ORSet<T> other, bool addDeltaOp)
        {
            var commonKeys = ElementsMap.Count < other.ElementsMap.Count
                ? ElementsMap.Keys.Where(other.ElementsMap.ContainsKey)
                : other.ElementsMap.Keys.Where(ElementsMap.ContainsKey);

            var entries00 = MergeCommonKeys(commonKeys, this, other);
            var entries0 = addDeltaOp
                ? entries00.AddRange(ElementsMap.Where(entry => !other.ElementsMap.ContainsKey(entry.Key)))
                : MergeDisjointKeys(ElementsMap.Keys.Where(key => !other.ElementsMap.ContainsKey(key)), ElementsMap, other.VersionVector, entries00);

            var otherUniqueKeys = other.ElementsMap.Keys.Where(key => !ElementsMap.ContainsKey(key));
            var entries = MergeDisjointKeys(otherUniqueKeys, other.ElementsMap, VersionVector, entries0);
            var mergedVector = VersionVector.Merge(other.VersionVector);

            ClearAncestor();
            return new ORSet<T>(entries, mergedVector);
        }

        internal static ImmutableDictionary<T, VersionVector> MergeDisjointKeys(IEnumerable<T> keys,
            ImmutableDictionary<T, VersionVector> elementsMap, VersionVector vector,
            ImmutableDictionary<T, VersionVector> accumulator)
        {
            return keys.Aggregate(accumulator, (acc, k) =>
            {
                var dots = elementsMap[k];
                return vector.IsSame(dots) || vector.IsAfter(dots)
                    ? acc
                    : acc.SetItem(k, ORSet.SubtractDots(dots, vector));
            });
        }

        public ImmutableHashSet<UniqueAddress> ModifiedByNodes => VersionVector.ModifiedByNodes;
        public bool NeedPruningFrom(UniqueAddress removedNode) => VersionVector.NeedPruningFrom(removedNode);
        IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode) => PruningCleanup(removedNode);

        IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => Prune(removedNode, collapseInto);

        public ORSet<T> Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            var pruned = ElementsMap.Aggregate(ImmutableDictionary<T, VersionVector>.Empty, (acc, kv) => kv.Value.NeedPruningFrom(removedNode)
                ? acc.SetItem(kv.Key, kv.Value.Prune(removedNode, collapseInto))
                : acc);

            if (pruned.IsEmpty) return new ORSet<T>(ElementsMap, VersionVector.Prune(removedNode, collapseInto));
            else
            {
                var newSet = new ORSet<T>(ElementsMap.SetItems(pruned), VersionVector.Prune(removedNode, collapseInto));
                return pruned.Keys.Aggregate(newSet, (set, elem) => set.Add(collapseInto, elem));
            }
        }

        public ORSet<T> PruningCleanup(UniqueAddress removedNode)
        {
            var updated = ElementsMap.Aggregate(ElementsMap, (acc, kv) => kv.Value.NeedPruningFrom(removedNode)
                ? acc.SetItem(kv.Key, kv.Value.PruningCleanup(removedNode))
                : acc);

            return new ORSet<T>(updated, VersionVector.PruningCleanup(removedNode));
        }

        
        public bool Equals(ORSet<T> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return VersionVector == other.VersionVector && ElementsMap.SequenceEqual(other.ElementsMap);
        }

        
        public IEnumerator<T> GetEnumerator() => ElementsMap.Keys.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        
        public override bool Equals(object obj) => obj is ORSet<T> set && Equals(set);

        
        public override int GetHashCode()
        {
            unchecked
            {
                return (ElementsMap.GetHashCode() * 397) ^ (VersionVector.GetHashCode());
            }
        }

        IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta) => MergeDelta((IDeltaOperation)delta);
        IReplicatedData IDeltaReplicatedData.ResetDelta() => ResetDelta();


        public override string ToString()
        {
            var sb = new StringBuilder("ORSet(");
            sb.AppendJoin(", ", Elements);
            sb.Append(')');
            return sb.ToString();
        }

        #region delta replication

        public interface IDeltaOperation : IReplicatedDelta, IRequireCausualDeliveryOfDeltas, IReplicatedDataSerialization, IEquatable<IDeltaOperation>
        {
        }

        internal abstract class AtomicDeltaOperation : IDeltaOperation, IReplicatedDeltaSize, ORSet.IDeltaOperation
        {
            public abstract ORSet<T> Underlying { get; }
            public abstract IReplicatedData Merge(IReplicatedData other);

            public IDeltaReplicatedData Zero => ORSet<T>.Empty;
            public int DeltaSize => 1;

            public bool Equals(IDeltaOperation other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                if (other is AtomicDeltaOperation op)
                {
                    return Underlying.Equals(op.Underlying);
                }
                return false;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((AtomicDeltaOperation)obj);
            }

            public override int GetHashCode() => GetType().GetHashCode() ^ Underlying.GetHashCode();
            public IORSet UnderlyingSerialization => Underlying;
        }

        internal sealed class AddDeltaOperation : AtomicDeltaOperation, ORSet.IAddDeltaOperation
        {
            public AddDeltaOperation(ORSet<T> underlying)
            {
                Underlying = underlying;
            }

            public override ORSet<T> Underlying { get; }
            public override IReplicatedData Merge(IReplicatedData other)
            {
                switch (other)
                {
                    case AddDeltaOperation operation:
                    {
                        var u = operation.Underlying;
                        // Note that we only merge deltas originating from the same node
                        return new AddDeltaOperation(new ORSet<T>(
                            ConcatElementsMap(u.ElementsMap),
                            Underlying.VersionVector.Merge(u.VersionVector)));
                    }
                    case AtomicDeltaOperation _:
                        return new DeltaGroup(ImmutableArray.Create(this, other));
                    case DeltaGroup dg:
                    {
                        var vector = dg.Operations;
                        return new DeltaGroup(vector.Add(this));
                    }
                    default:
                        throw new ArgumentException($"Unknown delta operation of type {other.GetType()}", nameof(other));
                }
            }

            private ImmutableDictionary<T, VersionVector> ConcatElementsMap(
                ImmutableDictionary<T, VersionVector> thatMap)
            {
                //var u = Underlying.ElementsMap.ToBuilder();
                //foreach (var entry in thatMap)
                //{
                //    u[entry.Key] = entry.Value;
                //}
                //return u.ToImmutable();
                return Underlying.ElementsMap.SetItems(thatMap);
            }
        }

        internal sealed class RemoveDeltaOperation : AtomicDeltaOperation, ORSet.IRemoveDeltaOperation
        {
            public RemoveDeltaOperation(ORSet<T> underlying)
            {
                if (underlying.Count != 1)
                    throw new ArgumentException($"RemoveDeltaOperation should contain one removed element, but was {underlying}");

                Underlying = underlying;
            }

            public override ORSet<T> Underlying { get; }
            public override IReplicatedData Merge(IReplicatedData other)
            {
                if (other is AtomicDeltaOperation)
                {
                    return new DeltaGroup(ImmutableArray.Create(this, other));
                }
                else if (other is DeltaGroup group)
                {
                    var vector = group.Operations;
                    return new DeltaGroup(vector.Add(this));
                }
                else throw new ArgumentException($"Unknown delta operation of type {other.GetType()}", nameof(other));
            }
        }

        internal sealed class FullStateDeltaOperation : AtomicDeltaOperation, ORSet.IFullStateDeltaOperation
        {
            public FullStateDeltaOperation(ORSet<T> underlying)
            {
                Underlying = underlying;
            }

            public override ORSet<T> Underlying { get; }
            public override IReplicatedData Merge(IReplicatedData other)
            {
                if (other is AtomicDeltaOperation)
                {
                    return new DeltaGroup(ImmutableArray.Create(this, other));
                }
                else if (other is DeltaGroup group)
                {
                    var vector = group.Operations;
                    return new DeltaGroup(vector.Add(this));
                }
                else throw new ArgumentException($"Unknown delta operation of type {other.GetType()}", nameof(other));
            }
        }

        internal sealed class DeltaGroup : IDeltaOperation, IReplicatedDeltaSize, ORSet.IDeltaGroupOperation
        {
            public ImmutableArray<IReplicatedData> Operations { get; }

            public DeltaGroup(ImmutableArray<IReplicatedData> operations)
            {
                Operations = operations;
            }

            public IReplicatedData Merge(IReplicatedData other)
            {
                if (other is AddDeltaOperation thatAdd)
                {
                    // merge AddDeltaOp into last AddDeltaOp in the group, if possible
                    var last = Operations[Operations.Length - 1];
                    return last is AddDeltaOperation thisAdd
                        ? new DeltaGroup(Operations.SetItem(Operations.Length - 1, thisAdd.Merge(thatAdd)))
                        : new DeltaGroup(Operations.Add(other));
                }
                else if (other is DeltaGroup @group)
                {
                    var otherVector = @group.Operations;
                    return new DeltaGroup(Operations.AddRange(otherVector));
                }
                else
                {
                    return new DeltaGroup(Operations.Add(other));
                }
            }

            public IDeltaReplicatedData Zero => ORSet<T>.Empty;
            public int DeltaSize => Operations.Length;

            public bool Equals(IDeltaOperation other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                if (other is DeltaGroup group)
                {
                    return Operations.SequenceEqual(group.Operations);
                }
                return false;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is DeltaGroup group && Equals(group);
            }

            public override int GetHashCode()
            {
                var hash = 0;
                unchecked
                {
                    foreach (var op in Operations)
                    {
                        hash = (hash * 297) ^ op.GetHashCode();
                    }
                    return hash;
                }
            }

            public IEnumerable<IReplicatedData> OperationsSerialization => Operations;
        }

        [NonSerialized]
        private readonly IDeltaOperation _syncRoot; //HACK: we need to ignore this field during serialization. This is the only way to do so on Hyperion on .NET Core

        public IDeltaOperation Delta => _syncRoot;

        public ORSet<T> MergeDelta(IDeltaOperation delta)
        {
            if (delta == null) throw new ArgumentNullException();
            switch (delta)
            {
                case AddDeltaOperation op: return DryMerge(op.Underlying, addDeltaOp: true);
                case RemoveDeltaOperation op: return MergeRemoveDelta(op);
                case FullStateDeltaOperation op: return DryMerge(op.Underlying, addDeltaOp: false);
                case DeltaGroup group:
                    var acc = this;
                    foreach (var operation in group.Operations)
                    {
                        switch (operation)
                        {
                            case AddDeltaOperation op: acc = acc.DryMerge(op.Underlying, addDeltaOp: true); break;
                            case RemoveDeltaOperation op: acc = acc.MergeRemoveDelta(op); break;
                            case FullStateDeltaOperation op: acc = acc.DryMerge(op.Underlying, addDeltaOp: false); break;
                            default: throw new ArgumentException("GroupDelta should not be nested");
                        }
                    }
                    return acc;
                default: throw new ArgumentException($"Cannot merge delta of type {delta.GetType()}", nameof(delta));
            }
        }

        private ORSet<T> MergeRemoveDelta(RemoveDeltaOperation delta)
        {
            var other = delta.Underlying;
            var kv = other.ElementsMap.First();
            var elem = kv.Key;

            var thisDot = ElementsMap.GetValueOrDefault(elem);
            var deleteDotNodes = new List<UniqueAddress>();
            var deleteDotsAreGreater = true;
            using (var deleteDots = other.VersionVector.VersionEnumerator)
            {
                while (deleteDots.MoveNext())
                {
                    var current = deleteDots.Current;
                    deleteDotNodes.Add(current.Key);
                    deleteDotsAreGreater &= (thisDot != null && (thisDot.VersionAt(current.Key) <= current.Value));
                }
            }

            var newElementsMap = ElementsMap;
            if (deleteDotsAreGreater)
            {
                if (thisDot != null)
                {
                    using (var e = thisDot.VersionEnumerator)
                    {
                        var allContains = true;
                        while (e.MoveNext()) allContains &= deleteDotNodes.Contains(e.Current.Key);
                        if (allContains)
                            newElementsMap = ElementsMap.Remove(elem);
                    }
                }
            }

            ClearAncestor();
            return new ORSet<T>(newElementsMap, VersionVector.Merge(other.VersionVector));
        }

        public ORSet<T> ResetDelta()
        {
            return Delta == null ? this : AssignAncestor(new ORSet<T>(ElementsMap, VersionVector));
        }

        #endregion

        ORSet.IAddDeltaOperation IORSet.ToAddDeltaOperation()
        {
            return new AddDeltaOperation(this);
        }

        ORSet.IRemoveDeltaOperation IORSet.ToRemoveDeltaOperation()
        {
            return new RemoveDeltaOperation(this);
        }

        ORSet.IFullStateDeltaOperation IORSet.ToFullStateDeltaOperation()
        {
            return new FullStateDeltaOperation(this);
        }

        public Type SetType { get; } = typeof(T);
    }
}
