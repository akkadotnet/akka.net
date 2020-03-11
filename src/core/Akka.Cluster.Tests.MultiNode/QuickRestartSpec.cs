//-----------------------------------------------------------------------
// <copyright file="QuickRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.Util;

namespace Akka.Cluster.Tests.MultiNode
{
    public class QuickRestartSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public QuickRestartSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.cluster.auto-down-unreachable-after = off
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class QuickRestartSpec : MultiNodeClusterSpec
    {
        private readonly QuickRestartSpecConfig _config;
        private readonly Lazy<ImmutableList<Address>> _seedNodes;
        private int _rounds = 3;

        protected override TimeSpan ShutdownTimeout => TimeSpan.FromSeconds(45 * _rounds);

        public QuickRestartSpec() : this(new QuickRestartSpecConfig())
        {

        }

        protected QuickRestartSpec(QuickRestartSpecConfig config) : base(config, typeof(QuickRestartSpec))
        {
            _config = config;
            _seedNodes = new Lazy<ImmutableList<Address>>(() => ImmutableList<Address>.Empty
            .Add(GetAddress(_config.First))
            .Add(GetAddress(_config.Second))
            .Add(GetAddress(_config.Third)));
        }

        [MultiNodeFact]
        public void QuicklyRestartingNodeMust()
        {
            SetupStableSeedNodes();
            JoinAndRestart();
        }

        private void SetupStableSeedNodes()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                Cluster.JoinSeedNodes(_seedNodes.Value);
                AwaitMembersUp(Roles.Count);
                EnterBarrier("stable");
            });
        }

        private void JoinAndRestart()
        {
            var totalNumberOfNodes = Roles.Count + 1;
            ActorSystem restartingSystem = null; // only used on second
            for (var i = 1; i <= _rounds; i++)
            {
                var round = i; //non-loop variable closure
                Log.Info("Round-{0}", i);
                RunOn(() =>
                {
                    restartingSystem = restartingSystem == null
                        ? ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString($"akka.cluster.roles=[round-{round}]")
                            .WithFallback(Sys.Settings.Config))
                        : ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString($"akka.cluster.roles=[round-{round}]")
                            .WithFallback(
                                $"akka.remote.dot-netty.tcp.port={Cluster.Get(restartingSystem).SelfAddress.Port}")
                            .WithFallback(Sys.Settings.Config));
                    Log.Info("Restarting node has address {0}", Cluster.Get(restartingSystem).SelfUniqueAddress);
                    Cluster.Get(restartingSystem).JoinSeedNodes(_seedNodes.Value);
                    Within(TimeSpan.FromSeconds(20), () =>
                    {
                        AwaitAssert(() =>
                        {
                            Cluster.Get(restartingSystem).State.Members.Count.ShouldBe(totalNumberOfNodes);
                            Cluster.Get(restartingSystem).State.Members.All(x => x.Status == MemberStatus.Up).ShouldBeTrue();
                        });
                    });
                }, _config.Second);

                EnterBarrier("joined-"+i);
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.Get(Sys).State.Members.Count.ShouldBe(totalNumberOfNodes);
                        Cluster.Get(Sys).State.Members.All(x => x.Status == MemberStatus.Up).ShouldBeTrue();
                        // use the role to test that it is the new incarnation that joined, sneaky
                        Cluster.Get(Sys).State.Members.SelectMany(x => x.Roles).ShouldBe(new [] { $"round-{round}" });
                    });
                });
                EnterBarrier("members-up-"+i);

                // gating occurred after a while
                if (i > 1)
                    Thread.Sleep(ThreadLocalRandom.Current.Next(15) * 1000);

                Cluster.Get(Sys).State.Members.Count.ShouldBe(totalNumberOfNodes);
                Cluster.Get(Sys).State.Members.All(x => x.Status == MemberStatus.Up).ShouldBeTrue();
                Cluster.Get(Sys).State.Unreachable.Count.ShouldBe(0);

                EnterBarrier("before-terminate-"+i);
                RunOn(() =>
                {
                    restartingSystem.Terminate().Wait(RemainingOrDefault).ShouldBeTrue("Timed out when terminating actorsystem");
                }, _config.Second);

                // don't wait for it to be removed, new incarnation will join in next round
                EnterBarrier("terminated-"+i);
                Log.Info("End of round-"+i);
            }
        }

    }
}

