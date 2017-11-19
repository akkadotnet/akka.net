//-----------------------------------------------------------------------
// <copyright file="DDataShardCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster.Sharding
{
    internal sealed class DDataShardCoordinator : ActorBase, IShardCoordinator
    {
        public PersistentShardCoordinator.State CurrentState { get; set; }
        public ClusterShardingSettings Settings { get; }
        public IShardAllocationStrategy AllocationStrategy { get; }
        public ICancelable RebalanceTask { get; }
        public Cluster Cluster { get; }
        IActorContext IShardCoordinator.Context => Context;
        IActorRef IShardCoordinator.Self => Self;
        IActorRef IShardCoordinator.Sender => Self;
        public ILoggingAdapter Log { get; }
        public ImmutableDictionary<string, ICancelable> UnAckedHostShards { get; set; } = ImmutableDictionary<string, ICancelable>.Empty;
        public ImmutableHashSet<string> RebalanceInProgress { get; set; } = ImmutableHashSet<string>.Empty;
        public ImmutableHashSet<IActorRef> GracefullShutdownInProgress { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public ImmutableHashSet<IActorRef> AliveRegions { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public ImmutableHashSet<IActorRef> RegionTerminationInProgress { get; set; } = ImmutableHashSet<IActorRef>.Empty;
        public TimeSpan RemovalMargin { get; }

        public DDataShardCoordinator()
        {
        }

        protected override bool Receive(object message)
        {
            throw new NotImplementedException();
        }
        public void Update<TEvent>(TEvent e, Action<TEvent> handler) where TEvent : PersistentShardCoordinator.IDomainEvent
        {
            throw new NotImplementedException();
        }

        public bool HasAllRegionsRegistered()
        {
            throw new NotImplementedException();
        }
    }
}