//-----------------------------------------------------------------------
// <copyright file="ReadWriteAggregator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster;
using Akka.Event;
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
        protected IImmutableSet<UniqueAddress> Nodes { get; }
        protected IImmutableSet<UniqueAddress> Unreachable { get; }
        protected IImmutableSet<UniqueAddress> Reachable { get; }

        private readonly ICancelable _sendToSecondarySchedule;
        private readonly ICancelable _timeoutSchedule;

        private ILoggingAdapter _log;

        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected abstract int DoneWhenRemainingSize { get; }

        private readonly Lazy<(IImmutableSet<UniqueAddress>, IImmutableSet<UniqueAddress>)> _primaryAndSecondaryNodes;

        protected IImmutableSet<UniqueAddress> PrimaryNodes => _primaryAndSecondaryNodes.Value.Item1;
        protected IImmutableSet<UniqueAddress> SecondaryNodes => _primaryAndSecondaryNodes.Value.Item2;

        protected IImmutableSet<Address> Remaining;

        protected ReadWriteAggregator(IImmutableSet<UniqueAddress> nodes, IImmutableSet<UniqueAddress> unreachable, TimeSpan timeout)
        {
            Timeout = timeout;
            Nodes = nodes;
            Unreachable = unreachable;
            Reachable = nodes.Except(unreachable);
            Remaining = Nodes.Select(n => n.Address).ToImmutableHashSet();
            _sendToSecondarySchedule = Context.System.Scheduler.ScheduleTellOnceCancelable((int)Timeout.TotalMilliseconds / 5, Self, SendToSecondary.Instance, Self);
            _timeoutSchedule = Context.System.Scheduler.ScheduleTellOnceCancelable(Timeout, Self, ReceiveTimeout.Instance, Self);
            _primaryAndSecondaryNodes = new Lazy<(IImmutableSet<UniqueAddress>, IImmutableSet<UniqueAddress>)>(() =>
            {
                var primarySize = Nodes.Count - DoneWhenRemainingSize;
                if(primarySize >= Nodes.Count)
                {
                    return (Nodes, ImmutableHashSet<UniqueAddress>.Empty);
                }
                else
                {
                    var n = Nodes.OrderBy(x => ThreadLocalRandom.Current.Next()).ToArray();
                    var p = n.Take(primarySize).ToImmutableHashSet();
                    var s = n.Skip(primarySize).Take(MaxSecondaryNodes).ToImmutableHashSet();
                    return (p, s);
                }
            });
        }

        public static int CalculateMajorityWithMinCapacity(int minCapacity, int numberOfNodes)
        {
            if (numberOfNodes <= minCapacity) return numberOfNodes;
            
            return Math.Max(minCapacity, numberOfNodes / 2 + 1);
        }

        protected override void PostStop()
        {
            _sendToSecondarySchedule.Cancel();
            _timeoutSchedule.Cancel();
        }

        protected virtual ActorSelection Replica(UniqueAddress address)
        {
            return Context.ActorSelection(Context.Parent.Path.ToStringWithAddress(address.Address));
        }
    }
}
