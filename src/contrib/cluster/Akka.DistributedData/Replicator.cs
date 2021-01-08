//-----------------------------------------------------------------------
// <copyright file="Replicator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.DistributedData.Internal;
using Akka.Serialization;
using Akka.Util;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using Akka.Cluster;
using Akka.DistributedData.Durable;
using Akka.Event;
using Google.Protobuf;
using Gossip = Akka.DistributedData.Internal.Gossip;
using Status = Akka.DistributedData.Internal.Status;

namespace Akka.DistributedData
{
    using Digest = ByteString;

    /// <summary>
    /// <para>
    /// A replicated in-memory data store supporting low latency and high availability
    /// requirements.
    /// 
    /// The <see cref="Replicator"/> actor takes care of direct replication and gossip based
    /// dissemination of Conflict Free Replicated Data Types (CRDTs) to replicas in the
    /// the cluster.
    /// The data types must be convergent CRDTs and implement <see cref="IReplicatedData{T}"/>, i.e.
    /// they provide a monotonic merge function and the state changes always converge.
    /// 
    /// You can use your own custom <see cref="IReplicatedData{T}"/> or <see cref="IDeltaReplicatedData{T,TDelta}"/> types,
    /// and several types are provided by this package, such as:
    /// </para>
    /// <list type="bullet">
    ///     <item>
    ///         <term>Counters</term>
    ///         <description><see cref="GCounter"/>, <see cref="PNCounter"/></description> 
    ///     </item>
    ///     <item>
    ///         <term>Registers</term>
    ///         <description><see cref="LWWRegister{T}"/>, <see cref="Flag"/></description>
    ///     </item>
    ///     <item>
    ///         <term>Sets</term>
    ///         <description><see cref="GSet{T}"/>, <see cref="ORSet{T}"/></description>
    ///     </item>
    ///     <item>
    ///         <term>Maps</term> 
    ///         <description><see cref="ORDictionary{TKey,TValue}"/>, <see cref="ORMultiValueDictionary{TKey,TValue}"/>, <see cref="LWWDictionary{TKey,TValue}"/>, <see cref="PNCounterDictionary{TKey}"/></description>
    ///     </item>
    /// </list>
    /// <para>
    /// For good introduction to the CRDT subject watch the
    /// <a href="http://www.ustream.tv/recorded/61448875">The Final Causal Frontier</a>
    /// and <a href="http://vimeo.com/43903960">Eventually Consistent Data Structures</a>
    /// talk by Sean Cribbs and and the
    /// <a href="http://research.microsoft.com/apps/video/dl.aspx?id=153540">talk by Mark Shapiro</a>
    /// and read the excellent paper <a href="http://hal.upmc.fr/docs/00/55/55/88/PDF/techreport.pdf">
    /// A comprehensive study of Convergent and Commutative Replicated Data Types</a>
    /// by Mark Shapiro et. al.
    /// </para>
    /// <para>
    /// The <see cref="Replicator"/> actor must be started on each node in the cluster, or group of
    /// nodes tagged with a specific role. It communicates with other <see cref="Replicator"/> instances
    /// with the same path (without address) that are running on other nodes . For convenience it
    /// can be used with the <see cref="DistributedData"/> extension but it can also be started as an ordinary
    /// actor using the <see cref="Props"/>. If it is started as an ordinary actor it is important
    /// that it is given the same name, started on same path, on all nodes.
    /// </para>
    /// <para>
    /// <a href="paper http://arxiv.org/abs/1603.01529">Delta State Replicated Data Types</a>
    /// is supported. delta-CRDT is a way to reduce the need for sending the full state
    /// for updates. For example adding element 'c' and 'd' to set {'a', 'b'} would
    /// result in sending the delta {'c', 'd'} and merge that with the state on the
    /// receiving side, resulting in set {'a', 'b', 'c', 'd'}.
    /// </para>
    /// <para>
    /// The protocol for replicating the deltas supports causal consistency if the data type
    /// is marked with <see cref="IRequireCausualDeliveryOfDeltas"/>. Otherwise it is only eventually
    /// consistent. Without causal consistency it means that if elements 'c' and 'd' are
    /// added in two separate <see cref="Update"/> operations these deltas may occasionally be propagated
    /// to nodes in different order than the causal order of the updates. For this example it
    /// can result in that set {'a', 'b', 'd'} can be seen before element 'c' is seen. Eventually
    /// it will be {'a', 'b', 'c', 'd'}.
    /// </para>
    /// <para>
    /// == Update ==
    /// 
    /// To modify and replicate a <see cref="IReplicatedData{T}"/> value you send a <see cref="Update"/> message
    /// to the local <see cref="Replicator"/>.
    /// The current data value for the `key` of the <see cref="Update"/> is passed as parameter to the `modify`
    /// function of the <see cref="Update"/>. The function is supposed to return the new value of the data, which
    /// will then be replicated according to the given consistency level.
    /// 
    /// The `modify` function is called by the `Replicator` actor and must therefore be a pure
    /// function that only uses the data parameter and stable fields from enclosing scope. It must
    /// for example not access `sender()` reference of an enclosing actor.
    /// 
    /// <see cref="Update"/> is intended to only be sent from an actor running in same local `ActorSystem` as
    /// the <see cref="Replicator"/>, because the `modify` function is typically not serializable.
    /// 
    /// You supply a write consistency level which has the following meaning:
    /// <list type="bullet">
    ///     <item>
    ///         <term><see cref="WriteLocal"/></term>
    ///         <description>
    ///             The value will immediately only be written to the local replica, and later disseminated with gossip.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="WriteTo"/></term>
    ///         <description>
    ///             The value will immediately be written to at least <see cref="WriteTo.Count"/> replicas, including the local replica.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="WriteMajority"/></term>
    ///         <description>
    ///             The value will immediately be written to a majority of replicas, i.e. at least `N/2 + 1` replicas, 
    ///             where N is the number of nodes in the cluster (or cluster role group).
    ///         </description>     
    ///     </item>
    ///     <item>
    ///         <term><see cref="WriteAll"/></term> 
    ///         <description>
    ///             The value will immediately be written to all nodes in the cluster (or all nodes in the cluster role group).
    ///         </description>
    ///     </item>
    /// </list>
    /// 
    /// As reply of the <see cref="Update"/> a <see cref="UpdateSuccess"/> is sent to the sender of the
    /// <see cref="Update"/> if the value was successfully replicated according to the supplied consistency
    /// level within the supplied timeout. Otherwise a <see cref="IUpdateFailure"/> subclass is
    /// sent back. Note that a <see cref="UpdateTimeout"/> reply does not mean that the update completely failed
    /// or was rolled back. It may still have been replicated to some nodes, and will eventually
    /// be replicated to all nodes with the gossip protocol.
    /// 
    /// You will always see your own writes. For example if you send two <see cref="Update"/> messages
    /// changing the value of the same `key`, the `modify` function of the second message will
    /// see the change that was performed by the first <see cref="Update"/> message.
    /// 
    /// In the <see cref="Update"/> message you can pass an optional request context, which the <see cref="Replicator"/>
    /// does not care about, but is included in the reply messages. This is a convenient
    /// way to pass contextual information (e.g. original sender) without having to use <see cref="Ask"/>
    /// or local correlation data structures.
    /// </para>
    /// <para>
    /// == Get ==
    /// 
    /// To retrieve the current value of a data you send <see cref="Get"/> message to the
    /// <see cref="Replicator"/>. You supply a consistency level which has the following meaning:
    /// <list type="bullet">
    ///     <item>
    ///         <term><see cref="ReadLocal"/></term> 
    ///         <description>The value will only be read from the local replica.</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="ReadFrom"/></term> 
    ///         <description>The value will be read and merged from <see cref="ReadFrom.N"/> replicas, including the local replica.</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="ReadMajority"/></term>
    ///         <description>
    ///             The value will be read and merged from a majority of replicas, i.e. at least `N/2 + 1` replicas, where N is the number of nodes in the cluster (or cluster role group).
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="ReadAll"/></term>
    ///         <description>The value will be read and merged from all nodes in the cluster (or all nodes in the cluster role group).</description>
    ///     </item>
    /// </list>
    /// 
    /// As reply of the <see cref="Get"/> a <see cref="GetSuccess"/> is sent to the sender of the
    /// <see cref="Get"/> if the value was successfully retrieved according to the supplied consistency
    /// level within the supplied timeout. Otherwise a <see cref="GetFailure"/> is sent.
    /// If the key does not exist the reply will be <see cref="NotFound"/>.
    /// 
    /// You will always read your own writes. For example if you send a <see cref="Update"/> message
    /// followed by a <see cref="Get"/> of the same `key` the <see cref="Get"/> will retrieve the change that was
    /// performed by the preceding <see cref="Update"/> message. However, the order of the reply messages are
    /// not defined, i.e. in the previous example you may receive the <see cref="GetSuccess"/> before
    /// the <see cref="UpdateSuccess"/>.
    /// 
    /// In the <see cref="Get"/> message you can pass an optional request context in the same way as for the
    /// <see cref="Update"/> message, described above. For example the original sender can be passed and replied
    /// to after receiving and transforming <see cref="GetSuccess"/>.
    /// </para>
    /// <para>
    /// == Subscribe ==
    /// 
    /// You may also register interest in change notifications by sending <see cref="Subscribe"/>
    /// message to the <see cref="Replicator"/>. It will send <see cref="Changed"/> messages to the registered
    /// subscriber when the data for the subscribed key is updated. Subscribers will be notified
    /// periodically with the configured `notify-subscribers-interval`, and it is also possible to
    /// send an explicit <see cref="FlushChanges"/> message to the <see cref="Replicator"/> to notify the subscribers
    /// immediately.
    /// 
    /// The subscriber is automatically removed if the subscriber is terminated. A subscriber can
    /// also be deregistered with the <see cref="Unsubscribe"/> message.
    /// </para>
    /// <para>
    /// == Delete ==
    /// 
    /// A data entry can be deleted by sending a <see cref="Delete"/> message to the local
    /// local <see cref="Replicator"/>. As reply of the <see cref="Delete"/> a <see cref="DeleteSuccess"/> is sent to
    /// the sender of the <see cref="Delete"/> if the value was successfully deleted according to the supplied
    /// consistency level within the supplied timeout. Otherwise a <see cref="ReplicationDeleteFailure"/>
    /// is sent. Note that <see cref="ReplicationDeleteFailure"/> does not mean that the delete completely failed or
    /// was rolled back. It may still have been replicated to some nodes, and may eventually be replicated
    /// to all nodes.
    /// 
    /// A deleted key cannot be reused again, but it is still recommended to delete unused
    /// data entries because that reduces the replication overhead when new nodes join the cluster.
    /// Subsequent <see cref="Delete"/>, <see cref="Update"/> and <see cref="Get"/> requests will be replied with <see cref="DataDeleted"/>.
    /// Subscribers will receive <see cref="Deleted"/>.
    /// 
    /// In the <see cref="Delete"/> message you can pass an optional request context in the same way as for the
    /// <see cref="Update"/> message, described above. For example the original sender can be passed and replied
    /// to after receiving and transforming <see cref="DeleteSuccess"/>.
    /// </para>
    /// <para>
    /// == CRDT Garbage ==
    /// 
    /// One thing that can be problematic with CRDTs is that some data types accumulate history (garbage).
    /// For example a <see cref="GCounter"/> keeps track of one counter per node. If a <see cref="GCounter"/> has been updated
    /// from one node it will associate the identifier of that node forever. That can become a problem
    /// for long running systems with many cluster nodes being added and removed. To solve this problem
    /// the <see cref="Replicator"/> performs pruning of data associated with nodes that have been removed from the
    /// cluster. Data types that need pruning have to implement <see cref="IRemovedNodePruning{T}"/>. The pruning consists
    /// of several steps:
    /// <list type="">
    ///     <item>When a node is removed from the cluster it is first important that all updates that were
    /// done by that node are disseminated to all other nodes. The pruning will not start before the
    /// <see cref="ReplicatorSettings.MaxPruningDissemination"/> duration has elapsed. The time measurement is stopped when any
    /// replica is unreachable, but it's still recommended to configure this with certain margin.
    /// It should be in the magnitude of minutes.</item>
    /// <item>The nodes are ordered by their address and the node ordered first is called leader.
    /// The leader initiates the pruning by adding a <see cref="PruningInitialized"/> marker in the data envelope.
    /// This is gossiped to all other nodes and they mark it as seen when they receive it.</item>
    /// <item>When the leader sees that all other nodes have seen the <see cref="PruningInitialized"/> marker
    /// the leader performs the pruning and changes the marker to <see cref="PruningPerformed"/> so that nobody
    /// else will redo the pruning. The data envelope with this pruning state is a CRDT itself.
    /// The pruning is typically performed by "moving" the part of the data associated with
    /// the removed node to the leader node. For example, a <see cref="GCounter"/> is a `Map` with the node as key
    /// and the counts done by that node as value. When pruning the value of the removed node is
    /// moved to the entry owned by the leader node. See <see cref="IRemovedNodePruning{T}.Prune"/>.</item>
    /// <item>Thereafter the data is always cleared from parts associated with the removed node so that
    /// it does not come back when merging. See <see cref="IRemovedNodePruning{T}.PruningCleanup"/></item>
    /// <item>After another `maxPruningDissemination` duration after pruning the last entry from the
    /// removed node the <see cref="PruningPerformed"/> markers in the data envelope are collapsed into a
    /// single tombstone entry, for efficiency. Clients may continue to use old data and therefore
    /// all data are always cleared from parts associated with tombstoned nodes. </item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class Replicator : ReceiveActor
    {
        public static Props Props(ReplicatorSettings settings) =>
            Actor.Props.Create(() => new Replicator(settings)).WithDeploy(Deploy.Local).WithDispatcher(settings.Dispatcher);

        private static readonly Digest DeletedDigest = ByteString.Empty;
        private static readonly Digest LazyDigest = ByteString.CopyFrom(new byte[] { 0 });
        private static readonly Digest NotFoundDigest = ByteString.CopyFrom(new byte[] { 255 });

        private static readonly DataEnvelope DeletedEnvelope = new DataEnvelope(DeletedData.Instance);

        private readonly ReplicatorSettings _settings;

        private readonly Cluster.Cluster _cluster;
        private readonly Address _selfAddress;
        private readonly UniqueAddress _selfUniqueAddress;

        private readonly ICancelable _gossipTask;
        private readonly ICancelable _notifyTask;
        private readonly ICancelable _pruningTask;
        private readonly ICancelable _clockTask;

        private readonly Serializer _serializer;
        private readonly long _maxPruningDisseminationNanos;

        /// <summary>
        /// Cluster nodes, doesn't contain selfAddress.
        /// </summary>
        private ImmutableHashSet<Address> _nodes = ImmutableHashSet<Address>.Empty;

        private ImmutableHashSet<Address> AllNodes => _nodes.Union(_weaklyUpNodes);

        /// <summary>
        /// Cluster weaklyUp nodes, doesn't contain selfAddress
        /// </summary>
        private ImmutableHashSet<Address> _weaklyUpNodes = ImmutableHashSet<Address>.Empty;

        private ImmutableDictionary<UniqueAddress, long> _removedNodes = ImmutableDictionary<UniqueAddress, long>.Empty;
        private ImmutableDictionary<UniqueAddress, long> _pruningPerformed = ImmutableDictionary<UniqueAddress, long>.Empty;
        private ImmutableHashSet<UniqueAddress> _tombstonedNodes = ImmutableHashSet<UniqueAddress>.Empty;

        /// <summary>
        /// All nodes sorted with the leader first
        /// </summary>
        private ImmutableSortedSet<Member> _leader = ImmutableSortedSet<Member>.Empty.WithComparer(Member.LeaderStatusOrdering);
        private bool IsLeader => !_leader.IsEmpty && _leader.First().Address == _selfAddress;

        /// <summary>
        /// For pruning timeouts are based on clock that is only increased when all nodes are reachable.
        /// </summary>
        private long _previousClockTime;
        private long _allReachableClockTime = 0;
        private ImmutableHashSet<Address> _unreachable = ImmutableHashSet<Address>.Empty;

        /// <summary>
        /// The actual data.
        /// </summary>
        private ImmutableDictionary<string, (DataEnvelope envelope, Digest digest)> _dataEntries = ImmutableDictionary<string, (DataEnvelope, Digest)>.Empty;

        /// <summary>
        /// Keys that have changed, Changed event published to subscribers on FlushChanges
        /// </summary>
        private ImmutableHashSet<string> _changed = ImmutableHashSet<string>.Empty;

        /// <summary>
        /// For splitting up gossip in chunks.
        /// </summary>
        private long _statusCount = 0;
        private int _statusTotChunks = 0;

        private readonly Dictionary<string, HashSet<IActorRef>> _subscribers = new Dictionary<string, HashSet<IActorRef>>();
        private readonly Dictionary<string, HashSet<IActorRef>> _newSubscribers = new Dictionary<string, HashSet<IActorRef>>();
        private ImmutableDictionary<string, IKey> _subscriptionKeys = ImmutableDictionary<string, IKey>.Empty;

        private readonly ILoggingAdapter _log;

        private readonly bool _hasDurableKeys;
        private readonly IImmutableSet<string> _durableKeys;
        private readonly IImmutableSet<string> _durableWildcards;
        private readonly IActorRef _durableStore;

        private readonly DeltaPropagationSelector _deltaPropagationSelector;
        private readonly ICancelable _deltaPropagationTask;
        private readonly int _maxDeltaSize;

        public Replicator(ReplicatorSettings settings)
        {
            _settings = settings;
            _cluster = Cluster.Cluster.Get(Context.System);
            _selfAddress = _cluster.SelfAddress;
            _selfUniqueAddress = _cluster.SelfUniqueAddress;
            _log = Context.GetLogger();
            _maxDeltaSize = settings.MaxDeltaSize;

            if (_cluster.IsTerminated) throw new ArgumentException("Cluster node must not be terminated");
            if (!string.IsNullOrEmpty(_settings.Role) && !_cluster.SelfRoles.Contains(_settings.Role))
                throw new ArgumentException($"The cluster node {_selfAddress} does not have the role {_settings.Role}");

            _gossipTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, GossipTick.Instance, Self);
            _notifyTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.NotifySubscribersInterval, _settings.NotifySubscribersInterval, Self, FlushChanges.Instance, Self);
            _pruningTask = _settings.PruningInterval != TimeSpan.Zero
                ? Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.PruningInterval, _settings.PruningInterval, Self, RemovedNodePruningTick.Instance, Self)
                : null;
            _clockTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, ClockTick.Instance, Self);

            _serializer = Context.System.Serialization.FindSerializerForType(typeof(DataEnvelope));
            _maxPruningDisseminationNanos = _settings.MaxPruningDissemination.Ticks * 100;

            _previousClockTime = DateTime.UtcNow.Ticks * 100;

            _hasDurableKeys = settings.DurableKeys.Count > 0;
            var durableKeysBuilder = ImmutableHashSet<string>.Empty.ToBuilder();
            var durableWildcardsBuilder = ImmutableHashSet<string>.Empty.ToBuilder();
            foreach (var key in settings.DurableKeys)
            {
                if (key.EndsWith("*"))
                    durableWildcardsBuilder.Add(key.Substring(0, key.Length - 1));
                else
                    durableKeysBuilder.Add(key);
            }

            _durableKeys = durableKeysBuilder.ToImmutable();
            _durableWildcards = durableWildcardsBuilder.ToImmutable();

            _durableStore = _hasDurableKeys
                ? Context.Watch(Context.ActorOf(_settings.DurableStoreProps, "durableStore"))
                : Context.System.DeadLetters;

            _deltaPropagationSelector = new ReplicatorDeltaPropagationSelector(this);

            // Derive the deltaPropagationInterval from the gossipInterval.
            // Normally the delta is propagated to all nodes within the gossip tick, so that
            // full state gossip is not needed.
            var deltaPropagationInterval = new TimeSpan(Math.Max(
                (_settings.GossipInterval.Ticks / _deltaPropagationSelector.GossipInternalDivisor),
                TimeSpan.TicksPerMillisecond * 200));
            _deltaPropagationTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(deltaPropagationInterval, deltaPropagationInterval, Self, DeltaPropagationTick.Instance, Self);

            if (_hasDurableKeys) Load();
            else NormalReceive();
        }

        protected override void PreStart()
        {
            if (_hasDurableKeys) _durableStore.Tell(LoadAll.Instance);

            // not using LeaderChanged/RoleLeaderChanged because here we need one node independent of data center
            _cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents,
                typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent));
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            _gossipTask.Cancel();
            _deltaPropagationTask.Cancel();
            _notifyTask.Cancel();
            _pruningTask?.Cancel();
            _clockTask.Cancel();
        }

        protected override SupervisorStrategy SupervisorStrategy() => new OneForOneStrategy(e =>
        {
            var fromDurableStore = Equals(Sender, _durableStore) && !Equals(Sender, Context.System.DeadLetters);
            if ((e is LoadFailedException || e is ActorInitializationException) && fromDurableStore)
            {
                _log.Error(e,
                    "Stopping distributed-data Replicator due to load or startup failure in durable store, caused by: {0}",
                    e.Message);
                Context.Stop(Self);
                return Directive.Stop;
            }
            else return Actor.SupervisorStrategy.DefaultDecider.Decide(e);
        });

        private void Load()
        {
            var startTime = DateTime.UtcNow;
            var count = 0;

            NormalReceive();

            Receive<LoadData>(load =>
            {
                count += load.Data.Count;
                foreach (var entry in load.Data)
                {
                    var envelope = entry.Value.DataEnvelope;
                    var newEnvelope = Write(entry.Key, envelope);
                    if (!ReferenceEquals(newEnvelope, envelope))
                    {
                        _durableStore.Tell(new Store(entry.Key, new DurableDataEnvelope(newEnvelope), null));
                    }
                }
            });
            Receive<LoadAllCompleted>(_ =>
            {
                _log.Debug("Loading {0} entries from durable store took {1} ms", count,
                    (DateTime.UtcNow - startTime).TotalMilliseconds);
                Become(NormalReceive);
                Self.Tell(FlushChanges.Instance);
            });
            Receive<GetReplicaCount>(_ =>
            {
                // 0 until durable data has been loaded, used by test
                Sender.Tell(new ReplicaCount(0));
            });

            // ignore scheduled ticks when loading durable data
            Receive(new Action<RemovedNodePruningTick>(Ignore));
            Receive(new Action<FlushChanges>(Ignore));
            Receive(new Action<GossipTick>(Ignore));

            // ignore gossip and replication when loading durable data
            Receive(new Action<Read>(IgnoreDebug));
            Receive(new Action<Write>(IgnoreDebug));
            Receive(new Action<Status>(IgnoreDebug));
            Receive(new Action<Gossip>(IgnoreDebug));
        }

        private void NormalReceive()
        {
            Receive<Get>(g => ReceiveGet(g.Key, g.Consistency, g.Request));
            Receive<Update>(msg => ReceiveUpdate(msg.Key, msg.Modify, msg.Consistency, msg.Request));
            Receive<Read>(r => ReceiveRead(r.Key));
            Receive<Write>(w => ReceiveWrite(w.Key, w.Envelope));
            Receive<ReadRepair>(rr => ReceiveReadRepair(rr.Key, rr.Envelope));
            Receive<DeltaPropagation>(msg => ReceiveDeltaPropagation(msg.FromNode, msg.ShouldReply, msg.Deltas));
            Receive<FlushChanges>(_ => ReceiveFlushChanges());
            Receive<DeltaPropagationTick>(_ => ReceiveDeltaPropagationTick());
            Receive<GossipTick>(_ => ReceiveGossipTick());
            Receive<ClockTick>(c => ReceiveClockTick());
            Receive<Internal.Status>(s => ReceiveStatus(s.Digests, s.Chunk, s.TotalChunks));
            Receive<Gossip>(g => ReceiveGossip(g.UpdatedData, g.SendBack));
            Receive<Subscribe>(s => ReceiveSubscribe(s.Key, s.Subscriber));
            Receive<Unsubscribe>(u => ReceiveUnsubscribe(u.Key, u.Subscriber));
            Receive<Terminated>(t => ReceiveTerminated(t.ActorRef));
            Receive<ClusterEvent.MemberWeaklyUp>(m => ReceiveMemberWeaklyUp(m.Member));
            Receive<ClusterEvent.MemberUp>(m => ReceiveMemberUp(m.Member));
            Receive<ClusterEvent.MemberRemoved>(m => ReceiveMemberRemoved(m.Member));
            Receive<ClusterEvent.IMemberEvent>(m => ReceiveOtherMemberEvent(m.Member));
            Receive<ClusterEvent.UnreachableMember>(u => ReceiveUnreachable(u.Member));
            Receive<ClusterEvent.ReachableMember>(r => ReceiveReachable(r.Member));
            Receive<GetKeyIds>(_ => ReceiveGetKeyIds());
            Receive<Delete>(d => ReceiveDelete(d.Key, d.Consistency, d.Request));
            Receive<RemovedNodePruningTick>(r => ReceiveRemovedNodePruningTick());
            Receive<GetReplicaCount>(_ => ReceiveGetReplicaCount());
        }

        private void IgnoreDebug<T>(T msg)
        {
            _log.Debug("ignoring message [{0}] when loading durable data", typeof(T));
        }

        private static void Ignore<T>(T msg) { }

        private void ReceiveGet(IKey key, IReadConsistency consistency, object req)
        {
            var localValue = GetData(key.Id);

            _log.Debug("Received get for key {0}, local value {1}, consistency: {2}", key.Id, localValue, consistency);

            if (IsLocalGet(consistency))
            {
                if (localValue == null) Sender.Tell(new NotFound(key, req));
                else if (localValue.Data is DeletedData) Sender.Tell(new DataDeleted(key, req));
                else Sender.Tell(new GetSuccess(key, req, localValue.Data));
            }
            else
                Context.ActorOf(ReadAggregator.Props(key, consistency, req, _nodes, _unreachable, localValue, Sender)
                    .WithDispatcher(Context.Props.Dispatcher));
        }

        private bool IsLocalGet(IReadConsistency consistency)
        {
            if (consistency is ReadLocal) return true;
            if (consistency is ReadAll || consistency is ReadMajority) return _nodes.Count == 0;
            return false;
        }

        private void ReceiveRead(string key)
        {
            Sender.Tell(new ReadResult(GetData(key)));
        }

        private bool MatchingRole(Member m) => string.IsNullOrEmpty(_settings.Role) || m.HasRole(_settings.Role);

        private void ReceiveUpdate(IKey key, Func<IReplicatedData, IReplicatedData> modify, IWriteConsistency consistency, object request)
        {
            var localValue = GetData(key.Id);

            try
            {
                DataEnvelope envelope;
                IReplicatedData delta;
                if (localValue == null)
                {
                    var d = modify(null);
                    if (d is IDeltaReplicatedData withDelta)
                    {
                        envelope = new DataEnvelope(withDelta.ResetDelta());
                        delta = withDelta.Delta ?? DeltaPropagation.NoDeltaPlaceholder;
                    }
                    else
                    {
                        envelope = new DataEnvelope(d);
                        delta = null;
                    }
                }
                else if (localValue.Data is DeletedData)
                {
                    _log.Debug("Received update for deleted key {0}", key);
                    Sender.Tell(new DataDeleted(key, request));
                    return;
                }
                else
                {
                    var d = modify(localValue.Data);
                    if (d is IDeltaReplicatedData withDelta)
                    {
                        envelope = localValue.Merge(withDelta.ResetDelta());
                        delta = withDelta.Delta ?? DeltaPropagation.NoDeltaPlaceholder;
                    }
                    else
                    {
                        envelope = localValue.Merge(d);
                        delta = null;
                    }
                }

                // case Success((envelope, delta)) ⇒

                _log.Debug("Received Update for key {0}", key);

                // handle the delta
                if (delta != null)
                {
                    _deltaPropagationSelector.Update(key.Id, delta);
                }

                // note that it's important to do deltaPropagationSelector.update before setData,
                // so that the latest delta version is used
                var newEnvelope = SetData(key.Id, envelope);

                var durable = IsDurable(key.Id);
                if (IsLocalUpdate(consistency))
                {
                    if (durable)
                    {
                        var reply = new StoreReply(
                            successMessage: new UpdateSuccess(key, request),
                            failureMessage: new StoreFailure(key, request),
                            replyTo: Sender);
                        _durableStore.Tell(new Store(key.Id, new DurableDataEnvelope(newEnvelope), reply));
                    }
                    else Sender.Tell(new UpdateSuccess(key, request));
                }
                else
                {
                    DataEnvelope writeEnvelope;
                    Delta writeDelta;
                    if (delta == null || Equals(delta, DeltaPropagation.NoDeltaPlaceholder))
                    {
                        writeEnvelope = newEnvelope;
                        writeDelta = null;
                    }
                    else if (delta is IRequireCausualDeliveryOfDeltas)
                    {
                        var version = _deltaPropagationSelector.CurrentVersion(key.Id);
                        writeEnvelope = newEnvelope;
                        writeDelta = new Delta(newEnvelope.WithData(delta), version, version);
                    }
                    else
                    {
                        writeEnvelope = newEnvelope.WithData(delta);
                        writeDelta = null;
                    }

                    var writeAggregator = Context.ActorOf(WriteAggregator
                        .Props(key, writeEnvelope, writeDelta, consistency, request, _nodes, _unreachable, Sender, durable)
                        .WithDispatcher(Context.Props.Dispatcher));

                    if (durable)
                    {
                        var reply = new StoreReply(
                            successMessage: new UpdateSuccess(key, request),
                            failureMessage: new StoreFailure(key, request),
                            replyTo: writeAggregator);
                        _durableStore.Tell(new Store(key.Id, new DurableDataEnvelope(envelope), reply));
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Debug("Received update for key {0}, failed {1}", key, ex.Message);
                Sender.Tell(new ModifyFailure(key, "Update failed: " + ex.Message, ex, request));
            }
        }
        private bool IsDurable(string key) =>
            _durableKeys.Contains(key) || (_durableWildcards.Count > 0 && _durableWildcards.Any(key.StartsWith));

        private bool IsLocalUpdate(IWriteConsistency consistency)
        {
            if (consistency is WriteLocal) return true;
            if (consistency is WriteAll || consistency is WriteMajority) return _nodes.Count == 0;
            return false;
        }

        private void ReceiveWrite(string key, DataEnvelope envelope)
        {
            WriteAndStore(key, envelope, reply: true);
        }

        private void WriteAndStore(string key, DataEnvelope writeEnvelope, bool reply)
        {
            var newEnvelope = Write(key, writeEnvelope);
            if (newEnvelope != null)
            {
                if (IsDurable(key))
                {
                    var storeReply = reply
                        ? new StoreReply(WriteAck.Instance, WriteNack.Instance, Sender)
                        : null;

                    _durableStore.Tell(new Store(key, new DurableDataEnvelope(newEnvelope), storeReply));
                }
                else if (reply) Sender.Tell(WriteAck.Instance);
            }
            else if (reply) Sender.Tell(WriteNack.Instance);
        }

        private DataEnvelope Write(string key, DataEnvelope writeEnvelope)
        {
            switch(GetData(key))
            {
                case DataEnvelope envelope when envelope.Equals(writeEnvelope):
                    return envelope;
                case DataEnvelope envelope when envelope.Data is DeletedData:
                    // already deleted
                    return DeletedEnvelope;
                case DataEnvelope envelope:
                    try
                    {
                        // DataEnvelope will mergeDelta when needed
                        var merged = envelope.Merge(writeEnvelope).AddSeen(_selfAddress);
                        return SetData(key, merged);
                    }
                    catch (ArgumentException e)
                    {
                        _log.Warning("Couldn't merge [{0}] due to: {1}", key, e.Message);
                        return null;
                    }
                default:
                    // no existing data for the key
                    if (writeEnvelope.Data is IReplicatedDelta withDelta)
                        writeEnvelope = writeEnvelope.WithData(withDelta.Zero.MergeDelta(withDelta));

                return SetData(key, writeEnvelope.AddSeen(_selfAddress));
            }
        }

        private void ReceiveReadRepair(string key, DataEnvelope writeEnvelope)
        {
            WriteAndStore(key, writeEnvelope, reply: false);
            Sender.Tell(ReadRepairAck.Instance);
        }

        private void ReceiveGetKeyIds()
        {
            var keys = _dataEntries
                .Where(kvp => !(kvp.Value.envelope.Data is DeletedData))
                .Select(x => x.Key)
                .ToImmutableHashSet();
            Sender.Tell(new GetKeysIdsResult(keys));
        }

        private void ReceiveDelete(IKey key, IWriteConsistency consistency, object request)
        {
            var envelope = GetData(key.Id);
            if (envelope?.Data is DeletedData)
            {
                // already deleted
                Sender.Tell(new DataDeleted(key, request));
            }
            else
            {
                SetData(key.Id, DeletedEnvelope);
                var durable = IsDurable(key.Id);
                if (IsLocalUpdate(consistency))
                {
                    if (durable)
                    {
                        var reply = new StoreReply(
                            successMessage: new DeleteSuccess(key, request),
                            failureMessage: new StoreFailure(key, request),
                            replyTo: Sender);
                        _durableStore.Tell(new Store(key.Id, new DurableDataEnvelope(DeletedEnvelope), reply));
                    }
                    else Sender.Tell(new DeleteSuccess(key, request));
                }
                else
                {
                    var writeAggregator = Context.ActorOf(WriteAggregator
                        .Props(key, DeletedEnvelope, null, consistency, request, _nodes, _unreachable, Sender, durable)
                        .WithDispatcher(Context.Props.Dispatcher));

                    if (durable)
                    {
                        var reply = new StoreReply(
                            successMessage: new DeleteSuccess(key, request),
                            failureMessage: new StoreFailure(key, request),
                            replyTo: writeAggregator);
                        _durableStore.Tell(new Store(key.Id, new DurableDataEnvelope(DeletedEnvelope), reply));
                    }
                }
            }
        }

        private DataEnvelope SetData(string key, DataEnvelope envelope)
        {
            var deltaVersions = envelope.DeltaVersions;
            var currentVersion = _deltaPropagationSelector.CurrentVersion(key);
            var newEnvelope = currentVersion == 0 || currentVersion == deltaVersions.VersionAt(_selfUniqueAddress)
                ? envelope
                : envelope.WithDeltaVersions(deltaVersions.Merge(VersionVector.Create(_selfUniqueAddress, currentVersion)));

            Digest digest;
            if (_subscribers.ContainsKey(key) && !_changed.Contains(key))
            {
                var oldDigest = GetDigest(key);
                var dig = Digest(newEnvelope);
                if (!Equals(dig, oldDigest))
                    _changed = _changed.Add(key);

                digest = dig;
            }
            else if (newEnvelope.Data is DeletedData) digest = DeletedDigest;
            else digest = LazyDigest;

            _dataEntries = _dataEntries.SetItem(key, (newEnvelope, digest));
            if (newEnvelope.Data is DeletedData)
            {
                _deltaPropagationSelector.Delete(key);
            }

            return newEnvelope;
        }

        private Digest GetDigest(string key)
        {
            var contained = _dataEntries.TryGetValue(key, out var value);
            if (contained)
            {
                if (value.digest == LazyDigest)
                {
                    var digest = Digest(value.envelope);
                    _dataEntries = _dataEntries.SetItem(key, (value.envelope, digest));
                    return digest;
                }

                return value.digest;
            }

            return NotFoundDigest;
        }

        private Digest Digest(DataEnvelope envelope)
        {
            if (Equals(envelope.Data, DeletedData.Instance)) return DeletedDigest;

            var bytes = _serializer.ToBinary(envelope.WithoutDeltaVersions());
            var serialized = SHA1.Create().ComputeHash(bytes);
            return ByteString.CopyFrom(serialized);
        }

        private DataEnvelope GetData(string key)
        {
            return !_dataEntries.TryGetValue(key, out var value) ? null : value.envelope;
        }

        private long GetDeltaSequenceNr(string key, UniqueAddress from)
        {
            return _dataEntries.TryGetValue(key, out var tuple) ? tuple.envelope.DeltaVersions.VersionAt(@from) : 0L;
        }

        private bool IsNodeRemoved(UniqueAddress node, IEnumerable<string> keys)
        {
            if (_removedNodes.ContainsKey(node)) return true;

            return keys.Any(key => _dataEntries.TryGetValue(key, out var tuple) && tuple.envelope.Pruning.ContainsKey(node));
        }

        private void Notify(string keyId, HashSet<IActorRef> subs)
        {
            var key = _subscriptionKeys[keyId];
            var envelope = GetData(keyId);
            if (envelope != null)
            {
                var msg = envelope.Data is DeletedData
                    ? (object)new DataDeleted(key, null)
                    : new Changed(key, envelope.Data);

                foreach (var sub in subs) sub.Tell(msg);
            }
        }

        private void ReceiveFlushChanges()
        {
            if (_subscribers.Count != 0)
            {
                foreach (var key in _changed)
                {
                    if (_subscribers.TryGetValue(key, out var subs))
                        Notify(key, subs);
                }
            }

            // Changed event is sent to new subscribers even though the key has not changed,
            // i.e. send current value
            if (_newSubscribers.Count != 0)
            {
                foreach (var kvp in _newSubscribers)
                {
                    Notify(kvp.Key, kvp.Value);
                    if (!_subscribers.TryGetValue(kvp.Key, out var set))
                    {
                        set = new HashSet<IActorRef>();
                        _subscribers.Add(kvp.Key, set);
                    }

                    foreach (var sub in kvp.Value) set.Add(sub);
                }

                _newSubscribers.Clear();
            }

            _changed = ImmutableHashSet<string>.Empty;
        }

        private void ReceiveDeltaPropagationTick()
        {
            foreach (var entry in _deltaPropagationSelector.CollectPropagations())
            {
                var node = entry.Key;
                var deltaPropagation = entry.Value;

                // TODO split it to several DeltaPropagation if too many entries
                if (!deltaPropagation.Deltas.IsEmpty)
                {
                    Replica(node).Tell(deltaPropagation);
                }
            }

            if (_deltaPropagationSelector.PropagationCount % _deltaPropagationSelector.GossipInternalDivisor == 0)
                _deltaPropagationSelector.CleanupDeltaEntries();
        }

        private void ReceiveDeltaPropagation(UniqueAddress from, bool reply, ImmutableDictionary<string, Delta> deltas)
        {
            try
            {
                var isDebug = _log.IsDebugEnabled;
                if (isDebug)
                    _log.Debug("Received DeltaPropagation from [{0}], containing [{1}]", from.Address,
                        string.Join(", ", deltas.Select(d => $"{d.Key}:{d.Value.FromSeqNr}->{d.Value.ToSeqNr}")));

                if (IsNodeRemoved(from, deltas.Keys))
                {
                    // Late message from a removed node.
                    // Drop it to avoid merging deltas that have been pruned on one side.
                    _log.Debug("Skipping DeltaPropagation from [{0}] because that node has been removed", from.Address);
                }
                else
                {
                    foreach (var entry in deltas)
                    {
                        var key = entry.Key;
                        var delta = entry.Value;
                        var envelope = delta.DataEnvelope;
                        if (envelope.Data is IRequireCausualDeliveryOfDeltas)
                        {
                            var currentSeqNr = GetDeltaSequenceNr(key, from);
                            if (currentSeqNr >= delta.ToSeqNr)
                            {
                                _log.Debug("Skipping DeltaPropagation from [{0}] for [{1}] because toSeqNr [{2}] already handled [{3}]", from.Address, key, delta.FromSeqNr, currentSeqNr);
                                if (reply)
                                    Sender.Tell(WriteAck.Instance);
                            }
                            else if (delta.FromSeqNr > currentSeqNr + 1)
                            {
                                _log.Debug("Skipping DeltaPropagation from [{0}] for [{1}] because missing deltas between [{2}-{3}]", from.Address, key, currentSeqNr + 1, delta.FromSeqNr - 1);
                                if (reply)
                                    Sender.Tell(DeltaNack.Instance);
                            }
                            else
                            {
                                _log.Debug("Applying DeltaPropagation from [{0}] for [{1}] with sequence numbers [{2}-{3}], current was [{4}]", from.Address, key, delta.FromSeqNr, delta.ToSeqNr, currentSeqNr);
                                var newEnvelope = envelope.WithDeltaVersions(VersionVector.Create(from, delta.ToSeqNr));
                                WriteAndStore(key, newEnvelope, reply);
                            }
                        }
                        else
                        {
                            // causal delivery of deltas not needed, just apply it
                            WriteAndStore(key, envelope, reply);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // catching in case we need to support rolling upgrades that are
                // mixing nodes with incompatible delta-CRDT types
                _log.Warning("Couldn't process DeltaPropagation from [{0}] due to {1}", from, e);
            }
        }

        private void ReceiveGossipTick()
        {
            var node = SelectRandomNode(AllNodes);
            if (node != null)
            {
                GossipTo(node);
            }
        }

        private void GossipTo(Address address)
        {
            var to = Replica(address);
            if (_dataEntries.Count <= _settings.MaxDeltaElements)
            {
                var status = new Internal.Status(_dataEntries
                    .ToImmutableDictionary(x => x.Key, y => GetDigest(y.Key)), chunk: 0, totalChunks: 1);
                to.Tell(status);
            }
            else
            {
                var totChunks = _dataEntries.Count / _settings.MaxDeltaElements;
                for (var i = 1; i <= Math.Min(totChunks, 10); i++)
                {
                    if (totChunks == _statusTotChunks)
                    {
                        _statusCount++;
                    }
                    else
                    {
                        _statusCount = ThreadLocalRandom.Current.Next(0, totChunks);
                        _statusTotChunks = totChunks;
                    }
                    var chunk = (int)(_statusCount % totChunks);
                    var entries = _dataEntries.Where(x => Math.Abs(MurmurHash.StringHash(x.Key) % totChunks) == chunk)
                        .ToImmutableDictionary(x => x.Key, y => GetDigest(y.Key));
                    var status = new Internal.Status(entries, chunk, totChunks);
                    to.Tell(status);
                }
            }
        }

        private Address SelectRandomNode(ImmutableHashSet<Address> addresses)
        {
            if (addresses.Count > 0)
            {
                var random = ThreadLocalRandom.Current.Next(addresses.Count - 1);
                return addresses.Skip(random).First();
            }

            return null;
        }

        private ActorSelection Replica(Address address) => Context.ActorSelection(Self.Path.ToStringWithAddress(address));

        private bool IsOtherDifferent(string key, Digest otherDigest)
        {
            var d = GetDigest(key);
            return d != NotFoundDigest && d != otherDigest;
        }

        private void ReceiveStatus(IImmutableDictionary<string, ByteString> otherDigests, int chunk, int totChunks)
        {
            if (_log.IsDebugEnabled)
                _log.Debug("Received gossip status from [{0}], chunk {1}/{2} containing [{3}]", Sender.Path.Address, chunk + 1, totChunks, string.Join(", ", otherDigests.Keys));

            // if no data was send we do nothing
            if (otherDigests.Count == 0)
                return;

            var otherDifferentKeys = otherDigests
                .Where(x => IsOtherDifferent(x.Key, x.Value))
                .Select(x => x.Key)
                .ToImmutableHashSet();

            var otherKeys = otherDigests.Keys.ToImmutableHashSet();
            var myKeys = (totChunks == 1
                    ? _dataEntries.Keys
                    : _dataEntries.Keys.Where(x => Math.Abs(MurmurHash.StringHash(x) % totChunks) == chunk))
                .ToImmutableHashSet();

            var otherMissingKeys = myKeys.Except(otherKeys);

            var keys = otherDifferentKeys
                .Union(otherMissingKeys)
                .Take(_settings.MaxDeltaElements)
                .ToArray();

            if (keys.Length != 0)
            {
                if (_log.IsDebugEnabled)
                    _log.Debug("Sending gossip to [{0}]: {1}", Sender.Path.Address, string.Join(", ", keys));

                var g = new Gossip(keys.ToImmutableDictionary(x => x, _ => GetData(_)), !otherDifferentKeys.IsEmpty);
                Sender.Tell(g);
            }

            var myMissingKeys = otherKeys.Except(myKeys);
            if (!myMissingKeys.IsEmpty)
            {
                if (Context.System.Log.IsDebugEnabled)
                    Context.System.Log.Debug("Sending gossip status to {0}, requesting missing {1}", Sender.Path.Address, string.Join(", ", myMissingKeys));

                var status = new Internal.Status(myMissingKeys.ToImmutableDictionary(x => x, _ => NotFoundDigest), chunk, totChunks);
                Sender.Tell(status);
            }
        }

        private void ReceiveGossip(IImmutableDictionary<string, DataEnvelope> updatedData, bool sendBack)
        {
            if (_log.IsDebugEnabled)
                _log.Debug("Received gossip from [{0}], containing [{1}]", Sender.Path.Address, string.Join(", ", updatedData.Keys));

            var replyData = ImmutableDictionary<string, DataEnvelope>.Empty.ToBuilder();
            foreach (var d in updatedData)
            {
                var key = d.Key;
                var envelope = d.Value;
                var hadData = _dataEntries.ContainsKey(key);
                WriteAndStore(key, envelope, reply: false);
                if (sendBack)
                {
                    var data = GetData(key);
                    if (data != null && (hadData || data.Pruning.Count != 0))
                        replyData[key] = data;
                }
            }

            if (sendBack && replyData.Count != 0) Sender.Tell(new Gossip(replyData.ToImmutable(), sendBack: false));
        }

        private void ReceiveSubscribe(IKey key, IActorRef subscriber)
        {
            HashSet<IActorRef> set;
            if (!_newSubscribers.TryGetValue(key.Id, out set))
            {
                _newSubscribers[key.Id] = set = new HashSet<IActorRef>();
            }
            set.Add(subscriber);

            if (!_subscriptionKeys.ContainsKey(key.Id))
                _subscriptionKeys = _subscriptionKeys.SetItem(key.Id, key);

            Context.Watch(subscriber);
        }

        private void ReceiveUnsubscribe(IKey key, IActorRef subscriber)
        {
            HashSet<IActorRef> set;
            if (_subscribers.TryGetValue(key.Id, out set) && set.Remove(subscriber) && set.Count == 0)
                _subscribers.Remove(key.Id);

            if (_newSubscribers.TryGetValue(key.Id, out set) && set.Remove(subscriber) && set.Count == 0)
                _newSubscribers.Remove(key.Id);

            if (!HasSubscriber(subscriber))
                Context.Unwatch(subscriber);

            if (!_subscribers.ContainsKey(key.Id) || !_newSubscribers.ContainsKey(key.Id))
                _subscriptionKeys = _subscriptionKeys.Remove(key.Id);
        }

        private bool HasSubscriber(IActorRef subscriber)
        {
            return _subscribers.Any(kvp => kvp.Value.Contains(subscriber)) ||
                   _newSubscribers.Any(kvp => kvp.Value.Contains(subscriber));
        }

        private void ReceiveTerminated(IActorRef terminated)
        {
            if (Equals(terminated, _durableStore))
            {
                _log.Error("Stopping distributed-data replicator because durable store terminated");
                Context.Stop(Self);
            }
            else
            {
                var keys1 = _subscribers.Where(x => x.Value.Contains(terminated))
                    .Select(x => x.Key)
                    .ToImmutableHashSet();

                foreach (var k in keys1)
                {
                    HashSet<IActorRef> set;
                    if (_subscribers.TryGetValue(k, out set) && set.Remove(terminated) && set.Count == 0)
                        _subscribers.Remove(k);
                }

                var keys2 = _newSubscribers.Where(x => x.Value.Contains(terminated))
                    .Select(x => x.Key)
                    .ToImmutableHashSet();

                foreach (var k in keys2)
                {
                    HashSet<IActorRef> set;
                    if (_newSubscribers.TryGetValue(k, out set) && set.Remove(terminated) && set.Count == 0)
                        _newSubscribers.Remove(k);
                }

                var allKeys = keys1.Union(keys2);
                foreach (var k in allKeys)
                {
                    if (!_subscribers.ContainsKey(k) && !_newSubscribers.ContainsKey(k))
                        _subscriptionKeys = _subscriptionKeys.Remove(k);
                }
            }
        }

        private void ReceiveMemberWeaklyUp(Member m)
        {
            if (MatchingRole(m) && m.Address != _selfAddress)
            {
                _weaklyUpNodes = _weaklyUpNodes.Add(m.Address);
            }
        }

        private void ReceiveMemberUp(Member m)
        {
            if (MatchingRole(m))
            {
                _leader = _leader.Add(m);
                if (m.Address != _selfAddress)
                {
                    _nodes = _nodes.Add(m.Address);
                    _weaklyUpNodes = _weaklyUpNodes.Remove(m.Address);
                }
            }
        }

        private void ReceiveMemberRemoved(Member m)
        {
            if (m.Address == _selfAddress) Context.Stop(Self);
            else if (MatchingRole(m))
            {
                _log.Debug("Adding removed node [{0}] from MemberRemoved", m.UniqueAddress);

                // filter, it's possible that the ordering is changed since it based on MemberStatus
                _leader = _leader.Where(x => x.Address != m.Address).ToImmutableSortedSet(Member.LeaderStatusOrdering);

                _nodes = _nodes.Remove(m.Address);
                _weaklyUpNodes = _weaklyUpNodes.Remove(m.Address);
                
                _removedNodes = _removedNodes.SetItem(m.UniqueAddress, _allReachableClockTime);
                _unreachable = _unreachable.Remove(m.Address);
                _deltaPropagationSelector.CleanupRemovedNode(m.Address);
            }
        }

        private void ReceiveUnreachable(Member m)
        {
            if (MatchingRole(m)) _unreachable = _unreachable.Add(m.Address);
        }

        private void ReceiveReachable(Member m)
        {
            if (MatchingRole(m)) _unreachable = _unreachable.Remove(m.Address);
        }

        private void ReceiveOtherMemberEvent(Member m)
        {
            if (MatchingRole(m))
            {
                // replace, it's possible that the ordering is changed since it based on MemberStatus
                _leader = _leader.Where(x => x.UniqueAddress != m.UniqueAddress)
                    .ToImmutableSortedSet(Member.LeaderStatusOrdering);
                _leader = _leader.Add(m);
            }
        }

        private void ReceiveClockTick()
        {
            var now = DateTime.UtcNow.Ticks * TimeSpan.TicksPerMillisecond / 100; // we need ticks per nanosec.
            if (_unreachable.Count == 0)
                _allReachableClockTime += (now - _previousClockTime);
            _previousClockTime = now;
        }

        private void ReceiveRemovedNodePruningTick()
        {
            // See 'CRDT Garbage' section in Replicator Scaladoc for description of the process
            if (_unreachable.IsEmpty)
            {
                if (IsLeader)
                {
                    CollectRemovedNodes();
                    InitRemovedNodePruning();
                }

                PerformRemovedNodePruning();
                DeleteObsoletePruningPerformed();
            }
        }

        private void CollectRemovedNodes()
        {
            var knownNodes = AllNodes.Union(_removedNodes.Keys.Select(x => x.Address));
            var newRemovedNodes = new HashSet<UniqueAddress>();
            foreach (var pair in _dataEntries)
            {
                if (pair.Value.envelope.Data is IRemovedNodePruning removedNodePruning)
                {
                    newRemovedNodes.UnionWith(removedNodePruning.ModifiedByNodes.Where(n => !(n == _selfUniqueAddress || knownNodes.Contains(n.Address))));
                }
            }

            var removedNodesBuilder = _removedNodes.ToBuilder();
            foreach (var node in newRemovedNodes)
            {
                _log.Debug("Adding removed node [{0}] from data", node);
                removedNodesBuilder[node] = _allReachableClockTime;
            }
            _removedNodes = removedNodesBuilder.ToImmutable();
        }

        private void InitRemovedNodePruning()
        {
            // initiate pruning for removed nodes
            var removedSet = _removedNodes
                .Where(x => (_allReachableClockTime - x.Value) > _maxPruningDisseminationNanos)
                .Select(x => x.Key)
                .ToImmutableHashSet();

            if (!removedSet.IsEmpty)
            {
                foreach (var entry in _dataEntries)
                {
                    var key = entry.Key;
                    var envelope = entry.Value.envelope;

                    foreach (var removed in removedSet)
                    {
                        if (envelope.NeedPruningFrom(removed))
                        {
                            if (envelope.Data is IRemovedNodePruning)
                            {
                                if (envelope.Pruning.TryGetValue(removed, out var state))
                                {
                                    if (state is PruningInitialized initialized && initialized.Owner != _selfUniqueAddress)
                                    {
                                        var newEnvelope = envelope.InitRemovedNodePruning(removed, _selfUniqueAddress);
                                        _log.Debug("Initiating pruning of {0} with data {1}", removed, key);
                                        SetData(key, newEnvelope);
                                    }
                                }
                                else
                                {
                                    var newEnvelope = envelope.InitRemovedNodePruning(removed, _selfUniqueAddress);
                                    _log.Debug("Initiating pruning of {0} with data {1}", removed, key);
                                    SetData(key, newEnvelope);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void PerformRemovedNodePruning()
        {
            // perform pruning when all seen Init
            var prunningPerformed = new PruningPerformed(DateTime.UtcNow + _settings.PruningMarkerTimeToLive);
            var durablePrunningPerformed = new PruningPerformed(DateTime.UtcNow + _settings.DurablePruningMarkerTimeToLive);

            foreach (var entry in _dataEntries)
            {
                var key = entry.Key;
                var envelope = entry.Value.envelope;
                if (envelope.Data is IRemovedNodePruning data)
                {
                    foreach (var entry2 in envelope.Pruning)
                    {
                        if (entry2.Value is PruningInitialized init && init.Owner == _selfUniqueAddress && (AllNodes.IsEmpty || AllNodes.IsSubsetOf(init.Seen)))
                        {
                            var removed = entry2.Key;
                            var isDurable = IsDurable(key);
                            var newEnvelope = envelope.Prune(removed, isDurable ? durablePrunningPerformed : prunningPerformed);
                            _log.Debug("Perform pruning of [{0}] from [{1}] to [{2}]", key, removed, _selfUniqueAddress);
                            SetData(key, newEnvelope);
                            if (!newEnvelope.Data.Equals(data) && isDurable)
                            {
                                _durableStore.Tell(new Store(key, new DurableDataEnvelope(newEnvelope), null));
                            }
                        }
                    }
                }
            }
        }

        private void DeleteObsoletePruningPerformed()
        {
            var currentTime = DateTime.UtcNow;
            foreach (var entry in _dataEntries)
            {
                var key = entry.Key;
                var envelope = entry.Value.envelope;
                if (envelope.Data is IRemovedNodePruning)
                {
                    var toRemove = envelope.Pruning
                        .Where(pair => (pair.Value as PruningPerformed)?.IsObsolete(currentTime) ?? false)
                        .Select(pair => pair.Key)
                        .ToArray();

                    if (toRemove.Length > 0)
                    {
                        var removedNodesBuilder = _removedNodes.ToBuilder();
                        var newPruningBuilder = envelope.Pruning.ToBuilder();

                        removedNodesBuilder.RemoveRange(toRemove);
                        newPruningBuilder.RemoveRange(toRemove);

                        _removedNodes = removedNodesBuilder.ToImmutable();
                        var newEnvelope = envelope.WithPruning(newPruningBuilder.ToImmutable());

                        SetData(key, newEnvelope);
                    }
                }
            }
        }

        private void ReceiveGetReplicaCount() => Sender.Tell(new ReplicaCount(_nodes.Count + 1));

        #region delta propagation selector

        private sealed class ReplicatorDeltaPropagationSelector : DeltaPropagationSelector
        {
            private readonly Replicator _replicator;

            public ReplicatorDeltaPropagationSelector(Replicator replicator)
            {
                _replicator = replicator;
            }

            public override int GossipInternalDivisor { get; } = 5;

            protected override int MaxDeltaSize => _replicator._maxDeltaSize;

            // TODO optimize, by maintaining a sorted instance variable instead
            protected override ImmutableArray<Address> AllNodes
            {
                get
                {
                    var allNodes = _replicator.AllNodes.Except(_replicator._unreachable).OrderBy(x => x).ToImmutableArray();
                    return allNodes;
                }
            }
                

            protected override DeltaPropagation CreateDeltaPropagation(ImmutableDictionary<string, (IReplicatedData data, long from, long to)> deltas)
            {
                // Important to include the pruning state in the deltas. For example if the delta is based
                // on an entry that has been pruned but that has not yet been performed on the target node.
                var newDeltas = deltas
                    .Where(x => !Equals(x.Value.data, DeltaPropagation.NoDeltaPlaceholder))
                    .Select(x =>
                    {
                        var key = x.Key;
                        var (data, from, to) = x.Value;
                        var envelope = _replicator.GetData(key);
                        return envelope != null
                            ? new KeyValuePair<string, Delta>(key,
                                new Delta(envelope.WithData(data), from, to))
                            : new KeyValuePair<string, Delta>(key,
                                new Delta(new DataEnvelope(data), from, to));
                    })
                    .ToImmutableDictionary();

                return new DeltaPropagation(_replicator._selfUniqueAddress, shouldReply: false, deltas: newDeltas);
            }
        }

        #endregion
    }
}
