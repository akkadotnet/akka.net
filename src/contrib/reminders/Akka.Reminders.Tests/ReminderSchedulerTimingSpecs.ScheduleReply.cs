using Akka.Actor;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

/// <summary>
/// Issue #152: a reminder that was stored is picked up even when the overview read right after the
/// save fails, and the caller is told it was stored.
/// </summary>
public partial class ReminderSchedulerTimingSpecs
{
    [Fact(DisplayName = "Should_ReplySuccessAndDeliver_When_OverviewReloadFailsAfterReminderIsStored")]
    public async Task Should_ReplySuccessAndDeliver_When_OverviewReloadFailsAfterReminderIsStored()
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var (scheduler, entity, region) = Setup("stored-then-read-fails", storage);
        var key = new ReminderKey("stored");
        var t0 = VirtualTime.Now;
        Assert.Null(await StatusAsync(scheduler, entity, key, t0)); // the scheduler has finished loading

        storage.FailNextOverviewRead = true;
        var reply = await scheduler.Ask<ReminderProtocol.ReminderScheduled>(
            new ReminderProtocol.ScheduleReminder(entity, key, t0.AddSeconds(5), "payload"), ReplyTimeout, Ct);

        Assert.Equal(ReminderScheduleResponseCode.Success, reply.ResponseCode);
        Assert.False(storage.FailNextOverviewRead);

        VirtualTime.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(t0.AddSeconds(5), await NextDeliveryAsync(region));
    }

    [Theory(DisplayName = "Should_StillDeliverEarlierReminderOnTime_When_OverviewReloadFailsAfterALaterOneIsStored")]
    [InlineData(300)] // B is later than A and later than the stale TimeUntilNext counted from now
    [InlineData(90)]  // B is later than A, but sooner than the stale TimeUntilNext counted from now
    public async Task Should_StillDeliverEarlierReminderOnTime_When_OverviewReloadFailsAfterALaterOneIsStored(int laterDueSeconds)
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var (scheduler, entity, region) = Setup("earlier-stays-on-time-" + laterDueSeconds, storage);
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, new ReminderKey("a"), t0.AddSeconds(60), null);

        VirtualTime.Advance(TimeSpan.FromSeconds(50));
        storage.FailNextOverviewRead = true;
        var reply = await scheduler.Ask<ReminderProtocol.ReminderScheduled>(
            new ReminderProtocol.ScheduleReminder(entity, new ReminderKey("b"), t0.AddSeconds(laterDueSeconds), "payload"),
            ReplyTimeout, Ct);
        Assert.Equal(ReminderScheduleResponseCode.Success, reply.ResponseCode);
        Assert.False(storage.FailNextOverviewRead);

        // Nothing is due yet: the timer must not fire early because of the later reminder.
        VirtualTime.Advance(TimeSpan.FromTicks(1));
        await Task.Delay(100, Ct);
        Assert.Equal(0, storage.Fetches);

        VirtualTime.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1)); // exactly t0+60
        Assert.Equal(t0.AddSeconds(60), await NextDeliveryAsync(region));
    }
}
