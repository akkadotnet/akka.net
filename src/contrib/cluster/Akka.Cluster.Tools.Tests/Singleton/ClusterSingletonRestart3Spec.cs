//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonRestart2Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Xunit.Abstractions;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonRestart3Spec : AkkaSpec
    {
        private readonly ActorSystem _sys1;
        private readonly ActorSystem _sys2;
        private readonly ActorSystem _sys3;

        public ClusterSingletonRestart3Spec(ITestOutputHelper output) : base(@"
              akka.loglevel = DEBUG
              akka.actor.provider = ""cluster""
              akka.cluster.app-version = ""1.0.0""
              akka.cluster.auto-down-unreachable-after = 2s
              akka.cluster.singleton.min-number-of-hand-over-retries = 5
              akka.cluster.singleton.consider-app-version = true
              akka.remote {
                dot-netty.tcp {
                  hostname = ""127.0.0.1""
                  port = 0
                }
              }", output)
        {
            _sys1 = Sys;
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            InitializeLogger(_sys2);
            _sys3 = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString("akka.cluster.app-version = \"1.0.2\"")
                .WithFallback(Sys.Settings.Config));
            InitializeLogger(_sys3);
        }

        public void Join(ActorSystem from, ActorSystem to)
        {
            from.ActorOf(ClusterSingletonManager.Props(Props.Create(() => new Singleton()),
                PoisonPill.Instance,
                ClusterSingletonManagerSettings.Create(from)), "echo");


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
        public void Singleton_should_consider_AppVersion_when_handing_over()
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
                    proxy2.Tell("poke", probe.Ref);
                    var singleton = probe.ExpectMsg<Member>(TimeSpan.FromSeconds(1));
                    singleton.Should().Be(Cluster.Get(_sys1).SelfMember);
                    singleton.AppVersion.Version.Should().Be("1.0.0");
                });
            });
            
            // A new node with higher AppVersion joins the cluster
            Join(_sys3, _sys1);

            // Old node with the singleton instance left the cluster
            Cluster.Get(_sys1).Leave(Cluster.Get(_sys1).SelfAddress);

            // let it stabilize
            Task.Delay(TimeSpan.FromSeconds(5)).Wait();

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe(_sys2);
                    proxy2.Tell("poke", probe.Ref);

                    // note that _sys3 has a higher app-version, so the singleton should start there
                    var singleton = probe.ExpectMsg<Member>(TimeSpan.FromSeconds(1));
                    singleton.Should().Be(Cluster.Get(_sys3).SelfMember);
                    singleton.AppVersion.Version.Should().Be("1.0.2");
                });
            });
        }

        protected override async Task AfterAllAsync()
        {
            await base.AfterAllAsync();
            await ShutdownAsync(_sys2);
            await ShutdownAsync(_sys3);
        }

        public class Singleton : ReceiveActor
        {
            public Singleton()
            {
                ReceiveAny(o =>
                {
                    Sender.Tell(Cluster.Get(Context.System).SelfMember);
                });
            }
        }
    }
}
