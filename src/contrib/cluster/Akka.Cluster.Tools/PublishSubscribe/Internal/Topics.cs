//-----------------------------------------------------------------------
// <copyright file="Topics.cs" company="Akka.NET Project">
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
using Akka.Event;
using Akka.Remote;
using Akka.Routing;

namespace Akka.Cluster.Tools.PublishSubscribe.Internal
{
    /// <summary>
    /// A <see cref="DeadLetter"/> published when there are no subscribers
    /// for a topic that has received a <see cref="Publish"/> event.
    /// </summary>
    internal readonly struct NoSubscribersDeadLetter
    {
        public NoSubscribersDeadLetter(string topic, object message)
        {
            Topic = topic;
            Message = message;
        }

        public string Topic { get; }
        public object Message { get; }

        public override string ToString()
        {
            return $"NoSubscribersDeadLetter(Topic=[{Topic}],Message=[{Message}])";
        }
    }

    /// <summary>
    /// Base class for both topics and groups.
    /// </summary>
    internal abstract class TopicLike : ActorBase, IWithTimers
    {
        private const string PruneTimerKey = "PruneTimer";
        
        /// <summary>
        /// Timer interval to check to see if this actor needs to notify the <see cref="DistributedPubSubMediator"/>
        /// that this topic needs to be pruned.
        /// </summary>
        protected readonly TimeSpan PruneInterval;

        /// <summary>
        /// Hash set of all local <see cref="IActorRef"/> that subscribed to this topic
        /// </summary>
        protected readonly ISet<IActorRef> Subscribers;

        /// <summary>
        /// Delay before this actor notify the <see cref="DistributedPubSubMediator"/> that the topic is empty and
        /// it needs to be pruned.
        /// </summary>
        protected readonly TimeSpan EmptyTimeToLive;

        /// <summary>
        /// The current prune deadline.
        ///  * Set when the last subscriber is downed or unsubscribed from this topic.
        ///  * Reset to <c>null</c> when a new subscriber arrived.
        ///  * Deadline checked regularly every <see cref="PruneInterval"/> interval.
        /// </summary>
        protected Deadline PruneDeadline = null;

        /// <summary>
        /// Used to toggle what we do during publication when there are no subscribers
        /// </summary>
        protected readonly bool SendToDeadLettersWhenNoSubscribers;

        /// <summary>
        /// Creates a new instance of a topic or group actor.
        /// </summary>
        /// <param name="emptyTimeToLive">The TTL for how often this actor will be removed.</param>
        /// <param name="sendToDeadLettersWhenNone">When set to <c>true</c>, this actor will
        /// publish a <see cref="DeadLetter"/> for each message if the total number of subscribers == 0.</param>
        protected TopicLike(TimeSpan emptyTimeToLive, bool sendToDeadLettersWhenNone)
        {
            Subscribers = new HashSet<IActorRef>();
            EmptyTimeToLive = emptyTimeToLive;
            SendToDeadLettersWhenNoSubscribers = sendToDeadLettersWhenNone;
            PruneInterval = new TimeSpan(emptyTimeToLive.Ticks / 2);
        }

        public ITimerScheduler Timers { get; set; }

        protected override void PreStart()
        {
            base.PreStart();
            Timers.StartPeriodicTimer(PruneTimerKey, Prune.Instance, PruneInterval, PruneInterval, Self);
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            base.PostStop();
            Timers.CancelAll();
        }

