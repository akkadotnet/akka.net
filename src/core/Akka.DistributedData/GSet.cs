using Akka.DistributedData.Proto;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public sealed class GSet<T> : AbstractReplicatedData<GSet<T>>, IReplicatedDataSerialization
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
    }

    public sealed class GSetKey<T> : Key<GSet<T>>
    {
        public GSetKey(string id)
            : base(id)
        { }
    }
}
