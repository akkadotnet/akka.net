//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Hocon;
using Akka.Configuration;
using Akka.Actor.Dsl;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    [Collection(nameof(DistributedPubSubMediatorSpec))]
    public class DistributedPubSubMediatorSpec : AkkaSpec
    {
        private ActorSystem _sys2 = null;
 
        public Address Addr1 => Cluster.Get(Sys).SelfAddress;

        public DistributedPubSubMediatorSpec(ITestOutputHelper helper) : base(GetConfig(), helper)
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                           akka.actor.serialize-messages = on");
        }

        /// <summary>
        /// Checks to see if http://stackoverflow.com/questions/41249465/how-to-test-distributedpubsub-with-the-testkit-in-akka-net is resolved
        /// </summary>
        [Fact]
        public void DistributedPubSubMediator_can_be_activated_in_child_actor()
        {
            Within(TimeSpan.FromSeconds(5), () =>
            {
                var subProbe = CreateTestProbe();
                Cluster.Get(Sys).Subscribe(subProbe, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, typeof(ClusterEvent.MemberUp));
                Cluster.Get(Sys).Join(Addr1);
                subProbe.FishForMessage(x => x is ClusterEvent.MemberUp);
            });

            EventFilter.Exception<Exception>().Expect(0, () =>
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

                a.Tell(new Subscribe("foo1", TestActor));
                ExpectMsg<SubscribeAck>();

                a.Tell(new Publish("foo1", "msg"));
                ExpectMsg("msg");
            });
        }

        /// <summary>
        /// Reproduction spec for https://github.com/akkadotnet/akka.net/issues/4120
        /// </summary>
        [Fact]
        public void DistributedPubSub_can_run_serialize_messages()
        {
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);

            Within(TimeSpan.FromSeconds(15), () =>
            {
                // Join first node to self
                Cluster.Get(Sys).Join(Addr1);

                // Join second node
                Cluster.Get(_sys2).Join(Addr1);

                AwaitCondition(() =>
                {
                    return Cluster.Get(Sys).State.Members.Count(x => x.Status == MemberStatus.Up) == 2;
                });
            });

            Within(TimeSpan.FromSeconds(10), () =>
            {
                EventFilter.Exception<Exception>().Expect(0, () =>
                {
                    var mediator1 = DistributedPubSub.Get(Sys).Mediator;
                    var mediator2 = DistributedPubSub.Get(_sys2).Mediator;

                    var probe1 = CreateTestProbe();
                    var probe2 = CreateTestProbe(_sys2);

                    mediator1.Tell(new Subscribe("foo", probe1), probe1);
                    mediator2.Tell(new Subscribe("foo", probe2), probe2);

                    probe1.ExpectMsg<SubscribeAck>(Remaining);
                    probe2.ExpectMsg<SubscribeAck>(Remaining);

                    AwaitAssert(() =>
                    {
                        mediator1.Tell(Count.Instance);
                        ExpectMsg<int>(TimeSpan.FromMilliseconds(250)).ShouldBe(2);
                    }, interval: TimeSpan.FromMilliseconds(500));
                });
            });
            
        }

        protected override void AfterAll()
        {
            if (_sys2 != null)
            {
                Shutdown(_sys2);
            }
        }
    }
}