        /// <summary>
        /// Default <see cref="Receive"/> method for <see cref="DistributedPubSub"/> messages.
        /// </summary>
        /// <param name="message">The message we're going to process.</param>
        /// <returns>true if we handled it, false otherwise.</returns>
        private bool DefaultReceive(object message)
        {
            switch (message)
            {
                case Subscribe subscribe:
                    Context.Watch(subscribe.Ref);
                    Subscribers.Add(subscribe.Ref);
                    PruneDeadline = null;
                    Context.Parent.Tell(new Subscribed(new SubscribeAck(subscribe), Sender));
                    return true;

                case Unsubscribe unsubscribe:
                    Context.Unwatch(unsubscribe.Ref);
                    Remove(unsubscribe.Ref);
                    Context.Parent.Tell(new Unsubscribed(new UnsubscribeAck(unsubscribe), Sender));
                    return true;

                case Terminated terminated:
                    Remove(terminated.ActorRef);
                    return true;

                case Prune:
                    if (PruneDeadline is { IsOverdue: true })
                    {
                        PruneDeadline = null;
                        Context.Parent.Tell(NoMoreSubscribers.Instance);
                    }

                    return true;

                case TerminateRequest:
                    if (Subscribers.Count == 0 && !Context.GetChildren().Any())
                    {
                        Context.Stop(Self);
                    }
                    else
                    {
                        Context.Parent.Tell(NewSubscriberArrived.Instance);
                    }

                    return true;

                case Count:
                    Sender.Tell(Subscribers.Count);
                    return true;

                default:
                    foreach (var subscriber in Subscribers)
                        subscriber.Forward(message);

                    // no subscribers
                    if (Subscribers.Count == 0 && SendToDeadLettersWhenNoSubscribers)
                    {
                        var noSubs = new NoSubscribersDeadLetter(Context.Self.Path.Name, message);
                        var deadLetter = new DeadLetter(noSubs, Sender, Self);
                        Context.System.EventStream.Publish(deadLetter);
                    }

                    return true;
            }
        }

        /// <summary>
        /// Default message handler for both <see cref="Topic"/> and <see cref="Group"/>
        /// </summary>
        /// <param name="message">The message we're going to process.</param>
        /// <returns>true if we handled it, false otherwise.</returns>
        protected abstract bool Business(object message);

        /// <inheritdoc cref="ActorBase.Receive"/>
        protected override bool Receive(object message)
        {
            return Business(message) || DefaultReceive(message);
        }

        protected void Remove(IActorRef actorRef)
        {
            Subscribers.Remove(actorRef);

            if (Subscribers.Count == 0 && !Context.GetChildren().Any())
            {
                PruneDeadline = Deadline.Now + EmptyTimeToLive;
            }
        }
    }

    /// <summary>
    /// Actor responsible for owning a single topic.
    /// </summary>
    internal class Topic : TopicLike
    {
        private readonly RoutingLogic _routingLogic;
        private readonly PerGroupingBuffer _buffer;
        private readonly PubSubCache _cache;
        private readonly string _topicPrefix;

        /// <summary>
        /// Creates a new topic actor
        /// </summary>
        /// <param name="emptyTimeToLive">The TTL for how often this actor will be removed.</param>
        /// <param name="sendToDeadLettersWhenNone">When set to <c>true</c>, this actor will
        /// publish a <see cref="DeadLetter"/> for each message if the total number of subscribers == 0.</param>
        /// <param name="routingLogic">The routing logic to use for distributing messages to subscribers.</param>
        public Topic(TimeSpan emptyTimeToLive, RoutingLogic routingLogic, bool sendToDeadLettersWhenNone) : base(emptyTimeToLive, sendToDeadLettersWhenNone)
        {
            _routingLogic = routingLogic;
            _buffer = new PerGroupingBuffer();
            
            _topicPrefix = Self.Path.ToStringWithoutAddress();
            _cache = new PubSubCache();
        }

