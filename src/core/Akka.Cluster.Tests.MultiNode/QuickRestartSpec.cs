//-----------------------------------------------------------------------
// <copyright file="QuickRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
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
        public async Task QuicklyRestartingNodeMust()
        {
            await SetupStableSeedNodes();
            await JoinAndRestart();
        }

        private async Task SetupStableSeedNodes()
        {
            await WithinAsync(TimeSpan.FromSeconds(15), async () =>
            {
                Cluster.JoinSeedNodes(_seedNodes.Value);
                AwaitMembersUp(Roles.Count);
                EnterBarrier("stable");
            });
        }

        private async Task JoinAndRestart()
        {
            var totalNumberOfNodes = Roles.Count + 1;
            ActorSystem restartingSystem = null; // only used on second
            for (var i = 1; i <= _rounds; i++)
            {
                var round = i; //non-loop variable closure
                Log.Info("Round-{0}", i);
                await RunOnAsync(async () =>
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
                    await WithinAsync(TimeSpan.FromSeconds(20), async () =>
                    {
                        await AwaitAssertAsync(() =>
                        {
                            Cluster.Get(restartingSystem).State.Members.Count.ShouldBe(totalNumberOfNodes);
                            Cluster.Get(restartingSystem).State.Members.All(x => x.Status == MemberStatus.Up).ShouldBeTrue();
                        });
                    });
                }, _config.Second);

                EnterBarrier("joined-"+i);
                await WithinAsync(TimeSpan.FromSeconds(20), async () =>
                {
                    await AwaitAssertAsync(() =>
                    {
                        Cluster.Get(Sys).State.Members.Count.ShouldBe(totalNumberOfNodes);
                        Cluster.Get(Sys).State.Members.All(x => x.Status == MemberStatus.Up).ShouldBeTrue();
                        // use the role to test that it is the new incarnation that joined, sneaky
                        Cluster.Get(Sys).State.Members.SelectMany(x => x.Roles).ShouldBe(new [] { $"round-{round}" });
                    });
                });
                EnterBarrier("members-up-"+i);

                // intentional delay to simulate gating — not timeout jiggling [slopwatch:SW004]
                if (i > 1)
                    await Task.Delay(TimeSpan.FromSeconds(ThreadLocalRandom.Current.Next(15)));

                await WithinAsync(TimeSpan.FromSeconds(20), async () =>
                {
                    await AwaitAssertAsync(() =>
                    {
                        Cluster.Get(Sys).State.Members.Count.ShouldBe(totalNumberOfNodes);
                        Cluster.Get(Sys).State.Members.All(x => x.Status == MemberStatus.Up).ShouldBeTrue();
                        Cluster.Get(Sys).State.Unreachable.Count.ShouldBe(0);
                    });
                });

                EnterBarrier("before-terminate-"+i);
                await RunOnAsync(async () =>
                {
                    await restartingSystem.Terminate().WaitAsync(RemainingOrDefault);
                }, _config.Second);

                // don't wait for it to be removed, new incarnation will join in next round
                EnterBarrier("terminated-"+i);
                Log.Info("End of round-"+i);
            }
        }

    }
}
