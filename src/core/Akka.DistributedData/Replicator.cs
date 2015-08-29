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
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public class Replicator : ActorBase
    {
        static readonly ByteString DeletedDigest = ByteString.Empty;
        static readonly ByteString LazyDigest = ByteString.Create(new byte[] { 0 }, 0, 1);
        static readonly ByteString NotFoundDigest = ByteString.Create(new byte[] { 255 }, 0, 1);

        static readonly DataEnvelope DeletedEnvelope = new DataEnvelope(DeletedData.Instance);

        readonly ReplicatorSettings _settings;

        readonly Cluster.Cluster _cluster;
        readonly Address _selfAddress;
        readonly UniqueAddress _selfUniqueAddress;

        readonly ICancelable _gossipTask;
        readonly ICancelable _notifyTask;
        readonly ICancelable _pruningTask;
        readonly ICancelable _clockTask;

        readonly Serializer _serializer;
        readonly long _maxPruningDisseminationNanos;

        IImmutableSet<Address> _nodes;

        IImmutableDictionary<UniqueAddress, long> _removedNodes;
        IImmutableDictionary<UniqueAddress, long> _pruningPerformed;
        IImmutableSet<UniqueAddress> _tombstonedNodes;

        Address _leader = null;
        bool IsLeader
        {
            get { return _leader != null && _leader == _selfAddress; }
        }

        long _previousClockTime;
        long _allReachableClockTime = 0;
        IImmutableSet<Address> _unreachable;

        IImmutableDictionary<string, Tuple<DataEnvelope, ByteString>> _dataEntries;
        IImmutableSet<string> _changed;

        long _statusCount = 0;
        int _statusTotChunks = 0;

        readonly Dictionary<string, ISet<IActorRef>> _subscribers = new Dictionary<string, ISet<IActorRef>>();
        readonly Dictionary<string, ISet<IActorRef>> _newSubscribers = new Dictionary<string, ISet<IActorRef>>();
        IImmutableDictionary<string, IKey> _subscriptionKeys;


        readonly System.Reflection.MethodInfo _getMethodInfo;
        readonly System.Reflection.MethodInfo _updateMethodInfo;
        readonly System.Reflection.MethodInfo _deleteMethodInfo;
        readonly System.Reflection.MethodInfo _subscribeMethodInfo;
        readonly System.Reflection.MethodInfo _unsubscribeMethodInfo;

        public Replicator(ReplicatorSettings settings)
        {
            _settings = settings;
            _cluster = Cluster.Cluster.Get(Context.System);
            _selfAddress = _cluster.SelfAddress;
            _selfUniqueAddress = _cluster.SelfUniqueAddress;

            if (_cluster.IsTerminated)
            {
                throw new ArgumentException("Cluster node must not be terminated");
            }
            if (!String.IsNullOrEmpty(_settings.Role) && !_cluster.SelfRoles.Contains(_settings.Role))
            {
                throw new ArgumentException(String.Format("The cluster node {0} does not have the role {1}", _selfAddress, _settings.Role));
            }

            //_gossipTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, GossipTick.Instance, Self);
            _notifyTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.NotifySubscribersInterval, _settings.NotifySubscribersInterval, Self, FlushChanges.Instance, Self);
            //_pruningTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.PruningInterval, _settings.PruningInterval, Self, RemovedNodePruningTick.Instance, Self);
            //_clockTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, ClockTick.Instance, Self);

            _serializer = Context.System.Serialization.FindSerializerForType(typeof(DataEnvelope));
            _maxPruningDisseminationNanos = (long)_settings.MaxPruningDissemination.Ticks * 100;

            _nodes = ImmutableHashSet<Address>.Empty;

            _removedNodes = ImmutableDictionary<UniqueAddress, long>.Empty;
            _pruningPerformed = ImmutableDictionary<UniqueAddress, long>.Empty;
            _tombstonedNodes = ImmutableHashSet<UniqueAddress>.Empty;

            _previousClockTime = MonotonicClock.GetNanos();
            _unreachable = ImmutableHashSet<Address>.Empty;

            _dataEntries = ImmutableDictionary<string, Tuple<DataEnvelope, ByteString>>.Empty;
            _changed = ImmutableHashSet<string>.Empty;

            _subscriptionKeys = ImmutableDictionary<string, IKey>.Empty;

            var methods = GetType().GetMethods();

            _getMethodInfo = (MethodInfo)GetType().GetMember("ReceiveGet*", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod)[0];
            _updateMethodInfo = (MethodInfo)GetType().GetMember("ReceiveUpdate*", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod)[0];
            _deleteMethodInfo = (MethodInfo)GetType().GetMember("ReceiveDelete*", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod)[0];
            _subscribeMethodInfo = (MethodInfo)GetType().GetMember("ReceiveSubscribe*", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod)[0];
            _unsubscribeMethodInfo = (MethodInfo)GetType().GetMember("ReceiveUnsubscribe*", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod)[0];
        }

        public static Props GetProps(ReplicatorSettings settings)
        {
            return Props.Create(() => new Replicator(settings)).WithDeploy(Deploy.Local).WithDispatcher(settings.Dispatcher);
        }

        protected override void PreStart()
        {
            var leaderChangedClass = _settings.Role != null ? typeof(ClusterEvent.RoleLeaderChanged) : typeof(ClusterEvent.LeaderChanged);
            _cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, new[] { typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent), leaderChangedClass });
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            //_gossipTask.Cancel();
            _notifyTask.Cancel();
            //_pruningTask.Cancel();
            //_clockTask.Cancel();
        }

        protected override void PreRestart(Exception reason, object message)
        {
            base.PreRestart(reason, message);
        }

        protected override bool Receive(object message)
        {
            return message.Match()
                          .With<IGet>(g => 
                              {
                                  var genericArg = g.Key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
                                  var genericMethod = _getMethodInfo.MakeGenericMethod(genericArg);
                                  genericMethod.Invoke(this, new object[] { g.Key, g.Consistency, g.Request });
                              })
                          .With<IUpdate>(u => 
                              {
                                  var genericArg = u.Key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
                                  var genericMethod = _updateMethodInfo.MakeGenericMethod(genericArg);
                                  genericMethod.Invoke(this, new object[] { u.Key, u.Modify, u.Consistency, u.Request });
                              })
                          .With<Read>(r => ReceiveRead(r.Key))
                          .With<Write>(w => ReceiveWrite(w.Key, w.Envelope))
                          .With<ReadRepair>(rr => ReceiveReadRepair(rr.Key, rr.Envelope))
                          .With<FlushChanges>(x => ReceiveFlushChanges())
                          .With<GossipTick>(_ => ReceiveGossipTick())
                          .With<ClockTick>(c => ReceiveClockTick())
                          .With<Akka.DistributedData.Internal.Status>(s => ReceiveStatus(s.Digests, s.Chunk, s.TotChunks))
                          .With<Gossip>(g => ReceiveGossip(g.UpdatedData, g.SendBack))
                          .With<ISubscribe>(s => _subscribeMethodInfo.MakeGenericMethod(s.Key.GetType().GetInterface("IKey`1").GetGenericArguments()[0]).Invoke(this, new object[] { s.Key, s.Subscriber }))
                          .With<IUnsubscribe>(u => _unsubscribeMethodInfo.Invoke(this, new object[] { u.Key, u.Subscriber }))
                          .With<Terminated>(t => ReceiveTerminated(t.ActorRef))
                          .With<ClusterEvent.MemberUp>(m => ReceiveMemberUp(m.Member))
                          .With<ClusterEvent.MemberRemoved>(m => ReceiveMemberRemoved(m.Member))
                          .With<ClusterEvent.IMemberEvent>(_ => { })
                          .With<ClusterEvent.UnreachableMember>(u => ReceiveUnreachable(u.Member))
                          .With<ClusterEvent.ReachableMember>(r => ReceiveReachable(r.Member))
                          .With<ClusterEvent.LeaderChanged>(l => ReceiveLeaderChanged(l.Leader, null))
                          .With<ClusterEvent.RoleLeaderChanged>(r => ReceiveLeaderChanged(r.Leader, r.Role))
                          .With<GetKeyIds>(_ => GetKeyIds())
                          .With<IDelete>(d => _deleteMethodInfo.Invoke(this, new object[] { d.Key, d.Consistency }))
                          .With<RemovedNodePruningTick>(r => ReceiveRemovedNodePruningTick())
                          .With<GetReplicaCount>(_ => GetReplicaCount())
                          .WasHandled;
        }

        private void ReceiveGet<T>(Key<T> key, IReadConsistency consistency, Object req) where T : IReplicatedData
        {
            var localValue = GetData(key.Id);
            Context.System.Log.Debug("Received get for key {0}, local value {1}", key.Id, localValue);
            if (IsLocalGet(consistency))
            {
                if (localValue == null)
                {
                    Sender.Tell(new NotFound<T>(key, req));
                }
                else if (localValue.Data == DeletedData.Instance)
                {
                    Sender.Tell(new DataDeleted<T>(key));
                }
                else
                {
                    Sender.Tell(new GetSuccess<T>(key, req, (T)localValue.Data));
                }
            }
            else
            {
                Context.ActorOf(ReadAggregator<T>.GetProps<T>(key, consistency, req, _nodes, localValue, Sender).WithDispatcher(Context.Props.Dispatcher));
            }
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

        private void ReceiveRead(string key)
        {
            Sender.Tell(new ReadResult(GetData(key)));
        }

        private bool MatchingRole(Member m)
        {
            return _settings.Role != null && m.HasRole(_settings.Role);
        }

        private bool IsLocalSender()
        {
            return !Sender.Path.Address.HasGlobalScope;
        }

        private void ReceiveUpdate<T>(Key<T> key, Func<IReplicatedData, IReplicatedData> modify, IWriteConsistency consistency, object request) where T : IReplicatedData
        {
            try
            {
                var localValue = _dataEntries.GetValueOrDefault(key.Id);
                IReplicatedData modifiedValue;
                if(localValue == null)
                {
                    modifiedValue = modify(null);
                }
                else if(localValue.Item1.Data == DeletedData.Instance)
                {
                    Context.System.Log.Debug("Received update for deleted key {0}", key);
                    Sender.Tell(new DataDeleted<T>(key));
                    return;
                }
                else
                {
                    var env = localValue.Item1;
                    var existing = env.Data;
                    modifiedValue = existing.Merge(modify(existing));
                }

                Context.System.Log.Debug("Received Update for key {0}, old data {1}, new data {2}", key, localValue, modifiedValue);
                var envelope = new DataEnvelope(PruningCleanupTombstoned(modifiedValue));
                SetData(key.Id, envelope);
                if(IsLocalUpdate(consistency))
                {
                    Sender.Tell(new UpdateSuccess<T>(key, request));
                }
                else
                {
                    Context.ActorOf(WriteAggregator<T>.GetProps(key, envelope, consistency, request, _nodes, Sender).WithDispatcher(Context.Props.Dispatcher));
                }
            }
            catch(Exception ex)
            {
                Context.System.Log.Debug("Received update for key {0}, failed {1}", key, ex.Message);
                Sender.Tell(new ModifyFailure<T>(key, "Update failed: " + ex.Message, ex, request));
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
                var existing = envelope.Data;
                if (existing != DeletedData.Instance)
                {
                    if (existing.GetType() == writeEnvelope.Data.GetType() || writeEnvelope.Data == DeletedData.Instance)
                    {
                        var merged = envelope.Merge(PruningCleanupTombstoned(writeEnvelope).AddSeen(_selfAddress));
                        SetData(key, merged);
                    }
                    else
                    {
                        Context.System.Log.Warning("Wrong type for writing {0}, existing type {1}, got {2}", key, existing.GetType().Name, writeEnvelope.Data.GetType().Name);
                    }
                }
            }
            else
            {
                SetData(key, PruningCleanupTombstoned(writeEnvelope).AddSeen(_selfAddress));
            }
        }

        private void ReceiveReadRepair(string key, DataEnvelope writeEnvelope)
        {
            Write(key, writeEnvelope);
            Sender.Tell(ReadRepairAck.Instance);
        }

        private void GetKeyIds()
        {
            var keys = _dataEntries.Where(kvp => kvp.Value.Item1 != DataEnvelope.DeletedEnvelope)
                                   .Select(x => x.Key)
                                   .ToImmutableHashSet();
            Sender.Tell(new GetKeysIdsResult(keys));
        }

        private void ReceiveDelete<T>(Key<T> key, IWriteConsistency consistency) where T : IReplicatedData
        {
            Tuple<DataEnvelope, ByteString> value;
            var contained = _dataEntries.TryGetValue(key.Id, out value);
            if (contained)
            {
                if (value.Item1.Data == DeletedData.Instance)
                {
                    Sender.Tell(new DataDeleted<T>(key));
                }
                else
                {
                    SetData(key.Id, DeletedEnvelope);
                    if (IsLocalUpdate(consistency))
                    {
                        Sender.Tell(new DeleteSuccess<T>(key));
                    }
                    else
                    {
                        Context.ActorOf(WriteAggregator<T>.GetProps<T>(key, DeletedEnvelope, consistency, null, _nodes, Sender).WithDispatcher(Context.Props.Dispatcher));
                    }
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
                {
                    _changed = _changed.Add(key);
                }
                digest = dig;
            }
            else if (envelope.Data == DeletedData.Instance)
            {
                digest = DeletedDigest;
            }
            else
            {
                digest = LazyDigest;
            }
            _dataEntries = _dataEntries.SetItem(key, Tuple.Create(envelope, digest));
        }

        private ByteString GetDigest(String key)
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
                object msg;
                var keyType = key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
                if (data.Data.Equals(DeletedData.Instance))
                {
                    var type = typeof(DataDeleted<>).MakeGenericType(keyType);
                    msg = Activator.CreateInstance(type, key);
                }
                else
                {
                    var type = typeof(Changed<>).MakeGenericType(keyType);
                    msg = Activator.CreateInstance(type, key, data.Data);
                }
                foreach (var sub in subs)
                {
                    sub.Tell(msg);
                }
            }
        }

        private void ReceiveFlushChanges()
        {
            if (_subscribers.Any())
            {
                foreach (var key in _changed)
                {
                    if (_subscribers.ContainsKey(key))
                    {
                        Notify(key, _subscribers[key]);
                    }
                }
            }

            if (_newSubscribers.Any())
            {
                foreach (var kvp in _newSubscribers)
                {
                    Notify(kvp.Key, kvp.Value);
                    foreach (var sub in kvp.Value)
                    {
                        _subscribers.AddBinding(kvp.Key, sub);
                    }
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
                var status = new Akka.DistributedData.Internal.Status(_dataEntries.Select(x => new KeyValuePair<string, ByteString>(x.Key, GetDigest(x.Key))).ToImmutableDictionary(), 0, 1);
                to.Tell(status);
            }
            else
            {
                var totChunks = _dataEntries.Count / _settings.MaxDeltaElements;
                for (var i = 1; i <= Math.Min(totChunks, 10); i++)
                {
                    if (totChunks == _statusTotChunks)
                    {
                        _statusCount += 1;
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
                    var status = new Akka.DistributedData.Internal.Status(entries, chunk, totChunks);
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
            else
            {
                return null;
            }
        }

        private ActorSelection Replica(Address address)
        {
            return Context.ActorSelection(Self.Path.ToStringWithAddress(address));
        }

        private bool IsOtherDifferent(String key, ByteString otherDigest)
        {
            var d = GetDigest(key);
            return d != NotFoundDigest && d != otherDigest;
        }

        private void ReceiveStatus(IImmutableDictionary<string, ByteString> otherDigests, int chunk, int totChunks)
        {
            if (Context.System.Log.IsDebugEnabled)
            {
                var k = String.Join(", ", otherDigests.Keys);
                Context.System.Log.Debug("Received gossip status from {0}, chunk {1} of {2} containing {3}", Sender.Path.Address, chunk, totChunks, k);
            }

            var otherDifferentKeys = otherDigests.Where(x => IsOtherDifferent(x.Key, x.Value))
                                                 .Select(x => x.Key);

            var otherKeys = otherDigests.Keys.ToImmutableHashSet();
            var myKeys = (totChunks == 1 ? _dataEntries.Keys : _dataEntries.Keys.Where(x => x.GetHashCode() % totChunks == chunk)).ToImmutableHashSet();

            var otherMissingKeys = myKeys.Except(otherKeys);

            var keys = otherDifferentKeys.Union(otherMissingKeys).Take(_settings.MaxDeltaElements);

            if (keys.Any())
            {
                if (Context.System.Log.IsDebugEnabled)
                {
                    Context.System.Log.Debug("Sending gossip to {0}, containing {1}", Sender.Path.Address, String.Join(", ", keys));
                }
                var g = new Akka.DistributedData.Internal.Gossip(keys.Select(k => new KeyValuePair<string, DataEnvelope>(k, GetData(k))).ToImmutableDictionary(), otherDifferentKeys.Any());
                Sender.Tell(g);
            }
            var myMissingKeys = otherKeys.Except(myKeys);
            if (myMissingKeys.Any())
            {
                if (Context.System.Log.IsDebugEnabled)
                {
                    Context.System.Log.Debug("Sending gossip status to {0}, requesting missing {1}", Sender.Path.Address, String.Join(", ", myMissingKeys));
                }
                var status = new Akka.DistributedData.Internal.Status(myMissingKeys.Select(x => new KeyValuePair<string, ByteString>(x, NotFoundDigest)).ToImmutableDictionary(), chunk, totChunks);
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

        private void ReceiveSubscribe<T>(Key<T> key, IActorRef subscriber) where T : IReplicatedData
        {
            _newSubscribers.AddBinding(key.Id, subscriber);
            if (!_subscriptionKeys.ContainsKey(key.Id))
            {
                _subscriptionKeys = _subscriptionKeys.SetItem(key.Id, (IKey)key);
            }
            Context.Watch(subscriber);
        }

        private void ReceiveUnsubscribe<T>(Key<T> key, IActorRef subscriber) where T : IReplicatedData
        {
            _subscribers.RemoveBinding(key.Id, subscriber);
            _newSubscribers.RemoveBinding(key.Id, subscriber);
            if (!HasSubscriber(subscriber))
            {
                Context.Unwatch(subscriber);
            }
            if (!_subscribers.ContainsKey(key.Id) || !_newSubscribers.ContainsKey(key.Id))
            {
                _subscriptionKeys = _subscriptionKeys.Remove(key.Id);
            }
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
                _subscribers.RemoveBinding(k, terminated);
            }
            var keys2 = _newSubscribers.Where(x => x.Value.Contains(terminated))
                                       .Select(x => x.Key)
                                       .ToImmutableHashSet();
            foreach (var k in keys2)
            {
                _newSubscribers.RemoveBinding(k, terminated);
            }
            var allKeys = keys1.Union(keys2);
            foreach (var k in allKeys)
            {
                if (!_subscribers.ContainsKey(k) && !_newSubscribers.ContainsKey(k))
                {
                    _subscriptionKeys = _subscriptionKeys.Remove(k);
                }
            }
        }

        private void ReceiveMemberUp(Member m)
        {
            if (MatchingRole(m) && m.Address != _selfAddress)
            {
                _nodes = _nodes.Add(m.Address);
            }
        }

        private void ReceiveMemberRemoved(Member m)
        {
            if (m.Address == _selfAddress)
            {
                Context.Stop(Self);
            }
            else if (MatchingRole(m))
            {
                _nodes = _nodes.Remove(m.Address);
                _removedNodes = _removedNodes.SetItem(m.UniqueAddress, _allReachableClockTime);
                _unreachable = _unreachable.Remove(m.Address);
            }
        }

        private void ReceiveUnreachable(Member m)
        {
            if (MatchingRole(m))
            {
                _unreachable = _unreachable.Add(m.Address);
            }
        }

        private void ReceiveReachable(Member m)
        {
            if (MatchingRole(m))
            {
                _unreachable = _unreachable.Remove(m.Address);
            }
        }

        private void ReceiveLeaderChanged(Address leader, string role)
        {
            if (role == _settings.Role)
            {
                _leader = leader;
            }
        }

        private void ReceiveClockTick()
        {
            var now = MonotonicClock.GetNanos();
            if (_unreachable.Count == 0)
            {
                _allReachableClockTime += (now - _previousClockTime);
            }
            _previousClockTime = now;
        }

        private void ReceiveRemovedNodePruningTick()
        {
            if (IsLeader && _removedNodes.Any())
            {
                InitRemovedNodePruning();
            }
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
                    Context.System.Log.Debug("All pruning performed for {0}, tombstoned", removed);
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
                            SetData(key, PruningCleanupTombstoned(removed, envelope));
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
                            Context.System.Log.Debug("Perform pruning of {0} from {1} to {2}", key, removed, _selfUniqueAddress);
                            SetData(key, newEnvelope);
                        }
                    }
                }
            }
        }

        private DataEnvelope PruningCleanupTombstoned(DataEnvelope envelope)
        {
            return _tombstonedNodes.Aggregate(envelope, (env, address) =>
                {
                    return PruningCleanupTombstoned(address, env);
                });
        }

        private DataEnvelope PruningCleanupTombstoned(UniqueAddress removed, DataEnvelope envelope)
        {
            var pruningCleanedup = PruningCleanupTombstoned(removed, envelope.Data);
            if ((pruningCleanedup != envelope.Data) || envelope.Pruning.ContainsKey(removed))
                return new DataEnvelope(pruningCleanedup, envelope.Pruning.Remove(removed));
            else
                return envelope;
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

        private void GetReplicaCount()
        {
            Sender.Tell(new ReplicaCount(_nodes.Count + 1));
        }
    }
}
