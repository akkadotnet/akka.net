// //-----------------------------------------------------------------------
// // <copyright file="ClusterRouterRoutee4872BugFixSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tests.Routing
{
    public class ClusterRouterRoutee4872BugFixSpec : AkkaSpec
    {
        private static readonly Config ClusterConfig = Config.Empty
            .WithFallback(XunitActorLogger.AkkaConfig) // log actors output into xunit
            .WithFallback(ConfigurationFactory.ParseString(@"
akka {
  actor{
    provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
        deployment {
            /commandrouter {
                router = round-robin-group
		        routees.paths = [""/user/messageworker""]
                nr-of-instances = 2
                cluster {
                    enabled = on
                    allow-local-routees = on
                }
            }
        }
  }
  remote {
    dot-netty.tcp {
        public-hostname = ""localhost""
        hostname = ""0.0.0.0""
        port = 0
    }
  }            

  cluster {
    seed-nodes = [] # this will not be used, we will connect systems manually
  }
}"));
        
        public ClusterRouterRoutee4872BugFixSpec(ITestOutputHelper output) : base(output)
        {
            XunitActorLogger.OutputHelper = output;
        }

        [Fact]
        public async Task Should_Ask_Clustered_Group_Router_and_forward_ask_to_routee()
        {
            // Start first node with router and worker
            var sys1 = ActorSystem.Create("ClusterSystem", ClusterConfig);
            var worker1 = sys1.ActorOf(MessageActor.GetProps(), "messageworker");
            var router1 = sys1.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "commandrouter");

            // Start second node with router and worker
            var sys2 = ActorSystem.Create("ClusterSystem", ClusterConfig);
            var worker2 = sys2.ActorOf(MessageActor.GetProps(), "messageworker");
            var router2 = sys2.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "commandrouter");

            // connect systems with each other
            var cluster1 = Akka.Cluster.Cluster.Get(sys1);
            await cluster1.JoinAsync(cluster1.SelfAddress);
            await Akka.Cluster.Cluster.Get(sys2).JoinAsync(cluster1.SelfAddress);

            var routees1 = await router1.Ask<Routees>(GetRoutees.Instance);
            routees1.Members.Should().HaveCount(2);
            var routees2 = await router2.Ask<Routees>(GetRoutees.Instance);
            routees2.Members.Should().HaveCount(2);
        }

        class MessageActor : ReceiveActor
        {
            public static Props GetProps() => Props.Create(() => new MessageActor());
        }
        
        /// <summary>
        /// Logger used to output Akka.NET logs to XUnit test output
        /// </summary>
        class XunitActorLogger : ReceiveActor
        {
            // Going to be set before AkkaSystem started during spec initialization
            public static ITestOutputHelper OutputHelper { get; set; }

            public static readonly Config AkkaConfig = ConfigurationFactory.ParseString(
                $@"akka.loggers = [""{typeof(XunitActorLogger).FullName}, {typeof(XunitActorLogger).Assembly.GetName().Name}""]"
            );

            public XunitActorLogger()
            {
                Receive<Debug>(e => this.Log(LogLevel.DebugLevel, e.ToString()));
                Receive<Info>(e => this.Log(LogLevel.InfoLevel, e.ToString()));
                Receive<Warning>(e => this.Log(LogLevel.WarningLevel, e.ToString()));
                Receive<Error>(e => this.Log(LogLevel.ErrorLevel, e.ToString()));
                Receive<InitializeLogger>(_ => Sender.Tell(new LoggerInitialized()));
            }

            private void Log(LogLevel level, string str)
            {
                OutputHelper?.WriteLine($"[{level}] {str}");
            }
        }
    }
}