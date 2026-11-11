using Akka.Actor;
using Akka.Hosting;
using Akka.Hosting.TestKit;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;
using Akka.TestKit;

namespace Akka.Reminders.Tests;

/// <summary>
/// Tests for the write circuit breaker in ReminderScheduler.
/// Validates that when database writes fail (reads-work-writes-fail scenario),
/// the scheduler stops delivering full batches and probes with a single reminder
/// until writes recover.
/// </summary>
public class WriteCircuitBreakerSpecs : Akka.Hosting.TestKit.TestKit
{
    private readonly TestShardRegionResolver _resolver;
    private readonly InMemoryReminderStorage _innerStorage;
    private readonly FailableReminderStorage _storage;

    public WriteCircuitBreakerSpecs(ITestOutputHelper output) : base(output: output)
    {
        _resolver = new TestShardRegionResolver();
        _innerStorage = new InMemoryReminderStorage();
        _storage = new FailableReminderStorage(_innerStorage);
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.AddHocon("akka.scheduler.implementation = \"Akka.TestKit.TestScheduler, Akka.TestKit\"",
            HoconAddMode.Prepend);
    }

    private IActorRef CreateScheduler(
        int maxBatchSize = 1000,
        int deliveryCommitChunkSize = 100,
        TimeSpan? storageTimeout = null,
        TimeSpan? ackTimeout = null)
    {
        var settings = new ReminderSettings
        {
            MaxSlippage = TimeSpan.FromSeconds(1),
            StorageTimeout = storageTimeout ?? TimeSpan.FromSeconds(30),
            MaxDeliveryAttempts = 3,
            RetryBackoffBase = TimeSpan.FromSeconds(5),
            MaxBatchSize = maxBatchSize,
            DeliveryCommitChunkSize = deliveryCommitChunkSize,
            AckTimeout = ackTimeout ?? ReminderSettings.DefaultAckTimeout
        };

        return Sys.ActorOf(
            Props.Create(() => new ReminderScheduler(settings, _resolver, _storage, Sys.Scheduler)),
            $"reminder-scheduler-{Guid.NewGuid():N}");
    }

    private async Task<List<ReminderEnvelope<string>>> CollectMessages(TestProbe probe, int count, TimeSpan timeout)
    {
        var messages = new List<ReminderEnvelope<string>>();
        for (var i = 0; i < count; i++)
        {
            messages.Add(await probe.ExpectMsgAsync<ReminderEnvelope<string>>(timeout));
        }
        return messages;
    }

    private async Task WaitForSchedulerReady(IActorRef scheduler)
    {
        var probe = CreateTestProbe();
        await AwaitAssertAsync(async () =>
        {
            scheduler.Tell(new ReminderProtocol.GetReminders(new ReminderEntity("test-region", "ready")), probe.Ref);
            var response = await probe.ExpectMsgAsync<ReminderProtocol.RemindersForEntity>(TimeSpan.FromMilliseconds(250));
            Assert.Equal(FetchRemindersResponseCode.Success, response.ResponseCode);
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));
    }

    private async Task SeedOverdueReminders(int count, DateTimeOffset now)
    {
        for (var i = 0; i < count; i++)
        {
            await _innerStorage.ScheduleReminderAsync(new ScheduledReminder(
                new ReminderEntity("test-region", $"entity-{i}"),
                new ReminderKey($"reminder-{i}"),
                // Intentionally overdue so we can trigger processing immediately in tests.
                now.AddSeconds(-1),
                $"message-{i}",
                RepeatInterval: null,
                AttemptCount: 0,
                LastFailureReason: null));
        }
    }

    [Fact]
    public async Task CircuitBreaker_ShouldOpenOnWriteFailure_AndStopFurtherProcessingInRun()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        await SeedOverdueReminders(5, now);

        _storage.FailWrites = true;
        var scheduler = CreateScheduler(maxBatchSize: 1000, deliveryCommitChunkSize: 1000);
        await WaitForSchedulerReady(scheduler);

