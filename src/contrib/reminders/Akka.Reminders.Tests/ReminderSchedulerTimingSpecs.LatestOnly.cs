using Akka.Actor;
using Akka.Reminders.Storage;
using Akka.TestKit;

namespace Akka.Reminders.Tests;

/// <summary>
/// Issue #150: a recurring reminder has at most one live delivered occurrence, and a retry never
/// resets the next occurrence.
/// </summary>
public partial class ReminderSchedulerTimingSpecs
{
    [Fact(DisplayName = "Should_ExpirePreviousUnackedOccurrence_When_NextOccurrenceIsSentEarly")]
    public async Task Should_ExpirePreviousUnackedOccurrence_When_NextOccurrenceIsSentEarly()
    {
        var (scheduler, entity, region) = Setup("latest-only");
        var key = new ReminderKey("series");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, key, t0.AddSeconds(5), TimeSpan.FromSeconds(2));

        VirtualTime.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(t0.AddSeconds(5), await NextDeliveryAsync(region)); // sent, never acked

        // Another reminder wakes the scheduler at t0+6.5, inside MaxSlippage (1 s) of the t0+7 slot.
        await ScheduleAsync(scheduler, entity, new ReminderKey("other"), t0.AddSeconds(6.5), null);
        VirtualTime.Advance(TimeSpan.FromSeconds(1.5));

        await AwaitStatusAsync(scheduler, entity, key, t0.AddSeconds(7), ReminderCompletionStatus.AwaitingAck);
        Assert.Equal(ReminderCompletionStatus.Expired, (await StatusAsync(scheduler, entity, key, t0.AddSeconds(5)))?.CompletionStatus);

