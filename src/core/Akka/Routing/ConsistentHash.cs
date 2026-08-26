//-----------------------------------------------------------------------
// <copyright file="ConsistentHash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Util;

namespace Akka.Routing
{
    /// <summary>
    /// Consistent Hashing node ring implementation.
    /// 
    ///  A good explanation of Consistent Hashing:
    /// http://weblogs.java.net/blog/tomwhite/archive/2007/11/consistent_hash.html
    /// 
    /// Note that toString of the ring nodes are used for the node
    /// hash, i.e. make sure it is different for different nodes.
    /// </summary>
    /// <typeparam name="T">The type of objects to store in the hash.</typeparam>
    public class ConsistentHash<T>
    {
        private readonly SortedDictionary<int, T> _nodes;
        private readonly int _virtualNodesFactor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHash{T}"/> class.
        /// </summary>
        /// <param name="nodes">TBD</param>
        /// <param name="virtualNodesFactor">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="virtualNodesFactor"/> is less than one.
        /// </exception>
        public ConsistentHash(SortedDictionary<int, T> nodes, int virtualNodesFactor)
        {
            _nodes = nodes;

            if (virtualNodesFactor < 1) throw new ArgumentException("virtualNodesFactor must be >= 1", nameof(virtualNodesFactor));

            _virtualNodesFactor = virtualNodesFactor;
        }

        private (int[], T[])? _ring = null;
        private (int[], T[])? RingTuple
        {
            get { return _ring ??= (_nodes.Keys.ToArray(), _nodes.Values.ToArray()); }
            }

        private int[] NodeHashRing
        {
            get { return RingTuple.Value.Item1; }
        }

        private T[] NodeRing
        {
            get { return RingTuple.Value.Item2; }
        }

        /// <summary>
        /// Adds a node to the hash ring.
        /// 
        /// Note that <see cref="ConsistentHash{T}"/> is immutable and
        /// this operation returns a new instance.
        /// </summary>
        /// <param name="node">The node to add to the hash ring</param>
        /// <returns>A new instance of this hash ring with the given node added.</returns>
        public ConsistentHash<T> Add(T node)
        {
            return this + node;
        }

        /// <summary>
        /// Removes a node from the hash ring.
        /// 
        /// Note that <see cref="ConsistentHash{T}"/> is immutable and
        /// this operation returns a new instance.
        /// </summary>
        /// <param name="node">The node to remove from the hash ring</param>
        /// <returns>A new instance of this hash ring with the given node removed.</returns>
        public ConsistentHash<T> Remove(T node)
        {
            return this - node;
        }

        private int Idx(int i)
        {
            if (i >= 0) return i; //exact match
            else
            {
                var j = Math.Abs(i + 1);
                if (j >= NodeHashRing.Length) return 0; //after last, use first
                else return j; //next node clockwise
            }
        }

        /// <summary>
        /// Retrieves the node associated with the data key.
        /// </summary>
        /// <param name="key">The data key used for lookup.</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if the node ring is empty.
        /// </exception>
        /// <returns>The node associated with the data key</returns>
        public T NodeFor(byte[] key)
        {
            if (IsEmpty) throw new InvalidOperationException($"Can't get node for [{key}] from an empty node ring");

            return NodeRing[Idx(Array.BinarySearch(NodeHashRing, ConsistentHash.HashFor(key)))];
        }

        /// <summary>
        /// Retrieves the node associated with the data key.
        /// </summary>
        /// <param name="key">The data key used for lookup.</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if the node ring is empty.
        /// </exception>
        /// <returns>The node associated with the data key</returns>
        public T NodeFor(string key)
        {
            if (IsEmpty) throw new InvalidOperationException($"Can't get node for [{key}] from an empty node ring");

            return NodeRing[Idx(Array.BinarySearch(NodeHashRing, ConsistentHash.HashFor(key)))];
        }

        /// <summary>
        /// Check to determine if the node ring is empty (i.e. no nodes added or all removed)
        /// </summary>
        public bool IsEmpty
        {
            get { return !_nodes.Any(); }
        }

        /// <summary>
        /// This class represents a surrogate of a <see cref="ConsistentHashingGroup"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class ConsistentHashingGroupSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="ConsistentHashingGroup"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="ConsistentHashingGroup"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new ConsistentHashingGroup(Paths);
            }

