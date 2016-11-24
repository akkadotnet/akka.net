//-----------------------------------------------------------------------
// <copyright file="Replicator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;
using Akka.DistributedData.Internal;
using Akka.IO;
using Akka.Serialization;
using Akka.Util;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using Akka.Event;

namespace Akka.DistributedData
{
    public sealed partial class Replicator : ActorBase
    {
        public static Props Props(ReplicatorSettings settings)
        {
            return Actor.Props.Create(() => new Replicator(settings)).WithDeploy(Deploy.Local).WithDispatcher(settings.Dispatcher);
        }

        private static readonly ByteString DeletedDigest = ByteString.Empty;
        private static readonly ByteString LazyDigest = ByteString.Create(new byte[] { 0 }, 0, 1);
        private static readonly ByteString NotFoundDigest = ByteString.Create(new byte[] { 255 }, 0, 1);

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

        private IImmutableSet<Address> _nodes = ImmutableHashSet<Address>.Empty;

        private IImmutableDictionary<UniqueAddress, long> _removedNodes = ImmutableDictionary<UniqueAddress, long>.Empty;
        private IImmutableDictionary<UniqueAddress, long> _pruningPerformed = ImmutableDictionary<UniqueAddress, long>.Empty;
        private IImmutableSet<UniqueAddress> _tombstonedNodes = ImmutableHashSet<UniqueAddress>.Empty;

        private Address _leader = null;
        private bool IsLeader => _leader != null && _leader == _selfAddress;

        private long _previousClockTime;
        private long _allReachableClockTime = 0;
        private IImmutableSet<Address> _unreachable = ImmutableHashSet<Address>.Empty;

        private IImmutableDictionary<string, Tuple<DataEnvelope, ByteString>> _dataEntries = ImmutableDictionary<string, Tuple<DataEnvelope, ByteString>>.Empty;
        private IImmutableSet<string> _changed = ImmutableHashSet<string>.Empty;

        private long _statusCount = 0;
        private int _statusTotChunks = 0;

        private readonly Dictionary<string, ISet<IActorRef>> _subscribers = new Dictionary<string, ISet<IActorRef>>();
        private readonly Dictionary<string, ISet<IActorRef>> _newSubscribers = new Dictionary<string, ISet<IActorRef>>();
        private IImmutableDictionary<string, IKey> _subscriptionKeys = ImmutableDictionary<string, IKey>.Empty;

        private readonly ILoggingAdapter _log;

