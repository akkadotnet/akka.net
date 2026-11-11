using Akka.Actor;
using Akka.Hosting;
using Akka.Reminders.Sharding;
using FluentAssertions;
using Xunit;

namespace Akka.Reminders.Tests;

/// <summary>
/// Demonstrates the use of WithLocalReminders for fast, non-clustered testing of reminder functionality.
/// </summary>
public class LocalReminderSpecs : Akka.Hosting.TestKit.TestKit
{
    private readonly TestShardRegionResolver _resolver;

    public LocalReminderSpecs(ITestOutputHelper output) : base(output: output)
    {
        // Create a test shard region resolver that we can control
        _resolver = new TestShardRegionResolver();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // Configure local reminders for fast testing without cluster bootstrap delays
        builder.WithLocalReminders(reminders => reminders
            .WithInMemoryStorage()
            .WithResolver(_ => _resolver)
            .WithSettings(new ReminderSettings
            {
                MaxSlippage = TimeSpan.FromMilliseconds(100),
                StorageTimeout = TimeSpan.FromSeconds(1),
                MaxDeliveryAttempts = 3,
                RetryBackoffBase = TimeSpan.FromMilliseconds(100)
            }));
    }

    [Fact]
    public void WithLocalReminders_ShouldStartInstantly_WithoutClusterBootstrapDelay()
    {
        // The fact that this test runs at all demonstrates that WithLocalReminders
        // doesn't require the 30+ second cluster bootstrap delay.
        // The TestKit fixture itself starts up nearly instantly.

        // Assert - verify the reminder client is available
        var extension = Sys.ReminderClient();
        extension.Should().NotBeNull("ReminderClient extension should be registered");
    }

    [Fact]
    public async Task WithLocalReminders_ShouldScheduleAndDeliverReminder_ToRegisteredShardRegion()
    {
        // Arrange
        var targetActor = CreateTestProbe("billing-actor");
        _resolver.RegisterShardRegion("billing-shard", targetActor);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("billing-shard", "customer-123");

        var key = new ReminderKey("retry-payment");
        var message = new TestMessage("Payment Retry");
        var when = DateTimeOffset.UtcNow.AddMilliseconds(200);

        // Act - schedule a reminder
        var scheduleResult = await client.ScheduleSingleReminderAsync(key, when, message, ct: TestContext.Current.CancellationToken);
        scheduleResult.ResponseCode.Should().Be(ReminderScheduleResponseCode.Success);

        // Assert - the reminder should be delivered as a ReminderEnvelope
        var envelope = await targetActor.ExpectMsgAsync<ReminderEnvelope<TestMessage>>(TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
        envelope.Message.Content.Should().Be("Payment Retry");

        Output?.WriteLine($"Reminder delivered successfully to {targetActor.Ref.Path}");
    }

    [Fact]
    public async Task WithLocalReminders_ShouldSupportMultipleShardRegions()
    {
        // Arrange
        var billingActor = CreateTestProbe("billing-actor");
        var notificationActor = CreateTestProbe("notification-actor");

        _resolver.RegisterShardRegion("billing-shard", billingActor);
        _resolver.RegisterShardRegion("notification-shard", notificationActor);

        var extension = Sys.ReminderClient();
        var billingClient = extension.CreateClient("billing-shard", "customer-123");
        var notificationClient = extension.CreateClient("notification-shard", "user-456");

        // Act - schedule reminders to different shard regions
        await billingClient.ScheduleSingleReminderAsync(
            new ReminderKey("billing-reminder"),
            DateTimeOffset.UtcNow.AddMilliseconds(100),
            new TestMessage("Bill Customer"), ct: TestContext.Current.CancellationToken);

        await notificationClient.ScheduleSingleReminderAsync(
            new ReminderKey("notification-reminder"),
            DateTimeOffset.UtcNow.AddMilliseconds(100),
            new TestMessage("Send Notification"), ct: TestContext.Current.CancellationToken);

        // Assert - each reminder should be delivered as a ReminderEnvelope to its respective shard region
        var billingEnvelope = await billingActor.ExpectMsgAsync<ReminderEnvelope<TestMessage>>(TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
        billingEnvelope.Message.Content.Should().Be("Bill Customer");

        var notificationEnvelope = await notificationActor.ExpectMsgAsync<ReminderEnvelope<TestMessage>>(TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
        notificationEnvelope.Message.Content.Should().Be("Send Notification");
    }

    private record TestMessage(string Content);
}
