using Akka.DistributedData.Proto;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal interface IGSet
    {
        IImmutableSet<object> Elements { get; }
    }

    public static class GSet
    {
        public static GSet<T> Create<T>(IImmutableSet<T> elements)
        {
            return new GSet<T>(elements);
        }
    }

    public sealed class GSet<T> : AbstractReplicatedData<GSet<T>>, IReplicatedDataSerialization, IGSet
    {
        readonly IImmutableSet<T> _elements;

        public IImmutableSet<T> Elements
        {
            get { return _elements; }
        }

        public GSet()
            : this(ImmutableHashSet<T>.Empty)
        { }

        public GSet(IImmutableSet<T> elements)
        {
            _elements = elements;
        }

        public override GSet<T> Merge(GSet<T> other)
        {
            return new GSet<T>(Elements.Union(other.Elements));
        }

        public bool Contains(T element)
        {
            return _elements.Contains(element);
        }

        public bool IsEmpty()
        {
            return _elements.Count == 0;
        }

        public int Count()
        {
            return _elements.Count;
        }

        public GSet<T> Add(T element)
        {
            return new GSet<T>(_elements.Add(element));
        }

        IImmutableSet<object> IGSet.Elements
        {
            get { return _elements.Cast<object>().ToImmutableHashSet(); }
        }

        public override bool Equals(object obj)
        {
            var other = obj as GSet<T>;
            if(other != null)
            {
                return other.Elements == Elements;
            }
            return false;
        }
    }

    internal interface IGSetKey
    { }

    public sealed class GSetKey<T> : Key<GSet<T>>, IKeyWithGenericType, IGSetKey, IReplicatedDataSerialization
    {
        readonly Type _type;

        public GSetKey(string id)
            : base(id)
        {
            _type = typeof(T);
        }

        public Type Type
        {
            get { return _type; }
        }
    }
}
