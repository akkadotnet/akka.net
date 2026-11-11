using Akka.Actor;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

public partial class ReminderSchedulerTimingSpecs
{
    [Fact]
    public async Task Should_PrepareRecurringRetriesAndEarlySends_WithoutOccurrenceStatusReads()
    {
        var inner = new InMemoryReminderStorage();
        var storage = new FailableReminderStorage(inner);
        var (scheduler, entity, region) = Setup("batched-neighbours", storage);
        var key = new ReminderKey("series");
        var t0 = VirtualTime.Now;
        var earlier = new ScheduledReminder(entity, key, t0.AddSeconds(1), "payload", TimeSpan.FromSeconds(10),
            AttemptCount: 1, DeliveryDeadlineUtc: t0.AddSeconds(2), OccurrenceDueTimeUtc: t0.AddSeconds(-8));
        var current = new ScheduledReminder(entity, key, t0.AddSeconds(2), "payload", TimeSpan.FromSeconds(10),
            DeliveryDeadlineUtc: t0.AddSeconds(12), OccurrenceDueTimeUtc: t0.AddSeconds(2));
        await inner.UpsertReminderOccurrencesAsync([earlier, current], Ct);
        // A normal one-off scheduling command arms the timer after the seed, without querying occurrence status.
        await ScheduleAsync(scheduler, entity, new ReminderKey("wake"), t0.AddSeconds(1), null);
        VirtualTime.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        var first = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        var second = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Contains(new[] { first, second }, message => message.Key == key && message.DueTimeUtc == current.DueTimeUtc);
        Assert.Equal(0, storage.OccurrenceStatusReads);
        Assert.Equal(ReminderCompletionStatus.Expired,
            (await inner.GetReminderOccurrenceStatusAsync(entity, key, earlier.DueTimeUtc, Ct))?.CompletionStatus);
        Assert.Equal(ReminderCompletionStatus.AwaitingAck,
            (await inner.GetReminderOccurrenceStatusAsync(entity, key, current.DueTimeUtc, Ct))?.CompletionStatus);
        Assert.Equal(ReminderCompletionStatus.Pending,
            (await inner.GetReminderOccurrenceStatusAsync(entity, key, t0.AddSeconds(12), Ct))?.CompletionStatus);
    }

    [Fact]
    public async Task Should_RejectUnsupportedRecurringStorage_WhileOneOffRemindersStillWork()
    {
        var inner = new InMemoryReminderStorage();
        var legacy = new LegacyStorage(inner);
        var (scheduler, entity, region) = Setup("legacy-storage", legacy);
        var t0 = VirtualTime.Now;
        var recurring = await scheduler.Ask<ReminderProtocol.ReminderScheduled>(
            new ReminderProtocol.ScheduleReminder(entity, new ReminderKey("unsupported"), t0.AddSeconds(1), "payload", TimeSpan.FromSeconds(10)),
            ReplyTimeout, Ct);
        Assert.Equal(ReminderScheduleResponseCode.Error, recurring.ResponseCode);
        Assert.Contains(nameof(IConditionalReminderMutationStorage), recurring.Message);
        Assert.Empty(await inner.GetRemindersForEntityAsync(entity, ct: Ct));
        await ScheduleAsync(scheduler, entity, new ReminderKey("one-off"), t0.AddSeconds(1), null);
        VirtualTime.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        var envelope = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal(new ReminderKey("one-off"), envelope.Key);
    }

    // Forward only the original interface to simulate an existing provider without conditional-mutation support.
    private sealed class LegacyStorage(IReminderStorage inner) : IReminderStorage
    {
        public Task<ReminderProtocol.ReminderScheduled> ScheduleReminderAsync(ScheduledReminder reminder, CancellationToken ct = default)
            => inner.ScheduleReminderAsync(reminder, ct);

