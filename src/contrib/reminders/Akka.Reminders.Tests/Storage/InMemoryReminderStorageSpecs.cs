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
