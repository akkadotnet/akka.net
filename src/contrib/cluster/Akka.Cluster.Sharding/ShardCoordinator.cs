using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    /**
     * Interface of the pluggable shard allocation and rebalancing logic used by the [[ShardCoordinator]].
     *
     * Java implementations should extend [[AbstractShardAllocationStrategy]].
     */
    public interface IShardAllocationStrategy
    {
        /**
         * Invoked when the location of a new shard is to be decided.
         * @param requester actor reference to the [[ShardRegion]] that requested the location of the
         *   shard, can be returned if preference should be given to the node where the shard was first accessed
         * @param shardId the id of the shard to allocate
         * @param currentShardAllocations all actor refs to `ShardRegion` and their current allocated shards,
         *   in the order they were allocated
         * @return a `Future` of the actor ref of the [[ShardRegion]] that is to be responsible for the shard, must be one of
         *   the references included in the `currentShardAllocations` parameter
         */
        Task<IActorRef> AllocateShard(IActorRef requester, ShardId shardId, IDictionary<IActorRef, ShardId[]> currentShardAllocations);

        /**
         * Invoked periodically to decide which shards to rebalance to another location.
         * @param currentShardAllocations all actor refs to `ShardRegion` and their current allocated shards,
         *   in the order they were allocated
         * @param rebalanceInProgress set of shards that are currently being rebalanced, i.e.
         *   you should not include these in the returned set
         * @return a `Future` of the shards to be migrated, may be empty to skip rebalance in this round
         */
        Task<ISet<ShardId>> Rebalance(IDictionary<IActorRef, ShardId[]> currentShardAllocations, ISet<ShardId> rebalanceInProgress);
    }

    /**
     * The default implementation of [[ShardCoordinator.LeastShardAllocationStrategy]]
     * allocates new shards to the `ShardRegion` with least number of previously allocated shards.
     * It picks shards for rebalancing handoff from the `ShardRegion` with most number of previously allocated shards.
     * They will then be allocated to the `ShardRegion` with least number of previously allocated shards,
     * i.e. new members in the cluster. There is a configurable threshold of how large the difference
     * must be to begin the rebalancing. The number of ongoing rebalancing processes can be limited.
     */
    [Serializable]
    public class LeastShardAllocationStrategy : IShardAllocationStrategy
    {
        private readonly int _rebalanceThreshold;
        private readonly int _maxSimultaneousRebalance;

        public LeastShardAllocationStrategy(int rebalanceThreshold, int maxSimultaneousRebalance)
        {
            _rebalanceThreshold = rebalanceThreshold;
            _maxSimultaneousRebalance = maxSimultaneousRebalance;
        }

        public Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IDictionary<IActorRef, ShardId[]> currentShardAllocations)
        {
            var min = GetMinBy(currentShardAllocations, kv => kv.Value.Length);
            return Task.FromResult(min.Key);
        }

        public Task<ISet<ShardId>> Rebalance(IDictionary<IActorRef, ShardId[]> currentShardAllocations, ISet<ShardId> rebalanceInProgress)
        {
            if (rebalanceInProgress.Count < _maxSimultaneousRebalance)
            {
                var leastShardsRegion = GetMinBy(currentShardAllocations, kv => kv.Value.Length);
                var shards =
                    currentShardAllocations.Select(kv => kv.Value.Where(s => !rebalanceInProgress.Contains(s)).ToArray());
                var mostShards = GetMaxBy(shards, x => x.Length);

                if (mostShards.Length - leastShardsRegion.Value.Length >= _rebalanceThreshold)
                {
                    return Task.FromResult(new HashSet<ShardId> { mostShards.First() } as ISet<ShardId>);
                }
            }

            return Task.FromResult(new HashSet<ShardId>() as ISet<ShardId>);
        }

        private static T GetMinBy<T>(IEnumerable<T> collection, Func<T, int> extractor)
        {
            var minSize = int.MaxValue;
            var result = default(T);
            foreach (var value in collection)
            {
                var x = extractor(value);
                if (x < minSize)
                {
                    minSize = x;
                    result = value;
                }
            }
            return result;
        }

        private static T GetMaxBy<T>(IEnumerable<T> collection, Func<T, int> extractor)
        {
            var minSize = int.MinValue;
            var result = default(T);
            foreach (var value in collection)
            {
                var x = extractor(value);
                if (x > minSize)
                {
                    minSize = x;
                    result = value;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Singleton coordinator that decides where shards should be allocated.
    /// </summary>
    public class ShardCoordinator : PersistentActor
    {
        private readonly IShardAllocationStrategy _allocationStrategy;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private static readonly object RebalanceTick = new object();
        private readonly ICancelable _rebalanceTask;
        private State _persistentState;
        private readonly Dictionary<string, ICancelable> _unAckedHostShards;
        private readonly HashSet<string> _rebalanceInProgress;
        private string _typeName;
        private readonly ClusterShardingSettings _settings;
        private readonly Cluster _cluster;
        private readonly TimeSpan _removalMargin;
        private readonly ISet<IActorRef> _gracefullShutdownInProgress;
        private readonly ISet<IActorRef> _aliveRegions;
        private int _persistCount;

        public ShardCoordinator(string typeName, ClusterShardingSettings settings, IShardAllocationStrategy allocationStrategy)
        {
            _cluster = Cluster.Get(Context.System);

            _typeName = typeName;
            _settings = settings;
            _allocationStrategy = allocationStrategy;
            _removalMargin = _cluster.Settings.DownRemovalMargin;

            JournalPluginId = _settings.JournalPluginId;
            SnapshotPluginId = _settings.SnapshotPluginId;

            _persistentState = State.Empty;                              // = State.empty
            _rebalanceInProgress = new HashSet<String>();                  //  Set.empty[ShardId]
            _unAckedHostShards = new Dictionary<string, ICancelable>();     // Map.empty[ShardId, Cancellable]

            // regions that have requested handoff, for graceful shutdown
            _gracefullShutdownInProgress = new HashSet<IActorRef>();
            _aliveRegions = new HashSet<IActorRef>();
            _persistCount = 0;

            _rebalanceTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.TunningParameters.RebalanceInterval, _settings.TunningParameters.RebalanceInterval, Self, RebalanceTick, Self);
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.ClusterShuttingDown) });
        }

        public override String PersistenceId { get { return Self.Path.ToStringWithoutAddress(); } }

        protected override void PostStop()
        {
            base.PostStop();
            _rebalanceTask.Cancel();
            Cluster.Get(Context.System).Unsubscribe(Self);
        }

        protected override bool ReceiveRecover(Object message)
        {
            if (message is IDomainEvent)
            {
                var evt = message as IDomainEvent;
                _log.Debug("receiveRecover {0}", evt);

                if (message is ShardRegionRegistered)
                {
                    _persistentState = _persistentState.Updated(evt);
                }
                else if (message is ShardRegionProxyRegistered)
                {
                    _persistentState = _persistentState.Updated(evt);
                }
                else if (message is ShardRegionTerminated)
                {
                    var regionTerminated = (ShardRegionTerminated)message;
                    if (_persistentState.Regions.ContainsKey(regionTerminated.Region))
                    {
                        _persistentState = _persistentState.Updated(evt);
                    }
                    else
                    {
                        _log.Debug("ShardRegionTerminated but region {0} was not registered", regionTerminated.Region);
                    }
                }
                else if (message is ShardRegionProxyTerminated)
                {
                    var proxyTerminated = (ShardRegionProxyTerminated)message;
                    if (_persistentState.RegionProxies.Contains(proxyTerminated.RegionProxy))
                        _persistentState = _persistentState.Updated(evt);
                }
                else if (message is ShardHomeAllocated)
                {
                    _persistentState = _persistentState.Updated(evt);
                }
                else if (message is ShardHomeDeallocated)
                {
                    _persistentState = _persistentState.Updated(evt);
                }
                else return false;
                return true;
            }
            else if (message is SnapshotOffer)
            {
                var state = ((SnapshotOffer)message).Snapshot as State;
                if (state != null)
                {
                    _log.Debug("ReceiveRecover SnapshotOffer {0}", state);

                    //Old versions of the state object may not have unallocatedShard set,
                    // thus it will be null.
                    if (state.UnallocatedShards == null)
                    {
                        _persistentState = _persistentState.Copy(unallocatedShards: new HashSet<ShardId>());
                    }
                    else
                    {
                        _persistentState = state;
                    }

                    return true;
                }
            }
            else if (message is RecoveryCompleted)
            {
                foreach (var regionProxy in _persistentState.RegionProxies)
                    Context.Watch(regionProxy);

                foreach (var region in _persistentState.Regions)
                    Context.Watch(region.Key);

                foreach (var shard in _persistentState.Shards)
                    SendHostShardMessage(shard.Key, shard.Value);

                AllocateShardHomes();
                return true;
            }
            return false; //TODO: ????
        }

        private void AllocateShardHomes()
        {
            foreach (var unallocatedShard in _persistentState.UnallocatedShards)
            {
                Self.Tell(new GetShardHome(unallocatedShard));
            }
        }

        private void SendHostShardMessage(String shard, IActorRef region)
        {
            region.Tell(new HostShard(shard));
            var cancelable = new Cancelable(Context.System.Scheduler);
            Context.System.Scheduler.ScheduleTellOnce(_settings.TunningParameters.ShardStartTimeout, Self, new ResendShardHost(shard, region), Self, cancelable);
            _unAckedHostShards.Add(shard, cancelable);
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message is Register)                            HandleRegister(message as Register);
            else if (message is RegisterProxy)                  HandleRegisterProxy(message as RegisterProxy);
            else if (message is Terminated)                     HandleTerminated(message as Terminated);
            else if (message is DelayedShardRegionTerminated)   RegionTerminated((message as DelayedShardRegionTerminated).Region);
            else if (message is GetShardHome)                   HandleGetShardHome(message as GetShardHome);
            else if (message is AllocateShardResult)            HandleAllocateShardResult(message as AllocateShardResult);
            else if (message is ShardStarted)                   HandleShardStated(message as ShardStarted);
            else if (message is ResendShardHost)                HandleResendShardHost(message as ResendShardHost);
            else if (message is RebalanceTick)                  HandleRebalanceTick();
            else if (message is RebalanceResult)
            {
                var result = (RebalanceResult) message;
                ContinueRebalance(result.Shards);
            }
            else if (message is RebalanceDone)                  HandleRebalanceDone(message as RebalanceDone);
            else if (message is GracefulShutdownRequest)        HandleGracefulShutdownRequest(message as GracefulShutdownRequest);
            else if (message is SaveSnapshotSuccess)
                _log.Debug("Persistent snapshot saved successfully");
            else if (message is SaveSnapshotFailure)
                _log.Warning("Persistent snapshot failure: " + ((SaveSnapshotFailure) message).Cause.Message);
            else if (message is ShardHome)
            {
                // On rebalance, we send ourselves a GetShardHome message to reallocate a
                // shard. This recieve handles the "response" from that message. i.e. Ingores it.
            }
            else if (message is ClusterEvent.ClusterShuttingDown)
            {
                _log.Debug("Shutting down shard coordinator");
                // can't stop because supervisor will start it again,
                // it will soon be stopped when singleton is stopped
                Context.Become(ShuttingDown);
            }
            else if (message is GetCurrentRegions)
            {
                var regions = _persistentState.Regions.Keys
                    .Select(region => string.IsNullOrEmpty(region.Path.Address.Host) ? _cluster.SelfAddress : region.Path.Address)
                    .ToArray();
                Sender.Tell(new CurrentRegions(regions));
            }
            else if (message is ClusterEvent.CurrentClusterState)
            {
                /* ignore */
            }
            else return false;
            return true;
        }

        private void HandleGracefulShutdownRequest(GracefulShutdownRequest request)
        {
            if (!_gracefullShutdownInProgress.Contains(request.ShardRegion))
            {
                ShardId[] shards;
                if (_persistentState.Regions.TryGetValue(request.ShardRegion, out shards))
                {
                    _log.Debug("Graceful shutdown of region [{0}] with shards [{1}]", request.ShardRegion, string.Join(", ", shards));
                    _gracefullShutdownInProgress.Add(request.ShardRegion);
                    ContinueRebalance(shards.ToArray());
                }
            }
        }

        private void HandleRebalanceDone(RebalanceDone done)
        {
            _rebalanceInProgress.Remove(done.Shard);
            _log.Debug("Rebalance shard [{0}] done [{1}]", done.Shard, done.Ok);

            // The shard could have been removed by ShardRegionTerminated
            if (_persistentState.Shards.ContainsKey(done.Shard))
            {
                if (done.Ok)
                {
                    SaveSnapshotIfNeeded();
                    Persist(new ShardHomeDeallocated(done.Shard), deallocated =>
                    {
                        _persistentState = _persistentState.Updated(deallocated);
                        _log.Debug("Shard [{0}] deallocated", deallocated.Shard);
                        AllocateShardHomes();
                    });
                }
                else // rebalance not completed, graceful shutdown will be retried
                    _gracefullShutdownInProgress.Remove(_persistentState.Shards[done.Shard]);
            }
        }

        private void HandleRebalanceTick()
        {
            if (_persistentState.Regions.Count != 0)
            {
                var shardsTask =
                    _allocationStrategy.Rebalance(new Dictionary<IActorRef, ShardId[]>(_persistentState.Regions),
                        new HashSet<string>(_rebalanceInProgress));
                if (shardsTask.IsCompleted && !shardsTask.IsFaulted)
                {
                    ContinueRebalance(shardsTask.Result);
                }

                shardsTask.ContinueWith(
                    t =>
                        !(t.IsFaulted || t.IsCanceled)
                            ? new RebalanceResult(t.Result)
                            : new RebalanceResult(Enumerable.Empty<ShardId>()),
                    TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(Self);
            }
        }

        private void HandleResendShardHost(ResendShardHost resend)
        {
            IActorRef region;
            if (_persistentState.Shards.TryGetValue(resend.Shard, out region) && region.Equals(resend.Region))
            {
                SendHostShardMessage(resend.Shard, region);
            }
        }

        private void HandleShardStated(ShardStarted message)
        {
            var shard = message.Shard;
            ICancelable cancel;
            if (_unAckedHostShards.TryGetValue(shard, out cancel))
            {
                cancel.Cancel();
                _unAckedHostShards.Remove(shard);
            }
        }

        private void HandleAllocateShardResult(AllocateShardResult allocateResult)
        {
            if (allocateResult.ShardRegion == null)
            {
                _log.Debug("Shard [{0}] allocation failed. It will be retried", allocateResult.Shard);
            }
            else
            {
                ContinueGetShardHome(allocateResult.Shard, allocateResult.ShardRegion,
                    allocateResult.GetShardHomeSender);
            }
        }

        private void HandleGetShardHome(GetShardHome getShardHome)
        {
            var shard = getShardHome.Shard;
            if (!_rebalanceInProgress.Contains(shard))
            {
                IActorRef region;
                if (_persistentState.Shards.TryGetValue(shard, out region))
                {
                    Sender.Tell(new ShardHome(shard, region));
                }
                else
                {
                    var activeRegions = _persistentState.Regions.Where(kv => !_gracefullShutdownInProgress.Contains(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                    if (activeRegions.Count != 0)
                    {
                        var getShardHomeSender = Sender;
                        var regionTask = _allocationStrategy.AllocateShard(getShardHomeSender, shard, activeRegions);

                        // if task completed immediately, just continue
                        if (regionTask.IsCompleted && !regionTask.IsFaulted)
                        {
                            ContinueGetShardHome(shard, regionTask.Result, getShardHomeSender);
                        }

                        regionTask.ContinueWith(t =>
                            !(t.IsFaulted || t.IsCanceled)
                                ? new AllocateShardResult(shard, t.Result, getShardHomeSender)
                                : new AllocateShardResult(shard, null, getShardHomeSender),
                            TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.ExecuteSynchronously)
                            .PipeTo(Self);
                    }
                }
            }
        }

        private void HandleTerminated(Terminated terminated)
        {
            if (_persistentState.Regions.ContainsKey(terminated.ActorRef))
            {
                if (_removalMargin != TimeSpan.Zero && terminated.AddressTerminated &&
                    _aliveRegions.Contains(terminated.ActorRef))
                    Context.System.Scheduler.ScheduleTellOnce(_removalMargin, Self,
                        new DelayedShardRegionTerminated(terminated.ActorRef), Self);
                else
                    RegionTerminated(terminated.ActorRef);
            }
            else if (_persistentState.RegionProxies.Contains(terminated.ActorRef))
            {
                _log.Debug("Shard region proxy terminated: [{0}]", terminated.ActorRef);
                Persist(new ShardRegionProxyTerminated(terminated.ActorRef),
                    proxyTerminated => { _persistentState = _persistentState.Updated(proxyTerminated); });
            }
        }

        private void RegionTerminated(IActorRef terminatedRef)
        {
            ShardId[] shards;
            if (_persistentState.Regions.TryGetValue(terminatedRef, out shards))
            {
                _log.Debug("Shard region terminated: [{0}]", terminatedRef);
                foreach (var shard in shards)
                {
                    Self.Tell(new GetShardHome(shard));
                }
                _gracefullShutdownInProgress.Remove(terminatedRef);
                SaveSnapshotIfNeeded();
                Persist(new ShardRegionTerminated(terminatedRef), e =>
                {
                    _persistentState = _persistentState.Updated(e);
                    AllocateShardHomes();
                });
            }
        }

        private void HandleRegisterProxy(RegisterProxy registerProxy)
        {
            var proxy = registerProxy.ShardRegionProxy;
            _log.Debug("Shard region proxy registered: [{0}]", proxy);
            if (_persistentState.RegionProxies.Contains(proxy))
            {
                Sender.Tell(new RegisterAck(Self));
            }
            else
            {
                SaveSnapshotIfNeeded();
                var self = Self;
                Persist(new ShardRegionProxyRegistered(proxy), registered =>
                {
                    _persistentState = _persistentState.Updated(registered);
                    Context.Watch(proxy);
                    Sender.Tell(new RegisterAck(self));
                });
            }
        }

        private void HandleRegister(Register message)
        {
            var region = message.ShardRegion;
            _log.Debug("Shard region registered: [{0}]", region);
            _aliveRegions.Add(region);
            if (_persistentState.Regions.ContainsKey(region))
            {
                Sender.Tell(new RegisterAck(Self));
            }
            else
            {
                _gracefullShutdownInProgress.Remove(region);
                SaveSnapshotIfNeeded();
                var self = Self;
                Persist(new ShardRegionRegistered(region), registered =>
                {
                    var isFirstRegion = _persistentState.Regions.Count == 0;
                    _persistentState = _persistentState.Updated(registered);
                    Context.Watch(region);
                    Sender.Tell(new RegisterAck(self));

                    if (isFirstRegion)
                    {
                        AllocateShardHomes();
                    }
                });
            }
        }

        private void SaveSnapshotIfNeeded()
        {
            _persistCount++;
            if (_persistCount % _settings.TunningParameters.SnapshotAfter == 0)
            {
                _log.Debug("Saving snapshot, sequence number [{0}]", SnapshotSequenceNr);
                SaveSnapshot(_persistentState);
            }
        }

        private bool ShuttingDown(object message)
        {
            // ignore all
            return true;
        }

        private void ContinueRebalance(IEnumerable<ShardId> shards)
        {
            foreach (var shard in shards)
            {
                if (!_rebalanceInProgress.Contains(shard))
                {
                    IActorRef rebalanceFromRegion;
                    if (_persistentState.Shards.TryGetValue(shard, out rebalanceFromRegion))
                    {
                        _rebalanceInProgress.Add(shard);
                        _log.Debug("Rebalance shard [{0}] from [{1}]", shard, rebalanceFromRegion);

                        var regions = new HashSet<IActorRef>(_persistentState.Regions.Keys.Union(_persistentState.RegionProxies));
                        Context.ActorOf(RebalanceWorker.Props(shard, rebalanceFromRegion, _settings.TunningParameters.HandOffTimeout, regions));
                    }
                    else
                    {
                        _log.Debug("Rebalance of non-existing shard [{0}] is ignored", shard);
                    }
                }
            }
        }

        private void ContinueGetShardHome(string shard, IActorRef region, IActorRef getShardHomeSender)
        {
            if (!_rebalanceInProgress.Contains(shard))
            {
                IActorRef aref;
                if (_persistentState.Shards.TryGetValue(shard, out aref))
                {
                    getShardHomeSender.Tell(new ShardHome(shard, aref));
                }
                else
                {
                    if (_persistentState.Regions.ContainsKey(region) && !_gracefullShutdownInProgress.Contains(region))
                    {
                        SaveSnapshotIfNeeded();
                        Persist(new ShardHomeAllocated(shard, region), allocated =>
                        {
                            _persistentState = _persistentState.Updated(allocated);
                            _log.Debug("Shard [{0}] allocated at [{1}]", allocated.Shard, allocated.Region);

                            SendHostShardMessage(allocated.Shard, allocated.Region);
                            getShardHomeSender.Tell(new ShardHome(allocated.Shard, allocated.Region));
                        });
                    }
                    else
                    {
                        _log.Debug("Allocated region {0} for shard [{1}] is not (any longer) one of the registered regions", region, shard);
                    }
                }
            }
        }

        public static Props Props(string typeName, ClusterShardingSettings settings, IShardAllocationStrategy allocationStrategy)
        {
            return Actor.Props.Create(() => new ShardCoordinator(typeName, settings, allocationStrategy)).WithDeploy(Deploy.Local);
        }
    }

    /**
     * Persistent state of the event sourced ShardCoordinator.
     */
    [Serializable]
    public class State
    {

        //TODO: maybe we should switch this from immutable to concurrent-collections based?
        public static readonly State Empty = new State();

        /// <summary>
        /// Region for each shard.
        /// </summary>
        public readonly IDictionary<String, IActorRef> Shards;

        /// <summary>
        /// Shards for each region.
        /// </summary>
        public readonly IDictionary<IActorRef, String[]> Regions;

        public readonly ISet<IActorRef> RegionProxies;
        public readonly ISet<String> UnallocatedShards;

        private State() : this(new Dictionary<string, IActorRef>(), new Dictionary<IActorRef, String[]>(), new HashSet<IActorRef>(), new HashSet<string>()) { }

        private State(IDictionary<String, IActorRef> shards, IDictionary<IActorRef, String[]> regions, ISet<IActorRef> regionProxies, ISet<String> unallocatedShards)
        {
            Shards = shards;
            Regions = regions;
            RegionProxies = regionProxies;
            UnallocatedShards = unallocatedShards;
        }

        public State Updated(IDomainEvent e)
        {
            if (e is ShardRegionRegistered)
            {
                var message = e as ShardRegionRegistered;
                if (Regions.ContainsKey(message.Region)) throw new ArgumentException(string.Format("Region {0} is already registered", message.Region));

                var regions = new Dictionary<IActorRef, String[]>(Regions);
                regions.Add(message.Region, new String[0]);
                return Copy(regions: regions);
            }
            else if (e is ShardRegionProxyRegistered)
            {
                var message = e as ShardRegionProxyRegistered;
                if (RegionProxies.Contains(message.RegionProxy)) throw new ArgumentException(string.Format("Region proxy {0} is already registered", message.RegionProxy));

                var proxies = new HashSet<IActorRef>(RegionProxies);
                proxies.Add(message.RegionProxy);
                return Copy(regionProxies: proxies);
            }
            else if (e is ShardRegionTerminated)
            {
                var message = e as ShardRegionTerminated;
                if (!Regions.ContainsKey(message.Region)) throw new ArgumentException(string.Format("Region {0} not registered", message.Region));

                var regions = new Dictionary<IActorRef, String[]>(Regions);
                regions.Remove(message.Region);
                var shards = new Dictionary<String, IActorRef>(Shards);
                var toUnalloc = Regions[message.Region];
                var unallocatedShards = new HashSet<String>(UnallocatedShards);
                foreach (var shard in toUnalloc)
                {
                    shards.Remove(shard);
                    unallocatedShards.Remove(shard);
                }

                return Copy(regions: regions, shards: shards, unallocatedShards: unallocatedShards);
            }
            else if (e is ShardRegionProxyTerminated)
            {
                var message = e as ShardRegionProxyTerminated;
                if (!RegionProxies.Contains(message.RegionProxy)) throw new ArgumentException(string.Format("Region proxy {0} not registered", message.RegionProxy));

                var proxies = new HashSet<IActorRef>(RegionProxies);
                proxies.Remove(message.RegionProxy);
                return Copy(regionProxies: proxies);
            }
            else if (e is ShardHomeAllocated)
            {
                var message = e as ShardHomeAllocated;
                if (!Regions.ContainsKey(message.Region)) throw new ArgumentException(string.Format("Region {0} not registered", message.Region));
                if (Shards.ContainsKey(message.Shard)) throw new ArgumentException(string.Format("Shard {0} is already allocated", message.Shard));

                var shards = new Dictionary<String, IActorRef>(Shards);
                shards.Add(message.Shard, message.Region);
                var regions = new Dictionary<IActorRef, String[]>(Regions);
                var region = regions[message.Region];
                // add shard at the end of the region list
                var newRegion = new String[region.Length + 1];
                Array.Copy(region, newRegion, region.Length);
                newRegion[region.Length] = message.Shard;
                var unallocatedShards = new HashSet<String>(UnallocatedShards);
                unallocatedShards.Remove(message.Shard);

                return Copy(shards: shards, regions: regions, unallocatedShards: unallocatedShards);
            }
            else if (e is ShardHomeDeallocated)
            {
                var message = e as ShardHomeDeallocated;
                if (!Shards.ContainsKey(message.Shard)) throw new ArgumentException(string.Format("Shard {0} not allocated", message.Shard));
                var region = Shards[message.Shard];
                if (!Regions.ContainsKey(region)) throw new ArgumentException(string.Format("Region {0} for shard {1} not registered", region, message.Shard));

                var shards = new Dictionary<String, IActorRef>(Shards);
                shards.Remove(message.Shard);
                var regions = new Dictionary<IActorRef, String[]>(Regions);
                var toFilter = Regions[region];
                regions[region] = toFilter.Where(x => x != message.Shard).ToArray();
                var unallocatedShards = new HashSet<String>(UnallocatedShards);
                unallocatedShards.Add(message.Shard);

                return Copy(shards: shards, regions: regions, unallocatedShards: unallocatedShards);
            }
            else return this;
        }

        public State Copy(IDictionary<String, IActorRef> shards = null,
            IDictionary<IActorRef, String[]> regions = null,
            ISet<IActorRef> regionProxies = null,
            ISet<String> unallocatedShards = null)
        {
            if (shards == null && regions == null && regionProxies == null && unallocatedShards == null) return this;

            return new State(shards ?? Shards, regions ?? Regions, regionProxies ?? RegionProxies, unallocatedShards ?? UnallocatedShards);
        }
    }

}