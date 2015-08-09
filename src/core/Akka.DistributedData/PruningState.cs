using Akka.Actor;
using Akka.Cluster;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal interface IPruningPhase { }

    internal sealed class PruningInitialized : IPruningPhase
    {
        readonly IImmutableSet<Address> _seen;
        public IImmutableSet<Address> Seen
        {
            get { return _seen; }
        }

        public PruningInitialized(IImmutableSet<Address> seen)
        {
            _seen = seen;
        }
    }

    internal sealed class PruningPerformed : IPruningPhase
    {
        public static PruningPerformed Instance { get { return new PruningPerformed(); } }
    }

    internal sealed class PruningState
    {
        readonly UniqueAddress _owner;
        readonly IPruningPhase _phase;

        public UniqueAddress Owner
        {
            get { return _owner; }
        }

        public IPruningPhase Phase
        {
            get { return _phase; }
        }

        public PruningState(UniqueAddress owner, IPruningPhase phase)
        {
            _owner = owner;
            _phase = phase;
        }

        internal PruningState AddSeen(Address node)
        {
            if(_phase is PruningPerformed)
            {
                return this;
            }
            else if(_phase is PruningInitialized)
            {
                var p = (PruningInitialized)_phase;
                if(p.Seen.Contains(node) || _owner.Address == node)
                {
                    return this;
                }
                else
                {
                    return new PruningState(Owner, new PruningInitialized(p.Seen.Add(node)));
                }
            }
            else
            {
                throw new Exception("Invalid pruning phase provided");
            }
        }

        internal PruningState Merge(PruningState that)
        {
            if(this.Phase is PruningPerformed)
            {
                return this;
            }
            else if(that.Phase is PruningPerformed)
            {
                return that;
            }
            else if(this.Phase is PruningInitialized && that.Phase is PruningInitialized)
            {
                var p1 = (PruningInitialized)Phase;
                var p2 = (PruningInitialized)that.Phase;
                if(this.Owner == that.Owner)
                {
                    return new PruningState(Owner, new PruningInitialized(p1.Seen.Union(p2.Seen)));
                }
                else if(Member.AddressOrdering.Compare(this.Owner.Address, that.Owner.Address) > 0)
                {
                    return that;
                }
                else
                {
                    return this;
                }
            }
            else
            {
                throw new Exception("Invalid pruning state provided");
            }
        }
    }
}
