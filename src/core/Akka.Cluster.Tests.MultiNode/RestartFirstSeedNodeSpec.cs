//-----------------------------------------------------------------------
// <copyright file="RestartFirstSeedNodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class RestartFirstSeedNodeSpecConfig : MultiNodeConfig
    {
        public readonly RoleName Seed1;
        public readonly RoleName Seed2;
        public readonly RoleName Seed3;

        public RestartFirstSeedNodeSpecConfig()
        {
            Seed1 = Role("seed1");
            Seed2 = Role("seed2");
            Seed3 = Role("seed3");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = off
                akka.cluster.retry-unsuccessful-join-after = 3s
            ").WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class RestartFirstSeedNodeSpec : MultiNodeClusterSpec
    {
        private readonly RestartFirstSeedNodeSpecConfig _config;
        private Address _missedSeed;
        private static Address _seedNode1Address;

        private Lazy<ActorSystem> seed1System;
        private Lazy<ActorSystem> restartedSeed1System;

        public RestartFirstSeedNodeSpec() : this(new RestartFirstSeedNodeSpecConfig())
        {
        }

        protected RestartFirstSeedNodeSpec(RestartFirstSeedNodeSpecConfig config) : base(config, typeof(RestartFirstSeedNodeSpec))
        {
            _config = config;
            _missedSeed = GetAddress(config.Seed3).WithPort(61313);

            seed1System = new Lazy<ActorSystem>(() =>
            {
                return ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            });

            restartedSeed1System = new Lazy<ActorSystem>(() =>
            {
                var localConfig = ConfigurationFactory
                    .ParseString("akka.remote.dot-netty.tcp.port=" + GetSeedNodes().First().Port)
                    .WithFallback(Sys.Settings.Config);
                return ActorSystem.Create(Sys.Name, localConfig);
            });
        }

        [MultiNodeFact]
        public void RestartFirstSeedNodeSpecs()
        {
            Cluster_seed_nodes__must_be_able_to_restart_first_seed_node_and_join_other_seed_nodes();
        }

        public void Cluster_seed_nodes__must_be_able_to_restart_first_seed_node_and_join_other_seed_nodes()
        {
            Within(TimeSpan.FromSeconds(40), () =>
            {
                RunOn(() =>
                {
                    Sys.ActorOf(Props.Create(() => new Listener())
                        .WithDeploy(Deploy.Local), "address-receiver");

                    EnterBarrier("seed1-address-receiver-ready");
                }, _config.Seed2, _config.Seed3);

                RunOn(() =>
                {
                    EnterBarrier("seed1-address-receiver-ready");
                    _seedNode1Address = Cluster.Get(seed1System.Value).SelfAddress;
                    foreach (var r in new List<RoleName> { _config.Seed2, _config.Seed3 })
                    {
                        Sys.ActorSelection(new RootActorPath(GetAddress(r)) / "user" / "address-receiver").Tell(_seedNode1Address);
                        ExpectMsg("ok", TimeSpan.FromSeconds(5));
                    }
                }, _config.Seed1);
                EnterBarrier("seed1-address-transfered");

                // now we can join seed1System, seed2, seed3 together
                RunOn(() =>
                {
                    Cluster.Get(seed1System.Value).JoinSeedNodes(GetSeedNodes());
                    AwaitAssert(() =>
                    {
                        Cluster.Get(seed1System.Value)
                            .ReadView.Members.Count
                            .Should()
                            .Be(3);
                    }, TimeSpan.FromSeconds(10));
                    AwaitAssert(() =>
                    {
                        Cluster.Get(seed1System.Value)
                            .ReadView.Members.All(c => c.Status == MemberStatus.Up)
                            .Should()
                            .BeTrue();
                    });
                }, _config.Seed1);
                RunOn(() =>
                {
                    Cluster.JoinSeedNodes(GetSeedNodes());
                    AwaitMembersUp(3);
                }, _config.Seed2, _config.Seed3);
                EnterBarrier("started");

                // shutdown seed1System
                RunOn(() =>
                {
                    Shutdown(seed1System.Value, RemainingOrDefault);
                }, _config.Seed1);
                EnterBarrier("seed1-shutdown");

                RunOn(() =>
                {
                    Cluster.Get(restartedSeed1System.Value).JoinSeedNodes(GetSeedNodes());
                    Within(TimeSpan.FromSeconds(20), () =>
                    {
                        AwaitAssert(() =>
                        {
                            Cluster.Get(restartedSeed1System.Value)
                                .ReadView.Members.Count
                                .Should()
                                .Be(3);
                        });
                        AwaitAssert(() =>
                        {
                            Cluster.Get(restartedSeed1System.Value)
                                .ReadView.Members.All(c => c.Status == MemberStatus.Up)
                                .Should()
                                .BeTrue();
                        });
                    });
                }, _config.Seed1);

                RunOn(() =>
                {
                    AwaitMembersUp(3);
                }, _config.Seed2, _config.Seed3);
                EnterBarrier("seed1-restarted");
            });
        }

        private ImmutableList<Address> GetSeedNodes()
        {
            return ImmutableList.Create(
                _seedNode1Address,
                GetAddress(_config.Seed2),
                GetAddress(_config.Seed3),
                _missedSeed);
        }

        private class Listener : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                var address = message as Address;
                if (address != null)
                {
                    _seedNode1Address = address;
                    Sender.Tell("ok");
                }
            }
        }
    }
}
