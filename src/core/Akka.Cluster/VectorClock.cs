//-----------------------------------------------------------------------
// <copyright file="VectorClock.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Util.Internal;
using Node = System.String;

namespace Akka.Cluster
{
    /// <summary>
    /// <para>
    /// Representation of a Vector-based clock (counting clock), inspired by Lamport logical clocks.
    /// </para>
    /// <para>
    /// Reference:
    /// <ol>
    /// <li>Leslie Lamport (1978). "Time, clocks, and the ordering of events in a distributed system". Communications of the ACM 21 (7): 558-565.</li>
    /// <li>Friedemann Mattern (1988). "Virtual Time and Global States of Distributed Systems". Workshop on Parallel and Distributed Algorithms: pp. 215-226</li>
    /// </ol>
    /// </para>
    /// <para>
    /// Based on code from the 'vlock' VectorClock library by Coda Hale.
    /// </para>
    /// </summary>
    public sealed class VectorClock
    {
        /**
         * Hash representation of a versioned node name.
         */
        //TODO: Node is alias for string in akka. Alternatives to below implementation?
        //Do we really need to hash? Why not just use string and get rid of Node class		
        /// <summary>
        /// TBD
        /// </summary>
        public class Node : IComparable<Node>
        {
            readonly string _value;
#if DEBUG
            string ActualValue { get; set; }
#endif
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="value">TBD</param>
            public Node(string value)
            {
                _value = value;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="name">TBD</param>
            /// <returns>TBD</returns>
            public static Node Create(string name)
            {
#if DEBUG
                var node = Hash(name);
                node.ActualValue = name;
                return node;
#else
                return Hash(name);
#endif
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="hash">TBD</param>
            /// <returns>TBD</returns>
            public static Node FromHash(string hash)
            {
                return new Node(hash);
            }

            private static Node Hash(string name)
            {
                var md5 = System.Security.Cryptography.MD5.Create();
                var inputBytes = Encoding.UTF8.GetBytes(name);
                var hash = md5.ComputeHash(inputBytes);

                var sb = new StringBuilder();
                foreach (var t in hash)
                {
                    sb.Append(t.ToString("X2"));
                }
                return new Node(sb.ToString());
            }

            /// <summary>
            /// Returns a hash code for this instance.
            /// </summary>
            /// <returns>A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.</returns>
            public override int GetHashCode()
            {
                return _value.GetHashCode();
            }

            /// <summary>
            /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
            /// </summary>
            /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
            /// <returns><c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.</returns>
            public override bool Equals(object obj)
            {
                var that = obj as Node;
                if (that == null) return false;
                return _value.Equals(that._value);
            }

            /// <summary>
            /// Returns a <see cref="System.String" /> that represents this instance.
            /// </summary>
            /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
            public override string ToString()
            {
                return _value;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="other">TBD</param>
            /// <returns>TBD</returns>
            public int CompareTo(Node other)
            {
                return Comparer<String>.Default.Compare(_value, other._value);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Timestamp
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly long Zero = 0L;
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly long EndMarker = long.MinValue;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public enum Ordering
        {
            /// <summary>
            /// TBD
            /// </summary>
            After,
            /// <summary>
            /// TBD
            /// </summary>
            Before,
            /// <summary>
            /// TBD
            /// </summary>
            Same,
            /// <summary>
            /// TBD
            /// </summary>
            Concurrent,
            //TODO: Ideally this would be private, change to override of compare?
            /// <summary>
            /// TBD
            /// </summary>
            FullOrder
        }

        readonly ImmutableSortedDictionary<Node, long> _versions;
        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableSortedDictionary<Node, long> Versions { get { return _versions; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static VectorClock Create()
        {
            return Create(ImmutableSortedDictionary.Create<Node, long>());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="seedValues">TBD</param>
        /// <returns>TBD</returns>
        public static VectorClock Create(ImmutableSortedDictionary<Node, long> seedValues)
        {
            return new VectorClock(seedValues);
        }

        VectorClock(ImmutableSortedDictionary<Node, long> versions)
        {
            _versions = versions;
        }

        /// <summary>
        /// Increment the version for the node passed as argument. Returns a new VectorClock.
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public VectorClock Increment(Node node)
        {
            var currentTimestamp = _versions.GetOrElse(node, Timestamp.Zero);
            return new VectorClock(_versions.SetItem(node, currentTimestamp + 1));
        }

        /// <summary>
        /// Returns true if <code>this</code> and <code>that</code> are concurrent else false.
        /// </summary>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public bool IsConcurrentWith(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.Concurrent) == Ordering.Concurrent;
        }

        /// <summary>
        /// Returns true if <code>this</code> is before <code>that</code> else false.
        /// </summary>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public bool IsBefore(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.Before) == Ordering.Before;
        }

        /// <summary>
        /// Returns true if <code>this</code> is after <code>that</code> else false.
        /// </summary>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public bool IsAfter(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.After) == Ordering.After;
        }

        /// <summary>
        /// Returns true if this VectorClock has the same history as the 'that' VectorClock else false.
        /// </summary>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public bool IsSameAs(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.Same) == Ordering.Same;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="left">TBD</param>
        /// <param name="right">TBD</param>
        /// <returns>TBD</returns>
        public static bool operator >(VectorClock left, VectorClock right)
        {
            return left.IsAfter(right);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="left">TBD</param>
        /// <param name="right">TBD</param>
        /// <returns>TBD</returns>
        public static bool operator <(VectorClock left, VectorClock right)
        {
            return left.IsBefore(right);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="left">TBD</param>
        /// <param name="right">TBD</param>
        /// <returns>TBD</returns>
        public static bool operator ==(VectorClock left, VectorClock right)
        {
            if (ReferenceEquals(left, null))
                return false;

            return left.IsSameAs(right);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="left">TBD</param>
        /// <param name="right">TBD</param>
        /// <returns>TBD</returns>
        public static bool operator !=(VectorClock left, VectorClock right)
        {
            if (ReferenceEquals(left, null))
                return false;

            return left.IsConcurrentWith(right);
        }

        private static readonly KeyValuePair<Node, long> CmpEndMarker = new KeyValuePair<Node, long>(Node.Create("endmarker"), long.MinValue);

        /// <summary>
        /// <para>
        /// Vector clock comparison according to the semantics described by compareTo,
        /// with the ability to bail out early if the we can't reach the <see cref="Ordering"/>
        /// that we are looking for.
        /// </para>
        /// <para>
        /// The ordering always starts with <see cref="Ordering.Same"/> and can then go to
        /// <see cref="Ordering.Same"/>, <see cref="Ordering.Before"/> or <see cref="Ordering.After"/>.
        /// </para>
        /// <para>
        /// <ul>
        /// <li>If we're on <see cref="Ordering.After"/>, then we can only go to <see cref="Ordering.After"/> or <see cref="Ordering.Concurrent"/>.</li>
        /// <li>If we're on <see cref="Ordering.Before"/>, then we can only go to <see cref="Ordering.Before"/>Before or <see cref="Ordering.Concurrent"/>.</li>
        /// <li>If we go to <see cref="Ordering.Concurrent"/>, then we exit the loop immediately></li>
        /// <li>If you send in the ordering <see cref="Ordering.FullOrder"/>FullOrder, then you will get a full comparison.</li>
        /// </ul>
        /// </para>
        /// </summary>
        /// <param name="that">TBD</param>
        /// <param name="order">TBD</param>
        /// <returns>TBD</returns>
        internal Ordering CompareOnlyTo(VectorClock that, Ordering order)
        {
            if (Equals(that) || Versions.Equals(that.Versions)) return Ordering.Same;

            return Compare(_versions.GetEnumerator(), that._versions.GetEnumerator(), order == Ordering.Concurrent ? Ordering.FullOrder : order);
        }

        private static Ordering Compare(IEnumerator<KeyValuePair<Node, long>> i1, IEnumerator<KeyValuePair<Node, long>> i2, Ordering requestedOrder)
        {
            //TODO: Tail recursion issues?
            Func<KeyValuePair<Node, long>, KeyValuePair<Node, long>, Ordering, Ordering> compareNext = null;
            compareNext =
                (nt1, nt2, currentOrder) =>
                {
                    if (requestedOrder != Ordering.FullOrder && currentOrder != Ordering.Same &&
                        currentOrder != requestedOrder)
                        return currentOrder;
                    if (nt1.Equals(CmpEndMarker) && nt2.Equals(CmpEndMarker)) return currentOrder;
                    // i1 is empty but i2 is not, so i1 can only be Before
                    if (nt1.Equals(CmpEndMarker))
                        return currentOrder == Ordering.After ? Ordering.Concurrent : Ordering.Before;
                    // i2 is empty but i1 is not, so i1 can only be After
                    if (nt2.Equals(CmpEndMarker))
                        return currentOrder == Ordering.Before ? Ordering.Concurrent : Ordering.After;
                    // compare the nodes
                    var nc = nt1.Key.CompareTo(nt2.Key);
                    if (nc == 0)
                    {
                        // both nodes exist compare the timestamps
                        // same timestamp so just continue with the next nodes   
                        if (nt1.Value == nt2.Value)
                            return compareNext(NextOrElse(i1, CmpEndMarker), NextOrElse(i2, CmpEndMarker), currentOrder);
                        if (nt1.Value < nt2.Value)
                        {
                            // t1 is less than t2, so i1 can only be Before
                            if (currentOrder == Ordering.After) return Ordering.Concurrent;
                            return compareNext(NextOrElse(i1, CmpEndMarker), NextOrElse(i2, CmpEndMarker),
                                Ordering.Before);
                        }
                        if (currentOrder == Ordering.Before) return Ordering.Concurrent;
                        return compareNext(NextOrElse(i1, CmpEndMarker), NextOrElse(i2, CmpEndMarker), Ordering.After);
                    }
                    if (nc < 0)
                    {
                        // this node only exists in i1 so i1 can only be After
                        if (currentOrder == Ordering.Before) return Ordering.Concurrent;
                        return compareNext(NextOrElse(i1, CmpEndMarker), nt2, Ordering.After);
                    }
                    // this node only exists in i2 so i1 can only be Before
                    if (currentOrder == Ordering.After) return Ordering.Concurrent;
                    return compareNext(nt1, NextOrElse(i2, CmpEndMarker), Ordering.Before);
                };

            return compareNext(NextOrElse(i1, CmpEndMarker), NextOrElse(i2, CmpEndMarker), Ordering.Same);
        }

        private static T NextOrElse<T>(IEnumerator<T> iter, T @default)
        {
            return iter.MoveNext() ? iter.Current : @default;
        }

        /// <summary>
        /// <para>
        /// Compares the current vector clock with the supplied vector clock. The outcome will be one of the following:
        /// </para>
        /// <ol>
        /// <li><![CDATA[ Clock 1 is SAME(==)       as Clock 2 iff for all i c1(i) == c2(i) ]]></li>
        /// <li><![CDATA[ Clock 1 is BEFORE(<)      Clock 2 iff for all i c1(i) <= c2(i) and there exist a j such that c1(j) < c2(j) ]]></li>
        /// <li><![CDATA[ Clock 1 is AFTER(>)       Clock 2 iff for all i c1(i) >= c2(i) and there exist a j such that c1(j) > c2(j). ]]></li>
        /// <li><![CDATA[ Clock 1 is CONCURRENT(<>) to Clock 2 otherwise. ]]></li>
        /// </ol>
        /// </summary>
        /// <param name="that">The vector clock used to compare against.</param>
        /// <returns>TBD</returns>
        public Ordering CompareTo(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.FullOrder);
        }

        /// <summary>
        /// Merges the vector clock with another <see cref="VectorClock"/> (e.g. merges its versioned history).
        /// </summary>
        /// <param name="that">The vector clock to merge into the current clock.</param>
        /// <returns>A newly created <see cref="VectorClock"/> with the current vector clock and the given vector clock merged.</returns>
        public VectorClock Merge(VectorClock that)
        {
            var mergedVersions = that.Versions;
            foreach (var pair in _versions)
            {
                var mergedVersionsCurrentTime = mergedVersions.GetOrElse(pair.Key, Timestamp.Zero);
                if (pair.Value > mergedVersionsCurrentTime)
                {
                    mergedVersions = mergedVersions.SetItem(pair.Key, pair.Value);
                }
            }
            return new VectorClock(mergedVersions);
        }

        /// <summary>
        /// Removes the specified node from the current vector clock.
        /// </summary>
        /// <param name="removedNode">The node that is being removed.</param>
        /// <returns>A newly created <see cref="VectorClock"/> that has the given node removed.</returns>
        public VectorClock Prune(Node removedNode)
        {
            if (Versions.ContainsKey(removedNode))
            {
                return Create(Versions.Remove(removedNode));
            }
            return this;
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            var versions = _versions.Select(p => p.Key + "->" + p.Value);
            return $"VectorClock({string.Join(", ", versions)})";
        }
    }
}

