using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

/// <summary>
/// Issue #153: time that storage calls take during a fetch must not push the next fetch back.
/// </summary>
public partial class ReminderSchedulerTimingSpecs
{
    [Fact(DisplayName = "Should_FetchNextOccurrenceOnTime_When_TheDeliveryCommitWasSlow")]
    public async Task Should_FetchNextOccurrenceOnTime_When_TheDeliveryCommitWasSlow()
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var (scheduler, entity, region) = Setup("slow-commit", storage);
        var key = new ReminderKey("series");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(1), TimeSpan.FromSeconds(8));

        // The first delivery commit takes 2 s of virtual time.
        storage.OnCommit = _ =>
        {
            storage.OnCommit = null;
            VirtualTime.Advance(TimeSpan.FromSeconds(2));
        };
        VirtualTime.Advance(TimeSpan.FromSeconds(1));
        var first = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal(t0.AddSeconds(1), first.DueTimeUtc);
        Assert.Equal(t0.AddSeconds(3), VirtualTime.Now);

        // Once this replies, the fetch is over and its timer for the t0+9 slot is armed.
        await AwaitStatusAsync(scheduler, entity, key, t0.AddSeconds(9), ReminderCompletionStatus.Pending);

        VirtualTime.Advance(TimeSpan.FromSeconds(6)); // exactly t0+9
        var second = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal(t0.AddSeconds(9), second.DueTimeUtc);
    }
}