            /// <summary>
            /// The actor paths used by this router during routee selection.
            /// </summary>
            public string[] Paths { get; set; }
        }

        #region Operator overloads

        /// <summary>
        /// Adds a node to the hash ring.
        /// 
        /// Note that <see cref="ConsistentHash{T}"/> is immutable and
        /// this operation returns a new instance.
        /// </summary>
        /// <param name="hash">The hash ring used to derive a new ring with the given node added.</param>
        /// <param name="node">The node to add to the hash ring</param>
        /// <returns>A new instance of this hash ring with the given node added.</returns>
        public static ConsistentHash<T> operator +(ConsistentHash<T> hash, T node)
        {
            // Rebuild via Create from the existing nodes plus the new one. Create de-duplicates by
            // ToString() (the ring's node identity), so this is byte-identical to
            // ConsistentHash.Create(all nodes): collisions resolve in canonical order, and adding a
            // node already present (by ToString) is a no-op rather than a duplicated vnode set (#8031).
            // Values repeats each node virtualNodesFactor times; Distinct() (reference equality on the
            // stored instances) collapses that back to N so Create sorts N nodes, not N*V - Create's
            // ToString de-dup remains the correctness guarantee.
            return ConsistentHash.Create(hash._nodes.Values.Distinct().Append(node), hash._virtualNodesFactor);
        }

        /// <summary>
        /// Removes a node from the hash ring.
        /// 
        /// Note that <see cref="ConsistentHash{T}"/> is immutable and
        /// this operation returns a new instance.
        /// </summary>
        /// <param name="hash">The hash ring used to derive a new ring with the given node removed.</param>
        /// <param name="node">The node to remove from the hash ring</param>
        /// <returns>A new instance of this hash ring with the given node removed.</returns>
        public static ConsistentHash<T> operator -(ConsistentHash<T> hash, T node)
        {
            // Rebuild via Create from the existing nodes minus every entry whose ToString() matches
            // the removed node. Rebuilding is required because Create may have relocated a colliding
            // virtual node to a probed slot that a key-based delete would miss. Matching by ToString()
            // (the ring's node identity) - rather than T.Equals - avoids dropping a different node
            // that merely compares Equals-equal to the target (#8031).
            var nodeKey = node.ToString();
            // Distinct() first (reference equality collapses the virtualNodesFactor repeats in Values
            // to N distinct nodes) so the ToString filter and Create's sort run over N, not N*V.
            return ConsistentHash.Create(
                hash._nodes.Values.Distinct().Where(n => !string.Equals(n.ToString(), nodeKey, StringComparison.Ordinal)),
                hash._virtualNodesFactor);
        }

        #endregion
    }

    /// <summary>
    /// Static helper class for creating <see cref="ConsistentHash{T}"/> instances.
    /// </summary>
    public static class ConsistentHash
    {
        /// <summary>
        /// Factory method to create a <see cref="ConsistentHash{T}"/> instance.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="nodes">TBD</param>
        /// <param name="virtualNodesFactor">TBD</param>
        /// <returns>TBD</returns>
        public static ConsistentHash<T> Create<T>(IEnumerable<T> nodes, int virtualNodesFactor)
        {
            var sortedDict = new SortedDictionary<int, T>();
            // Build the ring in a canonical (node string) order so that every node in the
            // cluster produces an identical ring. This matters because the collision handling
            // below is order-sensitive: without a stable order two nodes could resolve the same
            // 32-bit hash collision differently and disagree on routing. See #8031.
            //
            // Nodes are identified by ToString() - the same value their ring keys are derived from
            // (the class requires ToString to be distinct per node). De-duplicating by it means a
            // node supplied more than once contributes a single set of virtual nodes instead of
            // having its duplicates probed into extra slots, which would skew routing toward it.
            var seenNodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in nodes.Select(n => (Node: n, Key: n.ToString()))
                         .OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!seenNodes.Add(entry.Key))
                    continue;
                var nodeHash = HashFor(entry.Key);
                for (var vnode = 1; vnode <= virtualNodesFactor; vnode++)
                {
                    var key = ConcatenateNodeHash(nodeHash, vnode);
                    // The ring key space is only 32 bits wide, so two virtual nodes can hash to the
                    // same slot. Rather than throwing (which used to wedge the entire router until a
                    // restart - #8031), relocate the loser. We re-hash it to a well-distributed slot
                    // rather than taking the adjacent key+1: an adjacent slot would leave the relocated
                    // virtual node a near-zero-width ring segment and starve that node of ~1/factor of
                    // its traffic, whereas a re-hashed slot lands in a sparse region and keeps a
                    // full-width segment, preserving the node's share of the ring. A short linear probe
                    // from there guarantees termination in the (astronomically rare) event the
                    // re-hashed slot is itself taken. The whole sequence is a pure function of the node
                    // hash, so every cluster node builds an identical ring.
                    if (sortedDict.ContainsKey(key))
                    {
                        key = ConcatenateNodeHash(nodeHash, unchecked(vnode + virtualNodesFactor));
                        while (sortedDict.ContainsKey(key))
                            key = unchecked(key + 1);
                    }
                    sortedDict.Add(key, entry.Node);
                }
            }

            return new ConsistentHash<T>(sortedDict, virtualNodesFactor);
        }

        #region Hashing methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="nodeHash">TBD</param>
        /// <param name="vnode">TBD</param>
        /// <returns>TBD</returns>
        internal static int ConcatenateNodeHash(int nodeHash, int vnode)
        {
            unchecked
            {
                var h = MurmurHash.StartHash((uint)nodeHash);
                h = MurmurHash.ExtendHash(h, (uint)vnode, MurmurHash.StartMagicA, MurmurHash.StartMagicB);
                return (int)MurmurHash.FinalizeHash(h);
            }
        }

        /// <summary>
        /// This class represents a surrogate of a <see cref="ConsistentHashingPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class ConsistentHashingPoolSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="ConsistentHashingPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="ConsistentHashingPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new ConsistentHashingPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }

            /// <summary>
            /// The number of routees associated with this pool.
            /// </summary>
             public int NrOfInstances { get; set; }
            /// <summary>
            /// Determine whether or not to use the pool dispatcher. The dispatcher is defined in the
            /// 'pool-dispatcher' configuration property in the deployment section of the router.
            /// </summary>
             public bool UsePoolDispatcher { get; set; }
            /// <summary>
            /// The resizer to use when dynamically allocating routees to the pool.
            /// </summary>
             public Resizer Resizer { get; set; }
            /// <summary>
            /// The strategy to use when supervising the pool.
            /// </summary>
             public SupervisorStrategy SupervisorStrategy { get; set; }
            /// <summary>
            /// The dispatcher to use when passing messages to the routees.
            /// </summary>
             public string RouterDispatcher { get; set; }
        }

        /// <summary>
        /// Translate the offered object into a byte array, or returns the original object
        /// if it needs to be serialized first.
        /// </summary>
        /// <param name="obj">An arbitrary .NET object</param>
        /// <returns>The object encoded into bytes - in the case of custom classes, the hashcode may be used.</returns>
        internal static object ToBytesOrObject(object obj)
        {
            switch (obj)
            {
                case null:
                    return new byte[] { 0 };
                case byte[] bytes:
                    return bytes;
                case int @int:
                    return BitConverter.GetBytes(@int);
                case uint @uint:
                    return BitConverter.GetBytes(@uint);
                case short @short:
                    return BitConverter.GetBytes(@short);
                case ushort @ushort:
                    return BitConverter.GetBytes(@ushort);
                case bool @bool:
                    return BitConverter.GetBytes(@bool);
                case long @long:
                    return BitConverter.GetBytes(@long);
                case ulong @ulong:
                    return BitConverter.GetBytes(@ulong);
                case char @char:
                    return BitConverter.GetBytes(@char);
                case float @float:
                    return BitConverter.GetBytes(@float);
                case double @double:
                    return BitConverter.GetBytes(@double);
                case decimal @decimal:
                    return new BitArray(decimal.GetBits(@decimal)).ToBytes();
                case Guid guid:
                    return guid.ToByteArray();
                default:
                    return obj;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bytes">TBD</param>
        /// <returns>TBD</returns>
        internal static int HashFor(byte[] bytes)
        {
            return MurmurHash.ByteHash(bytes);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="hashKey">TBD</param>
        /// <returns>TBD</returns>
        internal static int HashFor(string hashKey)
        {
            return MurmurHash.StringHash(hashKey);
        }

        #endregion
    }
}

