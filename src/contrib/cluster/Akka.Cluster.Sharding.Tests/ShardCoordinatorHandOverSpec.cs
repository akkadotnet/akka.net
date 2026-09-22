//-----------------------------------------------------------------------
// <copyright file="ShardCoordinatorHandOverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    /// <summary>
    /// A <see cref="ClusterSingletonManager"/> hand-over sends the coordinator its termination message, and a
    /// hand-over is not always a shutdown: while a cluster forms, two members of the same role can both reach
    /// Oldest, and the loser hands over while staying in the cluster. The coordinator used to read every
    /// termination message as "this node is shutting down" and stop the node's own <see cref="ShardRegion"/>,
    /// which <see cref="ClusterShardingGuardian"/> then dropped from <see cref="ClusterSharding"/>'s cache for the
    /// rest of the process. This spec drives that path on one node.
    /// </summary>
    public class ShardCoordinatorHandOverSpec : AkkaSpec
    {
        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.state-store-mode = ddata
                akka.cluster.sharding.distributed-data.durable.keys = []")
                .WithFallback(ClusterSingleton.DefaultConfig())
                .WithFallback(ClusterSharding.DefaultConfig());

        private sealed class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(message => Sender.Tell(message));
            }
        }

        private sealed class Extractor : IMessageExtractor
        {
            public string EntityId(object message) => message.ToString()!;
            public object EntityMessage(object message) => message;
            public string ShardId(object message) => "1";
            public string ShardId(string entityId, object? messageHint = null) => "1";
        }

        public ShardCoordinatorHandOverSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        [Fact(DisplayName = "ShardCoordinator should keep the local ShardRegion running when the singleton hands over and the node stays in the cluster")]
        public async Task Should_keep_local_region_when_handover_is_not_a_shutdown()
        {
            var cluster = Cluster.Get(Sys);
            cluster.Join(cluster.SelfAddress);
            await AwaitAssertAsync(() => cluster.SelfMember.Status.Should().Be(MemberStatus.Up), TimeSpan.FromSeconds(10));

            var region = ClusterSharding.Get(Sys).Start(
                "Entity", Props.Create<EchoActor>(), ClusterShardingSettings.Create(Sys), new Extractor());

            // One round trip: the coordinator is running and the region is registered with it.
            region.Tell("1");
            await ExpectMsgAsync("1", TimeSpan.FromSeconds(10));

            // ClusterSingletonManager hands its termination message to the singleton child, the BackoffSupervisor
            // named "singleton", which forwards it to the coordinator and treats it as the final stop message.
            // Sending it there is the single-node stand-in for a hand-over.
            var singleton = await Sys.ActorSelection("/system/sharding/EntityCoordinator/singleton")
                .ResolveOne(TimeSpan.FromSeconds(5));
            await WatchAsync(singleton);

            singleton.Tell(ShardCoordinator.Terminate.Instance);

            // The coordinator still stops on a hand-over; that is what lets the singleton manager finish it.
            // Its supervisor stops with it, which is the Terminated we see here. The old code passes this
            // step too, because it deferred the coordinator's stop until it had stopped the region.
            await ExpectTerminatedAsync(singleton, TimeSpan.FromSeconds(5));

            // The region must have survived. Watching an actor that has already stopped yields Terminated at
            // once, so a short quiet window after the watch is the whole check, and the cache must still hold it.
            await WatchAsync(region);
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
            ClusterSharding.Get(Sys).ShardRegion("Entity").Should().Be(region);

            // Stop the region ourselves so teardown does not wait on a coordinator that is gone.
            Sys.Stop(region);
            await ExpectTerminatedAsync(region, TimeSpan.FromSeconds(5));
        }
    }
}
