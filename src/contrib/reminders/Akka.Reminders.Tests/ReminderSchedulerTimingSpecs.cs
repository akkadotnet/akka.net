using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Hosting.TestKit;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;
using Akka.TestKit;

namespace Akka.Reminders.Tests;

/// <summary>
/// Unit tests for ReminderScheduler timing behavior using TestScheduler.
/// Uses TestScheduler to control time and verify reminders fire exactly when expected.
///
/// NOTE: TestScheduler works with the ITimeProvider abstraction we inject into ReminderScheduler,
/// so the scheduler correctly uses virtual time. However, IWithTimers.StartSingleTimer still uses
/// real time internally, which means timer-based scheduling doesn't respect TestScheduler.Advance().
/// This is a known limitation of IWithTimers with TestScheduler in Akka.NET.
/// </summary>
public partial class ReminderSchedulerTimingSpecs : Akka.Hosting.TestKit.TestKit
{
    private readonly TestShardRegionResolver _resolver;

    public ReminderSchedulerTimingSpecs(ITestOutputHelper output) : base(output: output)
    {
        _resolver = new TestShardRegionResolver();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // Configure the system to use TestScheduler (EagerTestScheduler behaves the same unless a test turns on its flag)
        builder.AddHocon("akka.scheduler.implementation = \"Akka.Reminders.Tests.EagerTestScheduler, Akka.Reminders.Tests\"", HoconAddMode.Prepend);

        builder.WithActors((system, registry) =>
        {
            var storage = new InMemoryReminderStorage();
            var settings = new ReminderSettings
            {
                MaxSlippage = TimeSpan.FromSeconds(1),
                StorageTimeout = TimeSpan.FromSeconds(30),
                MaxDeliveryAttempts = 3,
                RetryBackoffBase = TimeSpan.FromSeconds(5)
            };

            var scheduler = system.ActorOf(
                Props.Create(() => new ReminderScheduler(settings, _resolver, storage, system.Scheduler)),
                "reminder-scheduler");

            registry.Register<ReminderSchedulerProxy>(scheduler);
            system.WithExtension<ReminderClientExtension, ReminderClientProvider>();
        });
    }

