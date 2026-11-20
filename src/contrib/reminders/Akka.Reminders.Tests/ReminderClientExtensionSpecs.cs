using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Reminders.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Akka.Reminders.Tests;

/// <summary>
/// Tests for ReminderClientExtension behavior, initialization, and error handling.
/// </summary>
public class ReminderClientExtensionSpecs
{
    private readonly ITestOutputHelper _output;

    public ReminderClientExtensionSpecs(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Extension_should_be_singleton_per_ActorSystem()
    {
        // Arrange
        var config = ConfigurationFactory.ParseString(@"
            akka.actor.provider = cluster
            akka.remote.dot-netty.tcp.port = 0
        ");

        using var system = ActorSystem.Create("test", config);

        // Act - Get extension twice using different methods
        var ext1 = ReminderClientExtension.Get(system);
        var ext2 = system.ReminderClient();
        var ext3 = ReminderClientExtension.Get(system);

        // Assert - All should be the same instance
        Assert.Same(ext1, ext2);
        Assert.Same(ext2, ext3);
    }

    [Fact]
    public void Extension_should_throw_clear_error_when_WithReminders_not_called()
    {
        // Arrange
        var config = ConfigurationFactory.ParseString(@"
            akka.actor.provider = cluster
            akka.remote.dot-netty.tcp.port = 0
        ");

        using var system = ActorSystem.Create("test", config);
        var extension = ReminderClientExtension.Get(system);

        // Act & Assert - Should fail with clear error when trying to create a client
        var ex = Assert.Throws<ConfigurationException>(() =>
            extension.CreateClient("test-region", "entity-1"));

        Assert.Contains("WithReminders()", ex.Message);
        Assert.Contains("ReminderSchedulerProxy", ex.Message);
        Assert.Contains("ActorRegistry", ex.Message);
    }

    [Fact]
    public void Extension_can_be_created_before_WithReminders_completes()
    {
        // Arrange
        var config = ConfigurationFactory.ParseString(@"
            akka.actor.provider = cluster
            akka.remote.dot-netty.tcp.port = 0
        ");

        using var system = ActorSystem.Create("test", config);

        // Act - Get extension before WithReminders is configured
        // This should NOT throw - lazy initialization allows this
        var extension = ReminderClientExtension.Get(system);

        // Assert
        Assert.NotNull(extension);
    }

    // Note: Full integration test with WithReminders() is covered in ReminderClusterIntegrationSpecs

    [Fact]
    public void Static_Get_method_should_work_from_any_context()
    {
        // Arrange
        var config = ConfigurationFactory.ParseString(@"
            akka.actor.provider = cluster
            akka.remote.dot-netty.tcp.port = 0
        ");

        using var system = ActorSystem.Create("test", config);

        // Act - Test static Get method
        var ext1 = ReminderClientExtension.Get(system);

        // Act - Test extension method
        var ext2 = system.ReminderClient();

        // Assert
        Assert.NotNull(ext1);
        Assert.NotNull(ext2);
        Assert.Same(ext1, ext2);
    }
}
