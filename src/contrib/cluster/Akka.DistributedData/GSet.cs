//-----------------------------------------------------------------------
// <copyright file="GSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="elements">TBD</param>
        /// <returns>TBD</returns>
        public static GSet<T> Create<T>(params T[] elements) => new GSet<T>(ImmutableHashSet.Create(elements));

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="elements">TBD</param>
        /// <returns>TBD</returns>
        public static GSet<T> Create<T>(IImmutableSet<T> elements) => new GSet<T>(elements);
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
    /// <typeparam name="T">TBD</typeparam>
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
        /// TBD
        /// </summary>
        public static readonly GSet<T> Empty = new GSet<T>();

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableSet<T> Elements { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public GSet() : this(ImmutableHashSet<T>.Empty) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="elements">TBD</param>
        public GSet(IImmutableSet<T> elements) : this(elements, null) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="elements">TBD</param>
        /// <param name="delta"></param>
        public GSet(IImmutableSet<T> elements, GSet<T> delta)
        {
            Elements = elements;
            _syncRoot = delta;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public bool Contains(T element) => Elements.Contains(element);

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsEmpty => Elements.Count == 0;

        /// <summary>
        /// TBD
        /// </summary>
        public int Count => Elements.Count;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public GSet<T> Add(T element)
        {
            var newDelta = Delta != null
                ? new GSet<T>(Delta.Elements.Add(element))
                : new GSet<T>(ImmutableHashSet.Create(element));
            return AssignAncestor(new GSet<T>(Elements.Add(element), newDelta));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(GSet<T> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Elements.SetEquals(other.Elements);
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => Elements.GetEnumerator();

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GSet<T> && Equals((GSet<T>)obj);

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder("GSet(");
            sb.AppendJoin(", ", Elements);

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
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class GSetKey<T> : Key<GSet<T>>, IGSetKey, IReplicatedDataSerialization
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        public GSetKey(string id)
            : base(id)
        {
        }


        public Type SetType { get; } = typeof(T);
    }
}