        var lateAck = await scheduler.Ask<ReminderProtocol.ReminderAckResponse>(
            new ReminderProtocol.ReminderAck(entity, key, t0.AddSeconds(5)), ReplyTimeout, Ct);
        Assert.Equal(ReminderAckResponseCode.NotFound, lateAck.ResponseCode);
    }

    // Seeds storage directly: an older occurrence still waits as a retry although its next slot was
    // already sent and acked. The scheduler itself no longer produces this state (sending the next
    // slot early expires the older one), but a version before this fix, or a failed lookup, can leave it.
    [Fact(DisplayName = "Should_LeaveNextOccurrenceAlone_When_AnOlderOccurrenceIsRetried")]
    public async Task Should_LeaveNextOccurrenceAlone_When_AnOlderOccurrenceIsRetried()
    {
        var (entity, key, t0) = (new ReminderEntity("retry-region", "e1"), new ReminderKey("series"), VirtualTime.Now);
        var interval = TimeSpan.FromSeconds(10);
        var storage = new InMemoryReminderStorage();

        // The first occurrence (due t0-5) was sent once and its retry is due now; its next slot (t0+5)
        // was already sent early and acked.
        var first = new ScheduledReminder(entity, key, t0, "payload", interval, AttemptCount: 1,
            DeliveryDeadlineUtc: t0.AddSeconds(5), OccurrenceDueTimeUtc: t0.AddSeconds(-5));
        var next = new ScheduledReminder(entity, key, t0.AddSeconds(5), "payload", interval,
            DeliveryDeadlineUtc: t0.AddSeconds(15), OccurrenceDueTimeUtc: t0.AddSeconds(5));
        await storage.UpsertReminderOccurrencesAsync([first, next], Ct);
        await storage.MarkRemindersAsAwaitingAckAsync([new AwaitingAckReminder(entity, key, next.DueTimeUtc, t0, t0.AddSeconds(10))], Ct);
        Assert.True((await storage.AcknowledgeReminderAsync(entity, key, next.DueTimeUtc, t0, Ct)).Success);

        var region = CreateTestProbe();
        _resolver.RegisterShardRegion("retry-region", region);
        var scheduler = StartScheduler(DedicatedSettings(), storage, "retry-keeps-next");

        Assert.Equal(first.DueTimeUtc, await NextDeliveryAsync(region)); // the retry goes out
        Assert.Equal(ReminderCompletionStatus.Delivered, (await StatusAsync(scheduler, entity, key, next.DueTimeUtc))?.CompletionStatus);
    }

    private static ReminderSettings LatestOnlySettings(double maxBackoffSeconds = 600) => new()
    {
        MaxSlippage = TimeSpan.FromSeconds(1),
        AckTimeout = TimeSpan.FromSeconds(3),
        RetryBackoffBase = TimeSpan.FromSeconds(0.1),
        MaxRetryBackoff = TimeSpan.FromSeconds(maxBackoffSeconds),
        MaxDeliveryAttempts = 1000,
        StorageTimeout = TimeSpan.FromSeconds(30)
    };

    private sealed record Sent(double At, string Key, double Due);

    /// <summary>
    /// Moves virtual time forward in small steps until <paramref name="untilSeconds"/> after
    /// <paramref name="t0"/>, lets the scheduler finish after each step, and records (and optionally acks)
    /// what it sent. The step never lands on a timer's exact tick.
    /// </summary>
    private async Task DriveAsync(IActorRef scheduler, TestProbe region, DateTimeOffset t0, double untilSeconds, List<Sent> sent,
        Func<bool> ack, Func<Task>? afterStep = null)
    {
        async Task SyncAsync()
        {
            for (var i = 0; i < 3; i++)
                await StatusAsync(scheduler, new ReminderEntity("sync", "sync"), new ReminderKey("sync"), DateTimeOffset.UnixEpoch);
        }

        while ((VirtualTime.Now - t0).TotalSeconds < untilSeconds)
        {
            VirtualTime.Advance(TimeSpan.FromMilliseconds(50) + TimeSpan.FromTicks(7));
            await SyncAsync();
            var at = Math.Round((VirtualTime.Now - t0).TotalSeconds, 2);
            while (region.HasMessages)
            {
                var envelope = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
                sent.Add(new Sent(at, envelope.Key.Name, Math.Round((envelope.DueTimeUtc - t0).TotalSeconds, 2)));
                if (ack())
                    await scheduler.Ask<ReminderProtocol.ReminderAckResponse>(
                        new ReminderProtocol.ReminderAck(envelope.Entity, envelope.Key, envelope.DueTimeUtc), ReplyTimeout, Ct);
            }

            if (afterStep is not null)
                await afterStep();
            await SyncAsync();
        }
    }

    // The sequence from issue #150, through real scheduler passes.
    [Fact(DisplayName = "Should_SendEachSlotOnce_When_AnUnackedSlotIsRetriedAfterTheNextSlotWasSentEarlyAndAcked (#150)")]
    public async Task Should_SendEachSlotOnce_When_AnUnackedSlotIsRetriedAfterTheNextSlotWasSentEarlyAndAcked()
    {
        var (scheduler, entity, region) = Setup("issue-150", settings: LatestOnlySettings());
        var series = new ReminderKey("A");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, series, t0, TimeSpan.FromSeconds(10));

        var sent = new List<Sent>();
        var acking = false;
        await DriveAsync(scheduler, region, t0, 8.85, sent, () => acking); // first slot sent and retried, never acked
        acking = true;
        await ScheduleAsync(scheduler, entity, new ReminderKey("B"), t0.AddSeconds(9.0), null); // wakes the scheduler at 9.0 s
        await DriveAsync(scheduler, region, t0, 12.5, sent, () => acking);

        Assert.Single(sent, s => s.Key == "A" && s.Due == 10); // sent early at 9.0 s, acked, and not again at 10 s
        Assert.Equal(ReminderCompletionStatus.Pending, (await StatusAsync(scheduler, entity, series, t0.AddSeconds(20)))?.CompletionStatus);
    }

    // The shard region is gone from the start and comes back inside the slippage window of the next slot.
    // That slot's retry row then sorts before the older occurrence's retry row in the same chunk.
    [Fact(DisplayName = "Should_NotCommitOneRowAsExpiredAndSent_When_NextOccurrenceIsProcessedBeforeThePreviousOne")]
    public async Task Should_NotCommitOneRowAsExpiredAndSent_When_NextOccurrenceIsProcessedBeforeThePreviousOne()
    {
        var conflicting = new List<string>();
        var storage = new FailableReminderStorage(new InMemoryReminderStorage())
        {
            OnCommit = batch =>
            {
                lock (conflicting)
                    conflicting.AddRange(batch.CompletedReminders.Concat(batch.ActiveCompletions)
                        .Where(c => batch.AwaitingAckReminders.Any(a => a.Entity == c.Entity && a.Key == c.Key && a.DueTimeUtc == c.DueTimeUtc))
                        .Select(c => c.DueTimeUtc.ToString("O")));
            }
        };
        var (scheduler, entity, region) = Setup("region-returns", storage, LatestOnlySettings(maxBackoffSeconds: 0.3));
        var series = new ReminderKey("A");
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, series, t0, TimeSpan.FromSeconds(10));
        _resolver.UnregisterShardRegion(entity.ShardRegionName);

        var sent = new List<Sent>();
        var back = false;
        await DriveAsync(scheduler, region, t0, 11.0, sent, () => true, async () =>
        {
            // Come back once the 10 s slot has itself been put back for a retry.
            if (!back && (await StatusAsync(scheduler, entity, series, t0.AddSeconds(10)))?.AttemptCount > 0)
            {
                back = true;
                _resolver.RegisterShardRegion(entity.ShardRegionName, region);
            }
        });

        Assert.True(back);
        Assert.Empty(conflicting);
        Assert.Contains(sent, s => s.Key == "A" && s.Due == 10 && s.At < 10); // the pass that saw the region again delivered
    }

    [Fact(DisplayName = "Should_DeliverEverything_When_ARepeatIntervalIsLongerThanTheTimeSinceYearOne")]
    public async Task Should_DeliverEverything_When_ARepeatIntervalIsLongerThanTheTimeSinceYearOne()
    {
        var (scheduler, entity, region) = Setup("huge-interval", settings: LatestOnlySettings());
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, new ReminderKey("huge"), t0.AddSeconds(1), TimeSpan.FromDays(365.25 * 3000));
        await ScheduleAsync(scheduler, entity, new ReminderKey("normal"), t0.AddSeconds(2), null);

        // Not DriveAsync: a scheduler that keeps crashing would not answer its sync queries.
        var keys = new List<string>();
        for (var i = 0; i < 80 && keys.Count < 2; i++)
        {
            VirtualTime.Advance(TimeSpan.FromMilliseconds(50) + TimeSpan.FromTicks(7));
            await Task.Delay(25, Ct);
            while (region.HasMessages)
                keys.Add((await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct)).Key.Name);
        }

        Assert.Contains("huge", keys);
        Assert.Contains("normal", keys);
    }

    // A zero interval makes "the previous occurrence" the occurrence itself; it must not expire itself.
    [Fact(DisplayName = "Should_DeliverEverything_When_ARepeatIntervalIsZero")]
    public async Task Should_DeliverEverything_When_ARepeatIntervalIsZero()
    {
        var (scheduler, entity, region) = Setup("zero-interval", settings: LatestOnlySettings());
        var t0 = VirtualTime.Now;
        await ScheduleAsync(scheduler, entity, new ReminderKey("zero"), t0.AddSeconds(2), TimeSpan.Zero);
        await ScheduleAsync(scheduler, entity, new ReminderKey("normal"), t0.AddSeconds(1.2), null);

        var keys = new List<string>();
        for (var i = 0; i < 80 && !(keys.Contains("zero") && keys.Contains("normal")); i++)
        {
            VirtualTime.Advance(TimeSpan.FromMilliseconds(50) + TimeSpan.FromTicks(7));
            await Task.Delay(25, Ct);
            while (region.HasMessages)
                keys.Add((await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct)).Key.Name);
        }

        Assert.Contains("zero", keys);
        Assert.Contains("normal", keys);
    }
}
