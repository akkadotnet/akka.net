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
    private sealed record EntityEnvelope(string EntityId);

    private sealed class EntityActor : ReceiveActor
    {
        public EntityActor()
        {
            Receive<string>(message => Sender.Tell(message));
        }
    }

    private sealed class MessageExtractor : IMessageExtractor
    {
        public string EntityId(object message) => message is EntityEnvelope envelope ? envelope.EntityId : null;

        public object EntityMessage(object message) =>
            message is EntityEnvelope envelope ? envelope.EntityId : message;

        public string ShardId(object message) =>
            message is EntityEnvelope envelope ? ShardId(envelope.EntityId) : null;

        public string ShardId(string entityId, object messageHint = null) => entityId.Split('-')[0];
    }

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
    public async Task Private_replicator_should_recover_at_the_same_path_without_restarting_consumers()
    {
        const string typeName = "replicator-resiliency";
        const string replicatorPath = "/system/sharding/replicator";
        var region = ClusterSharding.Get(Sys).Start(
            typeName,
            Props.Create<EntityActor>(),
            ClusterShardingSettings.Create(Sys),
            new MessageExtractor());

        region.Tell(new EntityEnvelope("1-before"));
        await ExpectMsgAsync("1-before");
        var shard = LastSender.Path.Parent;

        var coordinator = await Sys.ActorSelection(
                $"/system/sharding/{typeName}Coordinator/singleton/coordinator")
            .ResolveOne(3.Seconds());
        var coordinatorWatcher = CreateTestProbe();
        coordinatorWatcher.Watch(coordinator);
        var shardWatcher = CreateTestProbe();
        shardWatcher.Watch(await Sys.ActorSelection(shard).ResolveOne(3.Seconds()));

        var firstReplicator = await Sys.ActorSelection(replicatorPath).ResolveOne(3.Seconds());
        Watch(firstReplicator);
        Sys.Stop(firstReplicator);
        await ExpectTerminatedAsync(firstReplicator);

        IActorRef replacement = null;
        await AwaitAssertAsync(async () =>
        {
            replacement = await Sys.ActorSelection(replicatorPath).ResolveOne(1.Seconds());
            replacement.Should().NotBe(firstReplicator);
            replacement.Path.ToStringWithoutAddress().Should().Be(replicatorPath);
        }, 10.Seconds());

        // Existing shard: exercises its DData remember-entities store through the new replicator.
        region.Tell(new EntityEnvelope("1-after"));
        await ExpectMsgAsync("1-after");

        // New shard: exercises the existing DData coordinator and its remember-entities store.
        region.Tell(new EntityEnvelope("2-after"));
        await ExpectMsgAsync("2-after");

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