        /// <inheritdoc cref="TopicLike.Business"/>
        protected override bool Business(object message)
        {
            switch (message)
            {
                case Subscribe { Group: not null } subscribe:
                    var encodedGroup = _cache.EncodeName(subscribe.Group);
                    _buffer.BufferOr(_cache.MakeKey(Self.Path, encodedGroup), subscribe, Sender, () =>
                    {
                        var child = Context.Child(encodedGroup);
                        if (!child.IsNobody())
                        {
                            child.Forward(message);
                        }
                        else
                        {
                            NewGroupActor(encodedGroup).Forward(message);
                        }
                    });
                    PruneDeadline = null;
                    return true;

                case Subscribed:
                    Context.Parent.Forward(message);
                    return true;

                case Unsubscribe { Group: not null } unsubscribe:
                    encodedGroup = _cache.EncodeName(unsubscribe.Group);
                    _buffer.BufferOr(_cache.MakeKey(Self.Path, encodedGroup), unsubscribe, Sender, () =>
                    {
                        var child = Context.Child(encodedGroup);
                        if (!child.IsNobody())
                        {
                            child.Forward(message);
                        }
                        else
                        {
                            // no such group here
                            _cache.TryRemoveTopic(unsubscribe.Group);
                        }
                    });
                    return true;

                case Unsubscribed:
                    Context.Parent.Forward(message);
                    return true;

                case Cluster:
                    var key = Utils.MakeKey(Sender);
                    _buffer.InitializeGrouping(key);
                    Sender.Tell(TerminateRequest.Instance);
                    return true;

                case NewSubscriberArrived:
                    key = Utils.MakeKey(Sender);
                    _buffer.ForwardMessages(key, Sender);
                    return true;

                case Terminated terminated:
                    key = Utils.MakeKey(terminated.ActorRef);
                    _buffer.RecreateAndForwardMessagesIfNeeded(key, () => NewGroupActor(terminated.ActorRef.Path.Name));
                    Remove(terminated.ActorRef);
                    _cache.TryRemoveKey(key, _topicPrefix);
                    return true;
            }

            return false;
        }

        private IActorRef NewGroupActor(string encodedGroup)
        {
            var g = Context.ActorOf(
                Props.Create(() => new Group(EmptyTimeToLive, _routingLogic, SendToDeadLettersWhenNoSubscribers))
                    .WithDeploy(Deploy.Local),
                encodedGroup);
            Context.Watch(g);
            Context.Parent.Tell(new RegisterTopic(g));
            return g;
        }
    }

    /// <summary>
    /// Actor that handles "group" subscribers to a topic.
    /// </summary>
    internal class Group : TopicLike
    {
        private readonly RoutingLogic _routingLogic;

        /// <summary>
        /// Creates a new group actor.
        /// </summary>
        /// <param name="emptyTimeToLive">The TTL for how often this actor will be removed.</param>
        /// <param name="sendToDeadLettersWhenNone">When set to <c>true</c>, this actor will
        /// publish a <see cref="DeadLetter"/> for each message if the total number of subscribers == 0.</param>
        /// <param name="routingLogic">The routing logic to use for distributing messages to subscribers.</param>
        public Group(TimeSpan emptyTimeToLive, RoutingLogic routingLogic, bool sendToDeadLettersWhenNone) : base(emptyTimeToLive, sendToDeadLettersWhenNone)
        {
            _routingLogic = routingLogic;
        }

