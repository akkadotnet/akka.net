//-----------------------------------------------------------------------
// <copyright file="PersistentShardCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    /// <summary>
    /// Singleton coordinator that decides where shards should be allocated.
    /// </summary>
    public partial class PersistentShardCoordinator : PersistentActor
    {
        #region State data type definition

        /// <summary>
        /// Persistent state of the event sourced PersistentShardCoordinator.
        /// </summary>
        [Serializable]
        protected internal sealed class State
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly State Empty = new State();

            /// <summary>
            /// Region for each shard.
            /// </summary>
            public readonly IImmutableDictionary<ShardId, IActorRef> Shards;

            /// <summary>
            /// Shards for each region.
            /// </summary>
            public readonly IImmutableDictionary<IActorRef, IImmutableList<ShardId>> Regions;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<IActorRef> RegionProxies;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<ShardId> UnallocatedShards;

            private State() : this(
                shards: ImmutableDictionary<ShardId, IActorRef>.Empty,
                regions: ImmutableDictionary<IActorRef, IImmutableList<ShardId>>.Empty,
                regionProxies: ImmutableHashSet<IActorRef>.Empty,
                unallocatedShards: ImmutableHashSet<ShardId>.Empty)
            { }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shards">TBD</param>
            /// <param name="regions">TBD</param>
            /// <param name="regionProxies">TBD</param>
            /// <param name="unallocatedShards">TBD</param>
            public State(
                IImmutableDictionary<ShardId, IActorRef> shards,
                IImmutableDictionary<IActorRef, IImmutableList<ShardId>> regions,
                IImmutableSet<IActorRef> regionProxies,
                IImmutableSet<ShardId> unallocatedShards)
            {
                Shards = shards;
                Regions = regions;
                RegionProxies = regionProxies;
                UnallocatedShards = unallocatedShards;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="e">TBD</param>
            /// <exception cref="ArgumentException">TBD</exception>
            /// <returns>TBD</returns>
            public State Updated(IDomainEvent e)
            {
                if (e is ShardRegionRegistered)
                {
                    var message = e as ShardRegionRegistered;
                    if (Regions.ContainsKey(message.Region))
                        throw new ArgumentException($"Region {message.Region} is already registered", nameof(e));

                    return Copy(regions: Regions.SetItem(message.Region, ImmutableList<ShardId>.Empty));
                }

                if (e is ShardRegionProxyRegistered)
                {
                    var message = e as ShardRegionProxyRegistered;
                    if (RegionProxies.Contains(message.RegionProxy))
                        throw new ArgumentException($"Region proxy {message.RegionProxy} is already registered", nameof(e));

                    return Copy(regionProxies: RegionProxies.Add(message.RegionProxy));
                }

                if (e is ShardRegionTerminated)
                {
                    var message = e as ShardRegionTerminated;
                    if (!Regions.TryGetValue(message.Region, out var shardRegions))
                        throw new ArgumentException($"Region {message.Region} not registered", nameof(e));

                    return Copy(
                        regions: Regions.Remove(message.Region),
                        shards: Shards.RemoveRange(shardRegions),
                        unallocatedShards: shardRegions.Aggregate(UnallocatedShards, (set, shard) => set.Add(shard)));
                }

                if (e is ShardRegionProxyTerminated)
                {
                    var message = e as ShardRegionProxyTerminated;
                    if (!RegionProxies.Contains(message.RegionProxy))
                        throw new ArgumentException($"Region proxy {message.RegionProxy} not registered", nameof(e));

                    return Copy(regionProxies: RegionProxies.Remove(message.RegionProxy));
                }

                if (e is ShardHomeAllocated)
                {
                    var message = e as ShardHomeAllocated;
                    if (!Regions.TryGetValue(message.Region, out var shardRegions))
                        throw new ArgumentException($"Region {message.Region} not registered", nameof(e));
                    if (Shards.ContainsKey(message.Shard))
                        throw new ArgumentException($"Shard {message.Shard} is already allocated", nameof(e));

                    return Copy(
                        shards: Shards.SetItem(message.Shard, message.Region),
                        regions: Regions.SetItem(message.Region, shardRegions.Add(message.Shard)),
                        unallocatedShards: UnallocatedShards.Remove(message.Shard));
                }

                if (e is ShardHomeDeallocated)
                {
                    var message = e as ShardHomeDeallocated;
                    if (!Shards.TryGetValue(message.Shard, out var region))
                        throw new ArgumentException($"Shard {message.Shard} not allocated", nameof(e));
                    if (!Regions.TryGetValue(region, out var shardRegions))
                        throw new ArgumentException($"Region {region} for shard {message.Shard} not registered", nameof(e));

                    return Copy(
                        shards: Shards.Remove(message.Shard),
                        regions: Regions.SetItem(region, shardRegions.Where(s => s != message.Shard).ToImmutableList()),
                        unallocatedShards: UnallocatedShards.Add(message.Shard));
                }

                return this;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shards">TBD</param>
            /// <param name="regions">TBD</param>
            /// <param name="regionProxies">TBD</param>
            /// <param name="unallocatedShards">TBD</param>
            /// <returns>TBD</returns>
            public State Copy(IImmutableDictionary<ShardId, IActorRef> shards = null,
                IImmutableDictionary<IActorRef, IImmutableList<ShardId>> regions = null,
                IImmutableSet<IActorRef> regionProxies = null,
                IImmutableSet<ShardId> unallocatedShards = null)
            {
                if (shards == null && regions == null && regionProxies == null && unallocatedShards == null) return this;

                return new State(shards ?? Shards, regions ?? Regions, regionProxies ?? RegionProxies, unallocatedShards ?? UnallocatedShards);
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as State;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shards.SequenceEqual(other.Shards)
                    && Regions.Keys.SequenceEqual(other.Regions.Keys)
                    && RegionProxies.SequenceEqual(other.RegionProxies)
                    && UnallocatedShards.SequenceEqual(other.UnallocatedShards);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 13;

                    foreach (var v in Shards)
                    {
                        hashCode = (hashCode * 397) ^ (v.Key?.GetHashCode() ?? 0);
                    }

                    foreach (var v in Regions)
                    {
                        hashCode = (hashCode * 397) ^ (v.Key?.GetHashCode() ?? 0);
                    }

                    foreach (var v in RegionProxies)
                    {
                        hashCode = (hashCode * 397) ^ (v?.GetHashCode() ?? 0);
                    }

                    foreach (var v in UnallocatedShards)
                    {
                        hashCode = (hashCode * 397) ^ (v?.GetHashCode() ?? 0);
                    }

                    return hashCode;
                }
            }

            #endregion
        }

        #endregion

        /// <summary>
        /// Factory method for the <see cref="Actor.Props"/> of the <see cref="PersistentShardCoordinator"/> actor.
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="allocationStrategy">TBD</param>
        /// <returns>TBD</returns>
        internal static Props Props(string typeName, ClusterShardingSettings settings, IShardAllocationStrategy allocationStrategy)
        {
            return Actor.Props.Create(() => new PersistentShardCoordinator(typeName, settings, allocationStrategy)).WithDeploy(Deploy.Local);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Cluster Cluster = Cluster.Get(Context.System);
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan DownRemovalMargin;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string TypeName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ClusterShardingSettings Settings;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IShardAllocationStrategy AllocationStrategy;

        private IImmutableDictionary<string, ICancelable> _unAckedHostShards = ImmutableDictionary<string, ICancelable>.Empty;
        private IImmutableSet<string> _rebalanceInProgress = ImmutableHashSet<string>.Empty;
        // regions that have requested handoff, for graceful shutdown
        private IImmutableSet<IActorRef> _gracefullShutdownInProgress = ImmutableHashSet<IActorRef>.Empty;
        private IImmutableSet<IActorRef> _aliveRegions = ImmutableHashSet<IActorRef>.Empty;
        private IImmutableSet<IActorRef> _regionTerminationInProgress = ImmutableHashSet<IActorRef>.Empty;

        private readonly ICancelable _rebalanceTask;

        private int _persistCount = 0;
        private State _currentState = State.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="allocationStrategy">TBD</param>
        public PersistentShardCoordinator(string typeName, ClusterShardingSettings settings, IShardAllocationStrategy allocationStrategy)
        {
            TypeName = typeName;
            Settings = settings;
            AllocationStrategy = allocationStrategy;
            DownRemovalMargin = Cluster.DowningProvider.DownRemovalMargin;

            JournalPluginId = Settings.JournalPluginId;
            SnapshotPluginId = Settings.SnapshotPluginId;

            _rebalanceTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(Settings.TunningParameters.RebalanceInterval, Settings.TunningParameters.RebalanceInterval, Self, RebalanceTick.Instance, Self);

            Cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, new[] { typeof(ClusterEvent.ClusterShuttingDown) });
        }

        private ILoggingAdapter _log;
        /// <summary>
        /// TBD
        /// </summary>
        public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }
        /// <summary>
        /// TBD
        /// </summary>
        protected State CurrentState { get { return _currentState; } }

        #region shared part

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            _rebalanceTask.Cancel();
            Cluster.Unsubscribe(Self);
        }

        private bool IsMember(IActorRef region)
        {
            var addr = region.Path.Address;
            return addr == Self.Path.Address || Cluster.ReadView.Members.Any(m => m.Address == addr && m.Status == MemberStatus.Up);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected bool Active(object message)
        {
            if (message is Register) HandleRegister(message as Register);
            else if (message is RegisterProxy) HandleRegisterProxy(message as RegisterProxy);
            else if (message is GetShardHome) HandleGetShardHome(message as GetShardHome);
            else if (message is AllocateShardResult) HandleAllocateShardResult(message as AllocateShardResult);
            else if (message is ShardStarted) HandleShardStated(message as ShardStarted);
            else if (message is ResendShardHost) HandleResendShardHost(message as ResendShardHost);
            else if (message is RebalanceTick) HandleRebalanceTick();
            else if (message is RebalanceResult) ContinueRebalance(((RebalanceResult)message).Shards.ToImmutableHashSet());
            else if (message is RebalanceDone) HandleRebalanceDone(message as RebalanceDone);
            else if (message is GracefulShutdownRequest) HandleGracefulShutdownRequest(message as GracefulShutdownRequest);
            else if (message is ShardHome)
            {
                // On rebalance, we send ourselves a GetShardHome message to reallocate a
                // shard. This receive handles the "response" from that message. i.e. Ignores it.
            }
            else if (message is ClusterEvent.ClusterShuttingDown)
            {
                Log.Debug("Shutting down shard coordinator");
                // can't stop because supervisor will start it again,
                // it will soon be stopped when singleton is stopped
                Context.Become(ShuttingDown);
            }
            else if (message is GetCurrentRegions)
            {
                var regions = _currentState.Regions.Keys
                    .Select(region => string.IsNullOrEmpty(region.Path.Address.Host) ? Cluster.SelfAddress : region.Path.Address)
                    .ToArray();
                Sender.Tell(new CurrentRegions(regions));
            }
            else if (message is ClusterEvent.CurrentClusterState)
            {
                /* ignore */
            }
            else return ReceiveTerminated(message);
            return true;
        }

        private void AllocateShardHomes()
        {
            foreach (var unallocatedShard in _currentState.UnallocatedShards)
            {
                Self.Tell(new GetShardHome(unallocatedShard));
            }
        }

        private void SendHostShardMessage(String shard, IActorRef region)
        {
            region.Tell(new HostShard(shard));
            var cancelable = new Cancelable(Context.System.Scheduler);
            Context.System.Scheduler.ScheduleTellOnce(Settings.TunningParameters.ShardStartTimeout, Self, new ResendShardHost(shard, region), Self, cancelable);
            _unAckedHostShards = _unAckedHostShards.SetItem(shard, cancelable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected void ApplyStateInitialized()
        {
            foreach (var entry in _currentState.Shards)
                SendHostShardMessage(entry.Key, entry.Value);

            AllocateShardHomes();
        }

        private void WatchStateActors()
        {
            // Optimization:
            // Consider regions that don't belong to the current cluster to be terminated.
            // This is an optimization that makes it operational faster and reduces the
            // amount of lost messages during startup.
            var nodes = Cluster.ReadView.Members.Select(x => x.Address).ToImmutableHashSet();

            foreach (var entry in _currentState.Regions)
            {
                var a = entry.Key.Path.Address;
                if ((string.IsNullOrEmpty(a.Host) && a.Port == null) || nodes.Contains(a))
                    Context.Watch(entry.Key);
                else
                    RegionTerminated(entry.Key);    // not part of the cluster
            }

            foreach (var proxy in _currentState.RegionProxies)
            {
                var a = proxy.Path.Address;
                if ((string.IsNullOrEmpty(a.Host) && a.Port == null) || nodes.Contains(a))
                    Context.Watch(proxy);
                else
                    RegionTerminated(proxy);        // not part of the cluster
            }

            // Let the quick (those not involving failure detection) Terminated messages
            // be processed before starting to reply to GetShardHome.
            // This is an optimization that makes it operational faster and reduces the
            // amount of lost messages during startup.
            Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(500), Self, StateInitialized.Instance, Self);
        }

        private bool ReceiveTerminated(object message)
        {
            if (message is Terminated)
            {
                var terminated = (Terminated)message;
                var terminatedRef = terminated.ActorRef;
                if (_currentState.Regions.ContainsKey(terminatedRef))
                {
                    if (DownRemovalMargin != TimeSpan.Zero && terminated.AddressTerminated && _aliveRegions.Contains(terminatedRef))
                    {
                        Context.System.Scheduler.ScheduleTellOnce(DownRemovalMargin, Self, new DelayedShardRegionTerminated(terminatedRef), Self);
                        _regionTerminationInProgress = _regionTerminationInProgress.Add(terminatedRef);
                    }
                    else
                        RegionTerminated(terminatedRef);
                }
                else if (_currentState.RegionProxies.Contains(terminatedRef))
                    RegionProxyTerminated(terminatedRef);
            }
            else if (message is DelayedShardRegionTerminated)
                RegionTerminated(((DelayedShardRegionTerminated)message).Region);
            else return false;
            return true;
        }

        private void HandleGracefulShutdownRequest(GracefulShutdownRequest request)
        {
            if (!_gracefullShutdownInProgress.Contains(request.ShardRegion))
            {
                if (_currentState.Regions.TryGetValue(request.ShardRegion, out var shards))
                {
                    Log.Debug("Graceful shutdown of region [{0}] with shards [{1}]", request.ShardRegion, string.Join(", ", shards));
                    _gracefullShutdownInProgress = _gracefullShutdownInProgress.Add(request.ShardRegion);
                    ContinueRebalance(shards.ToImmutableHashSet());
                }
            }
        }

        private void HandleRebalanceDone(RebalanceDone done)
        {
            _rebalanceInProgress = _rebalanceInProgress.Remove(done.Shard);
            Log.Debug("Rebalance shard [{0}] done [{1}]", done.Shard, done.Ok);

            // The shard could have been removed by ShardRegionTerminated
            if (_currentState.Shards.TryGetValue(done.Shard, out var region))
            {
                if (done.Ok)
                    Update(new ShardHomeDeallocated(done.Shard), e =>
                    {
                        _currentState = _currentState.Updated(e);
                        Log.Debug("Shard [{0}] deallocated", e.Shard);
                        AllocateShardHomes();
                    });
                else
                    // rebalance not completed, graceful shutdown will be retried
                    _gracefullShutdownInProgress = _gracefullShutdownInProgress.Remove(region);
            }
        }

        private void HandleRebalanceTick()
        {
            if (_currentState.Regions.Count != 0)
            {
                var shardsTask = AllocationStrategy.Rebalance(_currentState.Regions, _rebalanceInProgress);
                if (shardsTask.IsCompleted && !shardsTask.IsFaulted)
                    ContinueRebalance(shardsTask.Result);
                else
                    shardsTask.ContinueWith(t => !(t.IsFaulted || t.IsCanceled)
                        ? new RebalanceResult(t.Result)
                        : new RebalanceResult(Enumerable.Empty<ShardId>()))
                    .PipeTo(Self);
            }
        }

        private void HandleResendShardHost(ResendShardHost resend)
        {
            if (_currentState.Shards.TryGetValue(resend.Shard, out var region) && region.Equals(resend.Region))
                SendHostShardMessage(resend.Shard, region);
        }

        private void HandleShardStated(ShardStarted message)
        {
            var shard = message.Shard;
            if (_unAckedHostShards.TryGetValue(shard, out var cancel))
            {
                cancel.Cancel();
                _unAckedHostShards = _unAckedHostShards.Remove(shard);
            }
        }

        private void HandleAllocateShardResult(AllocateShardResult allocateResult)
        {
            if (allocateResult.ShardRegion == null)
                Log.Debug("Shard [{0}] allocation failed. It will be retried", allocateResult.Shard);
            else
                ContinueGetShardHome(allocateResult.Shard, allocateResult.ShardRegion, allocateResult.GetShardHomeSender);
        }

        private void HandleGetShardHome(GetShardHome getShardHome)
        {
            var shard = getShardHome.Shard;
            if (!_rebalanceInProgress.Contains(shard))
            {
                if (_currentState.Shards.TryGetValue(shard, out var region))
                {
                    if (_regionTerminationInProgress.Contains(region))
                        Log.Debug("GetShardHome [{0}] request ignored, due to region [{1}] termination in progress.", shard, region);
                    else
                        Sender.Tell(new ShardHome(shard, region));
                }
                else
                {
                    var activeRegions = _currentState.Regions.RemoveRange(_gracefullShutdownInProgress);
                    if (activeRegions.Count != 0)
                    {
                        var getShardHomeSender = Sender;
                        var regionTask = AllocationStrategy.AllocateShard(getShardHomeSender, shard, activeRegions);

                        // if task completed immediately, just continue
                        if (regionTask.IsCompleted && !regionTask.IsFaulted)
                            ContinueGetShardHome(shard, regionTask.Result, getShardHomeSender);
                        else
                            regionTask.ContinueWith(t => !(t.IsFaulted || t.IsCanceled)
                                ? new AllocateShardResult(shard, t.Result, getShardHomeSender)
                                : new AllocateShardResult(shard, null, getShardHomeSender))
                            .PipeTo(Self);
                    }
                }
            }
        }

        private void RegionTerminated(IActorRef terminatedRef)
        {
            if (_currentState.Regions.TryGetValue(terminatedRef, out var shards))
            {
                Log.Debug("Shard region terminated: [{0}]", terminatedRef);
                foreach (var shard in shards)
                    Self.Tell(new GetShardHome(shard));

                Update(new ShardRegionTerminated(terminatedRef), e =>
                {
                    _currentState = _currentState.Updated(e);
                    _gracefullShutdownInProgress = _gracefullShutdownInProgress.Remove(terminatedRef);
                    _regionTerminationInProgress = _regionTerminationInProgress.Remove(terminatedRef);
                    AllocateShardHomes();
                });
            }
        }

        private void RegionProxyTerminated(IActorRef proxyRef)
        {
            if (_currentState.RegionProxies.Contains(proxyRef))
            {
                Log.Debug("ShardRegion proxy terminated: [{0}]", proxyRef);
                Update(new ShardRegionProxyTerminated(proxyRef), e => _currentState = _currentState.Updated(e));
            }
        }

        private void HandleRegisterProxy(RegisterProxy registerProxy)
        {
            var proxy = registerProxy.ShardRegionProxy;
            Log.Debug("Shard region proxy registered: [{0}]", proxy);
            if (_currentState.RegionProxies.Contains(proxy))
                Sender.Tell(new RegisterAck(Self));
            else
            {
                var context = Context;
                var self = Self;
                Update(new ShardRegionProxyRegistered(proxy), e =>
                {
                    _currentState = _currentState.Updated(e);
                    context.Watch(proxy);
                    proxy.Tell(new RegisterAck(self));
                });
            }
        }

        private void HandleRegister(Register message)
        {
            var region = message.ShardRegion;
            if (IsMember(region))
            {
                Log.Debug("Shard region registered: [{0}]", region);
                _aliveRegions = _aliveRegions.Add(region);

                if (_currentState.Regions.ContainsKey(region))
                    Sender.Tell(new RegisterAck(Self));
                else
                {
                    var context = Context;
                    var self = Self;

                    _gracefullShutdownInProgress = _gracefullShutdownInProgress.Remove(region);
                    Update(new ShardRegionRegistered(region), e =>
                    {
                        var isFirstRegion = _currentState.Regions.Count == 0;
                        _currentState = _currentState.Updated(e);
                        context.Watch(region);
                        region.Tell(new RegisterAck(self));

                        if (isFirstRegion) AllocateShardHomes();
                    });
                }
            }
            else Log.Debug("ShardRegion [{0}] was not registered since the coordinator currently does not know about a node of that region", region);
        }

        private void SaveSnapshotIfNeeded()
        {
            _persistCount++;
            if (_persistCount % Settings.TunningParameters.SnapshotAfter == 0)
            {
                Log.Debug("Saving snapshot, sequence number [{0}]", SnapshotSequenceNr);
                SaveSnapshot(_currentState);
            }
        }

        private bool ShuttingDown(object message)
        {
            // ignore all
            return true;
        }

        private void ContinueRebalance(IImmutableSet<ShardId> shards)
        {
            foreach (var shard in shards)
            {
                if (!_rebalanceInProgress.Contains(shard))
                {
                    if (_currentState.Shards.TryGetValue(shard, out var rebalanceFromRegion))
                    {
                        _rebalanceInProgress = _rebalanceInProgress.Add(shard);
                        Log.Debug("Rebalance shard [{0}] from [{1}]", shard, rebalanceFromRegion);

                        var regions = _currentState.Regions.Keys.Union(_currentState.RegionProxies);
                        Context.ActorOf(RebalanceWorker.Props(shard, rebalanceFromRegion, Settings.TunningParameters.HandOffTimeout, regions)
                            .WithDispatcher(Context.Props.Dispatcher));
                    }
                    else
                        Log.Debug("Rebalance of non-existing shard [{0}] is ignored", shard);
                }
            }
        }

        private void ContinueGetShardHome(string shard, IActorRef region, IActorRef getShardHomeSender)
        {
            if (!_rebalanceInProgress.Contains(shard))
            {
                if (_currentState.Shards.TryGetValue(shard, out var aref))
                    getShardHomeSender.Tell(new ShardHome(shard, aref));
                else
                {
                    if (_currentState.Regions.ContainsKey(region) && !_gracefullShutdownInProgress.Contains(region))
                    {
                        Update(new ShardHomeAllocated(shard, region), e =>
                        {
                            _currentState = _currentState.Updated(e);
                            Log.Debug("Shard [{0}] allocated at [{1}]", e.Shard, e.Region);

                            SendHostShardMessage(e.Shard, e.Region);
                            getShardHomeSender.Tell(new ShardHome(e.Shard, e.Region));
                        });
                    }
                    else
                        Log.Debug("Allocated region {0} for shard [{1}] is not (any longer) one of the registered regions", region, shard);
                }
            }
        }

        #endregion

        #region persistent part

        /// <summary>
        /// TBD
        /// </summary>
        public override String PersistenceId { get { return Self.Path.ToStringWithoutAddress(); } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool ReceiveRecover(Object message)
        {
            if (message is IDomainEvent)
            {
                var evt = message as IDomainEvent;
                Log.Debug("ReceiveRecover {0}", evt);

                if (message is ShardRegionRegistered) _currentState = _currentState.Updated(evt);
                else if (message is ShardRegionProxyRegistered) _currentState = _currentState.Updated(evt);
                else if (message is ShardRegionTerminated)
                {
                    var regionTerminated = (ShardRegionTerminated)message;
                    if (_currentState.Regions.ContainsKey(regionTerminated.Region))
                        _currentState = _currentState.Updated(evt);
                    else
                        Log.Debug("ShardRegionTerminated but region {0} was not registered", regionTerminated.Region);
                }
                else if (message is ShardRegionProxyTerminated)
                {
                    var proxyTerminated = (ShardRegionProxyTerminated)message;
                    if (_currentState.RegionProxies.Contains(proxyTerminated.RegionProxy))
                        _currentState = _currentState.Updated(evt);
                }
                else if (message is ShardHomeAllocated) _currentState = _currentState.Updated(evt);
                else if (message is ShardHomeDeallocated) _currentState = _currentState.Updated(evt);
                else return false;
                return true;
            }
            else if (message is SnapshotOffer)
            {
                var state = ((SnapshotOffer)message).Snapshot as State;
                if (state != null)
                {
                    Log.Debug("ReceiveRecover SnapshotOffer {0}", state);

                    // Old versions of the state object may not have unallocatedShard set,
                    // thus it will be null.
                    if (state.UnallocatedShards == null)
                        _currentState = state.Copy(unallocatedShards: ImmutableHashSet<ShardId>.Empty);
                    else
                        _currentState = state;

                    return true;
                }
            }
            else if (message is RecoveryCompleted)
            {
                WatchStateActors();
                return true;
            }
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool ReceiveCommand(object message)
        {
            return WaitingForStateInitialized(message);
        }

        private bool WaitingForStateInitialized(object message)
        {
            if (message is StateInitialized)
            {
                ApplyStateInitialized();
                Context.Become(msg => Active(msg) || HandleSnapshotResult(msg));
                return true;
            }
            else if (ReceiveTerminated(message)) return true;
            else return HandleSnapshotResult(message);
        }


        private bool HandleSnapshotResult(object message)
        {
            switch (message)
            {
                case SaveSnapshotSuccess m:
                    Log.Debug("Persistent snapshot saved successfully");
                    /*
                     * delete old events but keep the latest around because
                     *
                     * it's not safe to delete all events immediate because snapshots are typically stored with a weaker consistency
                     * level which means that a replay might "see" the deleted events before it sees the stored snapshot,
                     * i.e. it will use an older snapshot and then not replay the full sequence of events
                     *
                     * for debugging if something goes wrong in production it's very useful to be able to inspect the events
                     */
                    var deleteToSequenceNr = m.Metadata.SequenceNr - Settings.TunningParameters.KeepNrOfBatches * Settings.TunningParameters.SnapshotAfter;
                    if (deleteToSequenceNr > 0)
                    {
                        DeleteMessages(deleteToSequenceNr);
                    }
                    break;

                case SaveSnapshotFailure m:
                    Log.Warning("Persistent snapshot failure: {0}", m.Cause.Message);
                    break;
                case DeleteMessagesSuccess m:
                    Log.Debug("Persistent messages to {0} deleted successfully", m.ToSequenceNr);
                    DeleteSnapshots(new SnapshotSelectionCriteria(m.ToSequenceNr - 1));
                    break;
                case DeleteMessagesFailure m:
                    Log.Warning("Persistent messages to {0} deletion failure: {1}", m.ToSequenceNr, m.Cause.Message);
                    break;
                case DeleteSnapshotsSuccess m:
                    Log.Debug("Persistent snapshots matching {0} deleted successfully", m.Criteria);
                    break;
                case DeleteSnapshotsFailure m:
                    Log.Warning("Persistent snapshots matching {0} deletion failure: {1}", m.Criteria, m.Cause.Message);
                    break;
                default:
                    return false;
            }
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TEvent">TBD</typeparam>
        /// <param name="e">TBD</param>
        /// <param name="handler">TBD</param>
        /// <returns>TBD</returns>
        protected void Update<TEvent>(TEvent e, Action<TEvent> handler) where TEvent : IDomainEvent
        {
            SaveSnapshotIfNeeded();
            Persist(e, handler);
        }

        #endregion
    }
}