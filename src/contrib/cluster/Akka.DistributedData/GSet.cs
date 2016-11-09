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

namespace Akka.DistributedData
{
    internal interface IGSet
    {
        IImmutableSet<object> Elements { get; }
    }

    public static class GSet
    {
        public static GSet<T> Create<T>(params T[] elements) => new GSet<T>(ImmutableHashSet.Create(elements));

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
    [Serializable]
    public sealed class GSet<T> : FastMerge<GSet<T>>, IReplicatedDataSerialization, IGSet, IEquatable<GSet<T>>, IEnumerable<T>
    {
        public static readonly GSet<T> Empty = new GSet<T>();

        public IImmutableSet<T> Elements { get; }

        public GSet() : this(ImmutableHashSet<T>.Empty) { }

        public GSet(IImmutableSet<T> elements)
        {
            Elements = elements;
        }

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

        public bool Contains(T element) => Elements.Contains(element);

        public bool IsEmpty => Elements.Count == 0;

        public int Count => Elements.Count;

        public GSet<T> Add(T element) => AssignAncestor(new GSet<T>(Elements.Add(element)));

        IImmutableSet<object> IGSet.Elements => Elements.Cast<object>().ToImmutableHashSet();

        public bool Equals(GSet<T> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Elements.SetEquals(other.Elements);
        }

        public IEnumerator<T> GetEnumerator() => Elements.GetEnumerator();

        public override bool Equals(object obj) => obj is GSet<T> && Equals((GSet<T>) obj);

        public override int GetHashCode() => Elements.GetHashCode();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var sb = new StringBuilder("GSet(");
            foreach (var element in Elements)
            {
                sb.Append(element).Append(',');
            }
            sb.Append(")");

            return sb.ToString();
        }
    }

    internal interface IGSetKey
    { }

    public sealed class GSetKey<T> : Key<GSet<T>>, IKeyWithGenericType, IGSetKey, IReplicatedDataSerialization
    {
        public GSetKey(string id)
            : base(id)
        {
            Type = typeof(T);
        }

        public Type Type { get; }
    }
}
