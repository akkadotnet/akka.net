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
    /// Representation of a Vector-based clock (counting clock), inspired by Lamport logical clocks.
    /// 
    /// {{{
    /// Reference:
    ///     1) Leslie Lamport (1978). "Time, clocks, and the ordering of events in a distributed system". Communications of the ACM 21 (7): 558-565.
    ///    2) Friedemann Mattern (1988). "Virtual Time and Global States of Distributed Systems". Workshop on Parallel and Distributed Algorithms: pp. 215-226
    /// }}}
    /// 
    /// Based on code from the 'vlock' VectorClock library by Coda Hale.
    /// </summary>
    class VectorClock
    {
        /**
         * Hash representation of a versioned node name.
         */
        //TODO: Node is alias for string in akka. Alternatives to below implementation?
        //Do we really need to hash? Why not just use string and get rid of Node class
        public class Node : IComparable<Node>
        {
            readonly string _value;
#if DEBUG
            string ActualValue { get; set; }
#endif
            public Node(string value)
            {
                _value = value;
            }

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

            public static Node FromHash(string hash)
            {
                return new Node(hash);
            }

            //TODO: Use murmur here?
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

            public override int GetHashCode()
            {
                return _value.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                var that = obj as Node;
                if (that == null) return false;
                return _value.Equals(that._value);
            }

            public override string ToString()
            {
                return _value;
            }

            public int CompareTo(Node other)
            {
                return Comparer<String>.Default.Compare(_value, other._value);
            }
        }

        public class Timestamp
        {
            public static readonly long Zero = 0L;
            public static readonly long EndMarker = long.MinValue;
        }

        public enum Ordering
        {
            After,
            Before,
            Same,
            Concurrent,
            //TODO: Ideally this would be private, change to override of compare?
            FullOrder
        }

        readonly ImmutableSortedDictionary<Node, long> _versions;
        public ImmutableSortedDictionary<Node, long> Versions { get { return _versions; } }

        public static VectorClock Create()
        {
            return Create(ImmutableSortedDictionary.Create<Node, long>());
        }

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
        public VectorClock Increment(Node node)
        {
            var currentTimestamp = _versions.GetOrElse(node, Timestamp.Zero);
            return new VectorClock(_versions.SetItem(node, currentTimestamp + 1));
        }

        /// <summary>
        /// Returns true if <code>this</code> and <code>that</code> are concurrent else false.
        /// </summary>
        public bool IsConcurrentWith(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.Concurrent) == Ordering.Concurrent;
        }

        /// <summary>
        /// Returns true if <code>this</code> is before <code>that</code> else false.
        /// </summary>
        public bool IsBefore(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.Before) == Ordering.Before;
        }

        /// <summary>
        /// Returns true if <code>this</code> is after <code>that</code> else false.
        /// </summary>
        public bool IsAfter(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.After) == Ordering.After;
        }

        /// <summary>
        /// Returns true if this VectorClock has the same history as the 'that' VectorClock else false.
        /// </summary>
        public bool IsSameAs(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.Same) == Ordering.Same;
        }

        private readonly static KeyValuePair<Node, long> CmpEndMarker = new KeyValuePair<Node, long>(Node.Create("endmarker"), long.MinValue);

        /**
         * Vector clock comparison according to the semantics described by compareTo, with the ability to bail
         * out early if the we can't reach the Ordering that we are looking for.
         *
         * The ordering always starts with Same and can then go to Same, Before or After
         * If we're on After we can only go to After or Concurrent
         * If we're on Before we can only go to Before or Concurrent
         * If we go to Concurrent we exit the loop immediately
         *
         * If you send in the ordering FullOrder, you will get a full comparison.
         */
        private Ordering CompareOnlyTo(VectorClock that, Ordering order)
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

        /**
         * Compare two vector clocks. The outcome will be one of the following:
         * 
         * {{{
         *   1. Clock 1 is SAME (==)       as Clock 2 iff for all i c1(i) == c2(i)
         *   2. Clock 1 is BEFORE (<)      Clock 2 iff for all i c1(i) <= c2(i) and there exist a j such that c1(j) < c2(j)
         *   3. Clock 1 is AFTER (>)       Clock 2 iff for all i c1(i) >= c2(i) and there exist a j such that c1(j) > c2(j).
         *   4. Clock 1 is CONCURRENT (<>) to Clock 2 otherwise.
         * }}}
         */
        public Ordering CompareTo(VectorClock that)
        {
            return CompareOnlyTo(that, Ordering.FullOrder);
        }

        /**
         * Merges this VectorClock with another VectorClock. E.g. merges its versioned history.
         */
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

        public override string ToString()
        {
            return String.Format("VectorClock({0})",
                _versions.Select(p => p.Key + "->" + p.Value).Aggregate((n, t) => n + ", " + t));
        }
    }
}
