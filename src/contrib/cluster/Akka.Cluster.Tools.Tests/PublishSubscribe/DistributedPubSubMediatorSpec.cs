//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Actor.Dsl;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.TestKit;
using Xunit;

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
    }
}
