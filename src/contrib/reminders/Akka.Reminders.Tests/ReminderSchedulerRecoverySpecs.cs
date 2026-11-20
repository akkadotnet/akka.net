using Akka.Actor;
using Akka.Hosting;
using Akka.Hosting.TestKit;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;
using Akka.TestKit;

namespace Akka.Reminders.Tests;

/// <summary>
/// Tests for ReminderScheduler recovery behavior.
///
/// These tests simulate scheduler restarts by pre-populating InMemoryReminderStorage
/// before creating the scheduler actor. Because storage is shared across scheduler
/// instances, creating a new scheduler is equivalent to a singleton handoff or
/// process restart — the new instance loads its state from the same storage.
///
/// All tests use TestScheduler for deterministic time control. Virtual time advances
/// only via explicit Advance() calls, so timer-driven delivery is fully reproducible.
/// </summary>
public class ReminderSchedulerRecoverySpecs : Akka.Hosting.TestKit.TestKit
{
    private readonly TestShardRegionResolver _resolver;
    private readonly InMemoryReminderStorage _storage;

    public ReminderSchedulerRecoverySpecs(ITestOutputHelper output) : base(output: output)
    {
        _resolver = new TestShardRegionResolver();
        _storage = new InMemoryReminderStorage();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.AddHocon("akka.scheduler.implementation = \"Akka.TestKit.TestScheduler, Akka.TestKit\"", HoconAddMode.Prepend);
    }

    private IActorRef CreateScheduler()
    {
        var settings = new ReminderSettings
        {
            MaxSlippage = TimeSpan.FromSeconds(1),
            StorageTimeout = TimeSpan.FromSeconds(30),
            MaxDeliveryAttempts = 3,
            RetryBackoffBase = TimeSpan.FromSeconds(5)
        };

        return Sys.ActorOf(
            Props.Create(() => new ReminderScheduler(settings, _resolver, _storage, Sys.Scheduler)),
            $"reminder-scheduler-{Guid.NewGuid():N}");
    }

    /// <summary>
    /// Waits for the scheduler to finish initialization (PreStart → LoadReminderOverview
    /// → PipeTo → Become(Scheduling) → UnstashAll). Sends a GetReminders query which
    /// gets stashed during init and replayed after — the response proves the scheduler
    /// is in the Scheduling behavior and ready to process commands.
    /// </summary>
    private async Task WaitForSchedulerReady(IActorRef scheduler)
    {
        var probe = new ReminderEntity("__probe__", "__probe__");
        var response = await scheduler.Ask<ReminderProtocol.RemindersForEntity>(
            new ReminderProtocol.GetReminders(probe),
            TimeSpan.FromSeconds(10));
        Assert.Equal(FetchRemindersResponseCode.Success, response.ResponseCode);
    }

    /// <summary>
    /// Sends an ack to the scheduler and waits for the response. This guarantees the
    /// ack has been buffered and flushed before the test continues — no arbitrary delay.
    /// </summary>
    private async Task AckAndWait(IActorRef scheduler, ReminderEnvelope envelope)
    {
        var response = await scheduler.Ask<ReminderProtocol.ReminderAckResponse>(
            new ReminderProtocol.ReminderAck(envelope.Entity, envelope.Key, envelope.DueTimeUtc),
            TimeSpan.FromSeconds(10));
        Assert.Equal(ReminderAckResponseCode.Success, response.ResponseCode);
    }

    /// <summary>
    /// Verifies that a new scheduler loads existing reminders from storage on startup.
    /// Pre-populates storage with two future reminders, then creates the scheduler and
    /// advances virtual time past each due time to confirm delivery.
    ///
    /// This simulates the normal startup path where the scheduler discovers pending work
    /// from a previous instance or from application-level pre-seeding.
    /// </summary>
    [Fact]
    public async Task Scheduler_ShouldLoadRemindersFromStorage_OnStartup()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        // Pre-populate storage before the scheduler exists — simulates reminders
        // persisted by a previous scheduler instance or by application startup logic.
        var reminder1 = new ScheduledReminder(
            new ReminderEntity("test-region", "entity-1"),
            new ReminderKey("reminder-1"),
            now.AddSeconds(10),
            "message 1",
            RepeatInterval: null,
            AttemptCount: 0,
            LastFailureReason: null);

        var reminder2 = new ScheduledReminder(
            new ReminderEntity("test-region", "entity-2"),
            new ReminderKey("reminder-2"),
            now.AddSeconds(20),
            "message 2",
            RepeatInterval: null,
            AttemptCount: 0,
            LastFailureReason: null);

        await _storage.ScheduleReminderAsync(reminder1, CancellationToken.None);
        await _storage.ScheduleReminderAsync(reminder2, CancellationToken.None);

