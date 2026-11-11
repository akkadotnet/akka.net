using Akka.Actor;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

public partial class ReminderSchedulerTimingSpecs
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFetch_Should_RetryWithDelay_AndResumeAfterStorageRecovers(bool overviewAlsoFails)
    {
        var inner = new InMemoryReminderStorage();
        var storage = new FailableReminderStorage(inner);
        var entity = new ReminderEntity("read-outage", "e1");
        var key = new ReminderKey("one-off");
        var due = VirtualTime.Now.AddSeconds(1);
        await inner.UpsertReminderOccurrencesAsync([new ScheduledReminder(entity, key, due, "payload")], Ct);
        var region = CreateTestProbe();
        _resolver.RegisterShardRegion(entity.ShardRegionName, region);
        var settings = DedicatedSettings();
        var scheduler = StartScheduler(settings, storage, "read-outage");
        await StatusAsync(scheduler, entity, key, due);

        storage.FailFetchReads = true;
        storage.FailFetchAndOverview = overviewAlsoFails;
        VirtualTime.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        await StatusAsync(scheduler, entity, key, due);
        Assert.Equal(1, storage.Fetches);

        // Each query completes behind a processing pass. Moving only one tick must not re-run it.
        for (var i = 0; i < 30; i++)
        {
            VirtualTime.Advance(TimeSpan.FromTicks(1));
            await StatusAsync(scheduler, entity, key, due);
        }
        Assert.Equal(1, storage.Fetches);
        Assert.False(region.HasMessages);

        if (!overviewAlsoFails)
        {
            // A successful unrelated command refreshes the overdue overview and re-arms fetch.
            // It must not erase the delay for the still-failing fetch operation.
            var cancelled = await scheduler.Ask<ReminderProtocol.RemindersCancelled>(
                new ReminderProtocol.CancelReminder(entity, new ReminderKey("unrelated")), ReplyTimeout, Ct);
            Assert.Equal(ReminderCancelResponseCode.NotFound, cancelled.ResponseCode);
            VirtualTime.Advance(TimeSpan.FromTicks(1));
            await StatusAsync(scheduler, entity, key, due);
            Assert.Equal(1, storage.Fetches);
        }

        // Storage stays unavailable through another recovery tick; retry pacing must continue.
        VirtualTime.Advance(settings.StorageTimeout * 2 + TimeSpan.FromTicks(1));
        await StatusAsync(scheduler, entity, key, due);
        Assert.Equal(2, storage.Fetches);
        VirtualTime.Advance(TimeSpan.FromTicks(1));
        await StatusAsync(scheduler, entity, key, due);
        Assert.Equal(2, storage.Fetches);

        storage.FailFetchReads = storage.FailFetchAndOverview = false;
        VirtualTime.Advance(settings.StorageTimeout * 2 + TimeSpan.FromTicks(1));
        await StatusAsync(scheduler, entity, key, due);
        var envelope = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal(due, envelope.DueTimeUtc);
        Assert.Equal("payload", envelope.Message);
        Assert.Equal(3, storage.Fetches);
    }
}
