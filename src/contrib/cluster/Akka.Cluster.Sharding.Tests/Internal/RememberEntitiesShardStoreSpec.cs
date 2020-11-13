//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesShardStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests.Internal
{
    /// <summary>
    /// Covers the interaction between the shard and the remember entities store
    /// </summary>
    public abstract class RememberEntitiesShardStoreSpec : AkkaSpec
    {
        private ClusterShardingSettings shardingSettings;

        public RememberEntitiesShardStoreSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            shardingSettings = ClusterShardingSettings.Create(Sys);
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel=DEBUG
                #akka.loggers = [""akka.testkit.SilenceAllTestEventListener""]
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.state-store-mode = ddata
                akka.cluster.sharding.remember-entities = on
                # no leaks between test runs thank you
                akka.cluster.sharding.distributed-data.durable.keys = []
                akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSharding.DefaultConfig())
            .WithFallback(DistributedData.DistributedData.DefaultConfig());

        protected abstract Props StoreProps(string shardId, string typeName, ClusterShardingSettings settings);

        protected override void AtStartup()
        {
            // Form a one node cluster
            var cluster = Cluster.Get(Sys);
            cluster.Join(cluster.SelfAddress);
            AwaitAssert(() =>
            {
                cluster.ReadView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1);
            });
        }

        [Fact]
        public void The_store_specs()
        {
            //needs to be executed in order
            The_store_must_store_starts_and_stops_and_list_remembered_entity_ids();
            The_store_must_handle_a_late_request();
            The_store_must_handle_a_large_batch();
        }

        private void The_store_must_store_starts_and_stops_and_list_remembered_entity_ids()
        {
            var store = Sys.ActorOf(StoreProps("FakeShardId", "FakeTypeName", shardingSettings));

            store.Tell(RememberEntitiesShardStore.GetEntities.Instance);
            ExpectMsg<RememberEntitiesShardStore.RememberedEntities>().Entities.Should().BeEmpty();

            store.Tell(new RememberEntitiesShardStore.Update(ImmutableHashSet.Create("1", "2", "3"), ImmutableHashSet<string>.Empty));
            ExpectMsg(new RememberEntitiesShardStore.UpdateDone(ImmutableHashSet.Create("1", "2", "3"), ImmutableHashSet<string>.Empty));

            store.Tell(new RememberEntitiesShardStore.Update(ImmutableHashSet.Create("4", "5", "6"), ImmutableHashSet.Create("2", "3")));
            ExpectMsg(new RememberEntitiesShardStore.UpdateDone(ImmutableHashSet.Create("4", "5", "6"), ImmutableHashSet.Create("2", "3")));

            store.Tell(new RememberEntitiesShardStore.Update(ImmutableHashSet<string>.Empty, ImmutableHashSet.Create("6")));
            ExpectMsg(new RememberEntitiesShardStore.UpdateDone(ImmutableHashSet<string>.Empty, ImmutableHashSet.Create("6")));

            store.Tell(new RememberEntitiesShardStore.Update(ImmutableHashSet.Create("2"), ImmutableHashSet<string>.Empty));
            ExpectMsg(new RememberEntitiesShardStore.UpdateDone(ImmutableHashSet.Create("2"), ImmutableHashSet<string>.Empty));

            // the store does not support get after update
            var storeIncarnation2 = Sys.ActorOf(StoreProps("FakeShardId", "FakeTypeName", shardingSettings));

            storeIncarnation2.Tell(RememberEntitiesShardStore.GetEntities.Instance);
            ExpectMsg<RememberEntitiesShardStore.RememberedEntities>().Entities.Should().BeEquivalentTo("1", "2", "4", "5");
        }

        private void The_store_must_handle_a_late_request()
        {
            // the store does not support get after update
            var storeIncarnation3 = Sys.ActorOf(StoreProps("FakeShardId", "FakeTypeName", shardingSettings));

            Thread.Sleep(500);
            storeIncarnation3.Tell(RememberEntitiesShardStore.GetEntities.Instance);
            ExpectMsg<RememberEntitiesShardStore.RememberedEntities>().Entities.Should().BeEquivalentTo("1", "2", "4", "5"); // from previous test
        }

        private void The_store_must_handle_a_large_batch()
        {
            var store = Sys.ActorOf(StoreProps("FakeShardIdLarge", "FakeTypeNameLarge", shardingSettings));
            store.Tell(RememberEntitiesShardStore.GetEntities.Instance);
            ExpectMsg<RememberEntitiesShardStore.RememberedEntities>().Entities.Should().BeEmpty();

            store.Tell(new RememberEntitiesShardStore.Update(Enumerable.Range(1, 1000).Select(i => i.ToString()).ToImmutableHashSet(), Enumerable.Range(1001, 1000).Select(i => i.ToString()).ToImmutableHashSet()));
            var response = ExpectMsg<RememberEntitiesShardStore.UpdateDone>();
            response.Started.Should().HaveCount(1000);
            response.Stopped.Should().HaveCount(1000);

            Watch(store);
            Sys.Stop(store);
            ExpectTerminated(store);

            store = Sys.ActorOf(StoreProps("FakeShardIdLarge", "FakeTypeNameLarge", shardingSettings));
            store.Tell(RememberEntitiesShardStore.GetEntities.Instance);
            ExpectMsg<RememberEntitiesShardStore.RememberedEntities>().Entities.Should().HaveCount(1000);
        }
    }

    public class DDataRememberEntitiesShardStoreSpec : RememberEntitiesShardStoreSpec
    {
        private IActorRef replicator;

        public DDataRememberEntitiesShardStoreSpec(ITestOutputHelper helper) : base(helper)
        {
            var replicatorSettings = ReplicatorSettings.Create(Sys);
            replicator = Sys.ActorOf(Replicator.Props(replicatorSettings));
        }

        protected override Props StoreProps(string shardId, string typeName, ClusterShardingSettings settings)
        {
            return DDataRememberEntitiesShardStore.Props(shardId, typeName, settings, replicator, majorityMinCap: 1);
        }
    }

    public class EventSourcedRememberEntitiesShardStoreSpec : RememberEntitiesShardStoreSpec
    {
        public EventSourcedRememberEntitiesShardStoreSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        protected override Props StoreProps(string shardId, string typeName, ClusterShardingSettings settings)
        {
            return EventSourcedRememberEntitiesShardStore.Props(typeName, shardId, settings);
        }
    }
}
