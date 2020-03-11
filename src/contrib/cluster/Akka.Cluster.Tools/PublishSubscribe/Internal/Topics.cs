//-----------------------------------------------------------------------
// <copyright file="Topics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Remote;
using Akka.Routing;

namespace Akka.Cluster.Tools.PublishSubscribe.Internal
{
    /// <summary>
    /// TBD
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
        /// TBD
        /// </summary>
        /// <param name="emptyTimeToLive">TBD</param>
        protected TopicLike(TimeSpan emptyTimeToLive)
        {
            Subscribers = new HashSet<IActorRef>();
            EmptyTimeToLive = emptyTimeToLive;
            PruneInterval = new TimeSpan(emptyTimeToLive.Ticks / 2);
            PruneCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(PruneInterval, PruneInterval, Self, Prune.Instance, Self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            PruneCancelable.Cancel();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected bool DefaultReceive(object message)
        {
            if (message is Subscribe)
            {
                var subscribe = (Subscribe)message;

                Context.Watch(subscribe.Ref);
                Subscribers.Add(subscribe.Ref);
                PruneDeadline = null;
                Context.Parent.Tell(new Subscribed(new SubscribeAck(subscribe), Sender));
            }
            else if (message is Unsubscribe)
            {
                var unsubscribe = (Unsubscribe)message;

                Context.Unwatch(unsubscribe.Ref);
                Remove(unsubscribe.Ref);
                Context.Parent.Tell(new Unsubscribed(new UnsubscribeAck(unsubscribe), Sender));
            }
            else if (message is Terminated)
            {
                var terminated = (Terminated)message;
                Remove(terminated.ActorRef);
            }
            else if (message is Prune)
            {
                if (PruneDeadline != null && PruneDeadline.IsOverdue)
                {
                    PruneDeadline = null;
                    Context.Parent.Tell(NoMoreSubscribers.Instance);
                }
            }
            else if (message is TerminateRequest)
            {
                if (Subscribers.Count == 0 && !Context.GetChildren().Any())
                {
                    Context.Stop(Self);
                }
                else
                {
                    Context.Parent.Tell(NewSubscriberArrived.Instance);
                }
            }
            else if (message is Count)
            {
                Sender.Tell(Subscribers.Count);
            }
            else
            {
                foreach (var subscriber in Subscribers)
                    subscriber.Forward(message);
            }

            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected abstract bool Business(object message);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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
    /// TBD
    /// </summary>
    internal class Topic : TopicLike
    {
        private readonly RoutingLogic _routingLogic;
        private readonly PerGroupingBuffer _buffer;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="emptyTimeToLive">TBD</param>
        /// <param name="routingLogic">TBD</param>
        public Topic(TimeSpan emptyTimeToLive, RoutingLogic routingLogic) : base(emptyTimeToLive)
        {
            _routingLogic = routingLogic;
            _buffer = new PerGroupingBuffer();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Business(object message)
        {
            Subscribe subscribe;
            Unsubscribe unsubscribe;
            if ((subscribe = message as Subscribe) != null && subscribe.Group != null)
            {
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
            }
            else if ((unsubscribe = message as Unsubscribe) != null && unsubscribe.Group != null)
            {
                var encodedGroup = Utils.EncodeName(unsubscribe.Group);
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
            }
            else if (message is Subscribed)
            {
                Context.Parent.Forward(message);
            }
            else if (message is Unsubscribed)
            {
                Context.Parent.Forward(message);
            }
            else if (message is NoMoreSubscribers)
            {
                var key = Utils.MakeKey(Sender);
                _buffer.InitializeGrouping(key);
                Sender.Tell(TerminateRequest.Instance);
            }
            else if (message is NewSubscriberArrived)
            {
                var key = Utils.MakeKey(Sender);
                _buffer.ForwardMessages(key, Sender);
            }
            else if (message is Terminated)
            {
                var terminated = (Terminated)message;
                var key = Utils.MakeKey(terminated.ActorRef);
                _buffer.RecreateAndForwardMessagesIfNeeded(key, () => NewGroupActor(terminated.ActorRef.Path.Name));
                Remove(terminated.ActorRef);
            }
            else return false;
            return true;
        }

        private IActorRef NewGroupActor(string encodedGroup)
        {
            var g = Context.ActorOf(Props.Create(() => new Group(EmptyTimeToLive, _routingLogic)), encodedGroup);
            Context.Watch(g);
            Context.Parent.Tell(new RegisterTopic(g));
            return g;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class Group : TopicLike
    {
        private readonly RoutingLogic _routingLogic;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="emptyTimeToLive">TBD</param>
        /// <param name="routingLogic">TBD</param>
        public Group(TimeSpan emptyTimeToLive, RoutingLogic routingLogic) : base(emptyTimeToLive)
        {
            _routingLogic = routingLogic;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Business(object message)
        {
            if (message is SendToOneSubscriber)
            {
                if (Subscribers.Count != 0)
                {
                    var send = (SendToOneSubscriber)message;
                    var routees = Subscribers.Select(sub => (Routee)new ActorRefRoutee(sub)).ToArray();
                    new Router(_routingLogic, routees).Route(Utils.WrapIfNeeded(send.Message), Sender);
                }
            }
            else return false;
            return true;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal static class Utils
    {
        private static System.Text.RegularExpressions.Regex _pathRegex = new System.Text.RegularExpressions.Regex("^/remote/.+(/user/.+)");

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
