using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Storage;

public abstract partial class ReminderStorageSpecBase
{
    [Fact]
    public async Task ConditionalInserts_ShouldPreserveEveryExistingState_AndInsertMissingOccurrences()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("conditional-insert");
        var rows = Enum.GetValues<ReminderCompletionStatus>().Select(status =>
            new ScheduledReminder(entity, new ReminderKey(status.ToString()), now, "original", AttemptCount: 2,
                LastFailureReason: "retained", DeliveryDeadlineUtc: now.AddMinutes(1), OccurrenceDueTimeUtc: now)).ToArray();
        await Storage!.UpsertReminderOccurrencesAsync(rows, ct);
        foreach (var row in rows)
        {
            var status = Enum.Parse<ReminderCompletionStatus>(row.Key.Name);
            if (status == ReminderCompletionStatus.AwaitingAck)
                Assert.True(await Storage.MarkRemindersAsAwaitingAckAsync([new AwaitingAckReminder(entity, row.Key, now, now, now.AddSeconds(30))], ct));
            else if (status != ReminderCompletionStatus.Pending)
                Assert.True(await Storage.MarkRemindersAsCompletedAsync([new CompletedReminder(entity, row.Key, now, now, status)], ct));
        }
        var before = new List<ReminderOccurrenceStatus?>();
        foreach (var row in rows)
            before.Add(await Storage.GetReminderOccurrenceStatusAsync(entity, row.Key, now, ct));

        var missing = new ScheduledReminder(entity, new ReminderKey("new"), now, "new");
        var batch = ReminderMutationBatch.Empty with
        {
            PendingInserts = [.. rows.Select(row => row with { When = now.AddHours(1), AttemptCount = 0, LastFailureReason = null }), missing]
        };
        Assert.False(batch.IsEmpty);
        Assert.True(await Storage.CommitReminderMutationsAsync(batch, ct));
        for (var i = 0; i < rows.Length; i++)
            Assert.Equal(before[i], await Storage.GetReminderOccurrenceStatusAsync(entity, rows[i].Key, now, ct));
        Assert.Equal(ReminderCompletionStatus.Pending,
            (await Storage.GetReminderOccurrenceStatusAsync(entity, missing.Key, now, ct))?.CompletionStatus);