        // Tick 1: delivery-tracking write fails before any reminders are sent -> circuit opens.
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));
        await testProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CircuitBreaker_ShouldNotAffectNormalOperation_WhenWritesSucceed()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        await SeedOverdueReminders(5, now);

        var scheduler = CreateScheduler(maxBatchSize: 1000);
        await WaitForSchedulerReady(scheduler);
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));

        var messages = await CollectMessages(testProbe, 5, TimeSpan.FromSeconds(5));
        Assert.Equal(5, messages.Count);

        testScheduler.Advance(TimeSpan.FromMilliseconds(100));
        await testProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CircuitBreaker_ShouldLimitFirstFailureBlastRadius_ByDeliveryChunkSize()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        await SeedOverdueReminders(10, now);

        _storage.FailWrites = true;
        var scheduler = CreateScheduler(maxBatchSize: 1000, deliveryCommitChunkSize: 3);
        await WaitForSchedulerReady(scheduler);
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));

        // Failed writes now prevent delivery entirely, reducing the first-failure blast radius to zero.
        await testProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CircuitBreaker_ShouldCloseAndResumeBatchProcessing_WhenWritesRecover()
    {
        var testProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", testProbe);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;

        await SeedOverdueReminders(6, now);

        _storage.FailWrites = true;
        var scheduler = CreateScheduler(maxBatchSize: 1000, deliveryCommitChunkSize: 3);
        await WaitForSchedulerReady(scheduler);

        // Outage tick: failed writes prevent any delivery.
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));
        await testProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        // Database recovers: the probe succeeds, closes the circuit, and the same run resumes full-batch processing.
        _storage.FailWrites = false;

        testScheduler.Advance(TimeSpan.FromMilliseconds(100));
        await CollectMessages(testProbe, 6, TimeSpan.FromSeconds(5));
        await testProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AckTimeoutWriteFailure_ShouldUseBoundedRecoveryDelay()
    {
        var target = CreateTestProbe();
        var responseProbe = CreateTestProbe();
        _resolver.RegisterShardRegion("test-region", target);
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;
        var entity = new ReminderEntity("test-region", "ack-timeout");
        var key = new ReminderKey("bounded-recovery");
        var dueTime = now.AddSeconds(1);
        var scheduler = CreateScheduler(
            storageTimeout: TimeSpan.FromMilliseconds(100),
            ackTimeout: TimeSpan.FromMilliseconds(50));
        await WaitForSchedulerReady(scheduler);

        scheduler.Tell(new ReminderProtocol.ScheduleReminder(entity, key, dueTime, "payload"), responseProbe.Ref);
        var scheduled = await responseProbe.ExpectMsgAsync<ReminderProtocol.ReminderScheduled>(TimeSpan.FromSeconds(1), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderScheduleResponseCode.Success, scheduled.ResponseCode);

        testScheduler.Advance(TimeSpan.FromSeconds(2));
        await target.ExpectMsgAsync<ReminderEnvelope<string>>(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        _storage.FailWrites = true;
        testScheduler.Advance(TimeSpan.FromMilliseconds(100));

        await _storage.FirstCommitMutationFailure.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var attemptsAfterFailure = _storage.CommitMutationAttempts;
        await target.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        Assert.Equal(attemptsAfterFailure, _storage.CommitMutationAttempts);

        _storage.FailWrites = false;
        testScheduler.Advance(TimeSpan.FromMilliseconds(250));
        await AwaitAssertAsync(async () =>
        {
            var status = await _innerStorage.GetReminderOccurrenceStatusAsync(entity, key, dueTime);
            Assert.NotNull(status);
            Assert.Equal(ReminderCompletionStatus.Pending, status.CompletionStatus);
            Assert.Equal(1, status.AttemptCount);
            Assert.Equal("Ack timeout", status.LastFailureReason);
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeliveryOutcomeReads_ShouldReturnErrorWithoutChangingOccurrence()
    {
        var testScheduler = (TestScheduler)Sys.Scheduler;
        var now = testScheduler.Now;
        var entity = new ReminderEntity("test-region", "read-failure");
        var key = new ReminderKey("read-failure");
        var reminder = new ScheduledReminder(entity, key, now.AddHours(1), "payload");
        await _innerStorage.ScheduleReminderAsync(reminder, ct: TestContext.Current.CancellationToken);
        await _innerStorage.CommitReminderMutationsAsync(new ReminderMutationBatch(
            [],
            [],
            [new AwaitingAckReminder(entity, key, reminder.DueTimeUtc, now, now.AddMinutes(1))]), ct: TestContext.Current.CancellationToken);

        var scheduler = CreateScheduler();
        await WaitForSchedulerReady(scheduler);
        var client = new ReminderClient(scheduler, entity);
        var envelope = new ReminderEnvelope(
            entity,
            key,
            reminder.DueTimeUtc,
            ReminderDeadline.Infinite,
            "payload");
        _storage.FailReads = true;

        var nack = await client.NackAsync(envelope, "failed", ct: TestContext.Current.CancellationToken);
        var status = await client.GetOccurrenceStatusAsync(key, reminder.DueTimeUtc, ct: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderNackResponseCode.Error, nack.ResponseCode);
        Assert.Equal(ReminderOccurrenceStatusResponseCode.Error, status.ResponseCode);

        _storage.FailReads = false;
        var unchanged = await _innerStorage.GetReminderOccurrenceStatusAsync(entity, key, reminder.DueTimeUtc, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(unchanged);
        Assert.Equal(ReminderCompletionStatus.AwaitingAck, unchanged.CompletionStatus);
        Assert.Equal(0, unchanged.AttemptCount);
        Assert.Null(unchanged.LastFailureReason);
    }
}
