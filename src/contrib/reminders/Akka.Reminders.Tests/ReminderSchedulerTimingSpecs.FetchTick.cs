using Akka.Actor;
using Akka.Reminders.Storage;
using Akka.TestKit;

namespace Akka.Reminders.Tests;

/// <summary>
/// The scheduler arms a zero-delay fetch tick when a fetch leaves a reminder overdue. That tick must be
/// handled even when it reaches the mailbox before the fetch that armed it has finished.
/// </summary>
public partial class ReminderSchedulerTimingSpecs
{
    [Fact(DisplayName = "Should_RunFollowUpFetch_When_TickArrivesBeforePreviousFetchCompletes")]
    public async Task Should_RunFollowUpFetch_When_TickArrivesBeforePreviousFetchCompletes()
    {
        var (entity, key, t0) = (new ReminderEntity("restart-region", "e1"), new ReminderKey("first"), VirtualTime.Now);
        var inner = new InMemoryReminderStorage();
        await inner.UpsertReminderOccurrencesAsync([
            new ScheduledReminder(entity, key, t0, "payload"),
            new ScheduledReminder(entity, new ReminderKey("next"), t0.AddSeconds(1), "payload")], Ct);
        var commits = 0;
        var storage = new FailableReminderStorage(inner)
        {
            // The future row was outside the fetch horizon but becomes overdue before re-arming.
            OnCommit = _ => { if (Interlocked.Increment(ref commits) == 1) VirtualTime.Advance(TimeSpan.FromSeconds(2)); }
        };
        var region = CreateTestProbe();
        _resolver.RegisterShardRegion(entity.ShardRegionName, region);
        var scheduler = StartScheduler(DedicatedSettings() with { MaxSlippage = TimeSpan.Zero }, storage, "restart");
        Assert.Equal(ReminderCompletionStatus.Pending, (await StatusAsync(scheduler, entity, key, t0))?.CompletionStatus);

        // The first pass's overdue re-arm fires ahead of its own completion. The queued follow-up
        // must remain actionable, rather than being dropped because the previous pass is finishing.
        var eager = (EagerTestScheduler)VirtualTime;
        eager.FireZeroDelayAtOnce = true;
        VirtualTime.Advance(TimeSpan.Zero);

        Assert.Equal(t0, await NextDeliveryAsync(region));
        Assert.Equal(t0.AddSeconds(1), await NextDeliveryAsync(region));
        Assert.True(eager.FiredAtOnce > 0);
    }
}
