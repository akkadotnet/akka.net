//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Event;
using Akka.Pattern;
using Akka.Routing;
using Akka.Util;
using Status = Akka.Cluster.Tools.PublishSubscribe.Internal.Status;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    /// <summary>
    /// <para>
    /// This actor manages a registry of actor references and replicates
    /// the entries to peer actors among all cluster nodes or a group of nodes
    /// tagged with a specific role.
    /// </para>
    /// <para>
    /// The <see cref="DistributedPubSubMediator"/> is supposed to be started on all nodes,
    /// or all nodes with specified role, in the cluster. The mediator can be
    /// started with the <see cref="DistributedPubSub"/> extension or as an ordinary actor.
    /// </para>
    /// <para>
    /// Changes are only performed in the own part of the registry and those changes
    /// are versioned. Deltas are disseminated in a scalable way to other nodes with
    /// a gossip protocol. The registry is eventually consistent, i.e. changes are not
    /// immediately visible at other nodes, but typically they will be fully replicated
    /// to all other nodes after a few seconds.
    /// </para>
    /// <para>
    /// You can send messages via the mediator on any node to registered actors on
    /// any other node. There is three modes of message delivery.
    /// </para>
    /// <para>
    /// 1. <see cref="Send"/> -
    /// The message will be delivered to one recipient with a matching path, if any such
    /// exists in the registry. If several entries match the path the message will be sent
    /// via the supplied `routingLogic` (default random) to one destination. The sender of the
    /// message can specify that local affinity is preferred, i.e. the message is sent to an actor
    /// in the same local actor system as the used mediator actor, if any such exists, otherwise
    /// route to any other matching entry. A typical usage of this mode is private chat to one
    /// other user in an instant messaging application. It can also be used for distributing
    /// tasks to registered workers, like a cluster aware router where the routees dynamically
    /// can register themselves.
    /// </para>
    /// <para>
    /// 2. <see cref="SendToAll"/> -
    /// The message will be delivered to all recipients with a matching path. Actors with
    /// the same path, without address information, can be registered on different nodes.
    /// On each node there can only be one such actor, since the path is unique within one
    /// local actor system. Typical usage of this mode is to broadcast messages to all replicas
    /// with the same path, e.g. 3 actors on different nodes that all perform the same actions,
    /// for redundancy.
    /// </para>
    /// <para>
    /// 3. <see cref="Publish"/> -
    /// Actors may be registered to a named topic instead of path. This enables many subscribers
    /// on each node. The message will be delivered to all subscribers of the topic. For
    /// efficiency the message is sent over the wire only once per node (that has a matching topic),
    /// and then delivered to all subscribers of the local topic representation. This is the
    /// true pub/sub mode. A typical usage of this mode is a chat room in an instant messaging
    /// application.
    /// </para>
    /// <para>
    /// 4. <see cref="Publish"/> with sendOneMessageToEachGroup -
    /// Actors may be subscribed to a named topic with an optional property `group`.
    /// If subscribing with a group name, each message published to a topic with the
    /// `sendOneMessageToEachGroup` flag is delivered via the supplied `routingLogic`
    /// (default random) to one actor within each subscribing group.
    /// If all the subscribed actors have the same group name, then this works just like
    /// <see cref="Send"/> and all messages are delivered to one subscribe.
    /// If all the subscribed actors have different group names, then this works like normal
    /// <see cref="Publish"/> and all messages are broadcast to all subscribers.
    /// </para>
    /// <para>
    /// You register actors to the local mediator with <see cref="Put"/> or
    /// <see cref="Subscribe"/>. `Put` is used together with `Send` and
    /// `SendToAll` message delivery modes. The `ActorRef` in `Put` must belong to the same
    /// local actor system as the mediator. `Subscribe` is used together with `Publish`.
    /// Actors are automatically removed from the registry when they are terminated, or you
    /// can explicitly remove entries with <see cref="Remove"/> or
    /// <see cref="Unsubscribe"/>.
    /// </para>
    /// <para>
    /// Successful `Subscribe` and `Unsubscribe` is acknowledged with
    /// <see cref="SubscribeAck"/> and <see cref="UnsubscribeAck"/>
    /// replies.
    /// </para>
    /// </summary>
    public class DistributedPubSubMediator : ReceiveActor, IWithTimers
    {
        private const string GossipTimerKey = "GossipTimer";
        private const string PruneTimerKey = "PruneTimer";
        
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props(DistributedPubSubSettings settings)
        {
            return Actor.Props.Create(() => new DistributedPubSubMediator(settings)).WithDeploy(Deploy.Local);
        }

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly string _role;
        private readonly DistributedPubSubSettings _settings;
        private readonly TimeSpan _pruneInterval;
        private readonly PerGroupingBuffer _buffer;

        private ISet<Address> _nodes = new HashSet<Address>();
        private long _deltaCount;
        private readonly ILoggingAdapter _log;
        private readonly Dictionary<Address, Bucket> _registry = new();

        private readonly string _topicPrefix;
        private readonly PubSubCache _cache;

        public ITimerScheduler Timers { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableDictionary<Address, long> OwnVersions
        {
            get
            {
                return _registry
                    .Select(entry => new KeyValuePair<Address, long>(entry.Key, entry.Value.Version))
                    .ToImmutableDictionary(kv => kv.Key, kv => kv.Value);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public DistributedPubSubMediator(DistributedPubSubSettings settings)
        {
            if (settings.RoutingLogic is ConsistentHashingRoutingLogic)
                throw new ArgumentException("Consistent hashing routing logic cannot be used by the pub-sub mediator");

            _settings = settings;

            if (!string.IsNullOrEmpty(_settings.Role) && !_cluster.SelfRoles.Contains(_settings.Role))
                throw new ArgumentException($"The cluster member [{_cluster.SelfAddress}] doesn't have the role [{_settings.Role}]");

            _log = Context.GetLogger();
            
            _role = settings.Role;
            _pruneInterval = new TimeSpan(_settings.RemovedTimeToLive.Ticks / 2);
            _buffer = new PerGroupingBuffer();

            _topicPrefix = Self.Path.ToStringWithoutAddress();
            _cache = new PubSubCache();
            
            Receive<Send>(send =>
            {
                var routees = new List<Routee>();
                if (_registry.TryGetValue(_cluster.SelfAddress, out var bucket) &&
                    bucket.Content.TryGetValue(send.Path, out var valueHolder) &&
                    send.LocalAffinity)
                {
                    var routee = valueHolder.Routee;
                    if (routee != null)
                        routees.Add(routee);
                }
                else
                {
                    foreach (var entry in _registry)
                    {
                        if (entry.Value.Content.TryGetValue(send.Path, out valueHolder))
                        {
                            var routee = valueHolder.Routee;
                            if (routee != null)
                                routees.Add(routee);
                        }
                    }
                }

                if (routees.Count != 0)
                    new Router(_settings.RoutingLogic, routees.ToArray()).Route(
                        Internal.Utils.WrapIfNeeded(send.Message), Sender);
                else
                    IgnoreOrSendToDeadLetters(send);
            });
            Receive<SendToAll>(sendToAll =>
            {
                // TODO: Investigate this, this code looks very sketchy
                PublishMessage(sendToAll.Path, sendToAll, sendToAll.ExcludeSelf);
            });
            Receive<Publish>(publish =>
            {
                var encodedTopic = _cache.EncodeName(publish.Topic);
                var key = _cache.MakeKey(Self.Path, encodedTopic);
                if (publish.SendOneMessageToEachGroup)
                    PublishToEachGroup(key, publish);
                else
                    PublishMessage(key, publish);
            });
            Receive<Put>(put =>
            {
                if (put.Ref.Path.Address.HasGlobalScope)
                    _log.Warning("Registered actor must be local: [{0}]", put.Ref);
                else
                {
                    PutToRegistry(Internal.Utils.MakeKey(put.Ref), put.Ref);
                    Context.Watch(put.Ref);
                }
            });
            Receive<Remove>(remove =>
            {
                if (!_registry.TryGetValue(_cluster.SelfAddress, out var bucket)) 
                    return;
                if (!bucket.Content.TryGetValue(remove.Path, out var valueHolder) || valueHolder.Ref.IsNobody()) 
                    return;
                
                Context.Unwatch(valueHolder.Ref);
                PutToRegistry(remove.Path, null);
            });
            Receive<Subscribe>(subscribe =>
            {
                // each topic is managed by a child actor with the same name as the topic
                var encodedTopic = _cache.EncodeName(subscribe.Topic);

                _buffer.BufferOr(_cache.MakeKey(Self.Path, encodedTopic), subscribe, Sender, () =>
                {
                    var child = Context.Child(encodedTopic);
                    if (!child.IsNobody())
                        child.Forward(subscribe);
                    else
                        NewTopicActor(encodedTopic).Forward(subscribe);
                });
            });
            Receive<RegisterTopic>(register =>
            {
                HandleRegisterTopic(register.TopicRef);
            });
            Receive<NoMoreSubscribers>(_ =>
            {
                var key = Internal.Utils.MakeKey(Sender);
                _buffer.InitializeGrouping(key);
                Sender.Tell(TerminateRequest.Instance);
            });
            Receive<NewSubscriberArrived>(_ =>
            {
                var key = Internal.Utils.MakeKey(Sender);
                _buffer.ForwardMessages(key, Sender);
            });
            Receive<GetTopics>(_ =>
            {
                Sender.Tell(new CurrentTopics(GetCurrentTopics().ToImmutableHashSet()));
            });
            Receive<Subscribed>(subscribed =>
            {
                subscribed.Subscriber.Tell(subscribed.Ack);
            });
            Receive<Unsubscribe>(unsubscribe =>
            {
                var encodedTopic = _cache.EncodeName(unsubscribe.Topic);

                _buffer.BufferOr(_cache.MakeKey(Self.Path, encodedTopic), unsubscribe, Sender, () =>
                {
                    var child = Context.Child(encodedTopic);
                    if (!child.IsNobody())
                        child.Forward(unsubscribe);
                    else
                    {
                        // no such topic here
                        _cache.TryRemoveTopic(unsubscribe.Topic);
                    }
                });
            });
            Receive<Unsubscribed>(unsubscribed =>
            {
                unsubscribed.Subscriber.Tell(unsubscribed.Ack);
            });
            Receive<Status>(status =>
            {
                // only accept status from known nodes, otherwise old cluster with same address may interact
                // also accept from local for testing purposes
                if (!_nodes.Contains(Sender.Path.Address) && !Sender.Path.Address.HasLocalScope) 
                    return;
                
                // gossip chat starts with a Status message, containing the bucket versions of the other node
                var delta = CollectDelta(status.Versions).ToImmutableList();
                if (delta.Count != 0)
                    Sender.Tell(new Delta(delta));

                if (!status.IsReplyToStatus && OtherHasNewerVersions(status.Versions))
                    Sender.Tell(new Status(versions: OwnVersions, isReplyToStatus: true)); // it will reply with Delta
            });
            Receive<Delta>(delta =>
            {
                _deltaCount += 1;

                // reply from Status message in the gossip chat
                // the Delta contains potential updates (newer versions) from the other node
                // only accept deltas/buckets from known nodes, otherwise there is a risk of
                // adding back entries when nodes are removed
                if (_nodes.Contains(Sender.Path.Address))
                {
                    foreach (var bucket in delta.Buckets)
                    {
                        if (_nodes.Contains(bucket.Owner))
                        {
                            if (!_registry.TryGetValue(bucket.Owner, out var myBucket))
                                myBucket = new Bucket(bucket.Owner);

                            if (bucket.Version > myBucket.Version)
                                _registry[bucket.Owner] = new Bucket(myBucket.Owner, bucket.Version, myBucket.Content.SetItems(bucket.Content));
                        }
                    }
                }
            });
            Receive<GossipTick>(_ => HandleGossip());
            Receive<Prune>(_ => HandlePrune());
            Receive<Terminated>(terminated =>
            {
                var key = Internal.Utils.MakeKey(terminated.ActorRef);

                if (_registry.TryGetValue(_cluster.SelfAddress, out var bucket))
                {
                    if (bucket.Content.TryGetValue(key, out var holder) && terminated.ActorRef.Equals(holder.Ref))
                    {
                        PutToRegistry(key, null); // remove
                        _cache.TryRemoveKey(key, _topicPrefix);
                    }
                }

                _buffer.RecreateAndForwardMessagesIfNeeded(key, () => NewTopicActor(terminated.ActorRef.Path.Name));
            });
            Receive<ClusterEvent.CurrentClusterState>(state =>
            {
                var nodes = state.Members
                    .Where(m => m.Status != MemberStatus.Joining && IsMatchingRole(m, _role))
                    .Select(m => m.Address);

                _nodes = new HashSet<Address>(nodes);
            });
            Receive<ClusterEvent.MemberUp>(up =>
            {
                if (IsMatchingRole(up.Member, _role)) _nodes.Add(up.Member.Address);
            });
            Receive<ClusterEvent.MemberWeaklyUp>(weaklyUp =>
            {
                if (IsMatchingRole(weaklyUp.Member, _role)) _nodes.Add(weaklyUp.Member.Address);
            });
            Receive<ClusterEvent.MemberLeft>(left =>
            {
                if (!IsMatchingRole(left.Member, _role)) 
                    return;
                
                _nodes.Remove(left.Member.Address);
                _registry.Remove(left.Member.Address);
            });
            Receive<ClusterEvent.MemberDowned>(downed =>
            {
                if (!IsMatchingRole(downed.Member, _role)) 
                    return;
                
                _nodes.Remove(downed.Member.Address);
                _registry.Remove(downed.Member.Address);
            });
            Receive<ClusterEvent.MemberRemoved>(removed =>
            {
                var member = removed.Member;
                if (member.Address == _cluster.SelfAddress)
                {
                    Context.Stop(Self);
                    _cache.Clear();
                }
                else if (IsMatchingRole(member, _role))
                {
                    _nodes.Remove(member.Address);
                    _registry.Remove(member.Address);
                }
            });
            Receive<ClusterEvent.IMemberEvent>(_ => { /* ignore */ });
            Receive<Count>(_ =>
            {
                var count = _registry.Sum(entry => entry.Value.Content.Count(kv => !kv.Value.Ref.IsNobody()));
                Sender.Tell(count);
            });
            Receive<DeltaCount>(_ =>
            {
                Sender.Tell(_deltaCount);
            });
            Receive<CountSubscribers>(msg =>
            {
                var encTopic = _cache.EncodeName(msg.Topic);
                _buffer.BufferOr(_cache.MakeKey(Self.Path, encTopic), msg, Sender, () =>
                {
                    var child = Context.Child(encTopic);
                    if (!child.IsNobody())
                    {
                        child.Tell(Count.Instance, Sender);
                    }
                    else
                    {
                        Sender.Tell(0);
                    }
                });
            });
        }

        private bool OtherHasNewerVersions(IImmutableDictionary<Address, long> versions)
        {
            return versions.Any(entry =>
            {
                if (_registry.TryGetValue(entry.Key, out var bucket))
                    return entry.Value > bucket.Version;

                return entry.Value > 0L;
            });
        }

        private IEnumerable<Bucket> CollectDelta(IImmutableDictionary<Address, long> versions)
        {
            // missing entries are represented by version 0
            var filledOtherVersions = OwnVersions.ToDictionary(c => c.Key, _ => 0L);
            foreach (var version in versions)
            {
                filledOtherVersions[version.Key] = version.Value;
            }

            var count = 0;
            foreach (var entry in filledOtherVersions)
            {
                var owner = entry.Key;
                var v = entry.Value;

                if (!_registry.TryGetValue(owner, out var bucket))
                    bucket = new Bucket(owner);

                if (bucket.Version > v && count < _settings.MaxDeltaElements)
                {
                    var deltaContent = bucket.Content
                        .Where(kv => kv.Value.Version > v)
                        .ToImmutableDictionary(i => i.Key, i => i.Value);

                    count += deltaContent.Count;

                    if (count <= _settings.MaxDeltaElements)
                        yield return new Bucket(bucket.Owner, bucket.Version, deltaContent);
                    else
                    {
                        // exceeded the maxDeltaElements, pick the elements with lowest versions
                        var sortedContent = deltaContent.OrderBy(x => x.Value.Version).ToArray();
                        var chunk = sortedContent.Take(_settings.MaxDeltaElements - (count - sortedContent.Length)).ToList();
                        var content = chunk.ToImmutableDictionary(i => i.Key, i => i.Value);

                        yield return new Bucket(bucket.Owner, chunk.Last().Value.Version, content);
                    }
                }
            }
        }

        private IEnumerable<string> GetCurrentTopics()
        {
            var topicPrefix = _topicPrefix;
            foreach (var (_, bucket) in _registry)
            {
                foreach (var (key, _) in bucket.Content)
                {
                    var encodedTopic = Internal.Utils.KeyToEncodedTopic(key, topicPrefix);
                    if (key is null)
                        continue;
                    yield return encodedTopic;
                }
            }
        }

        private void HandleRegisterTopic(IActorRef actorRef)
        {
            PutToRegistry(Internal.Utils.MakeKey(actorRef), actorRef);
            Context.Watch(actorRef);
        }

        private void PutToRegistry(string key, IActorRef value)
        {
            var v = NextVersion();
            if (!_registry.TryGetValue(_cluster.SelfAddress, out var bucket))
                _registry.Add(_cluster.SelfAddress,
                    new Bucket(_cluster.SelfAddress, v, ImmutableDictionary<string, ValueHolder>.Empty.Add(key, new ValueHolder(v, value))));
            else
                _registry[_cluster.SelfAddress] = new Bucket(bucket.Owner, v, bucket.Content.SetItem(key, new ValueHolder(v, value)));
        }

        private void IgnoreOrSendToDeadLetters(IWrappedMessage message)
        {
            if (_settings.SendToDeadLettersWhenNoSubscribers)
            {
                var topic = message switch
                {
                    Publish publish => publish.Topic,
                    Send send => $"Send:{send.Path}",
                    _ => null
                };

                // Use the specialized DeadLetterWithNoSubscribers class to clearly indicate
                // that the message was not delivered because there were no subscribers,
                // not because the mediator itself is dead.
                var deadLetter = new DeadLetterWithNoSubscribers(message, topic, Sender, Context.Self);
                Context.System.DeadLetters.Tell(deadLetter);
            }
        }

        private void PublishMessage(string path, IWrappedMessage publish, bool allButSelf = false)
        {
            var counter = 0;
            foreach (var r in Refs())
            {
                if (r == null) continue;
                r.Forward(publish.Message);
                counter++;
            }

            if (counter == 0) IgnoreOrSendToDeadLetters(publish);
            return;

            IEnumerable<IActorRef> Refs()
            {
                foreach (var (address, bucket) in _registry)
                {
                    if (!(allButSelf && address == _cluster.SelfAddress) && bucket.Content.TryGetValue(path, out var valueHolder))
                    {
                        if (valueHolder != null && !valueHolder.Ref.IsNobody())
                            yield return valueHolder.Ref;
                    }
                }
            }
        }

        private void PublishToEachGroup(string path, Publish publish)
        {
            var prefix = path + "/";
            var lastKey = path + "0";   // '0' is the next char of '/'

            var groups = ExtractGroups(prefix, lastKey).GroupBy(kv => kv.Key).ToList();
            var wrappedMessage = new SendToOneSubscriber(publish.Message);

            if (groups.Count == 0)
            {
                IgnoreOrSendToDeadLetters(publish);
            }
            else
            {
                foreach (var g in groups)
                {
                    var routees = g.Select(r => r.Value).ToArray();
                    if (routees.Length != 0)
                        new Router(_settings.RoutingLogic, routees).Route(wrappedMessage, Sender);
                }
            }
        }

        private IEnumerable<KeyValuePair<string, Routee>> ExtractGroups(string prefix, string lastKey)
        {
            foreach (var (_, bucket) in _registry)
            {
                //TODO: optimize into tree-aware key range [prefix, lastKey]
                foreach (var (key, value) in bucket.Content.Where(kv => kv.Key.CompareTo(prefix) != -1 && kv.Key.CompareTo(lastKey) != 1))
                {
                    yield return new KeyValuePair<string, Routee>(key, value.Routee);
                }
            }
        }

        private void HandlePrune()
        {
            var modifications = new Dictionary<Address, Bucket>();
            foreach (var (owner, bucket) in _registry)
            {
                var oldRemoved = bucket.Content
                    .Where(kv => kv.Value.Ref.IsNobody() && (bucket.Version - kv.Value.Version) > _settings.RemovedTimeToLive.TotalMilliseconds)
                    .Select(kv => kv.Key)
                    .ToArray();

                if (oldRemoved.Length > 0)
                {
                    modifications.Add(owner, new Bucket(bucket.Owner, bucket.Version, bucket.Content.RemoveRange(oldRemoved)));
                }
            }

            foreach (var entry in modifications)
            {
                _registry[entry.Key] = entry.Value;
            }
        }

        private void HandleGossip()
        {
            var node = SelectRandomNode(_nodes.Except([_cluster.SelfAddress]).ToArray());
            if (node != null)
                GossipTo(node);
        }

        private void GossipTo(Address address)
        {
            var sel = Context.ActorSelection(Self.Path.ToStringWithAddress(address));
            sel.Tell(new Status(versions: OwnVersions, isReplyToStatus: false));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Address SelectRandomNode(IList<Address> addresses)
        {
            if (addresses == null || addresses.Count == 0) return null;
            return addresses[ThreadLocalRandom.Current.Next(addresses.Count)];
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            base.PreStart();
            if (_cluster.IsTerminated) throw new IllegalStateException("Cluster node must not be terminated");
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
            
            //Start periodic gossip to random nodes in cluster
            Timers.StartPeriodicTimer(GossipTimerKey, GossipTick.Instance, _settings.GossipInterval, _settings.GossipInterval, Self);
            Timers.StartPeriodicTimer(PruneTimerKey, Prune.Instance, _pruneInterval, _pruneInterval, Self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            Timers.CancelAll();
            base.PostStop();
            _cluster.Unsubscribe(Self);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsMatchingRole(Member member, string role)
        {
            return string.IsNullOrEmpty(role) || member.HasRole(role);
        }

        // the version is a timestamp because it is also used when pruning removed entries
        private long _version;
        private long NextVersion()
        {
            var current = DateTime.UtcNow.Ticks / 10000;
            _version = current > _version ? current : _version + 1;
            return _version;
        }

        private IActorRef NewTopicActor(string encodedTopic)
        {
            var t = Context.ActorOf(
                Actor.Props.Create(() => new Topic(_settings.RemovedTimeToLive, _settings.RoutingLogic, _settings.SendToDeadLettersWhenNoSubscribers))
                    .WithDeploy(Deploy.Local), 
                encodedTopic);
            HandleRegisterTopic(t);
            return t;
        }
    }
}
