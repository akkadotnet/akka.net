//-----------------------------------------------------------------------
// <copyright file="ReadWriteAggregator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Util;

namespace Akka.DistributedData
{
    internal abstract class ReadWriteAggregator : ActorBase
    {
        protected sealed class SendToSecondary
        {
            public static readonly SendToSecondary Instance = new SendToSecondary();
            private SendToSecondary() { }
        }

        private const int MaxSecondaryNodes = 10;

        protected TimeSpan Timeout { get; }
        protected IImmutableSet<Address> Nodes { get; }

        private readonly ICancelable _sendToSecondarySchedule;
        private readonly ICancelable _timeoutSchedule;
        protected abstract int DoneWhenRemainingSize { get; }

        protected Lazy<Tuple<IImmutableSet<Address>, IImmutableSet<Address>>> PrimaryAndSecondaryNodes;

        protected IImmutableSet<Address> Remaining;

        protected ReadWriteAggregator(IImmutableSet<Address> nodes, TimeSpan timeout)
        {
            Timeout = timeout;
            Nodes = nodes;
            Remaining = Nodes;
            _sendToSecondarySchedule = Context.System.Scheduler.ScheduleTellOnceCancelable((int)Timeout.TotalMilliseconds / 5, Self, SendToSecondary.Instance, Self);
            _timeoutSchedule = Context.System.Scheduler.ScheduleTellOnceCancelable(Timeout, Self, ReceiveTimeout.Instance, Self);
            PrimaryAndSecondaryNodes = new Lazy<Tuple<IImmutableSet<Address>, IImmutableSet<Address>>>(() =>
            {
                var primarySize = Nodes.Count - DoneWhenRemainingSize;
                if(primarySize >= nodes.Count)
                {
                    return Tuple.Create(nodes, (IImmutableSet<Address>)ImmutableHashSet<Address>.Empty);
                }
                else
                {
                    var n = Nodes.OrderBy(x => ThreadLocalRandom.Current.Next()).ToArray();
                    var p = n.Take(primarySize).ToImmutableHashSet();
                    var s = n.Skip(primarySize).Take(MaxSecondaryNodes).ToImmutableHashSet();
                    return Tuple.Create((IImmutableSet<Address>)p, (IImmutableSet<Address>)s);
                }
            });
        }

        protected override void PostStop()
        {
            _sendToSecondarySchedule.Cancel();
            _timeoutSchedule.Cancel();
        }

        protected virtual ActorSelection Replica(Address address)
        {
            return Context.ActorSelection(Context.Parent.Path.ToStringWithAddress(address));
        }
    }
}
