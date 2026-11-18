//-----------------------------------------------------------------------
// <copyright file="GSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Util.Internal;

namespace Akka.DistributedData
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IGSet
    {
        Type SetType { get; }
    }

    /// <summary>
    /// GSet helper methods.
    /// </summary>
    public static class GSet
    {
        /// <summary>
        /// Creates a grow-only set initialized with the supplied elements.
        /// </summary>
        /// <typeparam name="T">The type of elements stored in the set.</typeparam>
        /// <param name="elements">The initial elements.</param>
        /// <returns>A set containing the supplied elements.</returns>
        public static GSet<T> Create<T>(params T[] elements) => new(ImmutableHashSet.Create(elements));

        /// <summary>
        /// Creates a grow-only set from an immutable set of elements.
        /// </summary>
        /// <typeparam name="T">The type of elements stored in the set.</typeparam>
        /// <param name="elements">The initial immutable set of elements.</param>
        /// <returns>A grow-only set containing those elements.</returns>
        public static GSet<T> Create<T>(IImmutableSet<T> elements) => new(elements);
    }

    /// <summary>
    /// Implements a 'Add Set' CRDT, also called a 'G-Set'. You can't
    /// remove elements of a G-Set.
    /// 
    /// It is described in the paper
    /// <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">A comprehensive study of Convergent and Commutative Replicated Data Types</a>.
    /// 
    /// A G-Set doesn't accumulate any garbage apart from the elements themselves.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the set.</typeparam>
    [Serializable]
    public sealed class GSet<T> :
        FastMerge<GSet<T>>,
        IReplicatedDataSerialization,
        IGSet,
        IEquatable<GSet<T>>,
        IEnumerable<T>,
        IDeltaReplicatedData<GSet<T>, GSet<T>>,
        IReplicatedDelta
    {
        /// <summary>
        /// Gets an empty grow-only set.
        /// </summary>
        public static readonly GSet<T> Empty = new();

        /// <summary>
        /// Gets the elements in this set.
        /// </summary>
        public IImmutableSet<T> Elements { get; }

        /// <summary>
        /// Creates an empty grow-only set.
        /// </summary>
        public GSet() : this(ImmutableHashSet<T>.Empty) { }

        /// <summary>
        /// Creates a grow-only set containing the supplied immutable set.
        /// </summary>
        /// <param name="elements">The initial elements.</param>
        public GSet(IImmutableSet<T> elements) : this(elements, null) { }

        /// <summary>
        /// Creates a grow-only set from its elements and optional delta state.
        /// </summary>
        /// <param name="elements">The complete set of elements.</param>
        /// <param name="delta">The optional delta state accumulated for replication.</param>
        public GSet(IImmutableSet<T> elements, GSet<T> delta)
        {
            Elements = elements;
            _syncRoot = delta;
        }

        /// <summary>
        /// Merges two grow-only sets by taking the union of their elements.
        /// </summary>
        /// <param name="other">The set to merge with this instance.</param>
        /// <returns>A set containing elements from both sets.</returns>
        public override GSet<T> Merge(GSet<T> other)
        {
            if (ReferenceEquals(this, other) || other.IsAncestorOf(this)) return ClearAncestor();
            else if (IsAncestorOf(other)) return other.ClearAncestor();
            else
            {
                ClearAncestor();
                return new GSet<T>(Elements.Union(other.Elements));
            }
        }

        /// <summary>
        /// Checks whether this set contains an element.
        /// </summary>
        /// <param name="element">The element to look for.</param>
        /// <returns><see langword="true"/> if the element is present; otherwise, <see langword="false"/>.</returns>
        public bool Contains(T element) => Elements.Contains(element);

        /// <summary>
        /// Gets whether the set contains no elements.
        /// </summary>
        public bool IsEmpty => Elements.Count == 0;

        /// <summary>
        /// Gets the number of elements in the set.
        /// </summary>
        public int Count => Elements.Count;

        /// <summary>
        /// Returns a set with <paramref name="element"/> added.
        /// </summary>
        /// <param name="element">The element to add.</param>
        /// <returns>A set containing the existing elements and <paramref name="element"/>.</returns>
        public GSet<T> Add(T element)
        {
            var newDelta = Delta != null
                ? new GSet<T>(Delta.Elements.Add(element))
                : new GSet<T>(ImmutableHashSet.Create(element));
            return AssignAncestor(new GSet<T>(Elements.Add(element), newDelta));
        }

        /// <summary>
        /// Checks whether two grow-only sets contain the same elements.
        /// </summary>
        /// <param name="other">The set to compare with this instance.</param>
        /// <returns><see langword="true"/> if both sets contain the same elements; otherwise, <see langword="false"/>.</returns>
        public bool Equals(GSet<T> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Elements.SetEquals(other.Elements);
        }

        
        public IEnumerator<T> GetEnumerator() => Elements.GetEnumerator();

        
        public override bool Equals(object obj) => obj is GSet<T> set && Equals(set);

        
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 0;
                foreach (var element in Elements)
                {
                    hashCode = (hashCode * 397) ^ (element?.GetHashCode() ?? 0);
                }
                return hashCode;
            }
        }


        IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta) => MergeDelta((GSet<T>)delta);
        IReplicatedData IDeltaReplicatedData.ResetDelta() => ResetDelta();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        
        public override string ToString()
        {
            var sb = new StringBuilder("GSet(");
            sb.AppendJoin(", ", Elements);
            sb.Append(')');
            return sb.ToString();
        }

        [NonSerialized]
        private readonly GSet<T> _syncRoot; //HACK: we need to ignore this field during serialization. This is the only way to do so on Hyperion on .NET Core

        public GSet<T> Delta => _syncRoot;
        public GSet<T> MergeDelta(GSet<T> delta) => Merge(delta);

        public GSet<T> ResetDelta() => Delta == null ? this : AssignAncestor(new GSet<T>(Elements));
        IDeltaReplicatedData IReplicatedDelta.Zero => Empty;
        public Type SetType { get; } = typeof(T);
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Marker interface for serialization.
    /// </summary>
    internal interface IGSetKey
    {
        Type SetType { get; }
    }

    /// <summary>
    /// A typed key for a grow-only set CRDT.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the set.</typeparam>
    public sealed class GSetKey<T> : Key<GSet<T>>, IGSetKey, IReplicatedDataSerialization
    {
        /// <summary>
        /// Creates a key for a grow-only set.
        /// </summary>
        /// <param name="id">The unique identifier for the set key.</param>
        public GSetKey(string id)
            : base(id)
        {
        }


        public Type SetType { get; } = typeof(T);
    }
}
