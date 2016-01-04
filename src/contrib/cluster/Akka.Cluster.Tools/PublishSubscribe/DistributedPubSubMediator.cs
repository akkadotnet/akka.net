//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
    public class DistributedPubSubMediator : ReceiveActor
    {
        public static Props Props(DistributedPubSubSettings settings)
        {
            return Actor.Props.Create(() => new DistributedPubSubMediator(settings)).WithDeploy(Deploy.Local);
        }

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly DistributedPubSubSettings _settings;
        private readonly ICancelable _gossipCancelable;
        private readonly ICancelable _pruneCancelable;
        private readonly TimeSpan _pruneInterval;

        private ISet<Address> _nodes = new HashSet<Address>();
        private ILoggingAdapter _log;
        private IDictionary<Address, Bucket> _registry = new Dictionary<Address, Bucket>();

        public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        public IDictionary<Address, long> OwnVersions
        {
            get
            {
                return _registry
                    .Select(entry => new KeyValuePair<Address, long>(entry.Key, entry.Value.Version))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
        }

        public DistributedPubSubMediator(DistributedPubSubSettings settings)
        {
            if (settings.RoutingLogic is ConsistentHashingRoutingLogic)
                throw new ArgumentException("Consistent hashing routing logic cannot be used by the pub-sub mediator");

            _settings = settings;

            if (!string.IsNullOrEmpty(_settings.Role) && !_cluster.SelfRoles.Contains(_settings.Role))
                throw new ArgumentException(string.Format("The cluster member [{0}] doesn't have the role [{1}]", _cluster.SelfAddress, _settings.Role));

            //Start periodic gossip to random nodes in cluster
            _gossipCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.GossipInterval, _settings.GossipInterval, Self, GossipTick.Instance, Self);
            _pruneInterval = new TimeSpan(_settings.RemovedTimeToLive.Ticks / 2);
            _pruneCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_pruneInterval, _pruneInterval, Self, Prune.Instance, Self);

            Receive<Send>(send =>
            {
                var routees = new List<Routee>();

                Bucket bucket;
                if (_registry.TryGetValue(_cluster.SelfAddress, out bucket))
                {
                    ValueHolder valueHolder;
                    if (bucket.Content.TryGetValue(send.Path, out valueHolder) && send.LocalAffinity)
                    {
                        var routee = valueHolder.Routee;
                        if (routee != null) routees.Add(routee);
                    }
                    else
                    {
                        foreach (var entry in _registry)
                        {
                            if (entry.Value.Content.TryGetValue(send.Path, out valueHolder))
                            {
                                var routee = valueHolder.Routee;
                                if (routee != null) routees.Add(routee);
                            }
                        }
                    }
                }

                if (routees.Count != 0)
                {
                    new Router(_settings.RoutingLogic, routees.ToArray()).Route(Utils.WrapIfNeeded(send.Message), Sender);
                }
            });
            Receive<SendToAll>(sendToAll =>
            {
                PublishMessage(sendToAll.Path, sendToAll.Message, sendToAll.ExcludeSelf);
            });
            Receive<Publish>(publish =>
            {
                var topic = Uri.EscapeDataString(publish.Topic);
                var path = Self.Path / topic;
                if (publish.SendOneMessageToEachGroup)
                    PublishToEachGroup(path.ToStringWithoutAddress(), publish.Message);
                else
                    PublishMessage(path.ToStringWithoutAddress(), publish.Message);
            });
            Receive<Put>(put =>
            {
                if (!string.IsNullOrEmpty(put.Ref.Path.Address.Host))
                    Log.Warning("Registered actor must be local: [{0}]", put.Ref);
                else
                {
                    PutToRegistry(put.Ref.Path.ToStringWithoutAddress(), put.Ref);
                    Context.Watch(put.Ref);
                }
            });
            Receive<Remove>(remove =>
            {
                Bucket bucket;
                if (_registry.TryGetValue(_cluster.SelfAddress, out bucket))
                {
                    ValueHolder valueHolder;
                    if (bucket.Content.TryGetValue(remove.Path, out valueHolder) && valueHolder.Ref != null)
                    {
                        Context.Unwatch(valueHolder.Ref);
                        PutToRegistry(remove.Path, null);
                    }
                }
            });
            Receive<Subscribe>(subscribe =>
            {
                // each topic is managed by a child actor with the same name as the topic
                var topic = Uri.EscapeDataString(subscribe.Topic);
                var child = Context.Child(topic);
                if (!ActorRefs.Nobody.Equals(child))
                {
                    child.Forward(subscribe);
                }
                else
                {
                    var t = Context.ActorOf(Actor.Props.Create(() =>
                        new Topic(_settings.RemovedTimeToLive, _settings.RoutingLogic)), topic);
                    t.Forward(subscribe);
                    HandleRegisterTopic(t);
                }
            });
            Receive<RegisterTopic>(register =>
            {
                HandleRegisterTopic(register.TopicRef);
            });
            Receive<GetTopics>(getTopics =>
            {
                Sender.Tell(new CurrentTopics(GetCurrentTopics().ToArray()));
            });
            Receive<Subscribed>(subscribed =>
            {
                subscribed.Subscriber.Tell(subscribed.Ack);
            });
            Receive<Unsubscribe>(unsubscribe =>
            {
                var topic = Uri.EscapeDataString(unsubscribe.Topic);
                var child = Context.Child(topic);
                if (!ActorRefs.Nobody.Equals(child))
                    child.Forward(unsubscribe);
            });
            Receive<Unsubscribed>(unsubscribed =>
            {
                unsubscribed.Subscriber.Tell(unsubscribed.Ack);
            });
            Receive<Status>(status =>
            {
                // gossip chat starts with a Status message, containing the bucket versions of the other node
                var delta = CollectDelta(status.Versions).ToArray();
                if (delta.Length != 0)
                    Sender.Tell(new Delta(delta));

                if (OtherHasNewerVersions(status.Versions))
                    Sender.Tell(new Status(OwnVersions));
            });
            Receive<Delta>(delta =>
            {
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
                            Bucket myBucket;
                            if (!_registry.TryGetValue(bucket.Owner, out myBucket))
                                myBucket = new Bucket(bucket.Owner);

                            if (bucket.Version > myBucket.Version)
                            {
                                _registry.Add(bucket.Owner, new Bucket(myBucket.Owner, bucket.Version, myBucket.Content.AddRange(bucket.Content)));
                            }
                        }
                    }
                }
            });
            Receive<GossipTick>(_ => HandleGossip());
            Receive<Prune>(_ => HandlePrune());
            Receive<Terminated>(terminated =>
            {
                var key = terminated.ActorRef.Path.ToStringWithoutAddress();

                Bucket bucket;
                if (_registry.TryGetValue(_cluster.SelfAddress, out bucket))
                {
                    ValueHolder holder;
                    if (bucket.Content.TryGetValue(key, out holder) && terminated.ActorRef.Equals(holder.Ref))
                    {
                        PutToRegistry(key, null); // remove
                    }
                }
            });
            Receive<ClusterEvent.CurrentClusterState>(state =>
            {
                var nodes = state.Members
                    .Where(m => m.Status != MemberStatus.Joining && IsMatchingRole(m))
                    .Select(m => m.Address);

                _nodes = new HashSet<Address>(nodes);
            });
            Receive<ClusterEvent.MemberUp>(up =>
            {
                if (IsMatchingRole(up.Member)) _nodes.Add(up.Member.Address);
            });
            Receive<ClusterEvent.MemberRemoved>(removed =>
            {
                var member = removed.Member;
                if (member.Address == _cluster.SelfAddress)
                {
                    Context.Stop(Self);
                }
                else if (IsMatchingRole(member))
                {
                    _nodes.Remove(member.Address);
                    _registry.Remove(member.Address);
                }
            });
            Receive<ClusterEvent.IMemberEvent>(_ => { /* ignore */ });
            Receive<Count>(_ =>
            {
                var count = _registry.Sum(entry => entry.Value.Content.Count(kv => kv.Value.Ref != null));
                Sender.Tell(count);
            });
        }

        private bool OtherHasNewerVersions(IDictionary<Address, long> versions)
        {
            return versions.Any(entry =>
            {
                Bucket bucket;
                return (!_registry.TryGetValue(entry.Key, out bucket) && entry.Value > bucket.Version)
                       || entry.Value > 0L;
            });
        }

        private IEnumerable<Bucket> CollectDelta(IDictionary<Address, long> versions)
        {
            // missing entries are represented by version 0
            var filledOtherVersions = new Dictionary<Address, long>(versions);
            foreach (var entry in OwnVersions)
                if (filledOtherVersions.ContainsKey(entry.Key))
                    filledOtherVersions[entry.Key] = 0L;
                else
                    filledOtherVersions.Add(entry.Key, 0L);

            var count = 0;
            foreach (var entry in filledOtherVersions)
            {
                var owner = entry.Key;
                var v = entry.Value;

                Bucket bucket;
                if (!_registry.TryGetValue(owner, out bucket))
                    bucket = new Bucket(owner);

                if (bucket.Version > v && count < _settings.MaxDeltaElements)
                {
                    var deltaContent = bucket.Content
                        .Where(kv => kv.Value.Version > v)
                        .Aggregate(ImmutableDictionary<string, ValueHolder>.Empty,
                            (current, kv) => current.SetItem(kv.Key, kv.Value));

                    count += deltaContent.Count;

                    if (count <= _settings.MaxDeltaElements)
                        yield return new Bucket(bucket.Owner, bucket.Version, deltaContent);
                    else
                    {
                        // exceeded the maxDeltaElements, pick the elements with lowest versions
                        var sortedContent = deltaContent.OrderBy(x => x.Value.Version).ToArray();
                        var chunk = sortedContent.Take(_settings.MaxDeltaElements - (count - sortedContent.Length)).ToList();
                        var content = chunk.Aggregate(ImmutableDictionary<string, ValueHolder>.Empty,
                            (current, kv) => current.SetItem(kv.Key, kv.Value));

                        yield return new Bucket(bucket.Owner, chunk.Last().Value.Version, content);
                    }
                }
            }
        }

        private IEnumerable<string> GetCurrentTopics()
        {
            var topicPrefix = Self.Path.ToStringWithoutAddress();
            foreach (var entry in _registry)
            {
                var bucket = entry.Value;
                foreach (var kv in bucket.Content)
                {
                    var key = kv.Key;
                    var value = kv.Value;
                    if (key.StartsWith(topicPrefix))
                    {
                        var topic = key.Substring(topicPrefix.Length + 1);
                        if (!topic.Contains("/"))
                        {
                            yield return Uri.EscapeDataString(topic);
                        }
                    }
                }
            }
        }

        private void HandleRegisterTopic(IActorRef actorRef)
        {
            PutToRegistry(actorRef.Path.ToStringWithoutAddress(), actorRef);
            Context.Watch(actorRef);
        }

        private void PutToRegistry(string key, IActorRef value)
        {
            var v = NextVersion();
            Bucket bucket;
            if (!_registry.TryGetValue(_cluster.SelfAddress, out bucket))
            {
                _registry.Add(_cluster.SelfAddress,
                    new Bucket(_cluster.SelfAddress, v, ImmutableDictionary<string, ValueHolder>.Empty.Add(key, new ValueHolder(v, value))));
            }
            else
            {
                _registry[_cluster.SelfAddress] = new Bucket(bucket.Owner, v, bucket.Content.Add(key, new ValueHolder(v, value)));
            }
        }

        private void PublishMessage(string path, object message, bool excludeSelf = false)
        {
            foreach (var entry in _registry)
            {
                var address = entry.Key;
                var bucket = entry.Value;

                if (!(excludeSelf && address == _cluster.SelfAddress))
                {
                    var valueHolder = bucket.Content[path];
                    if (valueHolder != null && !valueHolder.Ref.Equals(ActorRefs.Nobody))
                    {
                        valueHolder.Ref.Forward(message);
                    }
                }
            }
        }

        private void PublishToEachGroup(string path, object message)
        {
            var prefix = path + "/";
            var lastKey = path + "0";   // '0' is the next char of '/'

            var groups = ExtractGroups(prefix, lastKey).GroupBy(kv => kv.Key);
            var wrappedMessage = new SendToOneSubscriber(message);

            foreach (var g in groups)
            {
                var routees = g.Select(r => r.Value).ToArray();
                if (routees.Length != 0)
                    new Router(_settings.RoutingLogic, routees).Route(wrappedMessage, Sender);
            }
        }

        private IEnumerable<KeyValuePair<string, Routee>> ExtractGroups(string prefix, string lastKey)
        {
            foreach (var bucket in _registry.Values)
            {
                //TODO: optimize into tree-aware key range [prefix, lastKey]
                foreach (var keyVal in bucket.Content.Where(kv => kv.Key.CompareTo(prefix) != -1 && kv.Key.CompareTo(lastKey) != 1))
                {
                    yield return new KeyValuePair<string, Routee>(keyVal.Key, keyVal.Value.Routee);
                }
            }
        }

        private void HandlePrune()
        {
            foreach (var entry in _registry)
            {
                var owner = entry.Key;
                var bucket = entry.Value;

                var oldRemoved = bucket.Content
                    .Where(kv => (bucket.Version - kv.Value.Version) > _settings.RemovedTimeToLive.TotalMilliseconds)
                    .Select(kv => kv.Key);

                if (oldRemoved.Any())
                {
                    _registry.Add(owner, new Bucket(bucket.Owner, bucket.Version, bucket.Content.RemoveRange(oldRemoved)));
                }
            }
        }

        private void HandleGossip()
        {
            var node = SelectRandomNode(_nodes.Except(new[] { _cluster.SelfAddress }).ToArray());
            if (node != null)
                GossipTo(node);
        }

        private void GossipTo(Address address)
        {
            Context.ActorSelection(Self.Path.ToStringWithAddress(address)).Tell(new Status(OwnVersions));
        }

        private Address SelectRandomNode(IList<Address> addresses)
        {
            if (addresses == null || addresses.Count == 0) return null;
            return addresses[ThreadLocalRandom.Current.Next(addresses.Count)];
        }

        protected override void PreStart()
        {
            base.PreStart();
            if (_cluster.IsTerminated) throw new IllegalStateException("Cluster node must not be terminated");
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            base.PostStop();
            _cluster.Unsubscribe(Self);
            _gossipCancelable.Cancel();
            _pruneCancelable.Cancel();
        }

        private bool IsMatchingRole(Member member)
        {
            return string.IsNullOrEmpty(_settings.Role) || member.HasRole(_settings.Role);
        }

        // the version is a timestamp because it is also used when pruning removed entries
        private long _version = 0L;
        private long NextVersion()
        {
            var current = DateTime.UtcNow.TimeOfDay.Ticks;
            _version = current > _version ? current : _version + 1;
            return _version;
        }
    }
}