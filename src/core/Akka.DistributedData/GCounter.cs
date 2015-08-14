using Akka.Cluster;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public sealed class GCounterKey : Key<GCounter>
    {
        public GCounterKey(string id)
            : base(id)
        { }
    }

    public sealed class GCounter : AbstractReplicatedData<GCounter>, IRemovedNodePruning<GCounter>
    {
        private static BigInteger Zero = new BigInteger(0);

        private readonly IImmutableDictionary<UniqueAddress, BigInteger> _state;

        public IImmutableDictionary<UniqueAddress, BigInteger> State
        {
            get { return _state; }
        }

        public static GCounter Empty
        {
            get { return new GCounter(); }
        }

        public BigInteger Value
        {
            get { return _state.Aggregate(Zero, (v, acc) => v + acc.Value); }
        }

        public GCounter()
            : this(ImmutableDictionary<UniqueAddress, BigInteger>.Empty)
        { }

        public GCounter(IImmutableDictionary<UniqueAddress, BigInteger> state)
        {
            _state = state;
        }

        public GCounter Increment(UniqueAddress node)
        {
            return Increment(node, new BigInteger(1));
        }

        public GCounter Increment(UniqueAddress node, long delta)
        {
            return Increment(node, new BigInteger(delta));
        }

        public GCounter Increment(UniqueAddress node, BigInteger delta)
        {
            if(delta < 0)
            {
                throw new ArgumentException("Can't decrement a GCounter");
            }
            if(delta == 0)
            {
                return this;
            }
            var currentNodeValue = _state.GetValueOrDefault(node);
            var updatedNodeValue = currentNodeValue + delta;
            var updatedState = _state.SetItem(node, updatedNodeValue);
            return new GCounter(updatedState);
        }

        public override GCounter Merge(GCounter other)
        {
            var merged = other._state;
            foreach(var kvp in _state)
            {
                var otherValue = merged.GetValueOrDefault(kvp.Key, Zero);
                if(kvp.Value > otherValue)
                {
                    merged = merged.SetItem(kvp.Key, kvp.Value);
                }
            }
            return new GCounter(merged);
        }

        public bool NeedPruningFrom(UniqueAddress removedNode)
        {
            return _state.ContainsKey(removedNode);
        }

        public GCounter Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            BigInteger prunedNodeValue;
            var contained = _state.TryGetValue(removedNode, out prunedNodeValue);
            if(contained)
            {
                return new GCounter(_state.Remove(removedNode)).Increment(collapseInto, prunedNodeValue);
            }
            else
            {
                return this;
            }
        }

        public GCounter PruningCleanup(UniqueAddress removedNode)
        {
            return new GCounter(_state.Remove(removedNode));
        }

        public override int GetHashCode()
        {
            return _state.GetHashCode();
        }

        public override string ToString()
        {
            return String.Format("GCounter({0})", Value);
        }

        public override bool Equals(object obj)
        {
            var other = obj as GCounter;
            if(other != null)
            {
                return Value == other.Value;
            }
            return false;
        }
    }
}