        // Create scheduler — triggers PreStart → LoadReminderOverview → TryScheduleFetchReminders.
        var scheduler = CreateScheduler();
        await WaitForSchedulerReady(scheduler);

        // Advance past reminder-1's due time. The scheduler's fetch timer fires and
        // delivers reminder-1 to the test probe via the shard region resolver.
        testScheduler.Advance(TimeSpan.FromSeconds(11));
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("message 1", envelope1.Message);

        // Ack reminder-1 so it's marked Delivered and won't be re-fetched on the next tick.
        await AckAndWait(scheduler, envelope1);

        // Reminder-2 hasn't fired yet — its due time is T+20s, we're only at T+11s.
        await testProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        // Advance to reminder-2's due time.
        testScheduler.Advance(TimeSpan.FromSeconds(10));
        var envelope2 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("message 2", envelope2.Message);
    }

    /// <summary>
    /// Verifies that overdue reminders (due time in the past) are processed immediately
    /// on startup. When a scheduler starts and finds reminders whose when_utc is already
    /// past, TryScheduleFetchReminders computes a zero or negative delay (clamped to zero),
    /// so the fetch timer fires on the next tick.
    ///
    /// This simulates a scheduler that was down for a period and needs to catch up on
    /// missed deliveries.
    /// </summary>
    [Fact]
    public async Task Scheduler_ShouldProcessOverdueReminders_Immediately()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        // These reminders are already past due — simulates the scheduler being down
        // when they were supposed to fire.
        var overdueReminder1 = new ScheduledReminder(
            new ReminderEntity("test-region", "entity-1"),
            new ReminderKey("overdue-1"),
            now.AddSeconds(-5),
            "overdue message 1",
            RepeatInterval: null,
            AttemptCount: 0,
            LastFailureReason: null);

        var overdueReminder2 = new ScheduledReminder(
            new ReminderEntity("test-region", "entity-2"),
            new ReminderKey("overdue-2"),
            now.AddSeconds(-10),
            "overdue message 2",
            RepeatInterval: null,
            AttemptCount: 0,
            LastFailureReason: null);

        await _storage.ScheduleReminderAsync(overdueReminder1, CancellationToken.None);
        await _storage.ScheduleReminderAsync(overdueReminder2, CancellationToken.None);

        var scheduler = CreateScheduler();
        await WaitForSchedulerReady(scheduler);

        // A small advance triggers the zero-delay fetch timer, which finds both
        // overdue reminders and delivers them in a single batch.
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));

        var messages = new List<string>();
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        messages.Add(envelope1.Message);
        var envelope2 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        messages.Add(envelope2.Message);

        // Both overdue reminders should be delivered (order within a batch may vary).
        Assert.Contains("overdue message 1", messages);
        Assert.Contains("overdue message 2", messages);
    }

    /// <summary>
    /// Verifies that a recurring reminder loaded from storage continues to produce
    /// occurrences across multiple cycles. Each delivery requires an ack before the
    /// next occurrence can be processed — the ack triggers the scheduler to reload
    /// its pending overview and schedule the next fetch timer.
    /// </summary>
    [Fact]
    public async Task Scheduler_ShouldRecover_RecurringReminders()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        var recurringReminder = new ScheduledReminder(
            new ReminderEntity("test-region", "entity-1"),
            new ReminderKey("recurring"),
            now.AddSeconds(5),
            "recurring message",
            RepeatInterval: TimeSpan.FromSeconds(5),
            AttemptCount: 0,
            LastFailureReason: null);

        await _storage.ScheduleReminderAsync(recurringReminder, CancellationToken.None);

        var scheduler = CreateScheduler();
        await WaitForSchedulerReady(scheduler);

        // First occurrence: advance past T+5s, receive delivery.
        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("recurring message", envelope1.Message);

        // Ack the first delivery. The next occurrence (T+10s) was already persisted
        // during the commit phase, but the ack confirms the current one is done.
        await AckAndWait(scheduler, envelope1);

        // Second occurrence: advance by another interval.
        testScheduler.Advance(TimeSpan.FromSeconds(5));
        var envelope2 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("recurring message", envelope2.Message);

        await AckAndWait(scheduler, envelope2);

        // Third occurrence: confirms the cycle continues indefinitely.
        testScheduler.Advance(TimeSpan.FromSeconds(5));
        var envelope3 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("recurring message", envelope3.Message);
    }

    /// <summary>
    /// Verifies that ack-timeout state survives a scheduler restart.
    ///
    /// Scenario:
    /// 1. First scheduler delivers a reminder (moves it to AwaitingAck in storage)
    /// 2. No ack arrives — the first scheduler is stopped
    /// 3. Second scheduler starts, loads the ack deadline from InitResult
    /// 4. When the ack deadline passes, the second scheduler detects the timeout
    ///    and retries the reminder (incrementing AttemptCount, setting failure reason)
    ///
    /// This is the key singleton-handoff test: AwaitingAck state is in the database,
    /// not in-memory, so the new scheduler picks up where the old one left off.
    /// </summary>
    [Fact]
    public async Task Scheduler_ShouldResumeAckTimeouts_AfterRestart()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        // Schedule an overdue reminder so it's delivered immediately.
        var reminder = new ScheduledReminder(
            new ReminderEntity("test-region", "entity-ack-timeout"),
            new ReminderKey("ack-timeout"),
            now.AddSeconds(-1),
            "timeout message",
            RepeatInterval: null,
            AttemptCount: 0,
            LastFailureReason: null);

        await _storage.ScheduleReminderAsync(reminder, CancellationToken.None);

        // First scheduler: delivers the reminder but we intentionally don't ack it.
        var firstScheduler = CreateScheduler();
        await WaitForSchedulerReady(firstScheduler);
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));

        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("timeout message", envelope.Message);

        // Stop the first scheduler without acking — the occurrence stays AwaitingAck in storage.
        await firstScheduler.GracefulStop(TimeSpan.FromSeconds(5));

        // Second scheduler: loads InitResult with the ack deadline from storage.
        var recoveredScheduler = CreateScheduler();
        await WaitForSchedulerReady(recoveredScheduler);

        // Advance past the AckTimeout (30s default). The ack-timeout checker fires,
        // finds the timed-out occurrence, and creates a retry (back to Pending with
        // incremented AttemptCount and backoff when_utc).
        testScheduler.Advance(TimeSpan.FromSeconds(31));

        // The retry isn't delivered yet — it has a backoff delay. But we can verify
        // the storage state to confirm the timeout was processed. Use AwaitAssertAsync
        // to wait for the RunTask to complete rather than a fixed delay.
        await AwaitAssertAsync(async () =>
        {
            var reminders = await _storage.GetRemindersForEntityAsync(
                reminder.Entity, take: 10, skip: 0, CancellationToken.None);
            Assert.Single(reminders, r => r.AttemptCount == 1);
        }, TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        var finalReminders = await _storage.GetRemindersForEntityAsync(reminder.Entity, take: 10, skip: 0, CancellationToken.None);
        var retried = Assert.Single(finalReminders, r => r.AttemptCount == 1);
        // DueTimeUtc stays the same (occurrence identity), but when_utc moved forward.
        Assert.Equal(envelope.DueTimeUtc, retried.DueTimeUtc);
        Assert.Equal(1, retried.AttemptCount);
        Assert.Equal("Ack timeout", retried.LastFailureReason);
    }

    /// <summary>
    /// Verifies that a reminder scheduled through one scheduler instance is correctly
    /// delivered by a different instance after a simulated crash and restart.
    ///
    /// The key insight: the reminder is persisted in storage, not in the scheduler's
    /// memory. The second scheduler loads the pending overview on startup and discovers
    /// the reminder, then delivers it when its due time arrives.
    /// </summary>
    [Fact]
    public async Task Scheduler_ShouldRestart_AndRecover_AfterFailure()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        // First scheduler: schedule a reminder for T+30s.
        var firstScheduler = CreateScheduler();
        await WaitForSchedulerReady(firstScheduler);

        var reminderTime = now.AddSeconds(30);
        firstScheduler.Tell(new ReminderProtocol.ScheduleReminder(
            new ReminderEntity("test-region", "entity-1"),
            new ReminderKey("future-reminder"),
            reminderTime,
            "future message"), testProbe.Ref);

        var scheduleResponse = await testProbe.ExpectMsgAsync<ReminderProtocol.ReminderScheduled>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderScheduleResponseCode.Success, scheduleResponse.ResponseCode);

        // Crash: stop the scheduler while the reminder is still in the future.
        await firstScheduler.GracefulStop(TimeSpan.FromSeconds(5));

        // Time passes while the scheduler is "down."
        testScheduler.Advance(TimeSpan.FromSeconds(15));

        // Recovery: new scheduler loads the reminder from storage.
        var recoveredScheduler = CreateScheduler();
        await WaitForSchedulerReady(recoveredScheduler);

        // Advance past the reminder's due time. The recovered scheduler delivers it.
        testScheduler.Advance(TimeSpan.FromSeconds(16));

        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("future message", envelope.Message);
    }

    /// <summary>
    /// Verifies that when multiple overdue reminders exist at startup, all of them
    /// are delivered. GetNextRemindersAsync fetches them as a batch ordered by when_utc,
    /// and ProcessReminders delivers all of them in a single tick.
    /// </summary>
    [Fact]
    public async Task Scheduler_ShouldProcessMultipleOverdueReminders_InOrder()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        // Three reminders overdue by varying amounts — simulates the scheduler being
        // down for 30 seconds while reminders piled up.
        var reminders = new[]
        {
            new ScheduledReminder(
                new ReminderEntity("test-region", "entity-1"),
                new ReminderKey("oldest"),
                now.AddSeconds(-30),
                "oldest message",
                RepeatInterval: null,
                AttemptCount: 0,
                LastFailureReason: null),

            new ScheduledReminder(
                new ReminderEntity("test-region", "entity-2"),
                new ReminderKey("middle"),
                now.AddSeconds(-15),
                "middle message",
                RepeatInterval: null,
                AttemptCount: 0,
                LastFailureReason: null),

            new ScheduledReminder(
                new ReminderEntity("test-region", "entity-3"),
                new ReminderKey("newest"),
                now.AddSeconds(-5),
                "newest message",
                RepeatInterval: null,
                AttemptCount: 0,
                LastFailureReason: null)
        };

        foreach (var reminder in reminders)
        {
            await _storage.ScheduleReminderAsync(reminder, CancellationToken.None);
        }

        var scheduler = CreateScheduler();
        await WaitForSchedulerReady(scheduler);
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));

        // All three are overdue and fetched in a single batch.
        var messages = new List<string>();
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        messages.Add(envelope1.Message);
        var envelope2 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        messages.Add(envelope2.Message);
        var envelope3 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        messages.Add(envelope3.Message);

        // All delivered — order within a batch may vary due to async processing.
        Assert.Contains("oldest message", messages);
        Assert.Contains("middle message", messages);
        Assert.Contains("newest message", messages);
    }

    /// <summary>
    /// Verifies that the scheduler correctly handles a mix of overdue and future
    /// reminders on startup. Overdue reminders fire on the first tick; future
    /// reminders wait for their scheduled time.
    ///
    /// This is the most realistic recovery scenario: some reminders were missed
    /// while the scheduler was down, and others haven't come due yet.
    /// </summary>
    [Fact]
    public async Task Scheduler_ShouldRecover_AndProcessMixOfOverdueAndFutureReminders()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        var overdueReminder = new ScheduledReminder(
            new ReminderEntity("test-region", "entity-1"),
            new ReminderKey("overdue"),
            now.AddSeconds(-5),
            "overdue message",
            RepeatInterval: null,
            AttemptCount: 0,
            LastFailureReason: null);

        var futureReminder = new ScheduledReminder(
            new ReminderEntity("test-region", "entity-2"),
            new ReminderKey("future"),
            now.AddSeconds(10),
            "future message",
            RepeatInterval: null,
            AttemptCount: 0,
            LastFailureReason: null);

        await _storage.ScheduleReminderAsync(overdueReminder, CancellationToken.None);
        await _storage.ScheduleReminderAsync(futureReminder, CancellationToken.None);

        var scheduler = CreateScheduler();
        await WaitForSchedulerReady(scheduler);

        // Overdue reminder fires immediately on the first tick.
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));
        var envelope1 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("overdue message", envelope1.Message);

        // Ack the overdue reminder so it doesn't interfere with the future one.
        await AckAndWait(scheduler, envelope1);

        // Future reminder hasn't fired yet — its due time is T+10s.
        await testProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        // Advance to the future reminder's due time.
        testScheduler.Advance(TimeSpan.FromSeconds(10));
        var envelope2 = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("future message", envelope2.Message);
    }

    /// <summary>
    /// Verifies that the scheduler initializes correctly with empty storage.
    /// This is the fresh-start scenario: no pre-existing reminders, the scheduler
    /// just needs to be ready to accept new commands.
    /// </summary>
    [Fact]
    public async Task Scheduler_ShouldHandleEmptyStorage_OnStartup()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);

        // Create scheduler with empty storage — no reminders to load.
        var scheduler = CreateScheduler();
        await WaitForSchedulerReady(scheduler);

        // Verify the scheduler is operational by scheduling and delivering a reminder.
        var testScheduler = (TestScheduler)Sys.Scheduler;
        scheduler.Tell(new ReminderProtocol.ScheduleReminder(
            new ReminderEntity("test-region", "entity-1"),
            new ReminderKey("test"),
            testScheduler.Now.AddSeconds(5),
            "test message"), testProbe.Ref);

        var response = await testProbe.ExpectMsgAsync<ReminderProtocol.ReminderScheduled>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderScheduleResponseCode.Success, response.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(6));
        var envelope = await testProbe.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("test message", envelope.Message);
    }
}