        public Replicator(ReplicatorSettings settings)
        {
            _settings = settings;
            _cluster = Cluster.Cluster.Get(Context.System);
            _selfAddress = _cluster.SelfAddress;
            _selfUniqueAddress = _cluster.SelfUniqueAddress;
            _log = Context.GetLogger();

            if (_cluster.IsTerminated) throw new ArgumentException("Cluster node must not be terminated");
            if (!string.IsNullOrEmpty(_settings.Role) && !_cluster.SelfRoles.Contains(_settings.Role))
                throw new ArgumentException($"The cluster node {_selfAddress} does not have the role {_settings.Role}");

            _gossipTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, GossipTick.Instance, Self);
            _notifyTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.NotifySubscribersInterval, _settings.NotifySubscribersInterval, Self, FlushChanges.Instance, Self);
            _pruningTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.PruningInterval, _settings.PruningInterval, Self, RemovedNodePruningTick.Instance, Self);
            _clockTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, ClockTick.Instance, Self);

            _serializer = Context.System.Serialization.FindSerializerForType(typeof(DataEnvelope));
            _maxPruningDisseminationNanos = _settings.MaxPruningDissemination.Ticks * 100;

            _previousClockTime = DateTime.UtcNow.Ticks * 100;
        }

        protected override void PreStart()
        {
            var leaderChangedClass = _settings.Role != null
                ? typeof(ClusterEvent.RoleLeaderChanged)
                : typeof(ClusterEvent.LeaderChanged);
            _cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents,
                typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent), leaderChangedClass);
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            _gossipTask.Cancel();
            _notifyTask.Cancel();
            _pruningTask.Cancel();
            _clockTask.Cancel();
        }

        protected override bool Receive(object message) => message.Match()
            .With<Get>(ReceiveGet)
            .With<Update>(msg => ReceiveUpdate(msg.Key, msg.Modify, msg.Consistency, msg.Request))
            .With<Read>(r => ReceiveRead(r.Key))
            .With<Write>(w => ReceiveWrite(w.Key, w.Envelope))
            .With<ReadRepair>(rr => ReceiveReadRepair(rr.Key, rr.Envelope))
            .With<FlushChanges>(x => ReceiveFlushChanges())
            .With<GossipTick>(_ => ReceiveGossipTick())
            .With<ClockTick>(c => ReceiveClockTick())
            .With<Internal.Status>(s => ReceiveStatus(s.Digests, s.Chunk, s.TotalChunks))
            .With<Gossip>(g => ReceiveGossip(g.UpdatedData, g.SendBack))
            .With<Subscribe>(s => ReceiveSubscribe(s.Key, s.Subscriber))
            .With<Unsubscribe>(u => ReceiveUnsubscribe(u.Key, u.Subscriber))
            .With<Terminated>(t => ReceiveTerminated(t.ActorRef))
            .With<ClusterEvent.MemberUp>(m => ReceiveMemberUp(m.Member))
            .With<ClusterEvent.MemberRemoved>(m => ReceiveMemberRemoved(m.Member))
            .With<ClusterEvent.IMemberEvent>(_ => { })
            .With<ClusterEvent.UnreachableMember>(u => ReceiveUnreachable(u.Member))
            .With<ClusterEvent.ReachableMember>(r => ReceiveReachable(r.Member))
            .With<ClusterEvent.LeaderChanged>(l => ReceiveLeaderChanged(l.Leader, null))
            .With<ClusterEvent.RoleLeaderChanged>(r => ReceiveLeaderChanged(r.Leader, r.Role))
            .With<GetKeyIds>(_ => ReceiveGetKeyIds())
            .With<Delete>(d => ReceiveDelete(d.Key, d.Consistency))
            .With<RemovedNodePruningTick>(r => ReceiveRemovedNodePruningTick())
            .With<GetReplicaCount>(_ => ReceiveGetReplicaCount())
            .WasHandled;

        private void ReceiveGet(Get message)
        {
            var key = message.Key;
            var consistency = message.Consistency;
            var req = message.Request;
            var localValue = GetData(key.Id);

            _log.Debug("Received get for key {0}, local value {1}", key.Id, localValue);

            if (IsLocalGet(consistency))
            {
                if (localValue == null) Sender.Tell(new NotFound(key, req));
                else if (localValue.Data is DeletedData) Sender.Tell(new DataDeleted(key));
                else Sender.Tell(new GetSuccess(key, req, localValue.Data));
            }
            else
                Context.ActorOf(ReadAggregator.Props(key, consistency, req, _nodes, localValue, Sender)
                        .WithDispatcher(Context.Props.Dispatcher));
        }

        private bool IsLocalGet(IReadConsistency consistency)
        {
            if (consistency is ReadLocal)
            {
                return true;
            }
            if (consistency is ReadAll || consistency is ReadMajority)
            {
                return _nodes.Count == 0;
            }
            return false;
        }

        private void ReceiveRead(string key) => Sender.Tell(new ReadResult(GetData(key)));

        private bool MatchingRole(Member m) => _settings.Role != null && m.HasRole(_settings.Role);

        private bool IsLocalSender() => !Sender.Path.Address.HasGlobalScope;

        private void ReceiveUpdate(IKey key, Func<IReplicatedData, IReplicatedData> modify, IWriteConsistency consistency, object request)
        {
            try
            {
                var localValue = GetData(key.Id);
                IReplicatedData modifiedValue;
                if (localValue == null) modifiedValue = modify(null);
                else if (localValue.Data is DeletedData)
                {
                    _log.Debug("Received update for deleted key {0}", key);
                    Sender.Tell(new DataDeleted(key));
                    return;
                }
                else
                {
                    var existing = localValue.Data;
                    modifiedValue = existing.Merge(modify(existing));
                }

                _log.Debug("Received Update for key {0}, old data {1}, new data {2}", key, localValue, modifiedValue);
                var envelope = new DataEnvelope(PruningCleanupTombstoned(modifiedValue));
                SetData(key.Id, envelope);
                if (IsLocalUpdate(consistency)) Sender.Tell(new UpdateSuccess(key, request));
                else Context.ActorOf(WriteAggregator.Props(key, envelope, consistency, request, _nodes, Sender).WithDispatcher(Context.Props.Dispatcher));
            }
            catch (Exception ex)
            {
                _log.Debug("Received update for key {0}, failed {1}", key, ex.Message);
                Sender.Tell(new ModifyFailure(key, "Update failed: " + ex.Message, ex, request));
            }
        }

        private bool IsLocalUpdate(IWriteConsistency consistency)
        {
            if (consistency is WriteLocal)
            {
                return true;
            }
            if (consistency is WriteAll || consistency is WriteMajority)
            {
                return _nodes.Count == 0;
            }
            return false;
        }

        private void ReceiveWrite(string key, DataEnvelope envelope)
        {
            Write(key, envelope);
            Sender.Tell(WriteAck.Instance);
        }

        private void Write(string key, DataEnvelope writeEnvelope)
        {
            var envelope = GetData(key);
            if (envelope != null)
            {
                if (envelope.Data is DeletedData) return; // already deleted
                else
                {
                    var existing = envelope.Data;
                    if (existing.GetType() == writeEnvelope.Data.GetType() || writeEnvelope.Data is DeletedData)
                    {
                        var merged = envelope.Merge(PruningCleanupTombstoned(writeEnvelope).AddSeen(_selfAddress));
                        SetData(key, merged);
                    }
                    else _log.Warning("Wrong type for writing {0}, existing type {1}, got {2}", key, existing.GetType().Name, writeEnvelope.Data.GetType().Name);
                }
            }
            else SetData(key, PruningCleanupTombstoned(writeEnvelope).AddSeen(_selfAddress));
        }

        private void ReceiveReadRepair(string key, DataEnvelope writeEnvelope)
        {
            Write(key, writeEnvelope);
            Sender.Tell(ReadRepairAck.Instance);
        }

        private void ReceiveGetKeyIds()
        {
            var keys = _dataEntries.Where(kvp => !(kvp.Value.Item1.Data is DeletedData))
                                   .Select(x => x.Key)
                                   .ToImmutableHashSet();
            Sender.Tell(new GetKeysIdsResult(keys));
        }

        private void ReceiveDelete(IKey key, IWriteConsistency consistency)
        {
            var envelope = GetData(key.Id);
            if (envelope != null)
            {
                if (envelope.Data is DeletedData) Sender.Tell(new DataDeleted(key));
                else
                {
                    SetData(key.Id, DeletedEnvelope);
                    if (IsLocalUpdate(consistency)) Sender.Tell(new DeleteSuccess(key));
                    else Context.ActorOf(WriteAggregator.Props(key, DeletedEnvelope, consistency, null, _nodes, Sender)
                            .WithDispatcher(Context.Props.Dispatcher));
                }
            }
        }

        private void SetData(string key, DataEnvelope envelope)
        {
            ByteString digest;
            if (_subscribers.ContainsKey(key) && !_changed.Contains(key))
            {
                var oldDigest = GetDigest(key);
                var dig = Digest(envelope);
                if (dig != oldDigest)
                    _changed = _changed.Add(key);

                digest = dig;
            }
            else if (envelope.Data is DeletedData) digest = DeletedDigest;
            else digest = LazyDigest;

            _dataEntries = _dataEntries.SetItem(key, Tuple.Create(envelope, digest));
        }

        private ByteString GetDigest(string key)
        {
            Tuple<DataEnvelope, ByteString> value;
            var contained = _dataEntries.TryGetValue(key, out value);
            if (contained)
            {
                if (value.Item2 == LazyDigest)
                {
                    var digest = Digest(value.Item1);
                    _dataEntries = _dataEntries.SetItem(key, Tuple.Create(value.Item1, digest));
                    return digest;
                }
                else
                {
                    return value.Item2;
                }
            }
            else
            {
                return NotFoundDigest;
            }
        }

        private ByteString Digest(DataEnvelope envelope)
        {
            if (envelope.Data == DeletedData.Instance)
            {
                return DeletedDigest;
            }
            else
            {
                var bytes = _serializer.ToBinary(envelope);
                var serialized = MD5.Create().ComputeHash(bytes);
                return ByteString.FromByteBuffer(new ByteBuffer(bytes));
            }
        }

        private DataEnvelope GetData(string key)
        {
            Tuple<DataEnvelope, ByteString> value;
            bool success = _dataEntries.TryGetValue(key, out value);
            if (value == null)
            {
                return null;
            }
            return value.Item1;
        }

        private void Notify(string keyId, ISet<IActorRef> subs)
        {
            var key = _subscriptionKeys[keyId];
            var data = GetData(keyId);
            if (data != null)
            {
                var msg = data.Data is DeletedData
                    ? (object)new DataDeleted(key)
                    : new Changed(key, data.Data);

                foreach (var sub in subs) sub.Tell(msg);
            }
        }

        private void ReceiveFlushChanges()
        {
            if (_subscribers.Count != 0)
            {
                foreach (var key in _changed)
                {
                    ISet<IActorRef> subs;
                    if (_subscribers.TryGetValue(key, out subs))
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
                    ISet<IActorRef> set;
                    if (!_subscribers.TryGetValue(kvp.Key, out set))
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

        private void ReceiveGossipTick()
        {
            var node = SelectRandomNode(_nodes);
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
                var status = new Internal.Status(_dataEntries.Select(x => new KeyValuePair<string, ByteString>(x.Key, GetDigest(x.Key))).ToImmutableDictionary(), 0, 1);
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
                    var entries = _dataEntries.Where(x => Math.Abs(x.Key.GetHashCode()) % totChunks == chunk)
                                              .Select(x => new KeyValuePair<string, ByteString>(x.Key, GetDigest(x.Key)))
                                              .ToImmutableDictionary();
                    var status = new Internal.Status(entries, chunk, totChunks);
                    to.Tell(status);
                }
            }
        }

        private Address SelectRandomNode(IEnumerable<Address> addresses)
        {
            var count = addresses.Count();
            if (count != 0)
            {
                var random = ThreadLocalRandom.Current.Next(count - 1);
                return addresses.Skip(count).First();
            }

            return null;
        }

        private ActorSelection Replica(Address address) => Context.ActorSelection(Self.Path.ToStringWithAddress(address));

        private bool IsOtherDifferent(String key, ByteString otherDigest)
        {
            var d = GetDigest(key);
            return d != NotFoundDigest && d != otherDigest;
        }

        private void ReceiveStatus(IImmutableDictionary<string, ByteString> otherDigests, int chunk, int totChunks)
        {
            if (_log.IsDebugEnabled)
                _log.Debug("Received gossip status from {0}, chunk {1} of {2} containing {3}", Sender.Path.Address, chunk, totChunks, string.Join(", ", otherDigests.Keys));

            var otherDifferentKeys = otherDigests
                .Where(x => IsOtherDifferent(x.Key, x.Value))
                .Select(x => x.Key)
                .ToArray();

            var otherKeys = otherDigests.Keys.ToImmutableHashSet();
            var myKeys = (totChunks == 1
                ? _dataEntries.Keys
                : _dataEntries.Keys.Where(x => x.GetHashCode() % totChunks == chunk))
                .ToImmutableHashSet();

            var otherMissingKeys = myKeys.Except(otherKeys);

            var keys = otherDifferentKeys
                .Union(otherMissingKeys)
                .Take(_settings.MaxDeltaElements)
                .ToArray();

            if (keys.Length != 0)
            {
                if (_log.IsDebugEnabled)
                    _log.Debug("Sending gossip to {0}, containing {1}", Sender.Path.Address, string.Join(", ", keys));

                var g = new Gossip(keys.Select(k => new KeyValuePair<string, DataEnvelope>(k, GetData(k))).ToImmutableDictionary(), otherDifferentKeys.Any());
                Sender.Tell(g);
            }

            var myMissingKeys = otherKeys.Except(myKeys);
            if (!myMissingKeys.IsEmpty)
            {
                if (Context.System.Log.IsDebugEnabled)
                    Context.System.Log.Debug("Sending gossip status to {0}, requesting missing {1}", Sender.Path.Address, string.Join(", ", myMissingKeys));

                var status = new Internal.Status(myMissingKeys.Select(x => new KeyValuePair<string, ByteString>(x, NotFoundDigest)).ToImmutableDictionary(), chunk, totChunks);
                Sender.Tell(status);
            }
        }

        private void ReceiveGossip(IImmutableDictionary<string, DataEnvelope> updatedData, bool sendBack)
        {
            if (Context.System.Log.IsDebugEnabled)
            {
                Context.System.Log.Debug("Received gossip from {0}, containing {1}", Sender.Path.Address, String.Join(", ", updatedData.Keys));
            }
            var replyData = ImmutableDictionary<String, DataEnvelope>.Empty;
            foreach (var d in replyData)
            {
                var key = d.Key;
                var envelope = d.Value;
                var hadData = _dataEntries.ContainsKey(key);
                Write(key, envelope);
                if (sendBack)
                {
                    var data = GetData(key);
                    if (data != null)
                    {
                        if (hadData || data.Pruning.Any())
                        {
                            replyData = replyData.SetItem(key, data);
                        }
                    }
                }
            }
            if (sendBack && replyData.Any())
            {
                Sender.Tell(new Gossip(replyData, false));
            }
        }

        private void ReceiveSubscribe(IKey key, IActorRef subscriber)
        {
            ISet<IActorRef> set;
            if (!_subscribers.TryGetValue(key.Id, out set)) set = new HashSet<IActorRef>();
            set.Add(subscriber);

            if (!_subscriptionKeys.ContainsKey(key.Id))
                _subscriptionKeys = _subscriptionKeys.SetItem(key.Id, key);

            Context.Watch(subscriber);
        }

        private void ReceiveUnsubscribe(IKey key, IActorRef subscriber)
        {
            ISet<IActorRef> set;
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
            var keys1 = _subscribers.Where(x => x.Value.Contains(terminated))
                                    .Select(x => x.Key)
                                    .ToImmutableHashSet();

            foreach (var k in keys1)
            {
                ISet<IActorRef> set;
                if (_subscribers.TryGetValue(k, out set) && set.Remove(terminated) && set.Count == 0)
                    _subscribers.Remove(k);
            }

            var keys2 = _newSubscribers.Where(x => x.Value.Contains(terminated))
                                       .Select(x => x.Key)
                                       .ToImmutableHashSet();

            foreach (var k in keys2)
            {
                ISet<IActorRef> set;
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

        private void ReceiveMemberUp(Member m)
        {
            if (MatchingRole(m) && m.Address != _selfAddress)
                _nodes = _nodes.Add(m.Address);
        }

        private void ReceiveMemberRemoved(Member m)
        {
            if (m.Address == _selfAddress) Context.Stop(Self);
            else if (MatchingRole(m))
            {
                _nodes = _nodes.Remove(m.Address);
                _removedNodes = _removedNodes.SetItem(m.UniqueAddress, _allReachableClockTime);
                _unreachable = _unreachable.Remove(m.Address);
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

        private void ReceiveLeaderChanged(Address leader, string role)
        {
            if (role == _settings.Role) _leader = leader;
        }

        private void ReceiveClockTick()
        {
            var now = DateTime.UtcNow.Ticks * 100;
            if (_unreachable.Count == 0)
            {
                _allReachableClockTime += (now - _previousClockTime);
            }
            _previousClockTime = now;
        }

        private void ReceiveRemovedNodePruningTick()
        {
            if (IsLeader && _removedNodes.Any())
                InitRemovedNodePruning();

            PerformRemovedNodePruning();
            TombstoneRemovedNodePruning();
        }

        private bool AllPruningPerformed(UniqueAddress removed)
        {
            return _dataEntries.All(x =>
                {
                    var key = x.Key;
                    var envelope = x.Value.Item1;
                    var data = x.Value.Item1.Data;
                    var pruning = x.Value.Item1.Pruning;
                    if (data is IRemovedNodePruning)
                    {
                        var z = pruning[removed];
                        return (z != null) && !(z.Phase is PruningInitialized);
                    }
                    return false;
                });
        }

        private void TombstoneRemovedNodePruning()
        {
            foreach (var performed in _pruningPerformed)
            {
                var removed = performed.Key;
                var timestamp = performed.Value;
                if ((_allReachableClockTime - timestamp) > _maxPruningDisseminationNanos && AllPruningPerformed(removed))
                {
                    _log.Debug("All pruning performed for {0}, tombstoned", removed);
                    _pruningPerformed = _pruningPerformed.Remove(removed);
                    _removedNodes = _removedNodes.Remove(removed);
                    _tombstonedNodes = _tombstonedNodes.Add(removed);

                    foreach (var entry in _dataEntries)
                    {
                        var key = entry.Key;
                        var envelope = entry.Value.Item1;
                        var pruning = envelope.Data as IRemovedNodePruning;
                        if (pruning != null)
                        {
                            SetData(key, PruningCleanupTombstoned(envelope, removed));
                        }
                    }
                }
            }
        }

        private void PerformRemovedNodePruning()
        {
            foreach (var entry in _dataEntries)
            {
                var key = entry.Key;
                var envelope = entry.Value.Item1;
                if (entry.Value.Item1.Data is IRemovedNodePruning)
                {
                    foreach (var pruning in entry.Value.Item1.Pruning)
                    {
                        var removed = pruning.Key;
                        var owner = pruning.Value.Owner;
                        var phase = pruning.Value.Phase as PruningInitialized;
                        if (phase != null && owner == _selfUniqueAddress && (_nodes.Count == 0 || _nodes.All(x => phase.Seen.Contains(x))))
                        {
                            var newEnvelope = envelope.Prune(removed);
                            _pruningPerformed = _pruningPerformed.SetItem(removed, _allReachableClockTime);
                            _log.Debug("Perform pruning of {0} from {1} to {2}", key, removed, _selfUniqueAddress);
                            SetData(key, newEnvelope);
                        }
                    }
                }
            }
        }

        private DataEnvelope PruningCleanupTombstoned(DataEnvelope envelope)
        {
            return _tombstonedNodes.Aggregate(envelope, PruningCleanupTombstoned);
        }

        private DataEnvelope PruningCleanupTombstoned(DataEnvelope envelope, UniqueAddress removed)
        {
            var pruningCleanedup = PruningCleanupTombstoned(removed, envelope.Data);
            return (pruningCleanedup != envelope.Data) || envelope.Pruning.ContainsKey(removed)
                ? new DataEnvelope(pruningCleanedup, envelope.Pruning.Remove(removed))
                : envelope;
        }

        private IReplicatedData PruningCleanupTombstoned(IReplicatedData data)
        {
            if (_tombstonedNodes.Count == 0)
            {
                return data;
            }
            else
            {
                return _tombstonedNodes.Aggregate(data, (d, removed) =>
                    {
                        return PruningCleanupTombstoned(removed, d);
                    });
            }
        }

        private IReplicatedData PruningCleanupTombstoned(UniqueAddress removed, IReplicatedData data)
        {
            var d = data as IRemovedNodePruning;
            if (d != null)
            {
                if (d.NeedPruningFrom(removed))
                {
                    return d.PruningCleanup(removed);
                }
            }
            return data;
        }

        private void InitRemovedNodePruning()
        {
            var removedSet = _removedNodes.Where(x => (_allReachableClockTime - x.Value) > _maxPruningDisseminationNanos)
                                          .Select(x => x.Key)
                                          .ToImmutableHashSet();

            if (removedSet.Any())
            {
                foreach (var entry in _dataEntries)
                {
                    var key = entry.Key;
                    var envelope = entry.Value.Item1;

                    foreach (var removed in removedSet)
                    {
                        Action init = () =>
                            {
                                var newEnvelope = envelope.InitRemovedNodePruning(removed, _selfUniqueAddress);
                                Context.System.Log.Debug("Initiating pruning of {0} with data {1}", removed, key);
                                SetData(key, newEnvelope);
                            };
                        if (envelope.NeedPruningFrom(removed))
                        {
                            if (envelope.Data is IRemovedNodePruning)
                            {
                                var res = envelope.Pruning[removed];
                                if (res == null)
                                {
                                    init();
                                }
                                else if (res.Phase is PruningInitialized && res.Owner == _selfUniqueAddress)
                                {
                                    init();
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ReceiveGetReplicaCount() => Sender.Tell(new ReplicaCount(_nodes.Count + 1));
    }
}
