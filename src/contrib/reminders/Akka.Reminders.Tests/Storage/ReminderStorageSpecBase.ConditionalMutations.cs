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
}
