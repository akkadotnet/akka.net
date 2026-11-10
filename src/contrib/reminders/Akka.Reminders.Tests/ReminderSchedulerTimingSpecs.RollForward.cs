using Akka.Actor;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.Storage;
using Akka.TestKit;

namespace Akka.Reminders.Tests;

/// <summary>
/// Issue #143: a recurring occurrence that passes its deadline before delivery is expired (never
/// delivered late) and the series continues at the next slot whose deadline has not passed.
/// </summary>
public partial class ReminderSchedulerTimingSpecs
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private TestScheduler VirtualTime => (TestScheduler)Sys.Scheduler;

    private static ReminderSettings DedicatedSettings(int maxDeliveryAttempts = 3, TimeSpan? ackTimeout = null) => new()
    {
        MaxSlippage = TimeSpan.FromSeconds(1),
        StorageTimeout = TimeSpan.FromSeconds(30),
        MaxDeliveryAttempts = maxDeliveryAttempts,
        RetryBackoffBase = ackTimeout.HasValue ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5),
        AckTimeout = ackTimeout ?? ReminderSettings.DefaultAckTimeout
    };

    private IActorRef StartScheduler(ReminderSettings settings, IReminderStorage storage, string name)
        => Sys.ActorOf(Props.Create(() => new ReminderScheduler(settings, _resolver, storage, Sys.Scheduler)), name);

    private IReminderStorage CreateStorage(string kind) => kind == "inmem"
        ? new InMemoryReminderStorage()
        : new SqliteReminderStorage(SqliteReminderStorageSettings.Create(SqliteConnectionString()), Sys);

    private string? _sqliteDirectory;

    /// <summary>
    /// A new database file in this test's own temp directory, which <see cref="AfterAllAsync"/> deletes.
    /// </summary>
    private string SqliteConnectionString()
    {
        _sqliteDirectory ??= Directory.CreateTempSubdirectory("akka-reminders-").FullName;
        return $"Data Source={Path.Combine(_sqliteDirectory, $"{Guid.NewGuid():N}.db")};Mode=ReadWriteCreate;Cache=Shared";
    }

    protected override async Task AfterAllAsync()
    {
        await base.AfterAllAsync();
        if (_sqliteDirectory is null)
            return;

        // Stop the scheduler and release pooled connections before deleting the database files.
        await Sys.Terminate();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_sqliteDirectory, recursive: true);
    }

    /// <summary>
    /// A SQLite storage holding <paramref name="poison"/> with a payload whose message type is gone.
    /// </summary>
    private async Task<SqliteReminderStorage> SqliteWithUnreadableRowAsync(ScheduledReminder poison)
    {
        var connectionString = SqliteConnectionString();
        var storage = new SqliteReminderStorage(SqliteReminderStorageSettings.Create(connectionString), Sys);
        await storage.ScheduleReminderAsync(poison, Ct);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync(Ct);
        var command = connection.CreateCommand();
        command.CommandText = $"UPDATE scheduled_reminders SET serializer_id = 987654 WHERE reminder_key = '{poison.Key.Name}'";
        Assert.Equal(1, await command.ExecuteNonQueryAsync(Ct));
        return storage;
    }

    private static async Task<ReminderOccurrenceStatus?> StatusAsync(IActorRef scheduler, ReminderEntity entity, ReminderKey key, DateTimeOffset due)
        => (await scheduler.Ask<ReminderProtocol.ReminderOccurrenceStatusResponse>(
            new ReminderProtocol.GetReminderOccurrenceStatus(entity, key, due), ReplyTimeout, Ct)).Status;

    private static async Task ScheduleAsync(IActorRef scheduler, ReminderEntity entity, ReminderKey key, DateTimeOffset due,
        TimeSpan? interval, TimeSpan? window = null, object? message = null)
        => Assert.Equal(ReminderScheduleResponseCode.Success, (await scheduler.Ask<ReminderProtocol.ReminderScheduled>(
            new ReminderProtocol.ScheduleReminder(entity, key, due, message ?? "payload", interval, window), ReplyTimeout, Ct)).ResponseCode);

    private (IActorRef Scheduler, ReminderEntity Entity, TestProbe Region) Setup(string name, IReminderStorage? storage = null,
        ReminderSettings? settings = null)
    {
        var region = CreateTestProbe();
        _resolver.RegisterShardRegion(name + "-region", region);
        return (StartScheduler(settings ?? DedicatedSettings(), storage ?? new InMemoryReminderStorage(), name),
            new ReminderEntity(name + "-region", "e1"), region);
    }

    /// <summary>
    /// A slot that is already due when the series rolls forward is fetched by a zero-delay timer,
    /// which the TestScheduler only fires on the next Advance.
    /// </summary>
    private async Task<DateTimeOffset> NextDeliveryAsync(TestProbe region)
    {
        for (var i = 0; i < 100 && !region.HasMessages; i++)
        {
            await Task.Delay(20, Ct);

            // Not TimeSpan.Zero: TestScheduler.Advance drops an item that the actor adds to the bucket
            // it is draining, and a zero-delay re-arm during a zero advance lands in that bucket.
            VirtualTime.Advance(TimeSpan.FromTicks(1));
        }

        return (await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct)).DueTimeUtc;
    }

    private Task AwaitStatusAsync(IActorRef scheduler, ReminderEntity entity, ReminderKey key, DateTimeOffset due, ReminderCompletionStatus expected)
        => AwaitAssertAsync(async () => Assert.Equal(expected, (await StatusAsync(scheduler, entity, key, due))?.CompletionStatus),
            ReplyTimeout, TimeSpan.FromMilliseconds(50), cancellationToken: Ct);

    [Fact(DisplayName = "Should_RollRecurringReminderForward_When_OccurrenceExpiresBeforeDelivery (#143 repro)")]
    public async Task Should_RollRecurringReminderForward_When_OccurrenceExpiresBeforeDelivery()
    {
        var (scheduler, entity, region) = Setup("lagged");
        var key = new ReminderKey("lagged");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), TimeSpan.FromSeconds(2));

        // One step of lag: the first occurrence (deadline t0+7) and the slots up to t0+17 are dead at t0+20.
        VirtualTime.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(t0.AddSeconds(19), await NextDeliveryAsync(region));
        await region.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300), Ct);
        Assert.Equal(ReminderCompletionStatus.Expired, (await StatusAsync(scheduler, entity, key, t0.AddSeconds(5)))?.CompletionStatus);
        Assert.Null(await StatusAsync(scheduler, entity, key, t0.AddSeconds(17)));

        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(t0.AddSeconds(21), await NextDeliveryAsync(region));
    }

    [Fact]
    public async Task Should_DeliverNextSlot_When_OccurrenceMissesItsDeliveryWindow()
    {
        var (scheduler, entity, region) = Setup("window");
        var key = new ReminderKey("window");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(500));

        VirtualTime.Advance(TimeSpan.FromMilliseconds(5700)); // past the 500 ms window, before the t0+7 slot
        await AwaitStatusAsync(scheduler, entity, key, t0.AddSeconds(7), ReminderCompletionStatus.Pending);
        Assert.Equal(ReminderCompletionStatus.Expired, (await StatusAsync(scheduler, entity, key, t0.AddSeconds(5)))?.CompletionStatus);
        await region.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300), Ct);

        VirtualTime.Advance(TimeSpan.FromMilliseconds(1300));
        Assert.Equal(t0.AddSeconds(7), await NextDeliveryAsync(region));
    }

    [Fact]
    public async Task Should_RollToSlotDueNow_When_SchedulerRunsExactlyAtTheDeadline()
    {
        var (scheduler, entity, region) = Setup("boundary");
        var key = new ReminderKey("boundary");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), TimeSpan.FromSeconds(2));

        VirtualTime.Advance(TimeSpan.FromSeconds(7)); // exactly the first occurrence's deadline

        Assert.Equal(t0.AddSeconds(7), await NextDeliveryAsync(region));
        Assert.Equal(ReminderCompletionStatus.Expired, (await StatusAsync(scheduler, entity, key, t0.AddSeconds(5)))?.CompletionStatus);
    }

    [Theory]
    [InlineData(3, ReminderCompletionStatus.Expired)] // the 5 s retry backoff would miss the t0+7 deadline
    [InlineData(1, ReminderCompletionStatus.Failed)]  // no attempts left
    public async Task Should_KeepSeriesAlive_When_ShardRegionIsMissing(int maxDeliveryAttempts, ReminderCompletionStatus terminal)
    {
        var (scheduler, entity, region) = Setup("srnf-" + terminal, settings: DedicatedSettings(maxDeliveryAttempts));
        var key = new ReminderKey("srnf");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), TimeSpan.FromSeconds(2));
        Assert.True(_resolver.UnregisterShardRegion(entity.ShardRegionName));

        VirtualTime.Advance(TimeSpan.FromSeconds(5));
        await AwaitStatusAsync(scheduler, entity, key, t0.AddSeconds(5), terminal);
        Assert.Equal(ReminderCompletionStatus.Pending, (await StatusAsync(scheduler, entity, key, t0.AddSeconds(7)))?.CompletionStatus);

        _resolver.RegisterShardRegion(entity.ShardRegionName, region);
        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(t0.AddSeconds(7), await NextDeliveryAsync(region));
    }

    [Fact]
    public async Task Should_NotForkSeries_When_AckTimeoutRetryGoesStale()
    {
        var (scheduler, entity, region) = Setup("fork", settings: DedicatedSettings(5, ackTimeout: TimeSpan.FromSeconds(2)));
        var key = new ReminderKey("fork");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), TimeSpan.FromSeconds(10));

        VirtualTime.Advance(TimeSpan.FromSeconds(5)); // delivered; successor t0+15 created; no ack
        Assert.Equal(t0.AddSeconds(5), await NextDeliveryAsync(region));
        VirtualTime.Advance(TimeSpan.FromSeconds(2)); // ack timeout: back to Pending, retry at t0+8
        await AwaitStatusAsync(scheduler, entity, key, t0.AddSeconds(5), ReminderCompletionStatus.Pending);

        // Lag to t0+27: the retry (deadline t0+15) and its successor (deadline t0+25) both roll to t0+25.
        VirtualTime.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(t0.AddSeconds(25), await NextDeliveryAsync(region));
        await region.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300), Ct);
        Assert.Equal(ReminderCompletionStatus.Expired, (await StatusAsync(scheduler, entity, key, t0.AddSeconds(15)))?.CompletionStatus);
        Assert.Equal(ReminderCompletionStatus.Pending, (await StatusAsync(scheduler, entity, key, t0.AddSeconds(35)))?.CompletionStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_NotDeliver_When_CancelledBeforeOrAfterRollForward(bool cancelFirst)
    {
        var (scheduler, entity, region) = Setup("cancel-" + cancelFirst);
        var key = new ReminderKey("cancel");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
        var cancel = new ReminderProtocol.CancelReminder(entity, key);

        if (cancelFirst)
            await scheduler.Ask<ReminderProtocol.RemindersCancelled>(cancel, ReplyTimeout, Ct);
        VirtualTime.Advance(TimeSpan.FromSeconds(8)); // past the 1 s window: rolls forward to t0+15
        if (!cancelFirst)
        {
            await AwaitStatusAsync(scheduler, entity, key, t0.AddSeconds(15), ReminderCompletionStatus.Pending);
            await scheduler.Ask<ReminderProtocol.RemindersCancelled>(cancel, ReplyTimeout, Ct);
        }

        VirtualTime.Advance(TimeSpan.FromSeconds(10));
        await region.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300), Ct);
        Assert.NotEqual(ReminderCompletionStatus.Pending, (await StatusAsync(scheduler, entity, key, t0.AddSeconds(15)))?.CompletionStatus);
        Assert.Null(await StatusAsync(scheduler, entity, key, t0.AddSeconds(25)));
    }

    [Theory]
    [InlineData("inmem")]
    [InlineData("sqlite")]
    public async Task Should_RollForwardAfterRestart_When_StoredRecurringOccurrenceIsStale(string kind)
    {
        var storage = CreateStorage(kind);
        var (entity, key, t0) = (new ReminderEntity("restart-region", "e1"), new ReminderKey("restart"), VirtualTime.Now);
        var due = t0.AddSeconds(-15);
        await storage.ScheduleReminderAsync(new ScheduledReminder(entity, key, due, "payload", TimeSpan.FromSeconds(2),
            DeliveryDeadlineUtc: due.AddSeconds(2), OccurrenceDueTimeUtc: due), Ct);
        var region = CreateTestProbe();
        _resolver.RegisterShardRegion("restart-region", region);
        var scheduler = StartScheduler(DedicatedSettings(), storage, "restart-" + kind);

        // Startup expiry leaves it alone; the overview reports it overdue, so a tick is armed.
        Assert.Equal(ReminderCompletionStatus.Pending, (await StatusAsync(scheduler, entity, key, due))?.CompletionStatus);
        Assert.Equal(t0.AddSeconds(-1), await NextDeliveryAsync(region));
        Assert.Equal(ReminderCompletionStatus.Expired, (await StatusAsync(scheduler, entity, key, due))?.CompletionStatus);
    }

    [Theory]
    [InlineData("inmem")]
    [InlineData("sqlite")]
    public async Task Should_KeepSeriesAlive_When_RegisteredAgainWithPastAnchor(string kind)
    {
        // Apps often register their schedules again on every start, with the same first due time.
        var (scheduler, entity, region) = Setup("again-" + kind, CreateStorage(kind));
        var key = new ReminderKey("daily");
        var t0 = VirtualTime.Now;
        var interval = TimeSpan.FromSeconds(10);
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), interval);
        foreach (var slot in new[] { 5, 15 })
        {
            VirtualTime.Advance(TimeSpan.FromSeconds(slot == 5 ? 5 : 10));
            Assert.Equal(t0.AddSeconds(slot), await NextDeliveryAsync(region));
            await scheduler.Ask<ReminderProtocol.ReminderAckResponse>(new ReminderProtocol.ReminderAck(entity, key, t0.AddSeconds(slot)), ReplyTimeout, Ct);
        }

        // At t0+17 the app registers the t0+5 anchor again: the pending t0+25 row is cancelled.
        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), interval);

        // The stale anchor rolls forward to the live slot (t0+15 again, at-least-once), and the series goes on.
        Assert.Equal(t0.AddSeconds(15), await NextDeliveryAsync(region));
        VirtualTime.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal(t0.AddSeconds(25), await NextDeliveryAsync(region));
    }

    [Fact]
    public async Task Should_NotBlockOtherReminders_When_StaleRecurringPayloadIsUnreadable()
    {
        var (entity, poison, t0) = (new ReminderEntity("poison-region", "e1"), new ReminderKey("poison"), VirtualTime.Now);
        var due = t0.AddSeconds(-15);
        var storage = await SqliteWithUnreadableRowAsync(new ScheduledReminder(entity, poison, due, "payload", TimeSpan.FromSeconds(2),
            DeliveryDeadlineUtc: due.AddSeconds(2), OccurrenceDueTimeUtc: due));

        var region = CreateTestProbe();
        _resolver.RegisterShardRegion("poison-region", region);
        var scheduler = StartScheduler(DedicatedSettings(), storage, "poison");
        await ScheduleAsync(scheduler, entity, new ReminderKey("innocent"), t0.AddSeconds(2), null, message: "innocent");

        // The overdue poison row arms a zero-delay tick; the fetch must fail that row, not throw.
        await AwaitAssertAsync(async () =>
        {
            // One tick, not zero: see NextDeliveryAsync.
            VirtualTime.Advance(TimeSpan.FromTicks(1));
            Assert.Equal(ReminderCompletionStatus.Failed, (await StatusAsync(scheduler, entity, poison, due))?.CompletionStatus);
        }, ReplyTimeout, TimeSpan.FromMilliseconds(50), cancellationToken: Ct);
        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("innocent", (await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct)).Message);
    }

    [Fact]
    public async Task Should_SkipLiveUnreadablePayload_WithoutBlockingOrHotLooping()
    {
        var (entity, poison, t0) = (new ReminderEntity("live-poison-region", "e1"), new ReminderKey("poison"), VirtualTime.Now);
        var due = t0.AddSeconds(-1); // overdue, but its deadline (t0+9) has not passed
        var storage = new FailableReminderStorage(await SqliteWithUnreadableRowAsync(new ScheduledReminder(entity, poison, due,
            "payload", TimeSpan.FromSeconds(10), DeliveryDeadlineUtc: due.AddSeconds(10), OccurrenceDueTimeUtc: due)));
        var region = CreateTestProbe();
        _resolver.RegisterShardRegion("live-poison-region", region);
        var scheduler = StartScheduler(DedicatedSettings(), storage, "live-poison");
        await ScheduleAsync(scheduler, entity, new ReminderKey("innocent"), t0.AddSeconds(2), null, message: "innocent");

        // The overdue row arms one zero-delay tick. That fetch skips it and must not arm another.
        await AwaitAssertAsync(() =>
        {
            VirtualTime.Advance(TimeSpan.FromTicks(1));
            Assert.True(storage.Fetches > 0);
        }, ReplyTimeout, TimeSpan.FromMilliseconds(50), cancellationToken: Ct);
        Assert.Equal(ReminderCompletionStatus.Pending, (await StatusAsync(scheduler, entity, poison, due))?.CompletionStatus);
        var fetches = storage.Fetches;
        for (var i = 0; i < 10; i++)
        {
            VirtualTime.Advance(TimeSpan.FromTicks(1));
            await Task.Delay(20, Ct);
        }
        Assert.Equal(fetches, storage.Fetches);

        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("innocent", (await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct)).Message);
        Assert.Equal(ReminderCompletionStatus.Pending, (await StatusAsync(scheduler, entity, poison, due))?.CompletionStatus);
    }

    [Fact]
    public async Task Should_ExpireLaterChunks_When_EarlierCommitsAreSlow()
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var (scheduler, entity, region) = Setup("slow-commit", storage, DedicatedSettings() with { DeliveryCommitChunkSize = 1 });
        var due = VirtualTime.Now.AddSeconds(1);
        var keys = Enumerable.Range(1, 4).Select(i => new ReminderKey("k" + i)).ToList();
        foreach (var key in keys)
            await ScheduleAsync(scheduler, entity, key, due, null, TimeSpan.FromMilliseconds(300));

        // Each commit takes 400 ms, longer than the 300 ms window: only the first chunk is still live.
        storage.OnCommit = _ => VirtualTime.Advance(TimeSpan.FromMilliseconds(400));
        VirtualTime.Advance(TimeSpan.FromSeconds(1));

        var delivered = (await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct)).Key;
        foreach (var key in keys.Where(k => k != delivered))
            await AwaitStatusAsync(scheduler, entity, key, due, ReminderCompletionStatus.Expired);
        await region.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300), Ct);
    }

    [Fact]
    public async Task Should_UpsertEachOccurrenceOnce_When_StaleRetryRollsOntoRetriedSuccessor()
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var (scheduler, entity, region) = Setup("dup", storage, DedicatedSettings(5, ackTimeout: TimeSpan.FromSeconds(2)));
        var duplicates = 0;
        storage.OnCommit = batch =>
        {
            if (batch.PendingUpserts.GroupBy(r => (r.Entity, r.Key, r.DueTimeUtc)).Any(g => g.Count() > 1))
                Interlocked.Increment(ref duplicates);
        };
        var key = new ReminderKey("dup");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), TimeSpan.FromSeconds(10));

        VirtualTime.Advance(TimeSpan.FromSeconds(5)); // delivered; successor t0+15 created; no ack
        Assert.Equal(t0.AddSeconds(5), await NextDeliveryAsync(region));
        VirtualTime.Advance(TimeSpan.FromSeconds(2)); // ack timeout: back to Pending, retry at t0+8
        await AwaitStatusAsync(scheduler, entity, key, t0.AddSeconds(5), ReminderCompletionStatus.Pending);

        // At t0+16 one chunk holds the stale retry, which rolls forward to t0+15, and the live t0+15
        // successor, which is retried because its region is missing: two rows for one key.
        Assert.True(_resolver.UnregisterShardRegion(entity.ShardRegionName));
        VirtualTime.Advance(TimeSpan.FromSeconds(9));
        await AwaitStatusAsync(scheduler, entity, key, t0.AddSeconds(5), ReminderCompletionStatus.Expired);
        Assert.Equal(0, Volatile.Read(ref duplicates));

        // The successor's retry (at t0+17) is the row that was kept.
        _resolver.RegisterShardRegion(entity.ShardRegionName, region);
        VirtualTime.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(t0.AddSeconds(15), await NextDeliveryAsync(region));
    }

    [Fact]
    public async Task Should_KeepRetry_When_StaleRollForwardIsAddedAfterRetriedSuccessor()
    {
        var inner = new InMemoryReminderStorage();
        var storage = new FailableReminderStorage(inner);
        var entity = new ReminderEntity("order-region", "e1");
        var key = new ReminderKey("order");
        var t0 = VirtualTime.Now;
        var interval = TimeSpan.FromSeconds(20);

        // The successor (slot t0+20) sorts before the stale row, whose when_utc is later. The stale
        // row's deadline has passed by t0+26, so it rolls forward onto the successor's slot.
        Assert.True(await inner.CommitReminderMutationsAsync(new ReminderMutationBatch(
        [
            new ScheduledReminder(entity, key, t0.AddSeconds(20), "payload", interval,
                DeliveryDeadlineUtc: t0.AddSeconds(40), OccurrenceDueTimeUtc: t0.AddSeconds(20)),
            new ScheduledReminder(entity, key, t0.AddSeconds(25), "payload", interval,
                DeliveryDeadlineUtc: t0.AddSeconds(20), OccurrenceDueTimeUtc: t0)
        ], [], []), Ct));

        // The region is never registered, so the successor is retried in the same chunk.
        var scheduler = StartScheduler(DedicatedSettings(), storage, "order");
        await StatusAsync(scheduler, entity, key, t0); // the scheduler has loaded both rows before time moves
        var slotRows = new List<ScheduledReminder>();
        storage.OnCommit = batch => slotRows.AddRange(batch.PendingUpserts.Where(r => r.DueTimeUtc == t0.AddSeconds(20)));
        VirtualTime.Advance(TimeSpan.FromSeconds(26));
        await AwaitStatusAsync(scheduler, entity, key, t0, ReminderCompletionStatus.Expired);

        var row = Assert.Single(slotRows);
        Assert.Equal(1, row.AttemptCount);
    }

    public static TheoryData<double, double?, double, double?> Slots => new()
    {
        // interval s, window s, now (s after due), expected next due (s after due); null = no next slot
        { 10, null, 3, 10 },      // before the deadline
        { 10, null, 10, 10 },     // exactly at the deadline: the next slot is due now
        { 10, null, 30, 30 },     // exactly at a later slot's due time
        { 2, null, 15, 14 },      // lagged 7.5 intervals
        { 10, 2, 5, 10 },         // window < interval, past the window
        { 10, 2, 22, 30 },        // exactly at slot 2's window deadline
        { 10, 60, 25, 20 },       // window > interval is clamped to the interval
        { 1, null, 2_592_000.5, 2_592_000 }, // 30 days at 1 s: O(1)
        { 0, null, 5, null },     // zero interval
        { -1, null, 5, null },    // negative interval
    };

    [Theory]
    [MemberData(nameof(Slots))]
    public void CreateNextRecurringOccurrence_Should_PickEarliestLiveSlot(double interval, double? window, double now, double? expected)
    {
        var due = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reminder = new ScheduledReminder(new ReminderEntity("r", "e"), new ReminderKey("k"), due, "m",
            TimeSpan.FromSeconds(interval), MaxDeliveryWindow: window.HasValue ? TimeSpan.FromSeconds(window.Value) : null);

        var next = ReminderScheduler.CreateNextRecurringOccurrence(reminder, due.AddSeconds(now));

        Assert.Equal(expected.HasValue ? due.AddSeconds(expected.Value) : (DateTimeOffset?)null, next?.DueTimeUtc);
        if (next is not null)
            Assert.True(next.DeliveryDeadlineUtc > due.AddSeconds(now));
    }

    [Fact]
    public void CreateNextRecurringOccurrence_Should_ReturnNull_When_NextSlotOverflows()
    {
        var due = DateTimeOffset.MaxValue.AddDays(-1);
        var reminder = new ScheduledReminder(new ReminderEntity("r", "e"), new ReminderKey("k"), due, "m", TimeSpan.FromDays(2));
        Assert.Null(ReminderScheduler.CreateNextRecurringOccurrence(reminder, due));
        Assert.Null(ReminderScheduler.CreateNextRecurringOccurrence(reminder with { RepeatInterval = TimeSpan.MaxValue }, due));
    }
}