        // The original upsert operation intentionally resets existing state, independently of insert-only behavior.
        var delivered = rows.Single(row => row.Key.Name == nameof(ReminderCompletionStatus.Delivered));
        Assert.True(await Storage.CommitReminderMutationsAsync(new ReminderMutationBatch([delivered], [], []), ct));
        Assert.Equal(ReminderCompletionStatus.Pending,
            (await Storage.GetReminderOccurrenceStatusAsync(entity, delivered.Key, now, ct))?.CompletionStatus);
    }

    [Fact]
    public async Task ActiveCompletions_ShouldExpireActiveOccurrences_WithoutChangingTerminalOrAbsentRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("conditional-complete");
        var states = Enum.GetValues<ReminderCompletionStatus>();
        var rows = states.Select(status => new ScheduledReminder(entity, new ReminderKey(status.ToString()), now, "payload")).ToArray();
        await Storage!.UpsertReminderOccurrencesAsync(rows, ct);
        foreach (var row in rows)
        {
            var status = Enum.Parse<ReminderCompletionStatus>(row.Key.Name);
            if (status == ReminderCompletionStatus.AwaitingAck)
                Assert.True(await Storage.MarkRemindersAsAwaitingAckAsync([new AwaitingAckReminder(entity, row.Key, now, now, now.AddSeconds(30))], ct));
            else if (status != ReminderCompletionStatus.Pending)
                Assert.True(await Storage.MarkRemindersAsCompletedAsync([new CompletedReminder(entity, row.Key, now, now, status)], ct));
        }
        var before = new List<ReminderOccurrenceStatus?>();
        foreach (var row in rows)
            before.Add(await Storage.GetReminderOccurrenceStatusAsync(entity, row.Key, now, ct));
        var absent = new ReminderKey("absent");
        var batch = ReminderMutationBatch.Empty with
        {
            ActiveCompletions = [.. rows.Select(row => new CompletedReminder(entity, row.Key, now, now.AddSeconds(1), ReminderCompletionStatus.Expired)),
                new CompletedReminder(entity, absent, now, now, ReminderCompletionStatus.Expired)]
        };
        Assert.False(batch.IsEmpty);
        Assert.True(await Storage.CommitReminderMutationsAsync(batch, ct));
        for (var i = 0; i < rows.Length; i++)
        {
            var actual = await Storage.GetReminderOccurrenceStatusAsync(entity, rows[i].Key, now, ct);
            if (states[i] is ReminderCompletionStatus.Pending or ReminderCompletionStatus.AwaitingAck)
                Assert.Equal(ReminderCompletionStatus.Expired, actual?.CompletionStatus);
            else
                Assert.Equal(before[i], actual);
        }
        Assert.Null(await Storage.GetReminderOccurrenceStatusAsync(entity, absent, now, ct));
    }

    [Fact]
    public async Task ConditionalMutations_ShouldRollBackTogether_WhenAwaitingAckTransitionFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("conditional-rollback");
        var previous = new ScheduledReminder(entity, new ReminderKey("previous"), now, "payload");
        var next = new ScheduledReminder(entity, new ReminderKey("next"), now.AddSeconds(10), "payload");
        await Storage!.UpsertReminderOccurrencesAsync([previous], ct);
        var batch = new ReminderMutationBatch([], [],
            [new AwaitingAckReminder(entity, new ReminderKey("absent"), now, now, now.AddSeconds(30))])
        {
            PendingInserts = [next],
            ActiveCompletions = [new CompletedReminder(entity, previous.Key, now, now, ReminderCompletionStatus.Expired)]
        };
        Assert.False(await Storage.CommitReminderMutationsAsync(batch, ct));
        Assert.Null(await Storage.GetReminderOccurrenceStatusAsync(entity, next.Key, next.DueTimeUtc, ct));
        Assert.Equal(ReminderCompletionStatus.Pending,
            (await Storage.GetReminderOccurrenceStatusAsync(entity, previous.Key, now, ct))?.CompletionStatus);
    }

    // #8965: the scheduler sends the newest slot of a series and, in the same commit, ends every older slot
    // that may still be live. Their keys come from the interval, not from storage, so some name rows that
    // are already acked or were never written.
    [Fact]
    public async Task CommitReminderMutations_ShouldEndSeveralOlderSlotsOfOneSeries_WhileMovingTheNewestToAwaitingAck()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var entity = CreateTestEntity("series-slots");
        var key = new ReminderKey("series");
        var interval = TimeSpan.FromSeconds(2);
        ScheduledReminder Slot(int n) => new(entity, key, now.AddSeconds(2 * n), "payload", interval,
            DeliveryDeadlineUtc: now.AddSeconds(2 * n + 2), OccurrenceDueTimeUtc: now.AddSeconds(2 * n));
        async Task<ReminderCompletionStatus?> StatusAsync(int n)
            => (await Storage!.GetReminderOccurrenceStatusAsync(entity, key, Slot(n).DueTimeUtc, ct))?.CompletionStatus;

        // Slot 1 is a Pending retry, 2 awaits its ack, 3 was acked, 4 was never written, 5 is the newest.
        await Storage!.UpsertReminderOccurrencesAsync([Slot(1), Slot(2), Slot(3), Slot(5)], ct);
        Assert.True(await Storage.MarkRemindersAsAwaitingAckAsync(
            new[] { 2, 3 }.Select(n => new AwaitingAckReminder(entity, key, Slot(n).DueTimeUtc, now, now.AddSeconds(30))), ct));
        Assert.True((await Storage.AcknowledgeReminderAsync(entity, key, Slot(3).DueTimeUtc, now, ct)).Success);

        var batch = new ReminderMutationBatch([], [], [new AwaitingAckReminder(entity, key, Slot(5).DueTimeUtc, now, now.AddSeconds(30))])
        {
            PendingInserts = [Slot(6)],
            ActiveCompletions = [.. new[] { 4, 3, 2, 1 }.Select(n => new CompletedReminder(entity, key, Slot(n).DueTimeUtc, now, ReminderCompletionStatus.Expired))]
        };
        Assert.True(await Storage.CommitReminderMutationsAsync(batch, ct));

        Assert.Equal(ReminderCompletionStatus.Expired, await StatusAsync(1));
        Assert.Equal(ReminderCompletionStatus.Expired, await StatusAsync(2));
        Assert.Equal(ReminderCompletionStatus.Delivered, await StatusAsync(3));
        Assert.Null(await StatusAsync(4));
        Assert.Equal(ReminderCompletionStatus.AwaitingAck, await StatusAsync(5));
        Assert.Equal(ReminderCompletionStatus.Pending, await StatusAsync(6));
    }
}
