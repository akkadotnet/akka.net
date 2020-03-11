//-----------------------------------------------------------------------
// <copyright file="StatsSampleSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;
using Akka.Configuration;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Cluster.Metrics.Tests.MultiNode
{
    public class StatsSampleSpecConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public IImmutableSet<RoleName> NodeList => ImmutableHashSet.Create<RoleName>(First, Second, Third);

        public StatsSampleSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            
            // this configuration will be used for all nodes
            // note that no fixed host names and ports are used
            CommonConfig = ConfigurationFactory.ParseString(@"
                # Enable metrics extension in akka-cluster-metrics.
                akka.extensions=[""Akka.Cluster.Metrics.ClusterMetricsExtensionProvider, Akka.Cluster.Metrics""]

                akka.actor.provider = cluster
                akka.remote.classic.log-remote-lifecycle-events = off
                akka.cluster.roles = [compute]
                #//#router-lookup-config
                akka.actor.deployment {
                  /statsService/workerRouter {
                      router = consistent-hashing-group
                      routees.paths = [""/user/statsWorker""]
                        cluster {
                            enabled = on
                            allow-local-routees = on
                            use-roles = [""compute""]
                        }
                    }
                }
                #//#router-lookup-config
            ");
        }
    }

    public class StatsSampleSpec : MultiNodeSpec
    {
        private readonly StatsSampleSpecConfig _config;

        private StatsSampleSpec(StatsSampleSpecConfig config) 
            : base(config, typeof(StatsSampleSpec))
        {
            _config = config;
        }
        
        public StatsSampleSpec() 
            : this(new StatsSampleSpecConfig())
        {
        }

        /// <inheritdoc />
        protected override int InitialParticipantsValueFactory => Roles.Count;

        [MultiNodeFact]
        public async Task Stats_sample_should_illustrate_how_to_startup_cluster()
        {
            await Should_startup_cluster();
            await Should_show_usage_of_the_statsService_from_one_node();
            await Should_show_usage_of_the_stats_service_from_all_nodes();
        }

        private async Task Should_startup_cluster()
        {
            await WithinAsync(15.Seconds(), async () =>
            {
                var cluster = Cluster.Get(Sys);
                cluster.Subscribe(TestActor, typeof(ClusterEvent.MemberUp));
                ExpectMsg<ClusterEvent.CurrentClusterState>();

                var firstAddress = Node(_config.First).Address;
                var secondAddress = Node(_config.Second).Address;
                var thirdAddress = Node(_config.Third).Address;
                
                cluster.Join(firstAddress);

                Sys.ActorOf(Props.Create<StatsWorker>(), "statsWorker");
                Sys.ActorOf(Props.Create<StatsService>(), "statsService");

                ReceiveN(3).Select(m => (m as ClusterEvent.MemberUp).Member.Address).Distinct()
                    .Should().BeEquivalentTo(firstAddress, secondAddress, thirdAddress);
                
                cluster.Unsubscribe(TestActor);
                
                EnterBarrier("all-up");
            });
        }

        private async Task Should_show_usage_of_the_statsService_from_one_node()
        {
            await RunOnAsync(AssertServiceOk, _config.Second);
            
            EnterBarrier("done-2");
        }
        
        private async Task Should_show_usage_of_the_stats_service_from_all_nodes()
        {
            await AssertServiceOk();
            
            EnterBarrier("done-3");
        }

        private async Task AssertServiceOk()
        {
            var service = Sys.ActorSelection(Node(_config.Third) / "user" / "statsService");
            // eventually the service should be ok,
            // first attempts might fail because worker actors not started yet
            await AwaitAssertAsync(() =>
            {
               service.Tell(new StatsJob("this is the text that will be analyzed"));
               ExpectMsg<StatsResult>(1.Seconds()).MeanWordLength.Should().BeApproximately(3.875, 0.001);
            });
        }
    }
}
