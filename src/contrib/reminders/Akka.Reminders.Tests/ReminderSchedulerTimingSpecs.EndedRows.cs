using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

/// <summary>
/// Issue #154: a row the same commit ends must not count as pending work in the in-memory overview.
/// </summary>
public partial class ReminderSchedulerTimingSpecs
{
    [Fact(DisplayName = "Should_NotFetchAgain_When_ARowEndsTerminallyBesideAFarFutureRow")]
    public async Task Should_NotFetchAgain_When_ARowEndsTerminallyBesideAFarFutureRow()
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var (scheduler, entity, _) = Setup("ended-row", storage, DedicatedSettings(maxDeliveryAttempts: 1));
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, new ReminderKey("far"), t0.AddSeconds(100), null);
        var dead = new ReminderKey("dead");
        await ScheduleAsync(scheduler, entity, dead, t0.AddSeconds(5), null);
        Assert.True(_resolver.UnregisterShardRegion(entity.ShardRegionName));

        // The only attempt finds no shard region, so the row ends as Failed.
        VirtualTime.Advance(TimeSpan.FromSeconds(5));
        await AwaitStatusAsync(scheduler, entity, dead, t0.AddSeconds(5), ReminderCompletionStatus.Failed);

        // A leftover "due now" entry would make the scheduler fetch again on the next tick.
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(20, Ct);
            VirtualTime.Advance(TimeSpan.FromTicks(1));
        }
        await StatusAsync(scheduler, entity, dead, t0.AddSeconds(5));

        Assert.Equal(1, storage.Fetches);
    }
}
