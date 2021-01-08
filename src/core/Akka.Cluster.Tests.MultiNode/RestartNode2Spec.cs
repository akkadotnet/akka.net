//-----------------------------------------------------------------------
// <copyright file="RestartNode2Spec.cs" company="Akka.NET Project">
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
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    /*
     * N.B. - Regions are used for targeting by DocFx to include
     * code inside relevant documentation.
     */
    #region MultiNodeSpecConfig
    public class RestartNode2SpecConfig : MultiNodeConfig
    {
        public RoleName Seed1 { get; }

        public RoleName Seed2 { get; }

        public RestartNode2SpecConfig()
        {
            Seed1 = Role("seed1");
            Seed2 = Role("seed2");

            CommonConfig = DebugConfig(true)
                .WithFallback(ConfigurationFactory.ParseString(@"
                  akka.cluster.auto-down-unreachable-after = 2s
                  akka.cluster.retry-unsuccessful-join-after = 3s
                  akka.remote.retry-gate-closed-for = 45s
                  akka.remote.log-remote-lifecycle-events = INFO
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }
    #endregion

    #region MultiNodeSpec
    public class RestartNode2Spec : MultiNodeClusterSpec
    {
        private class Watcher : ReceiveActor
        {
            public Watcher()
            {
                Receive<Address>(a =>
                {
                    seedNode1Address = a;
                    Sender.Tell("ok");
                });
            }
        }

        readonly RestartNode2SpecConfig _config;

        private Lazy<ActorSystem> seed1System;
        private Lazy<ActorSystem> restartedSeed1System;
        private static Address seedNode1Address;
        private ImmutableList<Address> SeedNodes
        {
            get
            {
                return ImmutableList.Create(seedNode1Address, GetAddress(_config.Seed2));
            }
        }

        public RestartNode2Spec() : this(new RestartNode2SpecConfig()) { }

        protected RestartNode2Spec(RestartNode2SpecConfig config) : base(config, typeof(RestartNode2Spec))
        {
            _config = config;
            seed1System = new Lazy<ActorSystem>(() => ActorSystem.Create(Sys.Name, Sys.Settings.Config));
            restartedSeed1System = new Lazy<ActorSystem>(
                () => ActorSystem.Create(Sys.Name, ConfigurationFactory
                    .ParseString("akka.remote.netty.tcp.port = " + SeedNodes.First().Port)
                    .WithFallback(Sys.Settings.Config)));
        }

        protected override void AfterTermination()
        {
            RunOn(() =>
            {
                Shutdown(seed1System.Value.WhenTerminated.IsCompleted ? restartedSeed1System.Value : seed1System.Value);
            }, _config.Seed1);
        }

        [MultiNodeFact]
        public void RestartNode2Specs()
        {
            Cluster_seed_nodes_must_be_able_to_restart_first_seed_node_and_join_other_seed_nodes();
        }

        public void Cluster_seed_nodes_must_be_able_to_restart_first_seed_node_and_join_other_seed_nodes()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                RunOn(() =>
                {
                    // seed1System is a separate ActorSystem, to be able to simulate restart
                    // we must transfer its address to seed2
                    Sys.ActorOf(Props.Create<Watcher>().WithDeploy(Deploy.Local), "address-receiver");
                    EnterBarrier("seed1-address-receiver-ready");
                }, _config.Seed2);


                RunOn(() =>
                {
                    EnterBarrier("seed1-address-receiver-ready");
                    seedNode1Address = Cluster.Get(seed1System.Value).SelfAddress;
                    foreach (var r in ImmutableList.Create(_config.Seed2))
                    {
                        Sys.ActorSelection(new RootActorPath(GetAddress(r)) / "user" / "address-receiver").Tell(seedNode1Address);
                        ExpectMsg("ok", TimeSpan.FromSeconds(5));
                    }
                }, _config.Seed1);
                EnterBarrier("seed1-address-transfered");

                // now we can join seed1System, seed2 together
                RunOn(() =>
                {
                    Cluster.Get(seed1System.Value).JoinSeedNodes(SeedNodes);
                    AwaitAssert(() => Cluster.Get(seed1System.Value).ReadView.Members.Count.Should().Be(2));
                    AwaitAssert(() => Cluster.Get(seed1System.Value).ReadView.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue());
                }, _config.Seed1);

                RunOn(() =>
                {
                    Cluster.JoinSeedNodes(SeedNodes);
                    AwaitMembersUp(2);
                }, _config.Seed2);
                EnterBarrier("started");

                // shutdown seed1System
                RunOn(() =>
                {
                    Shutdown(seed1System.Value, RemainingOrDefault);
                }, _config.Seed1);
                EnterBarrier("seed1-shutdown");

                RunOn(() =>
                {
                    Cluster.Get(restartedSeed1System.Value).JoinSeedNodes(SeedNodes);
                    Within(TimeSpan.FromSeconds(30), () =>
                    {
                        AwaitAssert(() => Cluster.Get(restartedSeed1System.Value).ReadView.Members.Count.Should().Be(2));
                        AwaitAssert(() => Cluster.Get(restartedSeed1System.Value).ReadView.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue());
                    });
                }, _config.Seed1);

                RunOn(() =>
                {
                    AwaitMembersUp(2);
                }, _config.Seed2);
                EnterBarrier("seed1-restarted");
            });
        }
    }
    #endregion
}
