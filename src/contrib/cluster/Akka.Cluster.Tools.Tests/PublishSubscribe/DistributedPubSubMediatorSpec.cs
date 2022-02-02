//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Actor.Dsl;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.TestKit;
using Xunit;
using Akka.Event;
using System.Threading;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    [Collection(nameof(DistributedPubSubMediatorSpec))]
    public class DistributedPubSubMediatorSpec : AkkaSpec
    {
        public DistributedPubSubMediatorSpec() : base(GetConfig()) { }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString("akka.actor.provider = \"Akka.Cluster.ClusterActorRefProvider, Akka.Cluster\"");
        }

        /// <summary>
        /// Checks to see if http://stackoverflow.com/questions/41249465/how-to-test-distributedpubsub-with-the-testkit-in-akka-net is resolved
        /// </summary>
        [Fact]
        public void DistributedPubSubMediator_can_be_activated_in_child_actor()
        {
            EventFilter.Exception<NullReferenceException>().Expect(0, () =>
            {
                var actor = Sys.ActorOf((dsl, context) =>
                {
                    IActorRef mediator = null;
                    dsl.OnPreStart = actorContext =>
                    {
                        mediator = DistributedPubSub.Get(actorContext.System).Mediator;
                    };

                    dsl.Receive<string>(s => s.Equals("check"), (s, actorContext) =>
                    {
                        actorContext.Sender.Tell(mediator);
                    });

                }, "childActor");

                actor.Tell("check");
                var a = ExpectMsg<IActorRef>();
                a.ShouldNotBe(ActorRefs.NoSender);
                a.ShouldNotBe(ActorRefs.Nobody);
            });
        }

        [Fact]
        public void DistributedPubSubMediator_should_get_dead_letter()
        {
            EventFilter.Exception<NullReferenceException>().Expect(0, () =>
            {
                var actor = Sys.ActorOf((dsl, context) =>
                {
                    IActorRef mediator = null;
                    ICancelable cancelable = null;
                    dsl.OnPreStart = actorContext =>
                    {
                        Sys.EventStream.Subscribe(context.Self, typeof(DeadLetter));
                        mediator = DistributedPubSub.Get(actorContext.System).Mediator;
                        mediator.Tell(new Subscribe("pub-sub", context.Self));
                    };

                    dsl.Receive<string>(s => s.Equals("UnSub"), (s, actorContext) =>
                    {
                        mediator.Tell(new Unsubscribe("pub-sub", context.Self));
                    }); 
                    dsl.Receive<SubscribeAck>((s, actorContext) =>
                    {
                        actorContext.System.Log.Info($"{s.Subscribe.Topic}");
                        cancelable = context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(5), context.Self, "UnSub", context.Self);
                    }); 
                    dsl.Receive<PublishTopic>((s, actorContext) =>
                    {
                        mediator.Tell(new Publish("pub-sub", "Good"));
                    }); 
                    dsl.Receive<DeadLetter>((s, actorContext) =>
                    {
                        actorContext.System.Log.Info($"Received deadletter {s.Message}");
                    }); 
                    dsl.Receive<UnsubscribeAck>((s, actorContext) =>
                    {
                        actorContext.System.Log.Info($"{s.Unsubscribe.Topic}");
                        cancelable = context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromMinutes(3), context.Self, PublishTopic.Instance, context.Self);
                    });

                }, "childActor");
                Thread.Sleep(TimeSpan.FromMinutes(5));
            });
        }
    }
    public sealed class QueryTopics
    {
        public static QueryTopics Instance = new QueryTopics();
    }

    public sealed class PublishTopic
    {
        public static PublishTopic Instance = new PublishTopic();
    }
}
