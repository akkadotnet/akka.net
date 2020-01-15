//-----------------------------------------------------------------------
// <copyright file="RestartNode3Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class RestartNode3SpecConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }
        public RoleName Third { get; private set; }

        public RestartNode3SpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.cluster.auto-down-unreachable-after = off
                    akka.cluster.allow-weakly-up-members = off"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class RestartNode3Spec : MultiNodeClusterSpec
    {
        private class Watcher : ReceiveActor
        {
            public Watcher()
            {
                Receive<UniqueAddress>(a =>
                {
                    secondUniqueAddress = a;
                    Sender.Tell("ok");
                });
            }
        }

        private readonly RestartNode3SpecConfig _config;
        private ImmutableList<Address> seedNodes => ImmutableList.Create(GetAddress(_config.First));
        private Lazy<ActorSystem> secondSystem;
        private Lazy<ActorSystem> restartedSecondSystem;
        private static UniqueAddress secondUniqueAddress = null;

        public RestartNode3Spec() : this(new RestartNode3SpecConfig())
        {
        }

        protected RestartNode3Spec(RestartNode3SpecConfig config) : base(config, typeof(RestartNode3Spec))
        {
            _config = config;
            secondSystem = new Lazy<ActorSystem>(() => ActorSystem.Create(Sys.Name, Sys.Settings.Config));
            restartedSecondSystem = new Lazy<ActorSystem>(() => ActorSystem.Create(
                Sys.Name,
                ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port=" + secondUniqueAddress.Address.Port)
                    .WithFallback(Sys.Settings.Config)));
        }

        protected override void AfterTermination()
        {
            if (!Log.IsDebugEnabled)
            {
                MuteDeadLetters();
                Sys.EventStream.SetLogLevel(LogLevel.ErrorLevel);
            }
            base.AfterTermination();
        }

        [MultiNodeFact]
        public void RestartNode3Specs()
        {
            Cluster_nodes_must_be_able_to_restart_and_join_again_when_down_before_up();
        }

        public void Cluster_nodes_must_be_able_to_restart_and_join_again_when_down_before_up()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                // secondSystem is a separate ActorSystem, to be able to simulate restart
                // we must transfer its address to first
                RunOn(() =>
                {
                    Sys.ActorOf(Props.Create<Watcher>().WithDeploy(Deploy.Local), "address-receiver");
                    EnterBarrier("second-address-receiver-ready");
                }, _config.First, _config.Third);

                RunOn(() =>
                {
                    EnterBarrier("second-address-receiver-ready");
                    secondUniqueAddress = Cluster.Get(secondSystem.Value).SelfUniqueAddress;
                    foreach (var r in ImmutableList.Create(_config.First, _config.Third))
                    {
                        Sys.ActorSelection(new RootActorPath(GetAddress(r)) / "user" / "address-receiver")
                            .Tell(secondUniqueAddress);
                        ExpectMsg("ok", TimeSpan.FromSeconds(5));
                    }
                }, _config.Second);
                EnterBarrier("second-address-transfered");

                // now we can join first, third together
                RunOn(() =>
                {
                    Cluster.JoinSeedNodes(seedNodes);
                    AwaitMembersUp(2);
                }, _config.First, _config.Third);
                EnterBarrier("first-third-up");

                // make third unreachable, so that leader can't perform its duties
                RunOn(() =>
                {
                    TestConductor.Blackhole(_config.First, _config.Third, ThrottleTransportAdapter.Direction.Both)
                        .Wait();
                    var thirdAddress = GetAddress(_config.Third);
                    AwaitAssert(() => ClusterView.UnreachableMembers.Select(c => c.Address)
                        .Should().BeEquivalentTo(thirdAddress));
                }, _config.First);
                EnterBarrier("third-unreachable");

                RunOn(() =>
                {
                    Cluster.Get(secondSystem.Value).JoinSeedNodes(seedNodes);
                    AwaitAssert(() => Cluster.Get(secondSystem.Value).ReadView.Members.Should().HaveCount(3));
                    AwaitAssert(() => Cluster.Get(secondSystem.Value).ReadView
                        .Members
                        .Where(m => m.Address == Cluster.Get(secondSystem.Value).SelfAddress)
                        .Select(c => c.Status)
                        .First()
                        .Should().Be(MemberStatus.Joining));
                }, _config.Second);
                EnterBarrier("second-joined");

                // shutdown secondSystem
                RunOn(() =>
                {
                    Shutdown(secondSystem.Value, Remaining);
                }, _config.Second);
                EnterBarrier("second-shutdown");

                // then immediately start restartedSecondSystem, which has the same address as secondSystem
                RunOn(() =>
                {
                    TestConductor.PassThrough(_config.First, _config.Third, ThrottleTransportAdapter.Direction.Both)
                        .Wait();
                }, _config.First);

                RunOn(() =>
                {
                    Cluster.Get(restartedSecondSystem.Value).JoinSeedNodes(seedNodes);
                    AwaitAssert(() => Cluster.Get(restartedSecondSystem.Value).ReadView.Members.Should().HaveCount(3));
                    AwaitAssert(() => Cluster.Get(restartedSecondSystem.Value).ReadView
                        .Members
                        .All(c => c.Status == MemberStatus.Up)
                        .Should().BeTrue());
                }, _config.Second);

                RunOn(() =>
                {
                    AwaitAssert(() => Cluster.Get(Sys).ReadView.Members.Should().HaveCount(3));
                    AwaitAssert(() => Cluster.Get(Sys).ReadView
                        .Members
                        .Count(
                            m =>
                                m.Address == secondUniqueAddress.Address &&
                                m.UniqueAddress.Uid != secondUniqueAddress.Uid).Should().BeGreaterThan(0)
                        );
                }, _config.First, _config.Third);
                EnterBarrier("second-restarted");
            });
        }
    }
}
