using Akka.Actor;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

public partial class ReminderSchedulerTimingSpecs
{
    [Fact]
    public async Task Should_RediscoverPending_When_RecoveryFetchSucceedsButCommitAndOverviewFail()
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var settings = DedicatedSettings(ackTimeout: TimeSpan.FromSeconds(10)) with
        {
            StorageTimeout = TimeSpan.FromSeconds(1)
        };
        var (scheduler, entity, region) = Setup("recovery-follow-up-fails", storage, settings);
        var due = VirtualTime.Now.AddSeconds(1);
        var key = new ReminderKey("persisted-but-unknown");
        Assert.Null(await StatusAsync(scheduler, entity, key, due));
        storage.ApplyNextScheduleThenReportFailure = true;
        var response = await scheduler.Ask<ReminderProtocol.ReminderScheduled>(
            new ReminderProtocol.ScheduleReminder(entity, key, due, "payload"), ReplyTimeout, Ct);
        Assert.Equal(ReminderScheduleResponseCode.Error, response.ResponseCode);
        await AwaitStatusAsync(scheduler, entity, key, due, ReminderCompletionStatus.Pending);

        // The recovery fetch discovers the row, but cannot move it to AwaitingAck or refresh
        // the cached overview. The pre-fetch overview is still empty.
        storage.FailWrites = true;
        storage.FailNextOverviewRead = true;
        VirtualTime.Advance(settings.StorageTimeout * 2);
        await AwaitStatusAsync(scheduler, entity, key, due, ReminderCompletionStatus.Pending);
        Assert.Equal(1, storage.Fetches);
        Assert.False(storage.FailNextOverviewRead);
        Assert.False(region.HasMessages);

        // Healthy storage must be revisited without a new command that reloads the overview.
        // Even the phantom ack deadline cannot help: the failed commit created no AwaitingAck row.
        storage.FailWrites = false;
        VirtualTime.Advance(settings.AckTimeout + settings.StorageTimeout * 2);
        await StatusAsync(scheduler, entity, key, due); // processing barrier, no overview refresh
        Assert.Equal(due, await NextDeliveryAsync(region));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_RediscoverStoredReminder_When_SchedulingWriteReportsFailure(bool throws)
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var (scheduler, entity, region) = Setup("schedule-recovery-" + throws, storage,
            DedicatedSettings() with { StorageTimeout = TimeSpan.FromSeconds(1) });
        var due = VirtualTime.Now.AddSeconds(5);
        var key = new ReminderKey("uncertain");
        Assert.Null(await StatusAsync(scheduler, entity, key, due)); // initialization barrier
        storage.ApplyNextScheduleThenReportFailure = !throws;
        storage.ApplyNextScheduleThenThrow = throws;

        var response = await scheduler.Ask<ReminderProtocol.ReminderScheduled>(
            new ReminderProtocol.ScheduleReminder(entity, key, due, "payload"), ReplyTimeout, Ct);
        Assert.Equal(ReminderScheduleResponseCode.Error, response.ResponseCode);
        await AwaitStatusAsync(scheduler, entity, key, due, ReminderCompletionStatus.Pending);

        // The cached overview was empty. Recovery must discover the row without another command.
        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        await AwaitAssertAsync(() =>
        {
            Assert.Equal(1, storage.Fetches);
            return Task.CompletedTask;
        }, ReplyTimeout, TimeSpan.FromMilliseconds(50), cancellationToken: Ct);
        await AwaitStatusAsync(scheduler, entity, key, due, ReminderCompletionStatus.Pending);
        VirtualTime.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(due, await NextDeliveryAsync(region));
    }

    [Fact]
    public async Task Should_NotCreateReminder_When_FailedSchedulingWriteDidNotPersist()
    {
        var storage = new FailableReminderStorage(new InMemoryReminderStorage());
        var (scheduler, entity, region) = Setup("schedule-not-stored", storage,
            DedicatedSettings() with { StorageTimeout = TimeSpan.FromSeconds(1) });
        var due = VirtualTime.Now;
        var key = new ReminderKey("absent");
        Assert.Null(await StatusAsync(scheduler, entity, key, due));
        storage.FailScheduleWrites = true;
        var response = await scheduler.Ask<ReminderProtocol.ReminderScheduled>(
            new ReminderProtocol.ScheduleReminder(entity, key, due, "payload"), ReplyTimeout, Ct);
        Assert.Equal(ReminderScheduleResponseCode.Error, response.ResponseCode);
        storage.FailScheduleWrites = false;

        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        await AwaitAssertAsync(() =>
        {
            Assert.Equal(1, storage.Fetches);
            return Task.CompletedTask;
        }, ReplyTimeout, TimeSpan.FromMilliseconds(50), cancellationToken: Ct);
        Assert.Null(await StatusAsync(scheduler, entity, key, due));
        Assert.False(region.HasMessages);

        // An authoritative empty fetch ends recovery; it must not become idle polling.
        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(await StatusAsync(scheduler, entity, key, due));
        Assert.Equal(1, storage.Fetches);
    }

    [Theory]
    [InlineData(false)] // known successful nack, followed by a failed overview read
    [InlineData(true)]  // indeterminate nack commit, followed by a failed overview read
    public async Task Should_RetainRecoveryFetch_When_AnUnrelatedAckCancelsTheLastAckTimeout(bool ambiguousCommit)
    {
        var inner = new InMemoryReminderStorage();
        var storage = new FailableReminderStorage(inner);
        var (scheduler, entity, region) = Setup("nack-recovery-" + ambiguousCommit, storage,
            DedicatedSettings(ackTimeout: TimeSpan.FromSeconds(10)) with { StorageTimeout = TimeSpan.FromSeconds(1) });
        var first = new ReminderKey("first");
        var second = new ReminderKey("second");
        var due = VirtualTime.Now.AddSeconds(5);
        await ScheduleAsync(scheduler, entity, first, due, null);
        await ScheduleAsync(scheduler, entity, second, due, null);
        VirtualTime.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(due, await NextDeliveryAsync(region));
        Assert.Equal(due, await NextDeliveryAsync(region));
        await AwaitStatusAsync(scheduler, entity, first, due, ReminderCompletionStatus.AwaitingAck);
        await AwaitStatusAsync(scheduler, entity, second, due, ReminderCompletionStatus.AwaitingAck);

        storage.ApplyNextCommitThenReportFailure = ambiguousCommit;
        storage.FailNextOverviewRead = true;
        var nack = await scheduler.Ask<ReminderProtocol.ReminderNackResponse>(
            new ReminderProtocol.ReminderNack(entity, first, due, "busy"), ReplyTimeout, Ct);
        Assert.Equal(ambiguousCommit ? ReminderNackResponseCode.Error : ReminderNackResponseCode.RetryScheduled,
            nack.ResponseCode);
        Assert.False(storage.FailNextOverviewRead);
        await AwaitStatusAsync(scheduler, entity, first, due, ReminderCompletionStatus.Pending);

        var ack = await scheduler.Ask<ReminderProtocol.ReminderAckResponse>(
            new ReminderProtocol.ReminderAck(entity, second, due), ReplyTimeout, Ct);
        Assert.Equal(ReminderAckResponseCode.Success, ack.ResponseCode);
        await AwaitStatusAsync(scheduler, entity, second, due, ReminderCompletionStatus.Delivered);
        Assert.Null(await inner.GetNextAwaitingAckDeadlineAsync(Ct));

        VirtualTime.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(due, await NextDeliveryAsync(region));
        await AwaitStatusAsync(scheduler, entity, first, due, ReminderCompletionStatus.AwaitingAck);
    }
}
