#nullable enable

using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Storage;

public abstract partial class ReminderStorageSpecBase
{
    [Theory(DisplayName = "Should_PersistRetryPolicyAcrossEveryReadAndMutation_When_OccurrenceIsRetried")]
    [InlineData(ReminderRetryPolicy.UseConfiguredAttemptLimit)]
    [InlineData(ReminderRetryPolicy.UntilAcknowledged)]
    public async Task Should_PersistRetryPolicyAcrossEveryReadAndMutation_When_OccurrenceIsRetried(ReminderRetryPolicy policy)
    {
        var ct = TestContext.Current.CancellationToken;
        var now = WholeSecondNow();
        var row = CreateTestReminder(when: now.AddDays(-365)) with { RetryPolicy = policy };
        Assert.IsAssignableFrom<IReminderRetryPolicyStorage>(Storage);
        var scheduled = await Storage!.ScheduleReminderAsync(row, ct);
        Assert.Equal(ReminderScheduleResponseCode.Success, scheduled.ResponseCode);
        Assert.Equal(policy, scheduled.OriginalCommand.RetryPolicy);
        Assert.Equal(policy, Assert.Single(await Storage.GetRemindersForEntityAsync(row.Entity, ct: ct)).RetryPolicy);
        Assert.Equal(policy, Assert.Single((await Storage.GetNextRemindersAsync(now, now, new ReminderBatchSize(1), ct)).Reminders).RetryPolicy);
        Assert.True(await Storage.MarkRemindersAsAwaitingAckAsync([new AwaitingAckReminder(row.Entity, row.Key, row.DueTimeUtc, now, now.AddSeconds(1))], ct));
        Assert.Equal(policy, (await Storage.GetAwaitingAckReminderAsync(row.Entity, row.Key, row.DueTimeUtc, ct))?.RetryPolicy);
        Assert.Equal(policy, Assert.Single(await Storage.GetTimedOutAckRemindersAsync(now.AddSeconds(2), new ReminderBatchSize(1), ct)).RetryPolicy);
        Assert.True(await Storage.CleanUpCompletedRemindersAsync(now.AddDays(1), ct));
        Assert.NotNull(await Storage.GetAwaitingAckReminderAsync(row.Entity, row.Key, row.DueTimeUtc, ct));
        var retry = row with { When = now.AddSeconds(3), AttemptCount = 50, OccurrenceDueTimeUtc = row.DueTimeUtc };
        Assert.True(await Storage.CommitReminderMutationsAsync(new ReminderMutationBatch([retry], [], []), ct));
        Assert.Equal(policy, Assert.Single(await Storage.GetRemindersForEntityAsync(row.Entity, ct: ct)).RetryPolicy);
        var readAhead = await Storage.GetNextRemindersAsync(now.AddSeconds(10), now, new ReminderBatchSize(1), ct);
        Assert.Equal(policy == ReminderRetryPolicy.UntilAcknowledged ? 0 : 1, readAhead.Reminders.Count);
        Assert.Equal(policy == ReminderRetryPolicy.UntilAcknowledged ? TimeSpan.FromSeconds(3) : TimeSpan.MaxValue,
            readAhead.NextOverview.TimeUntilNext);
        Assert.True(await Storage.CleanUpCompletedRemindersAsync(now.AddDays(1), ct));
        Assert.NotNull(await Storage.GetReminderOccurrenceStatusAsync(row.Entity, row.Key, row.DueTimeUtc, ct));
        Assert.Equal(ReminderCancelResponseCode.Success, (await Storage.CancelReminderAsync(row.Entity, row.Key, ct)).ResponseCode);
        Assert.True(await Storage.CleanUpCompletedRemindersAsync(DateTimeOffset.UtcNow.AddDays(1), ct));
        Assert.Null(await Storage.GetReminderOccurrenceStatusAsync(row.Entity, row.Key, row.DueTimeUtc, ct));
    }
}
