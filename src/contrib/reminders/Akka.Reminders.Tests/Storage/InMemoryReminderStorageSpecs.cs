using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Storage;

/// <summary>
/// Tests for <see cref="InMemoryReminderStorage"/> using the abstract test harness.
/// </summary>
public class InMemoryReminderStorageSpecs : ReminderStorageSpecBase
{
    protected override Task<IReminderStorage> CreateStorage()
    {
        return Task.FromResult<IReminderStorage>(new InMemoryReminderStorage());
    }

    protected override Task CleanupStorage(IReminderStorage storage)
    {
        // In-memory storage doesn't need cleanup
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Should_ReturnRowsInEntityKeyOrder_When_RowsShareADueTime")]
    public async Task RowsWithTheSameDueTime_ComeBackInTheSameOrderEveryTime()
    {
        // Rows with equal due times used to come back in hash order, which differs from one process to the next.
        var storage = new InMemoryReminderStorage();
        var when = DateTimeOffset.UtcNow.AddMinutes(1);
        var scheduled = (from region in new[] { "r2", "r0", "r1" }
                         from id in new[] { "e9", "e1", "e5" }
                         from key in new[] { "k2", "k0", "k1" }
                         select (Region: region, Id: id, Key: key)).ToList();
        foreach (var row in scheduled)
        {
            await storage.ScheduleReminderAsync(
                CreateTestReminder(CreateTestEntity(row.Region, row.Id), CreateTestKey(row.Key), when),
                TestContext.Current.CancellationToken);
        }

        var expected = scheduled
            .OrderBy(r => r.Region, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToList();

        var fetched = await storage.GetNextRemindersAsync(when, when, new ReminderBatchSize(100), TestContext.Current.CancellationToken);
        Assert.Equal(expected, fetched.Reminders.Select(r => (r.Entity.ShardRegionName, r.Entity.EntityId, r.Key.Name)).ToList());

        // One entity: the list orders by due time, then key.
        var listed = await storage.GetRemindersForEntityAsync(CreateTestEntity("r1", "e5"), 10, 0, TestContext.Current.CancellationToken);
        Assert.Equal(["k0", "k1", "k2"], listed.Select(r => r.Key.Name).ToList());
    }

    [Fact]
    public async Task RejectedMutationBatch_ShouldNotChangeAnyOccurrenceState()
    {
        var storage = new InMemoryReminderStorage();
        var reminder = CreateTestReminder();
        await storage.ScheduleReminderAsync(reminder, ct: TestContext.Current.CancellationToken);

        var missing = CreateTestReminder(
            CreateTestEntity("missing", "entity"),
            CreateTestKey("missing"),
            reminder.When);
        var committed = await storage.CommitReminderMutationsAsync(new ReminderMutationBatch(
            [],
            [new CompletedReminder(
                reminder.Entity,
                reminder.Key,
                reminder.DueTimeUtc,
                DateTimeOffset.UtcNow,
                ReminderCompletionStatus.Failed)],
            [new AwaitingAckReminder(
                missing.Entity,
                missing.Key,
                missing.DueTimeUtc,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1))]), ct: TestContext.Current.CancellationToken);

        Assert.False(committed);
        var status = await storage.GetReminderOccurrenceStatusAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);
        Assert.NotNull(status);
        Assert.Equal(ReminderCompletionStatus.Pending, status.CompletionStatus);
        Assert.Null(await storage.GetReminderOccurrenceStatusAsync(
            missing.Entity,
            missing.Key,
            missing.DueTimeUtc,
            ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LateAckStatus_ShouldPreserveExpiredOccurrenceDetails()
    {
        var storage = new InMemoryReminderStorage();
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now) with
        {
            AttemptCount = 2,
            LastFailureReason = "prior failure",
            DeliveryDeadlineUtc = now.AddMinutes(1)
        };
        await storage.ScheduleReminderAsync(reminder, ct: TestContext.Current.CancellationToken);
        await storage.CommitReminderMutationsAsync(new ReminderMutationBatch(
            [],
            [],
            [new AwaitingAckReminder(reminder.Entity, reminder.Key, reminder.DueTimeUtc, now, now.AddMinutes(1))]), ct: TestContext.Current.CancellationToken);

        var ack = await storage.AcknowledgeReminderAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            now.AddMinutes(2),
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(ReminderAckStorageStatus.NotFound, ack.Status);

        var status = await storage.GetReminderOccurrenceStatusAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);
        Assert.NotNull(status);
        Assert.Equal(ReminderCompletionStatus.Expired, status.CompletionStatus);
        Assert.Equal(2, status.AttemptCount);
        Assert.Equal("prior failure", status.LastFailureReason);
    }
}