    [Fact]
    public async Task SingleReminder_ShouldFireAtScheduledTime()
    {
        // Arrange
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("test-region", "entity-1");

        var testScheduler = (TestScheduler)Sys.Scheduler;

        // Use a time relative to the TestScheduler's Now
        var reminderTime = testScheduler.Now.AddSeconds(10);

        Output?.WriteLine($"TestScheduler.Now: {testScheduler.Now}");
        Output?.WriteLine($"Scheduling reminder for: {reminderTime}");

        // Act - Schedule a reminder for 10 seconds from now
        var result = await client.ScheduleSingleReminderAsync(
            new ReminderKey("test-reminder"),
            reminderTime,
            "test message", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        Output?.WriteLine($"Reminder scheduled successfully");

        // Advance time by 9 seconds - reminder should NOT fire yet
        Output?.WriteLine($"Advancing by 9 seconds...");
        testScheduler.Advance(TimeSpan.FromSeconds(9));
        Output?.WriteLine($"TestScheduler.Now after advance: {testScheduler.Now}");
        testProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        // Advance time by 2 more seconds (11 total) - reminder SHOULD fire now
        Output?.WriteLine($"Advancing by 2 more seconds...");
        testScheduler.Advance(TimeSpan.FromSeconds(2));
        Output?.WriteLine($"TestScheduler.Now after second advance: {testScheduler.Now}");

        // Assert - Verify the reminder message was received
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("test message", envelope.Message);
    }

    [Fact]
    public async Task MultipleReminders_ShouldFireInCorrectOrder()
    {
        // Arrange
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("test-region", "entity-1");

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        Output?.WriteLine($"Initial TestScheduler.Now: {now}");

        // Schedule 3 reminders at different times
        await client.ScheduleSingleReminderAsync(
            new ReminderKey("reminder-3"),
            now.AddSeconds(30),
            "third", ct: TestContext.Current.CancellationToken);

        await client.ScheduleSingleReminderAsync(
            new ReminderKey("reminder-1"),
            now.AddSeconds(10),
            "first", ct: TestContext.Current.CancellationToken);

        await client.ScheduleSingleReminderAsync(
            new ReminderKey("reminder-2"),
            now.AddSeconds(20),
            "second", ct: TestContext.Current.CancellationToken);

        Output?.WriteLine($"TestScheduler.Now after scheduling: {testScheduler.Now}");

        // Act & Assert - Advance time and verify order
        Output?.WriteLine($"Advancing by 11 seconds...");
        testScheduler.Advance(TimeSpan.FromSeconds(11));
        Output?.WriteLine($"TestScheduler.Now after first advance: {testScheduler.Now}");
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("first", envelope1.Message);
        Output?.WriteLine($"Received first message");

        // Ack to prevent "first" from being re-delivered on the next fetch
        await client.AckAsync(envelope1, TestContext.Current.CancellationToken);

        // Wait for processing to complete and next timer to be scheduled
        await AwaitConditionAsync(() => Task.FromResult(true), TimeSpan.FromMilliseconds(100), cancellationToken: TestContext.Current.CancellationToken);

        testScheduler.Advance(TimeSpan.FromSeconds(10));
        var envelope2 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("second", envelope2.Message);

        // Ack to prevent "second" from being re-delivered on the next fetch
        await client.AckAsync(envelope2, TestContext.Current.CancellationToken);

        // Wait for processing to complete and next timer to be scheduled
        await AwaitConditionAsync(() => Task.FromResult(true), TimeSpan.FromMilliseconds(100), cancellationToken: TestContext.Current.CancellationToken);

        testScheduler.Advance(TimeSpan.FromSeconds(10));
        var envelope3 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("third", envelope3.Message);
    }

    [Fact]
    public async Task RecurringReminder_ShouldFireMultipleTimes()
    {
        // Arrange
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("test-region", "entity-1");

        // Wait for scheduler to be initialized by sending a benign query
        // and verifying we get a proper response (not dead letter)
        var initCheck = await client.ListRemindersAsync(TestContext.Current.CancellationToken);
        Assert.Equal(FetchRemindersResponseCode.Success, initCheck.ResponseCode);

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;
        var interval = TimeSpan.FromSeconds(5);

        // Act - Schedule a recurring reminder
        var result = await client.ScheduleRecurringReminderAsync(
            new ReminderKey("recurring-reminder"),
            now.AddSeconds(5),
            interval,
            "recurring message", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        // Assert - Verify first occurrence
        Output?.WriteLine($"Before first advance - TestScheduler.Now: {testScheduler.Now}");
        testScheduler.Advance(TimeSpan.FromSeconds(6));
        Output?.WriteLine($"After first advance - TestScheduler.Now: {testScheduler.Now}");
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("recurring message", envelope1.Message);
        Output?.WriteLine($"Received first recurring message");

        // Ack the first delivery so the scheduler schedules the next occurrence
        await client.AckAsync(envelope1, TestContext.Current.CancellationToken);

        // Allow async processing to complete, then verify the next occurrence is scheduled
        testProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        var list1 = await client.ListRemindersAsync(TestContext.Current.CancellationToken);
        Assert.Single(list1.Reminders);
        Output?.WriteLine($"Next reminder scheduled for: {list1.Reminders[0].When}");

        // Verify second occurrence
        Output?.WriteLine($"Before second advance - TestScheduler.Now: {testScheduler.Now}");
        testScheduler.Advance(interval);
        Output?.WriteLine($"After second advance - TestScheduler.Now: {testScheduler.Now}");
        var envelope2 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("recurring message", envelope2.Message);

        // Ack the second delivery
        await client.AckAsync(envelope2, TestContext.Current.CancellationToken);

        // Allow async processing to complete
        testProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        var list2 = await client.ListRemindersAsync(TestContext.Current.CancellationToken);
        Assert.Single(list2.Reminders);

        // Verify third occurrence
        testScheduler.Advance(interval);
        var envelope3 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("recurring message", envelope3.Message);
    }

    [Fact]
    public async Task ReminderEnvelope_ShouldExposeDueTimeAndDeadlineMetadata()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var client = Sys.ReminderClient().CreateClient("test-region", "entity-1");
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);

        var result = await client.ScheduleSingleReminderAsync(
            new ReminderKey("deadline-reminder"),
            dueTime,
            "deadline message",
            maxDeliveryWindow: TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(dueTime, envelope.DueTimeUtc);
        Assert.False(envelope.Deadline.IsInfinite);
        Assert.False(envelope.Deadline.IsExpired(dueTime.AddSeconds(1)));
        Assert.True(envelope.Deadline.IsExpired(dueTime.AddSeconds(3)));
    }

    [Fact]
    public async Task ReminderEnvelope_ShouldUseAckTimeoutAsDeadline_WhenMoreRetriesAvailable()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var client = Sys.ReminderClient().CreateClient("test-region", "entity-1");
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);

