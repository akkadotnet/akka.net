//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
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
        private readonly ActorSystem _sys1;
        private readonly ActorSystem _sys2;
        private ActorSystem _sys3 = null;

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
            _sys1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
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
            Join(_sys1, _sys1);
            Join(_sys2, _sys1);

            var proxy2 = _sys2.ActorOf(
                ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(_sys2)), "proxy2");

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys2);
                    proxy2.Tell("hello", probe.Ref);
                    probe.ExpectMsg("hello", TimeSpan.FromSeconds(1));
                });
            });

            Shutdown(_sys1);
            // it will be downed by the join attempts of the new incarnation

            // ReSharper disable once PossibleInvalidOperationException
            var sys1Port = Cluster.Get(_sys1).SelfAddress.Port.Value;
            var sys3Config = ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port=" + sys1Port)
                .WithFallback(_sys1.Settings.Config);
            _sys3 = ActorSystem.Create(_sys1.Name, sys3Config);

            Join(_sys3, _sys2);

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys2);
                    proxy2.Tell("hello2", probe.Ref);
                    probe.ExpectMsg("hello2", TimeSpan.FromSeconds(1));
                });
            });

            Cluster.Get(_sys2).Leave(Cluster.Get(_sys2).SelfAddress);

            Within(TimeSpan.FromSeconds(15), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(_sys3)
                        .State.Members.Select(x => x.UniqueAddress)
                        .Should()
                        .Equal(Cluster.Get(_sys3).SelfUniqueAddress);
                });
            });

            var proxy3 =
                _sys3.ActorOf(ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(_sys3)),
                    "proxy3");

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys3);
                    proxy3.Tell("hello3", probe.Ref);
                    probe.ExpectMsg("hello3", TimeSpan.FromSeconds(1));
                });
            });
        }

        protected override void AfterTermination()
        {
            Shutdown(_sys1);
            Shutdown(_sys2);
            if(_sys3 != null)
                Shutdown(_sys3);
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
