//-----------------------------------------------------------------------
// <copyright file="ClusterRouterSupervisorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.Routing
{
    public class ClusterRouterSupervisorSpec : AkkaSpec
    {
        public ClusterRouterSupervisorSpec()
            : base(@"
    akka{
        actor{
            provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
        }
        remote.helios.tcp.port = 0
    }") { }

        #region Killable actor

        class KillableActor : ReceiveActor
        {
            private readonly IActorRef TestActor;

            public KillableActor(IActorRef testActor)
            {
                TestActor = testActor;
                Receive<string>(s => s == "go away", s =>
                {
                    throw new ArgumentException("Goodbye then!");
                });
            }
        }

        #endregion

        #region Tests

        [Fact]
        public void Cluster_aware_routers_must_use_provided_supervisor_strategy()
        {
            var router = Sys.ActorOf(new ClusterRouterPool(new RoundRobinPool(1, null, new OneForOneStrategy(
                exception =>
                {
                    TestActor.Tell("supervised");
                    return Directive.Stop;
                }), null), new ClusterRouterPoolSettings(1, true, null, 1)).Props(Props.Create(() => new KillableActor(TestActor))), "therouter");

            router.Tell("go away");
            ExpectMsg("supervised", TimeSpan.FromSeconds(2));
        }

        #endregion
    }
}

