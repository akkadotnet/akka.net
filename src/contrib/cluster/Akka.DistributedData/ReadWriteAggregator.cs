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
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal.Collections;

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
        protected IImmutableList<Address> Nodes { get; }
        protected IImmutableSet<Address> Unreachable { get; }
        protected IImmutableList<Address> Reachable { get; }
        protected bool Shuffle { get; }

        private readonly ICancelable _sendToSecondarySchedule;
        private readonly ICancelable _timeoutSchedule;

        private ILoggingAdapter _log;

        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected abstract int DoneWhenRemainingSize { get; }

        private readonly Lazy<(IImmutableList<Address>, IImmutableList<Address>)> _primaryAndSecondaryNodes;

        protected IImmutableList<Address> PrimaryNodes => _primaryAndSecondaryNodes.Value.Item1;
        protected IImmutableList<Address> SecondaryNodes => _primaryAndSecondaryNodes.Value.Item2;

        protected IImmutableSet<Address> Remaining;

        protected ReadWriteAggregator(IImmutableList<Address> nodes, IImmutableSet<Address> unreachable, TimeSpan timeout, bool shuffle)
        {
            Timeout = timeout;
            Nodes = nodes;
            Unreachable = unreachable;
            Shuffle = shuffle;
            Reachable = nodes.Except(unreachable).ToImmutableList();
            Remaining = Nodes.ToImmutableHashSet();
            _sendToSecondarySchedule = Context.System.Scheduler.ScheduleTellOnceCancelable((int)Timeout.TotalMilliseconds / 5, Self, SendToSecondary.Instance, Self);
            _timeoutSchedule = Context.System.Scheduler.ScheduleTellOnceCancelable(Timeout, Self, ReceiveTimeout.Instance, Self);
            _primaryAndSecondaryNodes = new Lazy<(IImmutableList<Address>, IImmutableList<Address>)>(() =>
            {
                var primarySize = Nodes.Count - DoneWhenRemainingSize;
                if (primarySize >= nodes.Count)
                {
                    return (nodes, ImmutableList<Address>.Empty);
                }
                else
                {
                    // Prefer to use reachable nodes over the unreachable nodes first.
                    // When RequiresCausalDeliveryOfDeltas (shuffle=false) use deterministic order to so that sequence numbers
                    // of subsequent updates are in sync on the destination nodes.
                    // The order is also kept when prefer-oldest is enabled.
                    var orderedNodes = Shuffle ?
                        Reachable.Shuffle().AddRange(unreachable.ToImmutableList().Shuffle())
                        :
                        Reachable.AddRange(Unreachable);

                    var p = orderedNodes.Take(primarySize).ToImmutableList();
                    var s = orderedNodes.Skip(primarySize).Take(MaxSecondaryNodes).ToImmutableList();
                    return (p, s.Take(MaxSecondaryNodes).ToImmutableList());
                }
            });
        }

        public static int CalculateMajority(int minCapacity, int numberOfNodes, int additional)
        {
            var majority = numberOfNodes / 2 + 1;
            return Math.Min(numberOfNodes, Math.Max(majority + additional, minCapacity));
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
