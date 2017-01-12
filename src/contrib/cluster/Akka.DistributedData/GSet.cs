//-----------------------------------------------------------------------
// <copyright file="GSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    /// TBD
    /// </summary>
    internal interface IGSet
    {
        /// <summary>
        /// TBD
        /// </summary>
        IImmutableSet<object> Elements { get; }
    }

    /// <summary>
    /// TBD
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
    public sealed class GSet<T> : FastMerge<GSet<T>>, IReplicatedDataSerialization, IGSet, IEquatable<GSet<T>>, IEnumerable<T>
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
        public GSet(IImmutableSet<T> elements)
        {
            Elements = elements;
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
        public GSet<T> Add(T element) => AssignAncestor(new GSet<T>(Elements.Add(element)));

        IImmutableSet<object> IGSet.Elements => Elements.Cast<object>().ToImmutableHashSet();

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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerator<T> GetEnumerator() => Elements.GetEnumerator();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is GSet<T> && Equals((GSet<T>) obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode() => Elements.GetHashCode();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            var sb = new StringBuilder("GSet(");
            sb.AppendJoin(", ", Elements);

            return sb.ToString();
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal interface IGSetKey
    { }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class GSetKey<T> : Key<GSet<T>>, IKeyWithGenericType, IGSetKey, IReplicatedDataSerialization
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        public GSetKey(string id)
            : base(id)
        {
            Type = typeof(T);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Type Type { get; }
    }
}