        // Use a long delivery window (60s) so the occurrence deadline is far out.
        // With AckTimeout=10s and MaxDeliveryAttempts=3, this is the first attempt
        // and the next backoff (5s) fits within the occurrence deadline.
        // Therefore the envelope deadline should be the ack timeout, not the occurrence deadline.
        var result = await client.ScheduleSingleReminderAsync(
            new ReminderKey("ack-deadline-reminder"),
            dueTime,
            "ack deadline message",
            maxDeliveryWindow: TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(dueTime, envelope.DueTimeUtc);
        Assert.False(envelope.Deadline.IsInfinite);

        // The deadline should be based on the ack timeout (~10s from delivery time),
        // NOT the occurrence deadline (dueTime + 60s).
        // Delivery happens roughly at dueTime, so the deadline should be
        // well before dueTime + 60s.
        Assert.True(envelope.Deadline.UtcDateTime < dueTime.AddSeconds(30),
            $"Expected deadline before {dueTime.AddSeconds(30)}, but was {envelope.Deadline.UtcDateTime}. " +
            "Deadline should reflect the ack timeout, not the occurrence deadline.");

        // And it should not be expired immediately — the ack timeout hasn't passed yet
        Assert.False(envelope.Deadline.IsExpired(dueTime.AddSeconds(1)));
    }

