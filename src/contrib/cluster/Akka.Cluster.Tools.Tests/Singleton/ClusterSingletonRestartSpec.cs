using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonRestartSpec : AkkaSpec
    {
        private readonly ActorSystem sys1;
        private readonly ActorSystem sys2;
        private ActorSystem sys3 = null;

        public ClusterSingletonRestartSpec() : base(@"
              akka.loglevel = INFO
              akka.actor.provider = ""cluster""
              akka.cluster.auto-down-unreachable-after = 2s
              akka.remote {
                dot-netty.tcp {
                  hostname = ""127.0.0.1""
                  port = 0
                }
              }")
        {
            sys1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        }

        public void Join(ActorSystem from, ActorSystem to)
        {
            from.ActorOf(ClusterSingletonManager.Props(Echo.Props,
                PoisonPill.Instance,
                ClusterSingletonManagerSettings.Create(from)), "echo");

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(from).Join(Cluster.Get(to).SelfAddress);
                    Cluster.Get(from).State.Members.Select(x => x.UniqueAddress).Should().Contain(Cluster.Get(from).SelfUniqueAddress);
                    Cluster.Get(from)
                        .State.Members.Select(x => x.Status)
                        .ToImmutableHashSet()
                        .Should()
                        .Equal(ImmutableHashSet<MemberStatus>.Empty.Add(MemberStatus.Up));
                });
            });
        }

        [Fact]
        public void Restarting_cluster_node_with_same_hostname_and_port_must_handover_to_next_oldest()
        {
            Join(sys1, sys1);
            Join(sys2, sys1);

            var proxy2 = sys2.ActorOf(
                ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(sys2)), "proxy2");

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(sys2);
                    proxy2.Tell("hello", probe.Ref);
                    probe.ExpectMsg("hello", TimeSpan.FromSeconds(1));
                });
            });

            Shutdown(sys1);
            // it will be downed by the join attempts of the new incarnation

            // ReSharper disable once PossibleInvalidOperationException
            var sys1Port = Cluster.Get(sys1).SelfAddress.Port.Value;
            var sys3Config = ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port=" + sys1Port)
                .WithFallback(sys1.Settings.Config);
            sys3 = ActorSystem.Create(sys1.Name, sys3Config);

            Join(sys3, sys2);

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(sys2);
                    proxy2.Tell("hello2", probe.Ref);
                    probe.ExpectMsg("hello2", TimeSpan.FromSeconds(1));
                });
            });

            Cluster.Get(sys2).Leave(Cluster.Get(sys2).SelfAddress);

            Within(TimeSpan.FromSeconds(15), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(sys3)
                        .State.Members.Select(x => x.UniqueAddress)
                        .Should()
                        .Equal(Cluster.Get(sys3).SelfUniqueAddress);
                });
            });

            var proxy3 =
                sys3.ActorOf(ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(sys3)),
                    "proxy3");

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(sys3);
                    proxy3.Tell("hello3", probe.Ref);
                    probe.ExpectMsg("hello3", TimeSpan.FromSeconds(1));
                });
            });
        }

        protected override void AfterTermination()
        {
            Shutdown(sys1);
            Shutdown(sys2);
            if(sys3 != null)
                Shutdown(sys3);
        }

        /// <summary>
        /// NOTE:
        /// For some reason the built-in <see cref="EchoActor"/> is over complicated and
        /// doesn't just reply to the sender, but also replies to <see cref="TestActor"/> as well.
        /// 
        /// Created this so we have something simple to work with.
        /// </summary>
        public class Echo : ReceiveActor
        {
            public static Props Props = Props.Create(() => new Echo());

            public Echo()
            {
                ReceiveAny(o =>
                {
                    Sender.Tell(o);
                });
            }
        }
    }
}
