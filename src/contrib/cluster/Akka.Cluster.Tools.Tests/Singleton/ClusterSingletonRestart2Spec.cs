//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonRestart2Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonRestart2Spec : AkkaSpec
    {
        private readonly ActorSystem _sys1;
        private readonly ActorSystem _sys2;
        private readonly ActorSystem _sys3;
        private ActorSystem _sys4 = null;

        public ClusterSingletonRestart2Spec() : base(@"
              akka.loglevel = INFO
              akka.actor.provider = ""cluster""
              akka.cluster.roles = [singleton]
              akka.cluster.auto-down-unreachable-after = 2s
              akka.cluster.singleton.min-number-of-hand-over-retries = 5
              akka.remote {
                dot-netty.tcp {
                  hostname = ""127.0.0.1""
                  port = 0
                }
              }")
        {
            _sys1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys3 = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString("akka.cluster.roles = [other]")
                .WithFallback(Sys.Settings.Config));
        }

        public void Join(ActorSystem from, ActorSystem to)
        {
            if (Cluster.Get(from).SelfRoles.Contains("singleton"))
            {
                from.ActorOf(ClusterSingletonManager.Props(Props.Create(() => new Singleton()),
                    PoisonPill.Instance,
                    ClusterSingletonManagerSettings.Create(from).WithRole("singleton")), "echo");
            }


            Within(TimeSpan.FromSeconds(45), () =>
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
        public void Restarting_cluster_node_during_hand_over_must_restart_singletons_in_restarted_node()
        {
            Join(_sys1, _sys1);
            Join(_sys2, _sys1);
            Join(_sys3, _sys1);

            var proxy3 = _sys3.ActorOf(
                ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(_sys3).WithRole("singleton")), "proxy3");

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys3);
                    proxy3.Tell("hello", probe.Ref);
                    probe.ExpectMsg<UniqueAddress>(TimeSpan.FromSeconds(1))
                        .Should()
                        .Be(Cluster.Get(_sys1).SelfUniqueAddress);
                });
            });

            Cluster.Get(_sys1).Leave(Cluster.Get(_sys1).SelfAddress);

            // at the same time, shutdown sys2, which would be the expected next singleton node
            Shutdown(_sys2);
            // it will be downed by the join attempts of the new incarnation

            // then restart it
            // ReSharper disable once PossibleInvalidOperationException
            var sys2Port = Cluster.Get(_sys2).SelfAddress.Port.Value;
            var sys4Config = ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port=" + sys2Port)
                .WithFallback(_sys1.Settings.Config);
            _sys4 = ActorSystem.Create(_sys1.Name, sys4Config);

            Join(_sys4, _sys3);

            // let it stabilize
            Task.Delay(TimeSpan.FromSeconds(5)).Wait();

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys3);
                    proxy3.Tell("hello2", probe.Ref);

                    // note that sys3 doesn't have the required singleton role, so singleton instance should be
                    // on the restarted node
                    probe.ExpectMsg<UniqueAddress>(TimeSpan.FromSeconds(1))
                        .Should()
                        .Be(Cluster.Get(_sys4).SelfUniqueAddress);
                });
            });
        }

        protected override void AfterTermination()
        {
            Shutdown(_sys1);
            Shutdown(_sys2);
            Shutdown(_sys3);
            if (_sys4 != null)
                Shutdown(_sys4);
        }

        public class Singleton : ReceiveActor
        {

            public Singleton()
            {
                ReceiveAny(o =>
                {
                    Sender.Tell(Cluster.Get(Context.System).SelfUniqueAddress);
                });
            }
        }
    }
}