        /// <inheritdoc cref="TopicLike.Business"/>
        protected override bool Business(object message)
        {
            switch (message)
            {
                case SendToOneSubscriber when Subscribers.Count == 0:
                    return true;
                
                case SendToOneSubscriber send:
                    var routees = Subscribers.Select(Routee (sub) => new ActorRefRoutee(sub)).ToArray();
                    new Router(_routingLogic, routees).Route(Utils.WrapIfNeeded(send.Message), Sender);
                    return true;
                
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Used for generating Uri-safe topic and group names.
    /// </summary>
    internal static class Utils
    {
        public readonly record struct MakeKeyInfo(ActorPath Path, string Topic);
    
        private static readonly System.Text.RegularExpressions.Regex PathRegex = new("^/remote/.+(/user/.+)");

        /// <summary>
        /// <para>
        /// Mediator uses <see cref="Router"/> to send messages to multiple destinations, Router in general
        /// unwraps messages from <see cref="RouterEnvelope"/> and sends the contents to <see cref="Routee"/>s.
        /// </para>
        /// <para>
        /// Using mediator services should not have an undesired effect of unwrapping messages
        /// out of <see cref="RouterEnvelope"/>. For this reason user messages are wrapped in
        /// <see cref="MediatorRouterEnvelope"/> which will be unwrapped by the <see cref="Router"/> leaving original
        /// user message.
        /// </para>
        /// </summary>
        /// <param name="message">TBD</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object WrapIfNeeded(object message)
        {
            return message is RouterEnvelope ? new MediatorRouterEnvelope(message) : message;
        }
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string? KeyToEncodedTopic(string key, string topicPrefix)
        {
            if (!key.StartsWith(topicPrefix)) 
                return null;
                    
            var topic = key[(topicPrefix.Length + 1)..];
            return !topic.Contains('/') ? topic : null;
        }

        #region Key related methods
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string MakeKey(IActorRef actorRef)
        {
            return PathRegex.Replace(actorRef.Path.ToStringWithoutAddress(), "$1");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string MakeKey(this PubSubCache cache, ActorPath path, string topic)
        {
            var info = new MakeKeyInfo(path, topic);
            if(cache.MakeKeyMap.TryGetValue(info, out var key))
                return key;
            
            key = PathRegex.Replace((path / topic).ToStringWithoutAddress(), "$1");
            cache.MakeKeyMap[info] = key;
            cache.MakeKeyReverseMap[key] = info;
            return key;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TryRemoveKey(this PubSubCache cache, string key, string topicPrefix)
        {
            if (cache.MakeKeyReverseMap.TryGetValue(key, out var keyInfo))
            {
                cache.MakeKeyMap.Remove(keyInfo);
                cache.MakeKeyReverseMap.Remove(key);
            }
            
            var encodedTopic = Utils.KeyToEncodedTopic(key, topicPrefix);
            if (encodedTopic == null) 
                return;

            if (!cache.EncodedToTopicMap.TryGetValue(encodedTopic, out var topic)) 
                return;
            
            cache.TopicToEncodedMap.Remove(topic);
            cache.EncodedToTopicMap.Remove(encodedTopic);
        }
        
        #endregion
        
        #region Topic/group name related methods
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string EncodeName(this PubSubCache cache, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            
            if (cache.TopicToEncodedMap.TryGetValue(name, out var encoded))
                return encoded;

            encoded = Uri.EscapeDataString(name);
            cache.TopicToEncodedMap[name] = encoded;
            cache.EncodedToTopicMap[encoded] = name;
            return encoded;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TryRemoveEncodedTopic(this PubSubCache cache, string encodedTopic)
        {
            if (!cache.EncodedToTopicMap.TryGetValue(encodedTopic, out var topic)) 
                return;
            
            cache.TopicToEncodedMap.Remove(topic);
            cache.EncodedToTopicMap.Remove(encodedTopic);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TryRemoveTopic(this PubSubCache cache, string topic)
        {
            if(!cache.TopicToEncodedMap.TryGetValue(topic, out var encodedTopic))
                return;
            
            cache.EncodedToTopicMap.Remove(encodedTopic);
            cache.TopicToEncodedMap.Remove(topic);
        }

        #endregion

        public static void Clear(this PubSubCache cache)
        {
            cache.TopicToEncodedMap.Clear();
            cache.EncodedToTopicMap.Clear();
            cache.MakeKeyMap.Clear();
            cache.MakeKeyReverseMap.Clear();
        }
    }
    
    internal sealed class PubSubCache
    {
        public readonly Dictionary<string, string> TopicToEncodedMap = new();
        public readonly Dictionary<string, string> EncodedToTopicMap = new();
        public readonly Dictionary<Utils.MakeKeyInfo, string> MakeKeyMap = new();
        public readonly Dictionary<string, Utils.MakeKeyInfo> MakeKeyReverseMap = new();
    }
}
