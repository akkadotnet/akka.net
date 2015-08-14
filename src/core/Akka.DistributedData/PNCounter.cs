using Akka.Cluster;
using Akka.DistributedData.Proto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public sealed class PNCounter : AbstractReplicatedData<PNCounter>, IRemovedNodePruning<PNCounter>, IReplicatedDataSerialization
    {
        readonly GCounter _increments;
        readonly GCounter _decrements;

        public BigInteger Value
        {
            get { return _increments.Value - _decrements.Value; }
        }

        public GCounter Increments
        {
            get { return _increments; }
        }

        public GCounter Decrements
        {
            get { return _decrements; }
        }

        public PNCounter()
            : this(GCounter.Empty, GCounter.Empty)
        { }

        public PNCounter(GCounter increments, GCounter decrements)
        {
            _increments = increments;
            _decrements = decrements;
        }

        public PNCounter Increment(Cluster.Cluster node, long delta = 1)
        {
            return Increment(node.SelfUniqueAddress, new BigInteger(delta));
        }

        public PNCounter Decrement(Cluster.Cluster node, long delta = 1)
        {
            return Decrement(node.SelfUniqueAddress, delta);
        }

        public PNCounter Increment(UniqueAddress address, long delta = 1)
        {
            return Increment(address, new BigInteger(delta));
        }

        public PNCounter Decrement(UniqueAddress address, long delta = 1)
        {
            return Decrement(address, new BigInteger(delta));
        }

        public PNCounter Increment(UniqueAddress address, BigInteger delta)
        {
            return Change(address, delta);
        }

        public PNCounter Decrement(UniqueAddress address, BigInteger delta)
        {
            return Change(address, -delta);
        }

        private PNCounter Change(UniqueAddress key, BigInteger delta)
        {
            if(delta > 0)
            {
                return new PNCounter(_increments.Increment(key, delta), _decrements);
            }
            else if(delta < 0)
            {
                return new PNCounter(_increments, _decrements.Increment(key, -delta));
            }
            else
            {
                return this;
            }
        }

        public override PNCounter Merge(PNCounter other)
        {
            return new PNCounter(_increments.Merge(other._increments), _decrements.Merge(other._decrements));
        }

        public bool NeedPruningFrom(Cluster.UniqueAddress removedNode)
        {
            return _increments.NeedPruningFrom(removedNode) || _decrements.NeedPruningFrom(removedNode);
        }

        public PNCounter Prune(Cluster.UniqueAddress removedNode, Cluster.UniqueAddress collapseInto)
        {
            return new PNCounter(_increments.Prune(removedNode, collapseInto), _decrements.Prune(removedNode, collapseInto));
        }

        public PNCounter PruningCleanup(Cluster.UniqueAddress removedNode)
        {
            return new PNCounter(_increments.PruningCleanup(removedNode), _decrements.PruningCleanup(removedNode));
        }

        public override string ToString()
        {
            return String.Format("PNCounter({0})", Value);
        }

        public override bool Equals(object obj)
        {
            var other = obj as PNCounter;
            if(other != null)
            {
                return other._increments == _increments && other._decrements == _decrements;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return _increments.GetHashCode() ^ _decrements.GetHashCode();
        }
    }

    public sealed class PNCounterKey : Key<PNCounter>
    {
        public PNCounterKey(string id)
            : base(id)
        { }
    }
}
