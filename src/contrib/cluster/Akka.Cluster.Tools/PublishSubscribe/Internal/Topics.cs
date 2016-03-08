//-----------------------------------------------------------------------
// <copyright file="Topics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
                if (PruneDeadline != null && PruneDeadline.IsOverdue) Context.Stop(Self);
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

        public Topic(TimeSpan emptyTimeToLive, RoutingLogic routingLogic) : base(emptyTimeToLive)
        {
            _routingLogic = routingLogic;
        }

        protected override bool Business(object message)
        {
            Subscribe subscribe;
            Unsubscribe unsubscribe;
            if ((subscribe = message as Subscribe) != null && subscribe.Group != null)
            {
                var encodedGroup = Encode(subscribe.Group);
                var child = encodedGroup == null ? ActorRefs.Nobody : Context.Child(encodedGroup);

                if (!ActorRefs.Nobody.Equals(child))
                {
                    child.Forward(message);
                }
                else
                {
                    var group = Context.ActorOf(Props.Create(() => new Group(EmptyTimeToLive, _routingLogic)), encodedGroup);
                    group.Forward(message);
                    Context.Watch(group);
                    Context.Parent.Tell(new RegisterTopic(group));
                }

                PruneDeadline = null;
            }
            else if ((unsubscribe = message as Unsubscribe) != null && unsubscribe.Group != null)
            {
                var encodedGroup = Encode(unsubscribe.Group);
                var child = encodedGroup == null ? ActorRefs.Nobody : Context.Child(encodedGroup);

                if (!child.Equals(ActorRefs.Nobody))
                {
                    child.Forward(message);
                }
            }
            else if (message is Subscribed)
            {
                Context.Parent.Forward(message);
            }
            else if (message is Unsubscribed)
            {
                Context.Parent.Forward(message);
            }
            else return false;
            return true;
        }

        private static string Encode(string name)
        {
            return name == null ? null : Uri.EscapeDataString(name);
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
    }
}