using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Storage;

/// <summary>
/// Issue #143 storage contract: a Pending recurring occurrence past its deadline belongs to the
/// scheduler, which rolls the series forward. Storage must not expire it and must keep returning it.
/// </summary>
public abstract partial class ReminderStorageSpecBase
{
    /// <summary>
    /// Whole seconds, so every provider (PostgreSQL stores microseconds) round-trips the value exactly.
    /// </summary>
    protected static DateTimeOffset WholeSecondNow()
        => new(DateTimeOffset.UtcNow.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private static ScheduledReminder StaleRecurring(ReminderEntity entity, string key, DateTimeOffset due)
        => new(entity, new ReminderKey(key), due, "payload", TimeSpan.FromSeconds(2),
            DeliveryDeadlineUtc: due.AddSeconds(2), OccurrenceDueTimeUtc: due);

    private async Task<ReminderCompletionStatus?> CompletionStatusAsync(ScheduledReminder r)
        => (await Storage!.GetReminderOccurrenceStatusAsync(r.Entity, r.Key, r.DueTimeUtc, TestContext.Current.CancellationToken))?.CompletionStatus;

    [Fact]
    public async Task ExpireRemindersAsync_Should_LeavePendingRecurringOccurrence_When_PastDeadline()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("expiry", "e1");
        var pendingRecurring = StaleRecurring(entity, "recurring-pending", now.AddSeconds(-10));
        var awaitingRecurring = StaleRecurring(entity, "recurring-awaiting", now.AddSeconds(-20));
        var oneOff = new ScheduledReminder(entity, new ReminderKey("one-off"), now.AddMinutes(-2), "payload",
            DeliveryDeadlineUtc: now.AddMinutes(-1), OccurrenceDueTimeUtc: now.AddMinutes(-2));
        foreach (var reminder in new[] { pendingRecurring, awaitingRecurring, oneOff })
            await Storage!.ScheduleReminderAsync(reminder, ct);
        Assert.True(await Storage!.MarkRemindersAsAwaitingAckAsync([new AwaitingAckReminder(entity, awaitingRecurring.Key,
            awaitingRecurring.DueTimeUtc, now.AddSeconds(-20), now.AddMinutes(1))], ct));

        Assert.Equal(2, await Storage.ExpireRemindersAsync(now, ct));

        Assert.Equal(ReminderCompletionStatus.Pending, await CompletionStatusAsync(pendingRecurring));
        Assert.Equal(ReminderCompletionStatus.Expired, await CompletionStatusAsync(awaitingRecurring)); // its successor already exists
        Assert.Equal(ReminderCompletionStatus.Expired, await CompletionStatusAsync(oneOff));
    }

    [Fact]
    public async Task Reads_Should_IncludeStalePendingRecurringOccurrence()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("reads", "e1");
        var stale = StaleRecurring(entity, "stale", now.AddSeconds(-10));
        await Storage!.ScheduleReminderAsync(stale, ct);

        var batch = await Storage.GetNextRemindersAsync(now.AddSeconds(1), now, new ReminderBatchSize(10), ct);
        Assert.Equal(stale.DueTimeUtc, Assert.Single(batch.Reminders).DueTimeUtc);

        // Without this the scheduler would see no pending work and never arm a tick.
        var overview = await Storage.GetRemindersOverviewAsync(now, ct);
        Assert.Equal(1, overview.TotalPendingReminders);
        Assert.True(overview.TimeUntilNext < TimeSpan.Zero);

        Assert.Single(await Storage.GetRemindersForEntityAsync(entity, ct: ct));
        var cancelled = await Storage.CancelAllRemindersForEntityAsync(entity, ct);
        Assert.Equal(ReminderCancelResponseCode.Success, cancelled.ResponseCode);
        Assert.Equal(ReminderCompletionStatus.Cancelled, await CompletionStatusAsync(stale));
    }

    /// <summary>
    /// Overwrites a stored payload's serializer id so it can no longer be deserialized. Returns false
    /// for providers that keep messages as objects.
    /// </summary>
    protected virtual Task<bool> CorruptPayloadAsync(ScheduledReminder reminder) => Task.FromResult(false);

    [Fact]
    public async Task GetNextRemindersAsync_Should_FailUnreadableOccurrence_And_ReturnTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("poison", "e1");
        var poison = StaleRecurring(entity, "poison", now.AddSeconds(-15));
        var healthy = new ScheduledReminder(entity, new ReminderKey("healthy"), now.AddSeconds(-1), "healthy");
        await Storage!.ScheduleReminderAsync(poison, ct);
        await Storage.ScheduleReminderAsync(healthy, ct);
        if (!await CorruptPayloadAsync(poison))
            return;

        var batch = await Storage.GetNextRemindersAsync(now.AddSeconds(1), now, new ReminderBatchSize(10), ct);

        Assert.Equal(healthy.Key, Assert.Single(batch.Reminders).Key);
        Assert.Equal(ReminderCompletionStatus.Failed, await CompletionStatusAsync(poison));
    }

    [Fact]
    public async Task GetNextRemindersAsync_Should_SkipLiveUnreadableOccurrences_And_PagePastThem()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("live-poison", "e1");
        var oneOff = new ScheduledReminder(entity, new ReminderKey("poison-one-off"), now.AddSeconds(-3), "payload");
        var recurring = new ScheduledReminder(entity, new ReminderKey("poison-recurring"), now.AddSeconds(-2), "payload",
            TimeSpan.FromSeconds(10), DeliveryDeadlineUtc: now.AddSeconds(8), OccurrenceDueTimeUtc: now.AddSeconds(-2));
        var healthy = new ScheduledReminder(entity, new ReminderKey("healthy"), now.AddSeconds(-1), "healthy");
        var later = new ScheduledReminder(entity, new ReminderKey("later"), now.AddSeconds(30), "later");
        foreach (var reminder in new[] { oneOff, recurring, healthy, later })
            await Storage!.ScheduleReminderAsync(reminder, ct);
        if (!await CorruptPayloadAsync(oneOff) || !await CorruptPayloadAsync(recurring))
            return;

        // The two live unreadable rows fill a page of two, so the fetch must page past them.
        var batch = await Storage!.GetNextRemindersAsync(now.AddSeconds(1), now, new ReminderBatchSize(2), ct);

        Assert.Equal(healthy.Key, Assert.Single(batch.Reminders).Key);
        Assert.Equal(ReminderCompletionStatus.Pending, await CompletionStatusAsync(oneOff));
        Assert.Equal(ReminderCompletionStatus.Pending, await CompletionStatusAsync(recurring));

        // The skipped rows are left out of the overview, so they cannot arm an immediate tick.
        Assert.Equal(1, batch.NextOverview.TotalPendingReminders);
        Assert.Equal(TimeSpan.FromSeconds(30), batch.NextOverview.TimeUntilNext);
    }

    [Fact]
    public async Task GetRemindersForEntityAsync_Should_SkipLiveUnreadableOccurrence_And_ListTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("list-poison", "e1");
        var poison = new ScheduledReminder(entity, new ReminderKey("poison"), now.AddMinutes(5), "payload");
        var healthy = new ScheduledReminder(entity, new ReminderKey("healthy"), now.AddMinutes(10), "healthy");
        await Storage!.ScheduleReminderAsync(poison, ct);
        await Storage.ScheduleReminderAsync(healthy, ct);
        if (!await CorruptPayloadAsync(poison))
            return;

        var listed = await Storage.GetRemindersForEntityAsync(entity, ct: ct);

        Assert.Equal(healthy.Key, Assert.Single(listed).Key);
    }
}
