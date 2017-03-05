//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public sealed class ClusterSingletonRestartSpec : AkkaSpec
    {
        public class EchoActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }

            public static Props Props { get; } = Props.Create(() => new EchoActor());
        }

        private readonly ActorSystem _sys1;
        private readonly ActorSystem _sys2;
        private ActorSystem _sys3;

        public ClusterSingletonRestartSpec() : base(GetConfig())
        {
            _sys1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys3 = null;
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"
              akka.loglevel = INFO
              akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
              akka.remote {{
                dot-netty.tcp {{
                  hostname = ""127.0.0.1""
                  port = 0
                }}
              }}
            ");
        }

        private void Join(ActorSystem from, ActorSystem to)
        {
            from.ActorOf(ClusterSingletonManager.Props(
                singletonProps: EchoActor.Props,
                terminationMessage: PoisonPill.Instance,
                settings: ClusterSingletonManagerSettings.Create(from)),
                name: "echo");

            Within(10.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(from).Join(Cluster.Get(to).SelfAddress);
                    Cluster.Get(from).State.Members.Select(c => c.UniqueAddress).Should().Contain(Cluster.Get(from).SelfUniqueAddress);
                    Cluster.Get(from).State.Members.Select(x => x.Status).ToImmutableHashSet().Should().Equal(ImmutableHashSet.Create(MemberStatus.Up));
                });
            });
        }

        [Fact]
        public void Restarting_cluster_node_with_same_hostname_and_port_must_handover_to_next_oldest()
        {
            Join(_sys1, _sys1);
            Join(_sys2, _sys1);

            var proxy2 = _sys2.ActorOf(ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(_sys2)), "proxy2");

            Within(5.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys2);
                    proxy2.Tell("hello", probe.Ref);
                    probe.ExpectMsg("hello", 1.Seconds());
                });
            });

            Shutdown(_sys1);
            // it will be downed by the join attempts of the new incarnation

            _sys3 = ActorSystem.Create(
                Sys.Name,
                ConfigurationFactory.ParseString($"akka.remote.dot-netty.tcp.port={Cluster.Get(_sys1).SelfAddress.Port}").WithFallback(Sys.Settings.Config));
            Join(_sys3, _sys2);

            Within(5.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys2);
                    proxy2.Tell("hello2", probe.Ref);
                    probe.ExpectMsg("hello2", 1.Seconds());
                });
            });

            Cluster.Get(_sys2).Leave(Cluster.Get(_sys2).SelfAddress);

            Within(10.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(_sys3).State.Members.Select(c => c.UniqueAddress)
                        .Should().BeEquivalentTo(ImmutableHashSet.Create(Cluster.Get(_sys3).SelfUniqueAddress));
                });
            });

            var proxy3 = _sys3.ActorOf(ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(_sys3)), "proxy3");

            Within(5.Seconds(), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys3);
                    proxy3.Tell("hello3", probe.Ref);
                    probe.ExpectMsg("hello3", 1.Seconds());
                });
            });
        }

        protected override void AfterTermination()
        {
            Shutdown(_sys1);
            Shutdown(_sys2);
            if (_sys3 != null)
                Shutdown(_sys3);
        }
    }
}
