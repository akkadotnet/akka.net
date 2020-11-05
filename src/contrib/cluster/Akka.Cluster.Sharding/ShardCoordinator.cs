//-----------------------------------------------------------------------
// <copyright file="ShardCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    internal interface IShardCoordinator
    {
        PersistentShardCoordinator.State CurrentState { get; set; }
        ClusterShardingSettings Settings { get; }
        IShardAllocationStrategy AllocationStrategy { get; }
        IActorContext Context { get; }
        ICancelable RebalanceTask { get; }
        Cluster Cluster { get; }
        IActorRef Self { get; }
        IActorRef Sender { get; }
        ILoggingAdapter Log { get; }
        ImmutableDictionary<string, ICancelable> UnAckedHostShards { get; set; }
        ImmutableDictionary<string, ImmutableHashSet<IActorRef>> RebalanceInProgress { get; set; }
        // regions that have requested handoff, for graceful shutdown
        ImmutableHashSet<IActorRef> GracefullShutdownInProgress { get; set; }
        ImmutableHashSet<IActorRef> AliveRegions { get; set; }
        ImmutableHashSet<IActorRef> RegionTerminationInProgress { get; set; }
        TimeSpan RemovalMargin { get; }
        void Update<TEvent>(TEvent e, Action<TEvent> handler) where TEvent : PersistentShardCoordinator.IDomainEvent;
        bool HasAllRegionsRegistered();
    }

    internal static class ShardCoordinator
    {
        #region shared part

        internal static void Cancel<TCoordinator>(this TCoordinator coordinator) where TCoordinator : IShardCoordinator
        {
            coordinator.RebalanceTask.Cancel();
            coordinator.Cluster.Unsubscribe(coordinator.Self);
        }

        static bool IsMember<TCoordinator>(this TCoordinator coordinator, IActorRef region) where TCoordinator : IShardCoordinator
        {
            var addr = region.Path.Address;
            return addr == coordinator.Self.Path.Address || coordinator.Cluster.ReadView.Members.Any(m => m.Address == addr && m.Status == MemberStatus.Up);
        }

        internal static bool Active<TCoordinator>(this TCoordinator coordinator, object message) where TCoordinator : IShardCoordinator
        {
            switch (message)
            {
                case PersistentShardCoordinator.Register msg: HandleRegister(coordinator, msg); return true;
                case PersistentShardCoordinator.RegisterProxy msg: HandleRegisterProxy(coordinator, msg); return true;
                case PersistentShardCoordinator.GetShardHome msg:
                    {
                        if (!HandleGetShardHome(coordinator, msg))
                        {
                            var shard = msg.Shard;
                            // location not known, yet
                            var activeRegions = coordinator.CurrentState.Regions.RemoveRange(coordinator.GracefullShutdownInProgress);
                            if (activeRegions.Count != 0)
                            {
                                var getShardHomeSender = coordinator.Sender;
                                var regionTask = coordinator.AllocationStrategy.AllocateShard(getShardHomeSender, shard, activeRegions);

                                // if task completed immediately, just continue
                                if (regionTask.IsCompleted && !regionTask.IsFaulted)
                                    ContinueGetShardHome(coordinator, shard, regionTask.Result, getShardHomeSender);
                                else
                                    regionTask.PipeTo(coordinator.Self,
                                        success: region => new PersistentShardCoordinator.AllocateShardResult(shard, region, getShardHomeSender),
                                        failure: _ => new PersistentShardCoordinator.AllocateShardResult(shard, null, getShardHomeSender));
                            }
                        }
                        return true;
                    }
                case PersistentShardCoordinator.AllocateShardResult msg: HandleAllocateShardResult(coordinator, msg); return true;
                case PersistentShardCoordinator.ShardStarted msg: HandleShardStated(coordinator, msg); return true;
                case ResendShardHost msg: HandleResendShardHost(coordinator, msg); return true;
                case RebalanceTick _: HandleRebalanceTick(coordinator); return true;
                case PersistentShardCoordinator.RebalanceResult msg: ContinueRebalance(coordinator, msg.Shards); return true;
                case RebalanceDone msg: HandleRebalanceDone(coordinator, msg.Shard, msg.Ok); return true;
                case PersistentShardCoordinator.GracefulShutdownRequest msg: HandleGracefulShutdownRequest(coordinator, msg); return true;
                case GetClusterShardingStats msg: HandleGetClusterShardingStats(coordinator, msg); return true;
                case PersistentShardCoordinator.ShardHome _:
                    // On rebalance, we send ourselves a GetShardHome message to reallocate a
                    // shard. This receive handles the "response" from that message. i.e. Ignores it.
                    return true;
                case ClusterEvent.ClusterShuttingDown msg:
                    coordinator.Log.Debug("Shutting down shard coordinator");
                    // can't stop because supervisor will start it again,
                    // it will soon be stopped when singleton is stopped
                    coordinator.Context.Become(ShuttingDown);
                    return true;
                case GetCurrentRegions _:
                    var regions = coordinator.CurrentState.Regions.Keys
                        .Select(region => string.IsNullOrEmpty(region.Path.Address.Host) ? coordinator.Cluster.SelfAddress : region.Path.Address)
                        .ToImmutableHashSet();
                    coordinator.Sender.Tell(new CurrentRegions(regions));
                    return true;
                case ClusterEvent.CurrentClusterState _:
                    /* ignore */
                    return true;

                case Terminate _:
                    coordinator.Log.Debug("Received termination message");
                    coordinator.Context.Stop(coordinator.Self);
                    return true;
                default: return ReceiveTerminated(coordinator, message);
            }
        }

        private static void AllocateShardHomesForRememberEntities<TCoordinator>(this TCoordinator coordinator) where TCoordinator : IShardCoordinator
        {
            if (coordinator.Settings.RememberEntities && coordinator.CurrentState.UnallocatedShards.Count > 0)
            {
                foreach (var unallocatedShard in coordinator.CurrentState.UnallocatedShards)
                {
                    coordinator.Self.Tell(new PersistentShardCoordinator.GetShardHome(unallocatedShard));
                }
            }
        }

        private static void SendHostShardMessage<TCoordinator>(this TCoordinator coordinator, String shard, IActorRef region) where TCoordinator : IShardCoordinator
        {
            region.Tell(new PersistentShardCoordinator.HostShard(shard));
            var cancel = coordinator.Context.System.Scheduler.ScheduleTellOnceCancelable(
                coordinator.Settings.TunningParameters.ShardStartTimeout,
                coordinator.Self,
                new ResendShardHost(shard, region),
                coordinator.Self);
            coordinator.UnAckedHostShards = coordinator.UnAckedHostShards.SetItem(shard, cancel);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static void StateInitialized<TCoordinator>(this TCoordinator coordinator) where TCoordinator : IShardCoordinator
        {
            foreach (var entry in coordinator.CurrentState.Shards)
                SendHostShardMessage(coordinator, entry.Key, entry.Value);

            AllocateShardHomesForRememberEntities(coordinator);
        }

        internal static void WatchStateActors<TCoordinator>(this TCoordinator coordinator) where TCoordinator : IShardCoordinator
        {
            // Optimization:
            // Consider regions that don't belong to the current cluster to be terminated.
            // This is an optimization that makes it operational faster and reduces the
            // amount of lost messages during startup.
            var nodes = coordinator.Cluster.ReadView.Members.Select(x => x.Address).ToImmutableHashSet();

            foreach (var entry in coordinator.CurrentState.Regions)
            {
                var a = entry.Key.Path.Address;
                if (a.HasLocalScope || nodes.Contains(a))
                    coordinator.Context.Watch(entry.Key);
                else
                    RegionTerminated(coordinator, entry.Key);    // not part of the cluster
            }

            foreach (var proxy in coordinator.CurrentState.RegionProxies)
            {
                var a = proxy.Path.Address;
                if (a.HasLocalScope || nodes.Contains(a))
                    coordinator.Context.Watch(proxy);
                else
                    RegionProxyTerminated(coordinator, proxy);        // not part of the cluster
            }

            // Let the quick (those not involving failure detection) Terminated messages
            // be processed before starting to reply to GetShardHome.
            // This is an optimization that makes it operational faster and reduces the
            // amount of lost messages during startup.
            coordinator.Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(500), coordinator.Self, PersistentShardCoordinator.StateInitialized.Instance, ActorRefs.NoSender);
        }

        internal static bool ReceiveTerminated<TCoordinator>(this TCoordinator coordinator, object message) where TCoordinator : IShardCoordinator
        {
            switch (message)
            {
                case Terminated terminated:
                    var terminatedRef = terminated.ActorRef;
                    if (coordinator.CurrentState.Regions.ContainsKey(terminatedRef))
                    {
                        if (coordinator.RemovalMargin != TimeSpan.Zero && terminated.AddressTerminated &&
                            coordinator.AliveRegions.Contains(terminatedRef))
                        {
                            coordinator.Context.System.Scheduler.ScheduleTellOnce(coordinator.RemovalMargin, coordinator.Self,
                                new DelayedShardRegionTerminated(terminatedRef), coordinator.Self);
                            coordinator.RegionTerminationInProgress = coordinator.RegionTerminationInProgress.Add(terminatedRef);
                        }
                        else
                            RegionTerminated(coordinator, terminatedRef);
                    }
                    else if (coordinator.CurrentState.RegionProxies.Contains(terminatedRef))
                        RegionProxyTerminated(coordinator, terminatedRef);
                    return true;
                case DelayedShardRegionTerminated msg:
                    RegionTerminated(coordinator, msg.Region);
                    return true;
            }
            return false;
        }

        private static void HandleGracefulShutdownRequest<TCoordinator>(this TCoordinator coordinator, PersistentShardCoordinator.GracefulShutdownRequest request) where TCoordinator : IShardCoordinator
        {
            if (!coordinator.GracefullShutdownInProgress.Contains(request.ShardRegion))
            {
                if (coordinator.CurrentState.Regions.TryGetValue(request.ShardRegion, out var shards))
                {
                    coordinator.Log.Debug("Graceful shutdown of region [{0}] with shards [{1}]", request.ShardRegion, string.Join(", ", shards));
                    coordinator.GracefullShutdownInProgress = coordinator.GracefullShutdownInProgress.Add(request.ShardRegion);
                    ContinueRebalance(coordinator, shards.ToImmutableHashSet());
                }
                else
                {
                    coordinator.Log.Debug("Unknown region requested graceful shutdown [{0}]", request.ShardRegion);
                }
            }
        }

        private static void HandleRebalanceDone<TCoordinator>(this TCoordinator coordinator, string shard, bool ok) where TCoordinator : IShardCoordinator
        {
            if (ok)
                coordinator.Log.Debug("Rebalance shard [{0}] completed successfully", shard);
            else
                coordinator.Log.Warning("Rebalance shard [{0}] didn't complete within [{1}]", shard, coordinator.Settings.TunningParameters.HandOffTimeout);

            // The shard could have been removed by ShardRegionTerminated
            if (coordinator.CurrentState.Shards.TryGetValue(shard, out var region))
            {
                if (ok)
                    coordinator.Update(new PersistentShardCoordinator.ShardHomeDeallocated(shard), e =>
                    {
                        coordinator.CurrentState = coordinator.CurrentState.Updated(e);
                        coordinator.ClearRebalanceInProgress(shard);
                        AllocateShardHomesForRememberEntities(coordinator);
                    });
                else
                {
                    // rebalance not completed, graceful shutdown will be retried
                    coordinator.GracefullShutdownInProgress = coordinator.GracefullShutdownInProgress.Remove(region);
                    coordinator.ClearRebalanceInProgress(shard);
                }
            }
            else
            {
                coordinator.ClearRebalanceInProgress(shard);
            }
        }

        private static void ClearRebalanceInProgress<TCoordinator>(this TCoordinator coordinator, string shard) where TCoordinator : IShardCoordinator
        {
            if (coordinator.RebalanceInProgress.TryGetValue(shard, out var pendingGetShardHome))
            {
                var msg = new PersistentShardCoordinator.GetShardHome(shard);
                foreach (var sender in pendingGetShardHome)
                {
                    coordinator.Self.Tell(msg, sender);
                }
                coordinator.RebalanceInProgress = coordinator.RebalanceInProgress.Remove(shard);
            }
        }

        private static void DeferGetShardHomeRequest<TCoordinator>(this TCoordinator coordinator, string shard, IActorRef from) where TCoordinator : IShardCoordinator
        {
            coordinator.Log.Debug("GetShardHome [{0}] request from [{1}] deferred, because rebalance is in progress for this shard. It will be handled when rebalance is done.", shard, from);
            var pending = coordinator.RebalanceInProgress.TryGetValue(shard, out var prev)
                ? prev
                : ImmutableHashSet<IActorRef>.Empty;
            coordinator.RebalanceInProgress = coordinator.RebalanceInProgress.SetItem(shard, pending.Add(from));
        }

        private static void HandleRebalanceTick<TCoordinator>(this TCoordinator coordinator) where TCoordinator : IShardCoordinator
        {
            if (coordinator.CurrentState.Regions.Count != 0)
            {
                var shardsTask = coordinator.AllocationStrategy.Rebalance(coordinator.CurrentState.Regions, coordinator.RebalanceInProgress.Keys.ToImmutableHashSet());
                if (shardsTask.IsCompleted && !shardsTask.IsFaulted)
                    ContinueRebalance(coordinator, shardsTask.Result);
                else
                    shardsTask.ContinueWith(t => !(t.IsFaulted || t.IsCanceled)
                        ? new PersistentShardCoordinator.RebalanceResult(t.Result)
                        : new PersistentShardCoordinator.RebalanceResult(ImmutableHashSet<ShardId>.Empty), TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(coordinator.Self);
            }
        }

        private static void HandleResendShardHost<TCoordinator>(this TCoordinator coordinator, ResendShardHost resend) where TCoordinator : IShardCoordinator
        {
            if (coordinator.CurrentState.Shards.TryGetValue(resend.Shard, out var region) && region.Equals(resend.Region))
                SendHostShardMessage(coordinator, resend.Shard, region);
        }

        private static void HandleShardStated<TCoordinator>(this TCoordinator coordinator, PersistentShardCoordinator.ShardStarted message) where TCoordinator : IShardCoordinator
        {
            var shard = message.Shard;
            if (coordinator.UnAckedHostShards.TryGetValue(shard, out var cancel))
            {
                cancel.Cancel();
                coordinator.UnAckedHostShards = coordinator.UnAckedHostShards.Remove(shard);
            }
        }

        private static void HandleAllocateShardResult<TCoordinator>(this TCoordinator coordinator, PersistentShardCoordinator.AllocateShardResult allocateResult) where TCoordinator : IShardCoordinator
        {
            if (allocateResult.ShardRegion == null)
                coordinator.Log.Debug("Shard [{0}] allocation failed. It will be retried", allocateResult.Shard);
            else
                ContinueGetShardHome(coordinator, allocateResult.Shard, allocateResult.ShardRegion, allocateResult.GetShardHomeSender);
        }

        internal static bool HandleGetShardHome<TCoordinator>(this TCoordinator coordinator, PersistentShardCoordinator.GetShardHome getShardHome) where TCoordinator : IShardCoordinator
        {
            var shard = getShardHome.Shard;

            if (coordinator.RebalanceInProgress.ContainsKey(shard))
            {
                coordinator.DeferGetShardHomeRequest(shard, coordinator.Sender);
                return true;
            }
            else if (!coordinator.HasAllRegionsRegistered())
            {
                coordinator.Log.Debug("GetShardHome [{0}] request ignored, because not all regions have registered yet.", shard);
                return true;
            }
            else
            {
                if (coordinator.CurrentState.Shards.TryGetValue(shard, out var region))
                {
                    if (coordinator.RegionTerminationInProgress.Contains(region))
                        coordinator.Log.Debug("GetShardHome [{0}] request ignored, due to region [{1}] termination in progress.", shard, region);
                    else
                        coordinator.Sender.Tell(new PersistentShardCoordinator.ShardHome(shard, region));

                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        private static void RegionTerminated<TCoordinator>(this TCoordinator coordinator, IActorRef terminatedRef) where TCoordinator : IShardCoordinator
        {
            if (coordinator.CurrentState.Regions.TryGetValue(terminatedRef, out var shards))
            {
                coordinator.Log.Debug("ShardRegion terminated: [{0}]", terminatedRef);
                coordinator.RegionTerminationInProgress = coordinator.RegionTerminationInProgress.Add(terminatedRef);

                foreach (var shard in shards)
                    coordinator.Self.Tell(new PersistentShardCoordinator.GetShardHome(shard));

                coordinator.Update(new PersistentShardCoordinator.ShardRegionTerminated(terminatedRef), e =>
                {
                    coordinator.CurrentState = coordinator.CurrentState.Updated(e);
                    coordinator.GracefullShutdownInProgress = coordinator.GracefullShutdownInProgress.Remove(terminatedRef);
                    coordinator.RegionTerminationInProgress = coordinator.RegionTerminationInProgress.Remove(terminatedRef);
                    coordinator.AliveRegions = coordinator.AliveRegions.Remove(terminatedRef);
                    AllocateShardHomesForRememberEntities(coordinator);
                });
            }
        }

        private static void RegionProxyTerminated<TCoordinator>(this TCoordinator coordinator, IActorRef proxyRef) where TCoordinator : IShardCoordinator
        {
            if (coordinator.CurrentState.RegionProxies.Contains(proxyRef))
            {
                coordinator.Log.Debug("ShardRegion proxy terminated: [{0}]", proxyRef);
                coordinator.Update(new PersistentShardCoordinator.ShardRegionProxyTerminated(proxyRef), e => coordinator.CurrentState = coordinator.CurrentState.Updated(e));
            }
        }

        private static void HandleRegisterProxy<TCoordinator>(this TCoordinator coordinator, PersistentShardCoordinator.RegisterProxy registerProxy) where TCoordinator : IShardCoordinator
        {
            var proxy = registerProxy.ShardRegionProxy;
            coordinator.Log.Debug("ShardRegion proxy registered: [{0}]", proxy);
            if (coordinator.CurrentState.RegionProxies.Contains(proxy))
                proxy.Tell(new PersistentShardCoordinator.RegisterAck(coordinator.Self));
            else
            {
                var context = coordinator.Context;
                var self = coordinator.Self;
                coordinator.Update(new PersistentShardCoordinator.ShardRegionProxyRegistered(proxy), e =>
                {
                    coordinator.CurrentState = coordinator.CurrentState.Updated(e);
                    context.Watch(proxy);
                    proxy.Tell(new PersistentShardCoordinator.RegisterAck(self));
                });
            }
        }

        private static void HandleRegister<TCoordinator>(this TCoordinator coordinator, PersistentShardCoordinator.Register message) where TCoordinator : IShardCoordinator
        {
            var region = message.ShardRegion;
            if (IsMember(coordinator, region))
            {
                coordinator.Log.Debug("ShardRegion registered: [{0}]", region);
                coordinator.AliveRegions = coordinator.AliveRegions.Add(region);

                if (coordinator.CurrentState.Regions.ContainsKey(region))
                {
                    region.Tell(new PersistentShardCoordinator.RegisterAck(coordinator.Self));
                    AllocateShardHomesForRememberEntities(coordinator);
                }
                else
                {
                    var context = coordinator.Context;
                    var self = coordinator.Self;

                    coordinator.GracefullShutdownInProgress = coordinator.GracefullShutdownInProgress.Remove(region);
                    coordinator.Update(new PersistentShardCoordinator.ShardRegionRegistered(region), e =>
                    {
                        coordinator.CurrentState = coordinator.CurrentState.Updated(e);
                        context.Watch(region);
                        region.Tell(new PersistentShardCoordinator.RegisterAck(self));

                        AllocateShardHomesForRememberEntities(coordinator);
                    });
                }
            }
            else coordinator.Log.Debug("ShardRegion [{0}] was not registered since the coordinator currently does not know about a node of that region", region);
        }

        private static void HandleGetClusterShardingStats<TCoordinator>(this TCoordinator coordinator, GetClusterShardingStats message) where TCoordinator : IShardCoordinator
        {
            var sender = coordinator.Sender;
            Task.WhenAll(
                coordinator.AliveRegions.Select(regionActor => regionActor.Ask<ShardRegionStats>(GetShardRegionStats.Instance, message.Timeout).ContinueWith(r => (regionActor, r.Result), TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion))
                ).ContinueWith(allRegionStats =>
                {
                    if (allRegionStats.IsCanceled)
                        return new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty);

                    if (allRegionStats.IsFaulted)
                        throw allRegionStats.Exception; //TODO check if this is the right way

                    var regions = allRegionStats.Result.ToImmutableDictionary(i =>
                    {
                        Address regionAddress = i.Item1.Path.Address;
                        Address address = (regionAddress.HasLocalScope && regionAddress.System == coordinator.Cluster.SelfAddress.System) ? coordinator.Cluster.SelfAddress : regionAddress;
                        return address;
                    }, j => j.Item2);

                    return new ClusterShardingStats(regions);
                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(sender);
        }

        private static bool ShuttingDown(object message)
        {
            // ignore all
            return true;
        }

        private static void ContinueRebalance<TCoordinator>(this TCoordinator coordinator, IImmutableSet<ShardId> shards) where TCoordinator : IShardCoordinator
        {
            if (coordinator.Log.IsInfoEnabled && (shards.Count > 0 || !coordinator.RebalanceInProgress.IsEmpty))
            {
                coordinator.Log.Info("Starting rebalance for shards [{0}]. Current shards rebalancing: [{1}]",
                    string.Join(",", shards),
                    string.Join(",", coordinator.RebalanceInProgress.Keys));
            }

            foreach (var shard in shards)
            {
                if (!coordinator.RebalanceInProgress.ContainsKey(shard))
                {
                    if (coordinator.CurrentState.Shards.TryGetValue(shard, out var rebalanceFromRegion))
                    {
                        coordinator.RebalanceInProgress = coordinator.RebalanceInProgress.SetItem(shard, ImmutableHashSet<IActorRef>.Empty);
                        coordinator.Log.Debug("Rebalance shard [{0}] from [{1}]", shard, rebalanceFromRegion);

                        var regions = coordinator.CurrentState.Regions.Keys.Union(coordinator.CurrentState.RegionProxies);
                        coordinator.Context.ActorOf(RebalanceWorker.Props(shard, rebalanceFromRegion, coordinator.Settings.TunningParameters.HandOffTimeout, regions)
                            .WithDispatcher(coordinator.Context.Props.Dispatcher));
                    }
                    else
                        coordinator.Log.Debug("Rebalance of non-existing shard [{0}] is ignored", shard);
                }
            }
        }

        private static void ContinueGetShardHome<TCoordinator>(this TCoordinator coordinator, string shard, IActorRef region, IActorRef getShardHomeSender) where TCoordinator : IShardCoordinator
        {
            if (!coordinator.RebalanceInProgress.ContainsKey(shard))
            {
                if (coordinator.CurrentState.Shards.TryGetValue(shard, out var aref))
                {
                    getShardHomeSender.Tell(new PersistentShardCoordinator.ShardHome(shard, aref));
                }
                else
                {
                    if (coordinator.CurrentState.Regions.ContainsKey(region) && !coordinator.GracefullShutdownInProgress.Contains(region))
                    {
                        coordinator.Update(new PersistentShardCoordinator.ShardHomeAllocated(shard, region), e =>
                        {
                            coordinator.CurrentState = coordinator.CurrentState.Updated(e);
                            coordinator.Log.Debug("Shard [{0}] allocated at [{1}]", e.Shard, e.Region);

                            SendHostShardMessage(coordinator, e.Shard, e.Region);
                            getShardHomeSender.Tell(new PersistentShardCoordinator.ShardHome(e.Shard, e.Region));
                        });
                    }
                    else
                        coordinator.Log.Debug("Allocated region {0} for shard [{1}] is not (any longer) one of the registered regions", region, shard);
                }
            }
            else
            {
                coordinator.DeferGetShardHomeRequest(shard, getShardHomeSender);
            }
        }

        #endregion

    }
}
