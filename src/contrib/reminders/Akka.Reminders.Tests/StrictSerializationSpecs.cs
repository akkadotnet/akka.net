using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Reminders.Sharding;
using FluentAssertions;
using Xunit;

namespace Akka.Reminders.Tests;

/// <summary>
/// Verifies that internal ReminderScheduler messages don't break strict serialization
/// (akka.actor.serialize-messages = on + allow-unregistered-types = false).
///
/// This is the regression guard for Aaronontheweb/akka-reminders#118.
/// When strict serialization is enabled, any internal message missing
/// INoSerializationVerificationNeeded or a binding will throw at runtime.
/// </summary>
public class StrictSerializationSpecs : Akka.Hosting.TestKit.TestKit
{
    private readonly TestShardRegionResolver _resolver;

    public StrictSerializationSpecs(ITestOutputHelper output) : base(output: output)
    {
        _resolver = new TestShardRegionResolver();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // Enable strict serialization — this is the scenario from issue #118
        // Any internal message without INoSerializationVerificationNeeded or
        // a serialization binding will throw at actor system creation time.
        builder.WithLocalReminders(reminders => reminders
            .WithInMemoryStorage()
            .WithResolver(_ => _resolver)
            .WithSettings(new ReminderSettings
            {
                MaxSlippage = TimeSpan.FromMilliseconds(100),
                StorageTimeout = TimeSpan.FromSeconds(1),
                MaxDeliveryAttempts = 3,
                RetryBackoffBase = TimeSpan.FromMilliseconds(100)
            }))
            .AddHocon(ConfigurationFactory.ParseString(@"
                akka.actor.serialize-messages = on
                akka.actor.serialization-settings.allow-unregistered-types = off
                akka.actor.serialization-bindings {
                    # TestKit-internal control messages (not part of the system under test).
                    # Akka.Hosting.TestKit 1.5.70 sends Akka.Actor.Identify (ResolveOne during
                    # shard-region resolution) and StableTestProbeRef+UpdateTarget (probe retargeting
                    # across host startup). Bind both to the built-in json serializer so strict mode
                    # (allow-unregistered-types = off) has a serializer for them.
                    ""Akka.Actor.Identify"" = json
                    ""Akka.Hosting.TestKit.TestKit+StableTestProbeRef+UpdateTarget, Akka.Hosting.TestKit"" = json
                    # The user-supplied reminder payload used by these specs. A real app running
                    # strict serialization would register a serializer for its own payload types;
                    # bind it to json here so the reminder can round-trip through delivery.
                    ""Akka.Reminders.Tests.StrictSerializationSpecs+StrictSerialTestMsg, Akka.Reminders.Tests"" = json
                }
            "), HoconAddMode.Prepend);
    }

    [Fact]
    public void StrictSerialization_ShouldNotThrow_WhenReminderSchedulerStarts()
    {
        // If any internal scheduler message lacks INoSerializationVerificationNeeded
        // or a serialization binding, the actor system creation will throw here.
        // This test passes as long as the system starts without SerializationException.

        var extension = Sys.ReminderClient();
        extension.Should().NotBeNull();

        Output?.WriteLine("Reminder system started successfully under strict serialization.");
    }

    /// <summary>
    /// The ReminderScheduler stashes all incoming commands while it performs its
    /// initial storage load (LoadReminderOverview). A command issued in that window
    /// can exceed the client's Ask timeout on a loaded CI runner, surfacing as
    /// ReminderScheduleResponseCode.Error even though the scheduler eventually
    /// processes the message.
    ///
    /// This is a deterministic readiness barrier: poll a read (which follows the
    /// same Ask path and is also stashed until init completes) until the scheduler
    /// actually answers. Once it answers, the stash window is closed and all
    /// subsequent commands are processed immediately.
    /// </summary>
    private async Task WaitForSchedulerReadyAsync(IReminderClient client)
    {
        var ct = TestContext.Current.CancellationToken;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await client.ListRemindersAsync(ct);
            if (response.ResponseCode != FetchRemindersResponseCode.Error)
                return; // scheduler answered -> ready

            await Task.Delay(100, ct);
        }
        throw new TimeoutException("ReminderScheduler did not become ready within 30s");
    }

    [Fact]
    public async Task StrictSerialization_ShouldAllowSchedulingAndDelivery()
    {
        // Arrange
        var targetActor = CreateTestProbe("billing-actor");
        _resolver.RegisterShardRegion("billing-shard", targetActor);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("billing-shard", "customer-123");

        // Deterministically wait out scheduler init before the first schedule
        await WaitForSchedulerReadyAsync(client);

        var key = new ReminderKey("strict-serial-check");
        var message = new StrictSerialTestMsg("hello");
        var when = DateTimeOffset.UtcNow.AddMilliseconds(200);

        // Act - if internal messages aren't marked INoSerializationVerificationNeeded,
        // this ScheduleSingleReminderAsync call will throw SerializationException
        // because InitResult is returned via Tell to the caller.
        var result = await client.ScheduleSingleReminderAsync(key, when, message, ct: TestContext.Current.CancellationToken);

        // Assert
        result.ResponseCode.Should().Be(ReminderScheduleResponseCode.Success);

        // The reminder should be delivered through the scheduler pipeline
        // without any serialization errors
        var envelope = await targetActor.ExpectMsgAsync<ReminderEnvelope<StrictSerialTestMsg>>(TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
        envelope.Message.Content.Should().Be("hello");
    }

    [Fact]
    public async Task StrictSerialization_ShouldAllowNackAndRetry()
    {
        var targetActor = CreateTestProbe("retry-actor");
        _resolver.RegisterShardRegion("retry-shard", targetActor);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("retry-shard", "entity-1");
        await WaitForSchedulerReadyAsync(client);

        var key = new ReminderKey("strict-retry");
        var when = DateTimeOffset.UtcNow.AddMilliseconds(100);

        var result = await client.ScheduleSingleReminderAsync(
            key,
            when,
            new StrictSerialTestMsg("retry"), ct: TestContext.Current.CancellationToken);
        result.ResponseCode.Should().Be(ReminderScheduleResponseCode.Success);

        var first = await targetActor.ExpectMsgAsync<ReminderEnvelope<StrictSerialTestMsg>>(
            TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
        var nack = await client.NackAsync(first, "test failure", ct: TestContext.Current.CancellationToken);
        nack.ResponseCode.Should().Be(ReminderNackResponseCode.RetryScheduled);

        var retry = await targetActor.ExpectMsgAsync<ReminderEnvelope<StrictSerialTestMsg>>(
            TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
        retry.DueTimeUtc.Should().Be(first.DueTimeUtc);
    }

    [Fact]
    public async Task StrictSerialization_ShouldAllowCancelReminder()
    {
        // Arrange
        var targetActor = CreateTestProbe("billing-actor");
        _resolver.RegisterShardRegion("billing-shard", targetActor);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("billing-shard", "customer-123");

        await WaitForSchedulerReadyAsync(client);

        var key = new ReminderKey("cancel-me");
        var message = new StrictSerialTestMsg("cancel");
        var when = DateTimeOffset.UtcNow.AddMilliseconds(200);

        await client.ScheduleSingleReminderAsync(key, when, message, ct: TestContext.Current.CancellationToken);

        // Act - CancelReminder is also internal and should not break strict serialization
        var cancelResult = await client.CancelReminderAsync(key, TestContext.Current.CancellationToken);

        // Assert
        cancelResult.ResponseCode.Should().Be(ReminderCancelResponseCode.Success);
    }

    [Fact]
    public async Task StrictSerialization_ShouldAllowCancelAllReminders()
    {
        // Arrange
        var targetActor = CreateTestProbe("billing-actor");
        _resolver.RegisterShardRegion("billing-shard", targetActor);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("billing-shard", "customer-123");

        await WaitForSchedulerReadyAsync(client);

        // Act
        var result = await client.CancelAllRemindersAsync(TestContext.Current.CancellationToken);

        // Assert - CancelAllReminders is internal, should not break
        // NotFound is fine since there are no reminders to cancel
        Assert.True(
            result.ResponseCode == ReminderCancelResponseCode.Success ||
            result.ResponseCode == ReminderCancelResponseCode.NotFound,
            "CancelAllReminders on an empty set returns NotFound or Success");
    }

    private record StrictSerialTestMsg(string Content);
}
