using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Hosting.TestKit;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;
using FluentAssertions;

namespace Akka.Reminders.Tests;

/// <summary>
/// Tests for nodes that don't have the reminder host role but still need to use reminders.
/// Verifies that such nodes can start successfully with just a proxy (no singleton manager).
/// See: https://github.com/Aaronontheweb/akka-reminders/issues/49
/// </summary>
public class NonHostRoleClusterSpecs : Akka.Hosting.TestKit.TestKit
{
    private const string ReminderHostRole = "reminder-host";
    private const string OtherServiceRole = "other-service";
    private const string ShardRegionName = "test-entity";

    public NonHostRoleClusterSpecs(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // Configure a node that does NOT have the reminder-host role
        // This simulates a service that needs to schedule reminders but shouldn't host the singleton
        builder
            .WithClustering(new ClusterOptions
            {
                // Intentionally NOT including "reminder-host" role
                Roles = [OtherServiceRole],
                SeedNodes = Array.Empty<string>()
            })
            .WithShardRegion<NonHostTestEntityShardRegion>(
                ShardRegionName,
                (_, _, resolver) => s => Props.Create(() => new NonHostTestEntity(s)),
                new NonHostMessageExtractor(),
                new ShardOptions
                {
                    Role = OtherServiceRole
                })
            // This should NOT throw even though this node doesn't have the reminder-host role
            .WithReminders(ReminderHostRole, reminders => reminders
                .WithStorage(_ => new InMemoryReminderStorage())
                .WithSettings(new ReminderSettings
                {
                    MaxSlippage = TimeSpan.FromSeconds(1),
                    StorageTimeout = TimeSpan.FromSeconds(10)
                }));
    }

    [Fact]
    public void NonHostNode_ShouldStartSuccessfully_WithProxyOnly()
    {
        // Arrange & Act - The system was already started in ConfigureAkka
        // If we get here without an exception, the test passes

        // Assert - Verify the cluster node has the correct role (not reminder-host)
        var cluster = Akka.Cluster.Cluster.Get(Sys);
        cluster.SelfRoles.Should().Contain(OtherServiceRole);
        cluster.SelfRoles.Should().NotContain(ReminderHostRole);
    }

    [Fact]
    public void NonHostNode_ShouldHaveProxyRegistered()
    {
        // Arrange
        var registry = ActorRegistry.For(Sys);

        // Act
        var proxyRegistered = registry.TryGet<ReminderSchedulerProxy>(out var proxy);

        // Assert
        proxyRegistered.Should().BeTrue("the proxy should be registered even on non-host nodes");
        proxy.Should().NotBeNull();
    }

    [Fact]
    public void NonHostNode_ShouldNotHaveSingletonManagerActor()
    {
        // Arrange & Act
        // Try to resolve the singleton manager path - it should NOT exist on non-host nodes
        var selection = Sys.ActorSelection("/system/reminder-scheduler");

        // Assert - The actor should not exist (will throw or return ActorNotFound)
        var probe = CreateTestProbe();
        selection.Tell(new Identify("test"), probe);
        var identity = probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3), cancellationToken: TestContext.Current.CancellationToken);

        // ActorRef should be null since the singleton manager wasn't created
        identity.Subject.Should().BeNull("the singleton manager should not be created on non-host nodes");
    }

    [Fact]
    public void NonHostNode_ReminderClient_ShouldBeAvailable()
    {
        // Arrange & Act
        var extension = Sys.ReminderClient();

        // Assert
        extension.Should().NotBeNull("ReminderClient extension should be available on non-host nodes");

        // Creating a client should work
        var client = extension.CreateClient(ShardRegionName, "test-entity-id");
        client.Should().NotBeNull("creating a reminder client should succeed on non-host nodes");
    }

    [Fact]
    public void NonHostNode_ProxyActor_ShouldExist()
    {
        // Arrange
        var selection = Sys.ActorSelection("/system/reminder-scheduler-proxy");
        var probe = CreateTestProbe();

        // Act
        selection.Tell(new Identify("test"), probe);
        var identity = probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        identity.Subject.Should().NotBeNull("the proxy actor should exist on non-host nodes");
    }
}

/// <summary>
/// Test entity for non-host role tests.
/// </summary>
internal sealed class NonHostTestEntity : ReceiveActor
{
    private readonly string _entityId;

    public NonHostTestEntity(string entityId)
    {
        _entityId = entityId;

        ReceiveAny(_ =>
        {
            // Just acknowledge receipt
        });
    }
}

/// <summary>
/// Marker type for the non-host test entity shard region.
/// </summary>
internal sealed class NonHostTestEntityShardRegion { }

/// <summary>
/// Message extractor for non-host test entities.
/// </summary>
internal sealed class NonHostMessageExtractor : HashCodeMessageExtractor
{
    public NonHostMessageExtractor() : base(10)
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

    public override object EntityMessage(object message) => message;
}
