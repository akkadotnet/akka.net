//-----------------------------------------------------------------------
// <copyright file="Topics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    internal abstract class TopicLike : ActorBase
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly TimeSpan PruneInterval;

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly ICancelable PruneCancelable;

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly ISet<IActorRef> Subscribers;

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly TimeSpan EmptyTimeToLive;

        /// <summary>
        /// TBD
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
            PruneCancelable =
                Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(PruneInterval, PruneInterval, Self,
                    Prune.Instance, Self);
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            base.PostStop();
            PruneCancelable.Cancel();
        }

        /// <summary>
        /// Default <see cref="Receive"/> method for <see cref="DistributedPubSub"/> messages.
        /// </summary>
        /// <param name="message">The message we're going to process.</param>
        /// <returns>true if we handled it, false otherwise.</returns>
        protected bool DefaultReceive(object message)
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

                case Prune _:
                    if (PruneDeadline != null && PruneDeadline.IsOverdue)
                    {
                        PruneDeadline = null;
                        Context.Parent.Tell(NoMoreSubscribers.Instance);
                    }

                    return true;

                case TerminateRequest _:
                    if (Subscribers.Count == 0 && !Context.GetChildren().Any())
                    {
                        Context.Stop(Self);
                    }
                    else
                    {
                        Context.Parent.Tell(NewSubscriberArrived.Instance);
                    }

                    return true;

                case Count _:
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

        /// <inheritdoc cref="TopicLike.Business"/>
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
        }

        /// <inheritdoc cref="TopicLike.Business"/>
        protected override bool Business(object message)
        {
            switch (message)
            {
                case Subscribe subscribe when subscribe.Group != null:
                    var encodedGroup = Utils.EncodeName(subscribe.Group);
                    _buffer.BufferOr(Utils.MakeKey(Self.Path / encodedGroup), subscribe, Sender, () =>
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

                case Unsubscribe unsubscribe when unsubscribe.Group != null:
                    encodedGroup = Utils.EncodeName(unsubscribe.Group);
                    _buffer.BufferOr(Utils.MakeKey(Self.Path / encodedGroup), unsubscribe, Sender, () =>
                    {
                        var child = Context.Child(encodedGroup);
                        if (!child.IsNobody())
                        {
                            child.Forward(message);
                        }
                        else
                        {
                            // no such group here
                        }
                    });
                    return true;

                case Subscribed _:
                    Context.Parent.Forward(message);
                    return true;

                case Unsubscribed _:
                    Context.Parent.Forward(message);
                    return true;

                case NoMoreSubscribers _:
                    var key = Utils.MakeKey(Sender);
                    _buffer.InitializeGrouping(key);
                    Sender.Tell(TerminateRequest.Instance);
                    return true;

                case NewSubscriberArrived _:
                    key = Utils.MakeKey(Sender);
                    _buffer.ForwardMessages(key, Sender);
                    return true;

                case Terminated terminated:
                    key = Utils.MakeKey(terminated.ActorRef);
                    _buffer.RecreateAndForwardMessagesIfNeeded(key, () => NewGroupActor(terminated.ActorRef.Path.Name));
                    Remove(terminated.ActorRef);
                    return true;
            }

            return false;
        }

        private IActorRef NewGroupActor(string encodedGroup)
        {
            var g = Context.ActorOf(Props.Create(() => new Group(EmptyTimeToLive, _routingLogic, SendToDeadLettersWhenNoSubscribers)), encodedGroup);
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
            if (message is SendToOneSubscriber send)
            {
                if (Subscribers.Count != 0)
                {
                    var routees = Subscribers.Select(sub => (Routee)new ActorRefRoutee(sub)).ToArray();
                    new Router(_routingLogic, routees).Route(Utils.WrapIfNeeded(send.Message), Sender);
                }
            }
            else return false;

            return true;
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Used for generating Uri-safe topic and group names.
    /// </summary>
    internal static class Utils
    {
        private static System.Text.RegularExpressions.Regex _pathRegex =
            new System.Text.RegularExpressions.Regex("^/remote/.+(/user/.+)");

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
        /// <returns>TBD</returns>
        public static object WrapIfNeeded(object message)
        {
            return message is RouterEnvelope ? new MediatorRouterEnvelope(message) : message;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorRef">TBD</param>
        /// <returns>TBD</returns>
        public static string MakeKey(IActorRef actorRef)
        {
            return MakeKey(actorRef.Path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static string EncodeName(string name)
        {
            return name == null ? null : Uri.EscapeDataString(name);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public static string MakeKey(ActorPath path)
        {
            return _pathRegex.Replace(path.ToStringWithoutAddress(), "$1");
        }
    }
}