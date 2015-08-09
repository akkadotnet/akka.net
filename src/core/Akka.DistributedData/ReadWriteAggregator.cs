using Akka;
using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public abstract class ReadWriteAggregator : ActorBase
    {
        protected class SendToSecondary { }

        private static int MaxSecondaryNodes = 10;

        private ICancelable sendToSecondarySchedule;
        private ICancelable timeoutSchedule;
        private readonly IImmutableSet<Address> _nodes;
        private readonly Random _random = new Random();
        protected abstract TimeSpan Timeout { get; }
        protected abstract int DoneWhenRemainingSize { get; }

        Lazy<Tuple<IImmutableSet<Address>, IImmutableSet<Address>>> _primaryAndSecondaryNodes;

        protected IImmutableSet<Address> Nodes
        {
            get { return _nodes; }
        }

        public ReadWriteAggregator(IImmutableSet<Address> nodes)
        {
            _nodes = nodes;
            sendToSecondarySchedule = Context.System.Scheduler.ScheduleTellOnceCancelable((int)Timeout.TotalMilliseconds / 5, Self, new SendToSecondary(), Self);
            timeoutSchedule = Context.System.Scheduler.ScheduleTellOnceCancelable(Timeout, Self, ReceiveTimeout.Instance, Self);
            _primaryAndSecondaryNodes = new Lazy<Tuple<IImmutableSet<Address>, IImmutableSet<Address>>>(() =>
            {
                var primarySize = _nodes.Count - DoneWhenRemainingSize;
                if(primarySize >= nodes.Count)
                {
                    return Tuple.Create(nodes, (IImmutableSet<Address>)ImmutableHashSet<Address>.Empty);
                }
                else
                {
                    var n = _nodes.OrderBy(x => _random.Next());
                    var p = n.Take(primarySize);
                    var s = p.Skip(primarySize).Take(MaxSecondaryNodes);
                    return Tuple.Create((IImmutableSet<Address>)p.ToImmutableHashSet(), (IImmutableSet<Address>)s.ToImmutableHashSet());
                }
            });
        }

        protected override void PostStop()
        {
            sendToSecondarySchedule.Cancel();
            timeoutSchedule.Cancel();
        }

        protected ActorSelection Replica(Address address)
        {
            return Context.ActorSelection(Context.Parent.Path.ToStringWithAddress(address));
        }
    }
}
