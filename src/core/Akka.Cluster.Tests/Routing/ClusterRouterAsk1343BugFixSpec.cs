//-----------------------------------------------------------------------
// <copyright file="ClusterRouterAsk1343BugFixSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Cluster.Tests.Routing
{
    /// <summary>
    /// Spec to get to the bottom of https://github.com/akkadotnet/akka.net/issues/1343
    /// </summary>
    public class ClusterRouterAsk1343BugFixSpec : AkkaSpec
    {
        public ClusterRouterAsk1343BugFixSpec()
            : base(@"
    akka{
        actor{
            ask-timeout = 0.5s # use a default ask timeout
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
                /router2 {
                    router = round-robin-group
                    nr-of-instances = 1
                    routees.paths = [""/user/echo""]
                    cluster {
                        enabled = on
                        max-nr-of-instances-per-node = 1
                        allow-local-routees = true
                    }
                }
                /router3 {
                    router = round-robin-group
                    nr-of-instances = 1
                    routees.paths = [""/user/echo""]
                    cluster {
                        enabled = on
                        max-nr-of-instances-per-node = 1
                        allow-local-routees = false #no one to route to!
                    }
                }
            }
        }
        
        remote.dot-netty.tcp.port = 0
    }")
        {
        }

        
        [Fact]
        public async Task Should_Ask_Clustered_Pool_Router_and_forward_ask_to_routee()
        {
            var router = Sys.ActorOf(EchoActor.Props(this, true).WithRouter(FromConfig.Instance), "router1");
            Assert.IsType<RoutedActorRef>(router);

            var result = await router.Ask<string>("foo");
            ExpectMsg<string>().ShouldBe(result);
        }

        [Fact]
        public async Task Should_Ask_Clustered_Group_Router_and_forward_ask_to_routee()
        {
            var echo = Sys.ActorOf(EchoActor.Props(this, true), "echo");
            var router = Sys.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "router2");
            Assert.IsType<RoutedActorRef>(router);

            var result = await router.Ask<string>("foo");
            ExpectMsg<string>().ShouldBe(result);
        }

        [Fact]
        public async Task Should_Ask_Clustered_Group_Router_and_with_no_routees_and_timeout()
        {
            var router = Sys.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "router3");
            Assert.IsType<RoutedActorRef>(router);

            await Assert.ThrowsAsync<AskTimeoutException>(async () => await router.Ask<int>("foo"));
        }
    }
}
