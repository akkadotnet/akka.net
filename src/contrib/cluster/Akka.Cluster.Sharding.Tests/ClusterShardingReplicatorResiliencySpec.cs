// -----------------------------------------------------------------------
//  <copyright file="ClusterShardingReplicatorResiliencySpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests;

public class ClusterShardingReplicatorResiliencySpec : AkkaSpec
{
    private sealed record ShardEnvelope(string EntityId, string Message);

    private sealed class EntityActor : ReceiveActor
    {
        public EntityActor()
        {
            Receive<string>(message => Sender.Tell(message));
        }
    }

    private static readonly HashCodeMessageExtractor MessageExtractor = HashCodeMessageExtractor.Create(
        10,
        message => message is ShardEnvelope envelope ? envelope.EntityId : null,
        message => message is ShardEnvelope envelope ? envelope.Message : message);

    private static Config SpecConfig =>
        ConfigurationFactory.ParseString(@"
            akka.loglevel = DEBUG
            akka.actor.provider = cluster
            akka.remote.dot-netty.tcp.port = 0

            akka.cluster.sharding.state-store-mode = ddata
            akka.cluster.sharding.remember-entities = on
            akka.cluster.sharding.remember-entities-store = ddata
            akka.cluster.sharding.distributed-data.majority-min-cap = 1
            akka.cluster.sharding.distributed-data.durable.keys = []")
            .WithFallback(ClusterSharding.DefaultConfig());

    public ClusterShardingReplicatorResiliencySpec(ITestOutputHelper helper)
        : base(SpecConfig, output: helper)
    {
    }

    protected override void AtStartup()
    {
        var cluster = Cluster.Get(Sys);
        cluster.Join(cluster.SelfAddress);
        AwaitAssert(() =>
            cluster.ReadView.Members.Count(member => member.Status == MemberStatus.Up).Should().Be(1));
    }

    [Fact]
    public async Task Private_replicator_should_recover_behind_its_stable_path_without_restarting_consumers()
    {
        const string typeName = "replicator-resiliency";
        const string replicatorPath = "/system/sharding/replicator";
        const string replicatorChildPath = replicatorPath + "/replicator";
        const string firstEntityId = "entity-1";
        var firstShardId = MessageExtractor.ShardId(firstEntityId);
        var existingShardEntityId = Enumerable.Range(2, 100)
            .Select(i => $"entity-{i}")
            .First(id => MessageExtractor.ShardId(id) == firstShardId);
        var newShardEntityId = Enumerable.Range(2, 100)
            .Select(i => $"entity-{i}")
            .First(id => MessageExtractor.ShardId(id) != firstShardId);
        var region = ClusterSharding.Get(Sys).Start(
            typeName,
            Props.Create<EntityActor>(),
            ClusterShardingSettings.Create(Sys),
            MessageExtractor);

        region.Tell(new ShardEnvelope(firstEntityId, "before"));
        await ExpectMsgAsync("before");
        var shard = LastSender.Path.Parent;

        var coordinator = await Sys.ActorSelection(
                $"/system/sharding/{typeName}Coordinator/singleton/coordinator")
            .ResolveOne(3.Seconds());
        var coordinatorWatcher = CreateTestProbe();
        await coordinatorWatcher.WatchAsync(coordinator);
        var shardWatcher = CreateTestProbe();
        await shardWatcher.WatchAsync(await Sys.ActorSelection(shard).ResolveOne(3.Seconds()));

        var supervisor = await Sys.ActorSelection(replicatorPath).ResolveOne(3.Seconds());
        var firstReplicator = await Sys.ActorSelection(replicatorChildPath).ResolveOne(3.Seconds());
        await WatchAsync(firstReplicator);
        Sys.Stop(firstReplicator);
        await ExpectTerminatedAsync(firstReplicator);

        // Exercise an operation issued during the backoff window. The replacement's local
        // availability signal must cause the coordinator to replay its interrupted write.
        region.Tell(new ShardEnvelope(newShardEntityId, "after-new"));

        IActorRef replacement = null;
        await AwaitAssertAsync(async () =>
        {
            var currentSupervisor = await Sys.ActorSelection(replicatorPath).ResolveOne(1.Seconds());
            currentSupervisor.Should().Be(supervisor);

            replacement = await Sys.ActorSelection(replicatorChildPath).ResolveOne(1.Seconds());
            replacement.Should().NotBe(firstReplicator);
            replacement.Path.ToStringWithoutAddress().Should().Be(replicatorChildPath);
        }, 10.Seconds());

        (await ExpectMsgAsync<string>(15.Seconds())).Should().Be("after-new");

        // Exercise the existing shard and its existing DData remember-entities store.
        region.Tell(new ShardEnvelope(existingShardEntityId, "after-existing"));
        await ExpectMsgAsync("after-existing");

        await coordinatorWatcher.ExpectNoMsgAsync(500.Milliseconds());
        await shardWatcher.ExpectNoMsgAsync(500.Milliseconds());
    }
}

public class PersistentShardingReplicatorCompatibilitySpec : AkkaSpec
{
    private sealed class NoOpMessageExtractor : IMessageExtractor
    {
        public string EntityId(object message) => null;
        public object EntityMessage(object message) => message;
        public string ShardId(object message) => null;
        public string ShardId(string entityId, object messageHint = null) => "1";
    }

    private static Config SpecConfig =>
        ConfigurationFactory.ParseString(@"
            akka.actor.provider = cluster
            akka.remote.dot-netty.tcp.port = 0

            akka.cluster.sharding.state-store-mode = persistence
            akka.cluster.sharding.remember-entities = on
            akka.cluster.sharding.remember-entities-store = ddata")
            .WithFallback(ClusterSharding.DefaultConfig());

    public PersistentShardingReplicatorCompatibilitySpec(ITestOutputHelper helper)
        : base(SpecConfig, output: helper)
    {
    }

    protected override void AtStartup()
    {
        var cluster = Cluster.Get(Sys);
        cluster.Join(cluster.SelfAddress);
        AwaitAssert(() =>
            cluster.ReadView.Members.Count(member => member.Status == MemberStatus.Up).Should().Be(1));
    }

    [Fact]
    public async Task Persistence_with_DData_remember_entities_setting_should_not_create_a_replicator()
    {
        ClusterSharding.Get(Sys).Start(
            "persistent-compatibility",
            Props.Empty,
            ClusterShardingSettings.Create(Sys),
            new NoOpMessageExtractor());

        await Assert.ThrowsAsync<ActorNotFoundException>(() =>
            Sys.ActorSelection("/system/sharding/replicator").ResolveOne(500.Milliseconds()));
    }
}