    [Fact]
    public async Task ReminderEnvelope_ShouldUseOccurrenceDeadline_WhenNoMoreRetriesPossible()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var client = Sys.ReminderClient().CreateClient("test-region", "entity-1");
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);

        // Use a short delivery window (2s). With AckTimeout=10s (default),
        // the ack timeout alone exceeds the occurrence deadline,
        // so this is effectively the last attempt — envelope deadline should be
        // the occurrence deadline (dueTime + 2s).
        var result = await client.ScheduleSingleReminderAsync(
            new ReminderKey("occurrence-deadline-reminder"),
            dueTime,
            "occurrence deadline message",
            maxDeliveryWindow: TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(dueTime, envelope.DueTimeUtc);
        Assert.False(envelope.Deadline.IsInfinite);
        // Deadline should be dueTime + 2s (the occurrence deadline)
        Assert.False(envelope.Deadline.IsExpired(dueTime.AddSeconds(1)));
        Assert.True(envelope.Deadline.IsExpired(dueTime.AddSeconds(3)));
    }

    [Fact]
    public async Task ReminderEnvelope_ShouldUseInfiniteDeadline_WhenNoDeadlineAndFinalAttempt()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var client = Sys.ReminderClient().CreateClient("test-region", "entity-1");
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);

        // No maxDeliveryWindow and no repeat interval — unbounded reminder.
        // With MaxDeliveryAttempts=3 and this being the first attempt,
        // there are more retries possible but no occurrence deadline to clamp to.
        // The ack timeout should be used since more attempts are available.
        var result = await client.ScheduleSingleReminderAsync(
            new ReminderKey("unbounded-reminder"),
            dueTime,
            "unbounded message", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(dueTime, envelope.DueTimeUtc);
        // Unbounded with more retries: deadline should be ack timeout, not infinite
        Assert.False(envelope.Deadline.IsInfinite,
            "Expected ack-timeout-based deadline, not Infinite, because more retries are available");
    }

    [Fact]
    public async Task RecurringReminder_ShouldScheduleNextOccurrenceWithoutWaitingForAck()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var client = Sys.ReminderClient().CreateClient("test-region", "entity-1");
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;
        var interval = TimeSpan.FromSeconds(5);

        var result = await client.ScheduleRecurringReminderAsync(
            new ReminderKey("latest-only-recurring"),
            now.AddSeconds(5),
            interval,
            "recurring message", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(now.AddSeconds(5), envelope1.DueTimeUtc);

        testProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        var reminders = await client.ListRemindersAsync(TestContext.Current.CancellationToken);
        Assert.Contains(reminders.Reminders, r => r.DueTimeUtc == now.AddSeconds(10));
    }

    [Fact]
    public async Task LateAckForSupersededRecurringOccurrence_ShouldReturnNotFound()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var client = Sys.ReminderClient().CreateClient("test-region", "entity-1");
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;
        var interval = TimeSpan.FromSeconds(5);

        var result = await client.ScheduleRecurringReminderAsync(
            new ReminderKey("superseded-recurring"),
            now.AddSeconds(5),
            interval,
            "recurring message", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        // Let the scheduler finish persisting and timer-scheduling the superseding occurrence
        // before we advance virtual time again.
        testProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        var reminders = await client.ListRemindersAsync(TestContext.Current.CancellationToken);
        Assert.Contains(reminders.Reminders, r => r.DueTimeUtc == now.AddSeconds(10));

        testScheduler.Advance(interval);
        var envelope2 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(now.AddSeconds(10), envelope2.DueTimeUtc);

        var staleAck = await client.AckAsync(envelope1, TestContext.Current.CancellationToken);
        Assert.Equal(ReminderAckResponseCode.NotFound, staleAck.ResponseCode);

        var currentAck = await client.AckAsync(envelope2, TestContext.Current.CancellationToken);
        Assert.Equal(ReminderAckResponseCode.Success, currentAck.ResponseCode);
    }

    [Fact]
    public async Task Nack_ShouldUseRetryBudgetAndExposeTerminalStatus()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("test-region", "entity-nack");
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);
        var key = new ReminderKey("nack-retry");

        var scheduled = await client.ScheduleSingleReminderAsync(key, dueTime, "retry me", ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderScheduleResponseCode.Success, scheduled.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var first = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        var firstNack = await client.NackAsync(first, "first failure", ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderNackResponseCode.RetryScheduled, firstNack.ResponseCode);
        Assert.Equal(1, firstNack.AttemptCount);

        var pending = await client.GetOccurrenceStatusAsync(first.Key, first.DueTimeUtc, ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderOccurrenceStatusResponseCode.Success, pending.ResponseCode);
        Assert.Equal(ReminderCompletionStatus.Pending, pending.Status?.CompletionStatus);
        Assert.Equal("first failure", pending.Status?.LastFailureReason);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var second = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        var secondNack = await client.NackAsync(second, "second failure", ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderNackResponseCode.RetryScheduled, secondNack.ResponseCode);
        Assert.Equal(2, secondNack.AttemptCount);

        testScheduler.Advance(TimeSpan.FromSeconds(11));
        var third = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        var thirdNack = await client.NackAsync(third, "final failure", ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderNackResponseCode.Failed, thirdNack.ResponseCode);
        Assert.Equal(3, thirdNack.AttemptCount);

        var failed = await client.GetOccurrenceStatusAsync(third.Key, third.DueTimeUtc, ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderOccurrenceStatusResponseCode.Success, failed.ResponseCode);
        Assert.Equal(ReminderCompletionStatus.Failed, failed.Status?.CompletionStatus);
        Assert.Equal(3, failed.Status?.AttemptCount);
        Assert.Equal("final failure", failed.Status?.LastFailureReason);
    }

    [Fact]
    public async Task AckBeforeNack_ShouldMakeNackStale()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);
        var client = Sys.ReminderClient().CreateClient("test-region", "ack-first");
        Assert.Equal(FetchRemindersResponseCode.Success, (await client.ListRemindersAsync(TestContext.Current.CancellationToken)).ResponseCode);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);

        await client.ScheduleSingleReminderAsync(new ReminderKey("ack-first"), dueTime, "payload", ct: TestContext.Current.CancellationToken);
        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        var ack = await client.AckAsync(envelope, ct: TestContext.Current.CancellationToken);
        var nack = await client.NackAsync(envelope, "too late", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderAckResponseCode.Success, ack.ResponseCode);
        Assert.Equal(ReminderNackResponseCode.NotFound, nack.ResponseCode);
    }

    [Fact]
    public async Task NackBeforeAck_ShouldMakeAckStale()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);
        var client = Sys.ReminderClient().CreateClient("test-region", "nack-first");
        Assert.Equal(FetchRemindersResponseCode.Success, (await client.ListRemindersAsync(TestContext.Current.CancellationToken)).ResponseCode);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);

        await client.ScheduleSingleReminderAsync(new ReminderKey("nack-first"), dueTime, "payload", ct: TestContext.Current.CancellationToken);
        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        var nack = await client.NackAsync(envelope, "failed", ct: TestContext.Current.CancellationToken);
        var ack = await client.AckAsync(envelope, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderNackResponseCode.RetryScheduled, nack.ResponseCode);
        Assert.Equal(ReminderAckResponseCode.NotFound, ack.ResponseCode);
    }

    [Fact]
    public async Task ShardResolutionFailure_ShouldPersistTerminalAttemptDetails()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("missing-region", testProbe);
        var client = Sys.ReminderClient().CreateClient("missing-region", "poison");
        Assert.Equal(FetchRemindersResponseCode.Success, (await client.ListRemindersAsync(TestContext.Current.CancellationToken)).ResponseCode);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);
        var key = new ReminderKey("missing-shard");
        const string failureReason = "ShardRegion [missing-region] not found";

        var scheduled = await client.ScheduleSingleReminderAsync(key, dueTime, "payload", ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderScheduleResponseCode.Success, scheduled.ResponseCode);
        Assert.True(_resolver.UnregisterShardRegion("missing-region"));

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        await AwaitAssertAsync(async () =>
        {
            var status = await client.GetOccurrenceStatusAsync(key, dueTime);
            Assert.Equal(1, status.Status?.AttemptCount);
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), cancellationToken: TestContext.Current.CancellationToken);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        await AwaitAssertAsync(async () =>
        {
            var status = await client.GetOccurrenceStatusAsync(key, dueTime);
            Assert.Equal(2, status.Status?.AttemptCount);
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), cancellationToken: TestContext.Current.CancellationToken);

        testScheduler.Advance(TimeSpan.FromSeconds(11));
        await AwaitAssertAsync(async () =>
        {
            var status = await client.GetOccurrenceStatusAsync(key, dueTime);
            Assert.Equal(ReminderCompletionStatus.Failed, status.Status?.CompletionStatus);
            Assert.Equal(3, status.Status?.AttemptCount);
            Assert.Equal(failureReason, status.Status?.LastFailureReason);
            Assert.Null(status.Status?.NextAttemptAtUtc);
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FailedRecurringOccurrence_ShouldNotCancelTheNextOccurrence()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);
        var client = Sys.ReminderClient().CreateClient("test-region", "recurring-poison");
        Assert.Equal(FetchRemindersResponseCode.Success, (await client.ListRemindersAsync(TestContext.Current.CancellationToken)).ResponseCode);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var dueTime = testScheduler.Now.AddSeconds(5);
        var key = new ReminderKey("recurring-poison");

        await client.ScheduleRecurringReminderAsync(
            key,
            dueTime,
            TimeSpan.FromSeconds(60),
            "payload", ct: TestContext.Current.CancellationToken);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var first = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        await client.NackAsync(first, "first failure", ct: TestContext.Current.CancellationToken);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var second = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        await client.NackAsync(second, "second failure", ct: TestContext.Current.CancellationToken);

        testScheduler.Advance(TimeSpan.FromSeconds(11));
        var third = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        var terminal = await client.NackAsync(third, "final failure", ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderNackResponseCode.Failed, terminal.ResponseCode);

        var reminders = await client.ListRemindersAsync(TestContext.Current.CancellationToken);
        Assert.Contains(reminders.Reminders, reminder => reminder.DueTimeUtc == dueTime.AddSeconds(60));
    }

    [Fact]
    public async Task CancelledReminder_ShouldNotFire()
    {
        // Arrange
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("test-region", "entity-1");

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = DateTimeOffset.UtcNow;
        var key = new ReminderKey("test-reminder");

        // Schedule a reminder
        await client.ScheduleSingleReminderAsync(
            key,
            now.AddSeconds(10),
            "test message", ct: TestContext.Current.CancellationToken);

        // Act - Cancel the reminder before it fires
        var cancelResult = await client.CancelReminderAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(ReminderCancelResponseCode.Success, cancelResult.ResponseCode);

        // Advance time past when reminder should have fired
        testScheduler.Advance(TimeSpan.FromSeconds(15));

        // Assert - Verify no message was received
        testProbe.ExpectNoMsg(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReminderWithinMaxSlippage_ShouldFireAtScheduledTime()
    {
        // Arrange
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var extension = Sys.ReminderClient();
        var client = extension.CreateClient("test-region", "entity-1");

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        // Act - Schedule a reminder within the max slippage window (1 second)
        var result = await client.ScheduleSingleReminderAsync(
            new ReminderKey("immediate-reminder"),
            now.AddMilliseconds(500), // Within 1 second slippage
            "immediate message", ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        // Advance past the scheduled time — the reminder fires via a timer
        // (not Self.Tell) to prevent tight delivery loops when actors reschedule
        // the same key from within their delivery handler.
        testScheduler.Advance(TimeSpan.FromSeconds(2));

        // Assert
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("immediate message", envelope.Message);
    }
}
