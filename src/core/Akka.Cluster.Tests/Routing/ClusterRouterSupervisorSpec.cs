//-----------------------------------------------------------------------
// <copyright file="ClusterRouterSupervisorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Dispatch;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.Routing
{
    public class ClusterRouterSupervisorSpec : AkkaSpec
    {
        public ClusterRouterSupervisorSpec()
            : base(@"
                    akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                    akka.remote.dot-netty.tcp.port = 0")
        {
        }

        class KillableActor : ReceiveActor
        {
            private readonly IActorRef TestActor;

            public KillableActor(IActorRef testActor)
            {
                TestActor = testActor;
                Receive<string>(s => s == "go away", _ =>
                {
                    throw new ArgumentException("Goodbye then!");
                });
            }
        }

        [Fact]
        public async Task Cluster_aware_routers_must_use_provided_supervisor_strategy()
        {
            var escalator = new OneForOneStrategy(
                _ =>
                {
                    TestActor.Tell("supervised");
                    return Directive.Stop;
                });

            var settings = new ClusterRouterPoolSettings(1, 1, true);

            var router =
                Sys.ActorOf(
                    new ClusterRouterPool(new RoundRobinPool(1, null, escalator, Dispatchers.DefaultDispatcherId),
                        settings)
                        .Props(Props.Create(() => new KillableActor(TestActor))), "therouter");

            router.Tell("go away");
            await ExpectMsgAsync("supervised");
        }
    }
}

