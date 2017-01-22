//-----------------------------------------------------------------------
// <copyright file="Topics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    internal abstract class TopicLike : ActorBase
    {
        protected readonly TimeSpan PruneInterval;
        protected readonly ICancelable PruneCancelable;
        protected readonly ISet<IActorRef> Subscribers;
        protected readonly TimeSpan EmptyTimeToLive;

        protected Deadline PruneDeadline = null;

        protected TopicLike(TimeSpan emptyTimeToLive)
        {
            Subscribers = new HashSet<IActorRef>();
            EmptyTimeToLive = emptyTimeToLive;
            PruneInterval = new TimeSpan(emptyTimeToLive.Ticks / 2);
            PruneCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(PruneInterval, PruneInterval, Self, Prune.Instance, Self);
        }

        protected override void PostStop()
        {
            base.PostStop();
            PruneCancelable.Cancel();
        }

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
            else
            {
                foreach (var subscriber in Subscribers)
                    subscriber.Forward(message);
            }

            return true;
        }

        protected abstract bool Business(object message);

        protected override bool Receive(object message)
        {
            return Business(message) || DefaultReceive(message);
        }

        private void Remove(IActorRef actorRef)
        {
            Subscribers.Remove(actorRef);

            if (Subscribers.Count == 0 && !Context.GetChildren().Any())
            {
                PruneDeadline = Deadline.Now + EmptyTimeToLive;
            }
        }
    }

    internal class Topic : TopicLike
    {
        private readonly RoutingLogic _routingLogic;
        private readonly PerGroupingBuffer _buffer;

        public Topic(TimeSpan emptyTimeToLive, RoutingLogic routingLogic) : base(emptyTimeToLive)
        {
            _routingLogic = routingLogic;
            _buffer = new PerGroupingBuffer();
        }

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

    internal class Group : TopicLike
    {
        private readonly RoutingLogic _routingLogic;

        public Group(TimeSpan emptyTimeToLive, RoutingLogic routingLogic) : base(emptyTimeToLive)
        {
            _routingLogic = routingLogic;
        }

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

    internal static class Utils
    {
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
        public static object WrapIfNeeded(object message)
        {
            return message is RouterEnvelope ? new MediatorRouterEnvelope(message) : message;
        }

        public static string MakeKey(IActorRef actorRef)
        {
            return MakeKey(actorRef.Path);
        }

        public static string EncodeName(string name)
        {
            return name == null ? null : Uri.EscapeDataString(name);
        }

        public static string MakeKey(ActorPath path)
        {
            return path.ToStringWithoutAddress();
        }
    }
}