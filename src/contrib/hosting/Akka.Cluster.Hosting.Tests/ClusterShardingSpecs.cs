using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Hosting.Tests.Lease;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Cluster.Hosting.Tests;

public class ClusterShardingSpecs
{
    public sealed class MyTopLevelActor : ReceiveActor
    {
    }

    public sealed class MyEntityActor : ReceiveActor
    {
        public MyEntityActor(string entityId, IActorRef sourceRef)
        {
            EntityId = entityId;
            SourceRef = sourceRef;

            Receive<GetId>(g => { Sender.Tell(EntityId); });
            Receive<GetSourceRef>(g => Sender.Tell(SourceRef));
        }

        public string EntityId { get; }

        public IActorRef SourceRef { get; }

        public sealed class GetId : IWithId
        {
            public GetId(string id)
            {
                Id = id;
            }

            public string Id { get; }
        }

        public sealed class GetSourceRef : IWithId
        {
            public GetSourceRef(string id)
            {
                Id = id;
            }

            public string Id { get; }
        }
    }

    public interface IWithId
    {
        string Id { get; }
    }

    public sealed class Extractor : HashCodeMessageExtractor
    {
        public Extractor() : base(30)
        {
        }

        public override string EntityId(object message)
        {
            if (message is IWithId withId)
                return withId.Id;
            return string.Empty;
        }
    }

    public ClusterShardingSpecs(ITestOutputHelper output)
    {
        Output = output;
    }

    public ITestOutputHelper Output { get; }

    [Fact]
    public async Task Should_use_ActorRegistry_with_ShardRegion()
    {
        // arrange
        using var host = await TestHelper.CreateHost(builder =>
        {
            builder.WithActors((system, registry) =>
                {
                    var tLevel = system.ActorOf(Props.Create(() => new MyTopLevelActor()), "toplevel");
                    registry.Register<MyTopLevelActor>(tLevel);
                })
                .WithShardRegion<MyEntityActor>("entities", (system, registry) =>
                {
                    var tLevel = registry.Get<MyTopLevelActor>();
                    return s => Props.Create(() => new MyEntityActor(s, tLevel));
                }, new Extractor(), new ShardOptions() { Role = "my-host", StateStoreMode = StateStoreMode.DData });
        }, new ClusterOptions() { Roles = new[] { "my-host" } }, Output);

        var actorSystem = host.Services.GetRequiredService<ActorSystem>();
        var actorRegistry = ActorRegistry.For(actorSystem);
        var shardRegion = actorRegistry.Get<MyEntityActor>();
        
        // act
        var id = await shardRegion.Ask<string>(new MyEntityActor.GetId("foo"), TimeSpan.FromSeconds(3));
        var sourceRef =
            await shardRegion.Ask<IActorRef>(new MyEntityActor.GetSourceRef("foo"), TimeSpan.FromSeconds(3));

        // assert
        Assert.Equal("foo", id);
        Assert.Equal(actorRegistry.Get<MyTopLevelActor>(), sourceRef);
    }

    [Fact(DisplayName = "ShardOptions with different values should generate valid ClusterShardSettings")]
    public void ShardOptionsTest()
    {
        var settings1 = ToSettings(new ShardOptions
        {
            RememberEntities = true,
            StateStoreMode = StateStoreMode.Persistence,
            RememberEntitiesStore = RememberEntitiesStore.Eventsourced,
            Role = "first",
            PassivateIdleEntityAfter = 1.Seconds(),
            SnapshotPluginId = "firstSnapshot",
            JournalPluginId = "firstJournal", 
            LeaseImplementation = new TestLeaseOption(),
            LeaseRetryInterval = 2.Seconds(),
            ShardRegionQueryTimeout = 3.Seconds(),
        });

        Assert.True(settings1.RememberEntities);
        Assert.Equal(StateStoreMode.Persistence, settings1.StateStoreMode);
        Assert.Equal(RememberEntitiesStore.Eventsourced, settings1.RememberEntitiesStore);
        Assert.Equal("first", settings1.Role);
        Assert.Equal(1.Seconds(), settings1.PassivateIdleEntityAfter);
        Assert.Equal("firstSnapshot", settings1.SnapshotPluginId);
        Assert.Equal("firstJournal", settings1.JournalPluginId);
        Assert.NotNull(settings1.LeaseSettings);
        Assert.Equal("test-lease", settings1.LeaseSettings!.LeaseImplementation);
        Assert.Equal(2.Seconds(), settings1.LeaseSettings.LeaseRetryInterval);
        Assert.Equal(3.Seconds(), settings1.ShardRegionQueryTimeout);
        
        var settings2 = ToSettings(new ShardOptions
        {
            RememberEntities = false,
            StateStoreMode = StateStoreMode.DData,
            RememberEntitiesStore = RememberEntitiesStore.DData,
            Role = "second",
            PassivateIdleEntityAfter = 4.Seconds(),
            SnapshotPluginId = "secondSnapshot",
            JournalPluginId = "secondJournal", 
            ShardRegionQueryTimeout = 5.Seconds(),
        });

        Assert.False(settings2.RememberEntities);
        Assert.Equal(StateStoreMode.DData, settings2.StateStoreMode);
        Assert.Equal(RememberEntitiesStore.DData, settings2.RememberEntitiesStore);
        Assert.Equal("second", settings2.Role);
        Assert.Equal(4.Seconds(), settings2.PassivateIdleEntityAfter);
        Assert.Equal("secondJournal", settings2.JournalPluginId);
        Assert.Equal("secondSnapshot", settings2.SnapshotPluginId);
        Assert.Null(settings2.LeaseSettings);
        Assert.Equal(5.Seconds(), settings2.ShardRegionQueryTimeout);
    }

    private static ClusterShardingSettings ToSettings(ShardOptions shardOptions)
    {
        var defaultConfig = ClusterSharding.DefaultConfig()
            .WithFallback(DistributedData.DistributedData.DefaultConfig())
            .WithFallback(ClusterSingleton.DefaultConfig());
        
        var shardingConfig = ConfigurationFactory.ParseString(shardOptions.ToString())
            .WithFallback(defaultConfig.GetConfig("akka.cluster.sharding"));
        var coordinatorConfig = defaultConfig.GetConfig(
            shardingConfig.GetString("coordinator-singleton"));

        return ClusterShardingSettings.Create(shardingConfig, coordinatorConfig);
    }
}