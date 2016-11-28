//-----------------------------------------------------------------------
// <copyright file="ORSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData
{
    [Serializable]
    public sealed class ORSetKey<T> : Key<ORSet<T>>
    {
        public ORSetKey(string id) : base(id) { }
    }
    
    internal interface IORSet { }

    public static class ORSet
    {
        public static ORSet<T> Create<T>(UniqueAddress node, T element) =>
            ORSet<T>.Empty.Add(node, element);

        public static ORSet<T> Create<T>(params KeyValuePair<UniqueAddress, T>[] elements) => 
            elements.Aggregate(ORSet<T>.Empty, (set, kv) => set.Add(kv.Key, kv.Value));

        public static ORSet<T> Create<T>(IEnumerable<KeyValuePair<UniqueAddress, T>> elements) =>
            elements.Aggregate(ORSet<T>.Empty, (set, kv) => set.Add(kv.Key, kv.Value));
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
    public class ORSet<T> : FastMerge<ORSet<T>>, IORSet, IReplicatedDataSerialization, IRemovedNodePruning<ORSet<T>>, IEquatable<ORSet<T>>, IEnumerable<T>
    {
        public static readonly ORSet<T> Empty = new ORSet<T>();

        private readonly IImmutableDictionary<T, VersionVector> _elementsMap;
        private readonly VersionVector _versionVector;

        /// <summary>
        /// INTERNAL API
        /// Subtract the <paramref name="vvector"/> from the <paramref name="dot"/>.
        /// What this means is that any (node, version) pair in
        /// <paramref name="dot"/> that is &lt;= an entry in <paramref name="vvector"/> is removed from <paramref name="dot"/>.
        /// Example [{a, 3}, {b, 2}, {d, 14}, {g, 22}] -
        ///         [{a, 4}, {b, 1}, {c, 1}, {d, 14}, {e, 5}, {f, 2}] =
        ///         [{b, 2}, {g, 22}]
        /// </summary>
        private static VersionVector SubtractDots(VersionVector dot, VersionVector vvector)
        {
            if (dot.IsEmpty) return VersionVector.Empty;

            if (dot is SingleVersionVector)
            {
                // if dot is dominated by version vector, drop it
                var single = (SingleVersionVector)dot;
                return vvector.VersionAt(single.Node) >= single.Version ? VersionVector.Empty : dot;
            }

            if (dot is MultiVersionVector)
            {
                var multi = (MultiVersionVector)dot;
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

        private static IImmutableDictionary<T, VersionVector> MergeCommonKeys(IEnumerable<T> commonKeys, ORSet<T> lhs, ORSet<T> rhs) => commonKeys.Aggregate(ImmutableDictionary<T, VersionVector>.Empty, (acc, k) =>
        {
            var l = lhs._elementsMap[k];
            var r = rhs._elementsMap[k];

            if (l is SingleVersionVector)
            {
                var lhsDots = (SingleVersionVector)l;
                if (r is SingleVersionVector)
                {
                    var rhsDots = (SingleVersionVector)r;
                    if (lhsDots.Node == rhsDots.Node && lhsDots.Version == rhsDots.Version)
                    {
                        return acc.SetItem(k, lhsDots);
                    }
                    else
                    {
                        var lhsKeep = SubtractDots(lhsDots, rhs._versionVector);
                        var rhsKeep = SubtractDots(rhsDots, lhs._versionVector);
                        var merged = lhsKeep.Merge(rhsKeep);
                        return merged.IsEmpty ? acc : acc.SetItem(k, merged);
                    }
                }
                else
                {
                    var rhsDots = (MultiVersionVector)r;
                    var commonDots = rhsDots.Versions
                        .Where(kv => lhsDots.Version == kv.Value && lhsDots.Node == kv.Key)
                        .ToImmutableDictionary();
                    var commonDotKeys = commonDots.Keys.ToImmutableArray();
                    var lhsUnique = commonDotKeys.Length != 0 ? VersionVector.Empty : lhsDots;
                    var rhsUniqueDots = rhsDots.Versions.RemoveRange(commonDotKeys);
                    var lhsKeep = SubtractDots(lhsUnique, rhs._versionVector);
                    var rhsKeep = SubtractDots(new MultiVersionVector(rhsUniqueDots), lhs._versionVector);
                    var merged = lhsKeep.Merge(rhsKeep).Merge(new MultiVersionVector(commonDots));

                    return merged.IsEmpty ? acc : acc.SetItem(k, merged);
                }
            }
            else
            {
                var lhsDots = (MultiVersionVector)l;
                if (r is SingleVersionVector)
                {
                    var rhsDots = (SingleVersionVector)r;
                    var commonDots = lhsDots.Versions
                        .Where(kv => kv.Value == rhsDots.Version && kv.Key == rhsDots.Node)
                        .ToImmutableDictionary();
                    var commonDotKeys = commonDots.Keys.ToImmutableArray();
                    var lhsUniqueDots = lhsDots.Versions.RemoveRange(commonDotKeys);
                    var rhsUnique = commonDotKeys.IsEmpty ? rhsDots : VersionVector.Empty;
                    var lhsKeep = SubtractDots(VersionVector.Create(lhsUniqueDots), rhs._versionVector);
                    var rhsKeep = SubtractDots(rhsUnique, lhs._versionVector);
                    var merged = lhsKeep.Merge(rhsKeep).Merge(VersionVector.Create(commonDots));
                    return merged.IsEmpty ? acc : acc.SetItem(k, merged);
                }
                else
                {
                    var rhsDots = (MultiVersionVector)r;
                    var commonDots = rhsDots.Versions
                        .Where(kv =>
                        {
                            long v;
                            return rhsDots.Versions.TryGetValue(kv.Key, out v) && v == kv.Value;
                        }).ToImmutableDictionary();
                    var commonDotKeys = commonDots.Keys.ToImmutableArray();
                    var lhsUniqueDots = lhsDots.Versions.RemoveRange(commonDotKeys);
                    var rhsUniqueDots = rhsDots.Versions.RemoveRange(commonDotKeys);
                    var lhsKeep = SubtractDots(VersionVector.Create(lhsUniqueDots), rhs._versionVector);
                    var rhsKeep = SubtractDots(VersionVector.Create(rhsUniqueDots), lhs._versionVector);
                    var merged = lhsKeep.Merge(rhsKeep).Merge(VersionVector.Create(commonDots));
                    return merged.IsEmpty ? acc : acc.SetItem(k, merged);
                }
            }
        });

        public ORSet()
        {
            _elementsMap = ImmutableDictionary<T, VersionVector>.Empty;
            _versionVector = VersionVector.Empty;
        }

        public ORSet(IImmutableDictionary<T, VersionVector> elementsMap, VersionVector versionVector)
        {
            _elementsMap = elementsMap;
            _versionVector = versionVector;
        }

        public IImmutableSet<T> Elements => _elementsMap.Keys.ToImmutableHashSet();

        public bool Contains(T elem) => _elementsMap.ContainsKey(elem);

        public bool IsEmpty => _elementsMap.Count == 0;

        public int Count => _elementsMap.Count;

        /// <summary>
        /// Adds an element to the set
        /// </summary>
        public ORSet<T> Add(UniqueAddress node, T element)
        {
            var vec = _versionVector.Increment(node);
            var dot = VersionVector.Create(node, vec.VersionAt(node));
            return AssignAncestor(new ORSet<T>(_elementsMap.SetItem(element, dot), vec));
        }

        /// <summary>
        /// Removes an element from the set.
        /// </summary>
        public ORSet<T> Remove(UniqueAddress node, T element) =>
            AssignAncestor(new ORSet<T>(_elementsMap.Remove(element), _versionVector));

        public ORSet<T> Clear(UniqueAddress node) =>
            AssignAncestor(new ORSet<T>(ImmutableDictionary<T, VersionVector>.Empty, _versionVector));

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
            else
            {
                var commonKeys = _elementsMap.Count < other._elementsMap.Count
                    ? _elementsMap.Keys.Where(other._elementsMap.ContainsKey)
                    : other._elementsMap.Keys.Where(_elementsMap.ContainsKey);
                var entries00 = MergeCommonKeys(commonKeys, this, other);
                var thisUniqueKeys = _elementsMap.Keys.Where(key => !other._elementsMap.ContainsKey(key));
                var entries0 = MergeDisjointKeys(thisUniqueKeys, _elementsMap, other._versionVector, entries00);
                var otherUniqueKeys = other._elementsMap.Keys.Where(key => !_elementsMap.ContainsKey(key));
                var entries = MergeDisjointKeys(otherUniqueKeys, other._elementsMap, _versionVector, entries0);
                var mergedVector = _versionVector.Merge(other._versionVector);

                ClearAncestor();
                return new ORSet<T>(entries, mergedVector);
            }
        }

        internal IImmutableDictionary<T, VersionVector> MergeDisjointKeys(IEnumerable<T> keys,
            IImmutableDictionary<T, VersionVector> elementsMap, VersionVector vector,
            IImmutableDictionary<T, VersionVector> accumulator)
        {
            return keys.Aggregate(accumulator, (acc, k) =>
            {
                var dots = elementsMap[k];
                return vector.IsSame(dots) || vector.IsAfter(dots) 
                ? acc 
                : acc.SetItem(k, SubtractDots(dots, vector));
            });
        }

        public bool NeedPruningFrom(UniqueAddress removedNode) => _versionVector.NeedPruningFrom(removedNode);

        public ORSet<T> Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            var pruned = _elementsMap.Aggregate(ImmutableDictionary<T, VersionVector>.Empty, (acc, kv) => kv.Value.NeedPruningFrom(removedNode)
                ? acc.SetItem(kv.Key, kv.Value.Prune(removedNode, collapseInto))
                : acc);

            if (pruned.IsEmpty) return new ORSet<T>(_elementsMap, _versionVector.Prune(removedNode, collapseInto));
            else
            {
                var newSet = new ORSet<T>(_elementsMap.AddRange(pruned), _versionVector.Prune(removedNode, collapseInto));
                return pruned.Keys.Aggregate(newSet, (set, elem) => set.Add(collapseInto, elem));
            }
        }

        public ORSet<T> PruningCleanup(UniqueAddress removedNode)
        {
            var updated = _elementsMap.Aggregate(_elementsMap, (acc, kv) => kv.Value.NeedPruningFrom(removedNode)
                ? acc.SetItem(kv.Key, kv.Value.PruningCleanup(removedNode))
                : acc);

            return new ORSet<T>(updated, _versionVector.PruningCleanup(removedNode));
        }

        public bool Equals(ORSet<T> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return _versionVector == other._versionVector && _elementsMap.SequenceEqual(other._elementsMap);
        }

        public IEnumerator<T> GetEnumerator() => _elementsMap.Keys.GetEnumerator();

        public override bool Equals(object obj) => obj is ORSet<T> && Equals((ORSet<T>)obj);

        public override int GetHashCode()
        {
            unchecked
            {
                return (_elementsMap.GetHashCode() * 397) ^ (_versionVector.GetHashCode());
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var sb = new StringBuilder("ORSet(");
            foreach (var element in Elements)
            {
                sb.Append(element).Append(", ");
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}