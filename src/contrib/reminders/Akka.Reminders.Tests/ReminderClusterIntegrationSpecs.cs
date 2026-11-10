using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Hosting.TestKit;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

/// <summary>
/// Integration tests for Akka.Reminders with real ClusterSharding and ClusterSingleton.
/// Tests reminder delivery to sharded entities in a single-node cluster.
/// </summary>
public class ReminderClusterIntegrationSpecs : Akka.Hosting.TestKit.TestKit
{
    private const string ShardRegionName = "test-entity";

    private bool _clusterFormed = false;

    public ReminderClusterIntegrationSpecs(ITestOutputHelper output) : base(output: output)
    {
    }

    private void EnsureClusterFormed()
    {
        if (_clusterFormed) return;

        // Form a single-node cluster by joining self
        var cluster = Akka.Cluster.Cluster.Get(Sys);
        cluster.Join(cluster.SelfAddress);

        // Wait for the cluster to be up
        AwaitCondition(() => cluster.State.Members.Count(m => m.Status == MemberStatus.Up) == 1,
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));

        _clusterFormed = true;
    }

    private async Task EnsureReminderSchedulerReady(IReminderClient client)
    {
        await AwaitAssertAsync(async () =>
        {
            var response = await client.ListRemindersAsync();
            Assert.Equal(FetchRemindersResponseCode.Success, response.ResponseCode);
        }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // Configure a single-node cluster
        // Note: Using port 0 initially, then we'll form a single-node cluster manually
        builder
            .WithClustering(new ClusterOptions
            {
                Roles = new[] { "reminder-host" },
                SeedNodes = Array.Empty<string>() // No seed nodes - will join self
            })
            .WithShardRegion<TestEntityShardRegion>(
                ShardRegionName,
                (_, _, resolver) => s => Props.Create(() => new TestEntity(s)),
                new MessageExtractor(),
                new ShardOptions
                {
                    Role = "reminder-host"
                })
            .WithReminders("reminder-host", reminders => reminders
                .WithStorage(_ => new InMemoryReminderStorage())
                .WithSettings(new ReminderSettings
                {
                    MaxSlippage = TimeSpan.FromSeconds(1),
                    StorageTimeout = TimeSpan.FromSeconds(10)
                }));
    }

    [Fact]
    public async Task SingleReminder_ShouldBeDelivered_ToShardedEntity()
    {
        // Arrange
        EnsureClusterFormed();
        var extension = Sys.ReminderClient();
        var client = extension.CreateClient(ShardRegionName, "entity-1");
        await EnsureReminderSchedulerReady(client);

        // Get the shard region to observe messages
        var shardRegion = await Sys.ActorSelection($"/system/sharding/{ShardRegionName}").ResolveOne(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var probe = CreateTestProbe();

        // Subscribe to entity events
        Sys.EventStream.Subscribe(probe.Ref, typeof(TestEntity.ReminderReceived));

        // Act - Schedule a reminder for immediate delivery
        // Use EntityMessage envelope so sharding can route it correctly
        var result = await client.ScheduleSingleReminderAsync(
            new ReminderKey("test-reminder"),
            DateTimeOffset.UtcNow.AddMilliseconds(100),
            new EntityMessage("entity-1", "test message"), ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        // Wait for the reminder to be delivered
        var received = probe.ExpectMsg<TestEntity.ReminderReceived>(TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("entity-1", received.EntityId);
        Assert.Equal("test message", received.Message);
    }

    [Fact]
    public async Task RecurringReminder_ShouldBeDelivered_MultipleTimesToShardedEntity()
    {
        // Arrange
        EnsureClusterFormed();
        var extension = Sys.ReminderClient();
        var client = extension.CreateClient(ShardRegionName, "entity-2");
        await EnsureReminderSchedulerReady(client);

        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe.Ref, typeof(TestEntity.ReminderReceived));

        // Act - Schedule a recurring reminder
        var result = await client.ScheduleRecurringReminderAsync(
            new ReminderKey("recurring-reminder"),
            DateTimeOffset.UtcNow.AddMilliseconds(100),
            TimeSpan.FromMilliseconds(500),
            new EntityMessage("entity-2", "recurring message"), ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        // Wait for multiple occurrences — first delivery traverses the full
        // cluster singleton → shard region → entity actor chain which has
        // higher latency on constrained CI runners.
        var msg1 = probe.ExpectMsg<TestEntity.ReminderReceived>(TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("entity-2", msg1.EntityId);
        Assert.Equal("recurring message", msg1.Message);

        var msg2 = probe.ExpectMsg<TestEntity.ReminderReceived>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("entity-2", msg2.EntityId);
        Assert.Equal("recurring message", msg2.Message);

        var msg3 = probe.ExpectMsg<TestEntity.ReminderReceived>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("entity-2", msg3.EntityId);
        Assert.Equal("recurring message", msg3.Message);

        // Cancel the reminder to stop it
        await client.CancelReminderAsync(new ReminderKey("recurring-reminder"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MultipleEntities_ShouldReceive_TheirOwnReminders()
    {
        // Arrange
        EnsureClusterFormed();
        var extension = Sys.ReminderClient();
        var client1 = extension.CreateClient(ShardRegionName, "entity-3");
        var client2 = extension.CreateClient(ShardRegionName, "entity-4");
        await EnsureReminderSchedulerReady(client1);

        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe.Ref, typeof(TestEntity.ReminderReceived));

        // Act - Schedule reminders for both entities
        await client1.ScheduleSingleReminderAsync(
            new ReminderKey("reminder-1"),
            DateTimeOffset.UtcNow.AddMilliseconds(100),
            new EntityMessage("entity-3", "message for entity-3"), ct: TestContext.Current.CancellationToken);

        await client2.ScheduleSingleReminderAsync(
            new ReminderKey("reminder-2"),
            DateTimeOffset.UtcNow.AddMilliseconds(150),
            new EntityMessage("entity-4", "message for entity-4"), ct: TestContext.Current.CancellationToken);

        // Assert - Both entities should receive their messages
        var messages = new List<TestEntity.ReminderReceived>();
        messages.Add(probe.ExpectMsg<TestEntity.ReminderReceived>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken));
        messages.Add(probe.ExpectMsg<TestEntity.ReminderReceived>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken));

        // Verify both entities received their correct messages
        Assert.Contains(messages, m => m.EntityId == "entity-3" && m.Message == "message for entity-3");
        Assert.Contains(messages, m => m.EntityId == "entity-4" && m.Message == "message for entity-4");
    }

    [Fact]
    public async Task CancelledReminder_ShouldNotBeDelivered_ToShardedEntity()
    {
        // Arrange
        EnsureClusterFormed();
        var extension = Sys.ReminderClient();
        var client = extension.CreateClient(ShardRegionName, "entity-5");
        await EnsureReminderSchedulerReady(client);

        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe.Ref, typeof(TestEntity.ReminderReceived));

        var key = new ReminderKey("cancel-test");

        // Act - Schedule and then immediately cancel
        await client.ScheduleSingleReminderAsync(
            key,
            DateTimeOffset.UtcNow.AddSeconds(2),
            new EntityMessage("entity-5", "should not be delivered"), ct: TestContext.Current.CancellationToken);

        var cancelResult = await client.CancelReminderAsync(key, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderCancelResponseCode.Success, cancelResult.ResponseCode);

        // Wait longer than the reminder was scheduled for
        probe.ExpectNoMsg(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// Envelope for messages sent to test entities through sharding.
/// </summary>
internal sealed record EntityMessage(string EntityId, string Payload);

/// <summary>
/// Test entity that receives reminder messages and publishes events to the event stream.
/// </summary>
internal sealed class TestEntity : ReceiveActor
{
    public sealed record ReminderReceived(string EntityId, string Message);

    private readonly string _entityId;

    public TestEntity(string entityId)
    {
        _entityId = entityId;

        ReceiveAsync<ReminderEnvelope<EntityMessage>>(async envelope =>
        {
            // Extract the EntityMessage payload from the reminder envelope
            var msg = envelope.Message;
            Context.System.EventStream.Publish(new ReminderReceived(_entityId, msg.Payload));

            // Ack the reminder so recurring reminders schedule their next occurrence
            var ext = Context.System.ReminderClient();
            await ext.AckAsync(envelope);
        });

        Receive<EntityMessage>(msg =>
        {
            // Handle direct EntityMessage delivery (non-reminder path)
            Context.System.EventStream.Publish(new ReminderReceived(_entityId, msg.Payload));
        });
    }
}

/// <summary>
/// Marker type for the test entity shard region.
/// </summary>
internal sealed class TestEntityShardRegion { }

/// <summary>
/// Message extractor for test entities.
/// Extracts entity ID from EntityMessage envelopes.
/// </summary>
internal sealed class MessageExtractor : HashCodeMessageExtractor
{
    public MessageExtractor() : base(10) // 10 shards
    {
    }

    public override string? EntityId(object message)
    {
        return message switch
        {
            EntityMessage env => env.EntityId,
            _ => null
        };
    }

    public override object EntityMessage(object message)
    {
        // Pass through the message as-is to the entity
        return message;
    }
}
