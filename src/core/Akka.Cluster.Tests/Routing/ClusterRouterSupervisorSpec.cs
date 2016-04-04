//-----------------------------------------------------------------------
// <copyright file="ClusterRouterSupervisorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
            deployment {
            /router1 {
                    router = round-robin-pool
                    nr-of-instances = 1
                    cluster {
                        enabled = on
                        max-nr-of-instances-per-node = 1
                        allow-local-routees = true
                    }
                }                
            }
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

        class RestartableActor : ReceiveActor
        {
            private readonly IActorRef TestActor;

            protected override void PostRestart(Exception reason)
            {
                base.PostRestart(reason);
                TestActor.Tell("restarted");
            }

            public RestartableActor(IActorRef testActor)
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

        [Fact]
        public void Cluster_aware_routers_must_use_default_supervisor_strategy_when_none_specified()
        {
            var router = Sys.ActorOf(Props.Create(() => new RestartableActor(TestActor)).WithRouter(FromConfig.Instance), "router1");

            router.Tell("go away");
            ExpectMsg("restarted", TimeSpan.FromSeconds(2));
        }

        #endregion
    }
}