        public Task<bool> UpsertReminderOccurrencesAsync(IEnumerable<ScheduledReminder> reminders, CancellationToken ct = default)
            => inner.UpsertReminderOccurrencesAsync(reminders, ct);

        public Task<bool> CommitReminderMutationsAsync(ReminderMutationBatch mutationBatch, CancellationToken ct = default)
            => inner.CommitReminderMutationsAsync(mutationBatch, ct);

        public Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(ReminderEntity entity, ReminderKey key, CancellationToken ct = default)
            => inner.CancelReminderAsync(entity, key, ct);

        public Task<IReadOnlyList<ScheduledReminder>> GetRemindersForEntityAsync(ReminderEntity entity, int take = 10, int skip = 0, CancellationToken ct = default)
            => inner.GetRemindersForEntityAsync(entity, take, skip, ct);

        public Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersForEntityAsync(ReminderEntity entity, CancellationToken ct = default)
            => inner.CancelAllRemindersForEntityAsync(entity, ct);

        public Task<ReminderOverview> GetRemindersOverviewAsync(DateTimeOffset now, CancellationToken ct = default)
            => inner.GetRemindersOverviewAsync(now, ct);

        public Task<PendingRemindersWithSummary> GetNextRemindersAsync(DateTimeOffset untilDeadline, DateTimeOffset now, ReminderBatchSize maxCount, CancellationToken ct = default)
            => inner.GetNextRemindersAsync(untilDeadline, now, maxCount, ct);

        public Task<bool> MarkRemindersAsCompletedAsync(IEnumerable<CompletedReminder> keys, CancellationToken ct = default)
            => inner.MarkRemindersAsCompletedAsync(keys, ct);

        public Task<bool> CleanUpCompletedRemindersAsync(DateTimeOffset olderThan, CancellationToken ct = default)
            => inner.CleanUpCompletedRemindersAsync(olderThan, ct);

        public Task<int> ExpireRemindersAsync(DateTimeOffset now, CancellationToken ct = default)
            => inner.ExpireRemindersAsync(now, ct);

        public Task<bool> MarkRemindersAsAwaitingAckAsync(IEnumerable<AwaitingAckReminder> reminders, CancellationToken ct = default)
            => inner.MarkRemindersAsAwaitingAckAsync(reminders, ct);

        public Task<IReadOnlyList<ScheduledReminder>> GetTimedOutAckRemindersAsync(DateTimeOffset now, ReminderBatchSize maxCount, CancellationToken ct = default)
            => inner.GetTimedOutAckRemindersAsync(now, maxCount, ct);

        public Task<ScheduledReminder?> GetAwaitingAckReminderAsync(ReminderEntity entity, ReminderKey key, DateTimeOffset dueTimeUtc, CancellationToken ct = default)
            => inner.GetAwaitingAckReminderAsync(entity, key, dueTimeUtc, ct);

        public Task<ReminderOccurrenceStatus?> GetReminderOccurrenceStatusAsync(ReminderEntity entity, ReminderKey key, DateTimeOffset dueTimeUtc, CancellationToken ct = default)
            => inner.GetReminderOccurrenceStatusAsync(entity, key, dueTimeUtc, ct);

        public Task<DateTimeOffset?> GetNextAwaitingAckDeadlineAsync(CancellationToken ct = default)
            => inner.GetNextAwaitingAckDeadlineAsync(ct);

        public Task<AckResult> AcknowledgeReminderAsync(ReminderEntity entity, ReminderKey key, DateTimeOffset dueTimeUtc, DateTimeOffset ackedAt, CancellationToken ct = default)
            => inner.AcknowledgeReminderAsync(entity, key, dueTimeUtc, ackedAt, ct);

        public Task<IReadOnlyList<AckResult>> AcknowledgeRemindersAsync(IEnumerable<ReminderAcknowledgement> acknowledgements, CancellationToken ct = default)
            => inner.AcknowledgeRemindersAsync(acknowledgements, ct);
    }
}
