#nullable enable

using Akka.Actor;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

public partial class ReminderSchedulerTimingSpecs
{
    [Theory(DisplayName = "Should_RetryBeyondAttemptLimitAcrossRestart_When_UntilAcknowledged")]
    [InlineData("inmem", "nack")]
    [InlineData("inmem", "timeout")]
    [InlineData("inmem", "missing-region")]
    [InlineData("sqlite", "nack")]
    [InlineData("sqlite", "timeout")]
    [InlineData("sqlite", "missing-region")]
    public async Task Should_RetryBeyondAttemptLimitAcrossRestart_When_UntilAcknowledged(string kind, string failure)
    {
        var connectionString = kind == "sqlite" ? SqliteConnectionString() : null;
        IReminderStorage storage = connectionString is null ? new InMemoryReminderStorage()
            : new SqliteReminderStorage(SqliteReminderStorageSettings.Create(connectionString), Sys);
        var settings = DedicatedSettings(2, TimeSpan.FromSeconds(1)) with
        {
            MaxSlippage = TimeSpan.Zero,
            MaxRetryBackoff = TimeSpan.FromSeconds(2)
        };
        var (scheduler, entity, region) = Setup("unlimited", storage, settings);
        IReminderClient client = new ReminderClient(scheduler, entity);
        var key = new ReminderKey("work");
        var due = VirtualTime.Now.AddSeconds(1);
        var scheduled = await client.ScheduleSingleReminderAsync(key, due, "payload",
            ReminderRetryPolicy.UntilAcknowledged, ct: Ct);
        Assert.Equal(ReminderScheduleResponseCode.Success, scheduled.ResponseCode);
        Assert.Equal(ReminderRetryPolicy.UntilAcknowledged, scheduled.OriginalCommand.RetryPolicy);
        if (failure == "missing-region")
            Assert.True(_resolver.UnregisterShardRegion(entity.ShardRegionName));
        var nextAt = due;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            VirtualTime.Advance(nextAt - VirtualTime.Now + TimeSpan.FromTicks(1));
            if (failure != "missing-region")
            {
                var envelope = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
                Assert.Equal(due, envelope.DueTimeUtc);
                // Even after the configured last attempt, this delivery still has an ack-timeout deadline.
                Assert.Equal(VirtualTime.Now.Add(settings.AckTimeout), envelope.Deadline.UtcDateTime);
                if (failure == "nack")
                {
                    var nack = await client.NackAsync(envelope, "try later", Ct);
                    Assert.Equal(ReminderNackResponseCode.RetryScheduled, nack.ResponseCode);
                }
                else
                    VirtualTime.Advance(settings.AckTimeout + TimeSpan.FromTicks(1));
            }
            await AwaitAssertAsync(async () =>
            {
                var status = await StatusAsync(scheduler, entity, key, due);
                Assert.Equal(ReminderCompletionStatus.Pending, status?.CompletionStatus);
                Assert.Equal(attempt + 1, status?.AttemptCount);
            }, ReplyTimeout, TimeSpan.FromMilliseconds(20), cancellationToken: Ct);
            var pending = Assert.Single((await client.ListRemindersAsync(Ct)).Reminders);
            Assert.Equal(ReminderRetryPolicy.UntilAcknowledged, pending.RetryPolicy);
            Assert.Equal(TimeSpan.FromSeconds(attempt == 0 ? 1 : 2), pending.When - VirtualTime.Now);
            nextAt = pending.When;

            if (attempt == 1)
            {
                await WatchAsync(scheduler);
                Sys.Stop(scheduler);
                await ExpectTerminatedAsync(scheduler, ReplyTimeout, cancellationToken: Ct);
                if (connectionString is not null)
                    storage = new SqliteReminderStorage(SqliteReminderStorageSettings.Create(connectionString), Sys);
                scheduler = StartScheduler(settings, storage, "unlimited-restarted");
                client = new ReminderClient(scheduler, entity);
                Assert.Equal(2, (await StatusAsync(scheduler, entity, key, due))?.AttemptCount);
            }
        }

        if (failure == "missing-region")
        {
            Assert.False(region.HasMessages);
            _resolver.RegisterShardRegion(entity.ShardRegionName, region);
        }
        VirtualTime.Advance(nextAt - VirtualTime.Now + TimeSpan.FromTicks(1));
        var recovered = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal(due, recovered.DueTimeUtc);
        Assert.Equal(ReminderAckResponseCode.Success, (await client.AckAsync(recovered, Ct)).ResponseCode);
        Assert.Equal(ReminderCompletionStatus.Delivered, (await StatusAsync(scheduler, entity, key, due))?.CompletionStatus);
        VirtualTime.Advance(TimeSpan.FromMinutes(1));
        await StatusAsync(scheduler, entity, key, due);
        Assert.False(region.HasMessages);
    }

    [Theory(DisplayName = "Should_RespectCancellationAndDeliveryWindows_When_UntilAcknowledged")]
    [InlineData("cancel")]
    [InlineData("window")]
    [InlineData("default-window")]
    public async Task Should_RespectCancellationAndDeliveryWindows_When_UntilAcknowledged(string ending)
    {
        var settings = DedicatedSettings(1, TimeSpan.FromSeconds(1)) with
        {
            MaxSlippage = TimeSpan.Zero,
            DefaultMaxDeliveryWindow = ending == "default-window" ? TimeSpan.FromSeconds(4) : null
        };
        var (scheduler, entity, region) = Setup("unlimited-end", settings: settings);
        IReminderClient client = new ReminderClient(scheduler, entity);
        var due = VirtualTime.Now.AddSeconds(1);
        var key = new ReminderKey("work");
        var scheduled = await client.ScheduleSingleReminderAsync(key, due, "payload", ReminderRetryPolicy.UntilAcknowledged,
            maxDeliveryWindow: ending == "window" ? TimeSpan.FromSeconds(4) : null, ct: Ct);
        Assert.Equal(ReminderScheduleResponseCode.Success, scheduled.ResponseCode);
        VirtualTime.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        if (ending == "cancel")
            Assert.Equal(ReminderCancelResponseCode.Success, (await client.CancelReminderAsync(key, Ct)).ResponseCode);
        VirtualTime.Advance(TimeSpan.FromSeconds(10));
        await AwaitStatusAsync(scheduler, entity, key, due,
            ending == "cancel" ? ReminderCompletionStatus.Cancelled : ReminderCompletionStatus.Expired);
        Assert.False(region.HasMessages);
    }

    [Theory(DisplayName = "Should_PreserveOldUnackedWorkAndDeliverCurrentWork_When_PruningRuns")]
    [InlineData("inmem")]
    [InlineData("sqlite")]
    public async Task Should_PreserveOldUnackedWorkAndDeliverCurrentWork_When_PruningRuns(string kind)
    {
        var storage = CreateStorage(kind);
        var t0 = VirtualTime.Now;
        var entity = new ReminderEntity("old-unlimited-region", "e1");
        var old = new ScheduledReminder(entity, new ReminderKey("old"), t0, "old", AttemptCount: int.MaxValue,
            OccurrenceDueTimeUtc: t0.AddYears(-1)) { RetryPolicy = ReminderRetryPolicy.UntilAcknowledged };
        await storage.UpsertReminderOccurrencesAsync([old], Ct);
        var settings = DedicatedSettings(1, TimeSpan.FromSeconds(1)) with
        {
            MaxSlippage = TimeSpan.Zero,
            MaxRetryBackoff = TimeSpan.FromSeconds(2),
            PruneInterval = TimeSpan.FromSeconds(1),
            PruneOlderThan = TimeSpan.FromDays(1)
        };
        var (scheduler, _, region) = Setup("old-unlimited", storage, settings);
        IReminderClient client = new ReminderClient(scheduler, entity);
        var currentDue = t0.AddSeconds(2);
        Assert.Equal(ReminderScheduleResponseCode.Success,
            (await client.ScheduleSingleReminderAsync(new ReminderKey("current"), currentDue, "current", ct: Ct)).ResponseCode);
        VirtualTime.Advance(TimeSpan.FromTicks(1));
        var oldEnvelope = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal(old.DueTimeUtc, oldEnvelope.DueTimeUtc);
        Assert.True(await storage.CleanUpCompletedRemindersAsync(t0.AddDays(1), Ct));
        Assert.Equal(ReminderCompletionStatus.AwaitingAck, (await StatusAsync(scheduler, entity, old.Key, old.DueTimeUtc))?.CompletionStatus);
        VirtualTime.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        await AwaitStatusAsync(scheduler, entity, old.Key, old.DueTimeUtc, ReminderCompletionStatus.Pending);
        var pending = Assert.Single((await client.ListRemindersAsync(Ct)).Reminders, row => row.Key == old.Key);
        Assert.Equal(int.MaxValue, pending.AttemptCount);
        Assert.Equal(TimeSpan.FromSeconds(2), pending.When - VirtualTime.Now);
        Assert.True(await storage.CleanUpCompletedRemindersAsync(t0.AddDays(1), Ct));
        VirtualTime.Advance(currentDue - VirtualTime.Now + TimeSpan.FromTicks(1));
        var current = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal("current", current.Message);
        Assert.Equal(ReminderAckResponseCode.Success, (await client.AckAsync(current, Ct)).ResponseCode);
        VirtualTime.Advance(pending.When - VirtualTime.Now + TimeSpan.FromTicks(1));
        var retry = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal(old.DueTimeUtc, retry.DueTimeUtc);
        Assert.Equal(ReminderAckResponseCode.Success, (await client.AckAsync(retry, Ct)).ResponseCode);
        Assert.True(await storage.CleanUpCompletedRemindersAsync(VirtualTime.Now.AddDays(1), Ct));
        Assert.Null(await StatusAsync(scheduler, entity, old.Key, old.DueTimeUtc));
    }

    [Theory(DisplayName = "Should_RejectUnsupportedPolicyBeforePersistence_When_StorageOrScheduleCannotSupportIt")]
    [InlineData("legacy")]
    [InlineData("recurring")]
    [InlineData("unknown")]
    [InlineData("zero-backoff")]
    [InlineData("zero-cap")]
    public async Task Should_RejectUnsupportedPolicyBeforePersistence_When_StorageOrScheduleCannotSupportIt(string scenario)
    {
        var inner = new InMemoryReminderStorage();
        IReminderStorage storage = scenario == "legacy" ? new LegacyStorage(inner) : inner;
        var settings = DedicatedSettings() with
        {
            RetryBackoffBase = scenario == "zero-backoff" ? TimeSpan.Zero : TimeSpan.FromSeconds(1),
            MaxRetryBackoff = scenario == "zero-cap" ? TimeSpan.Zero : TimeSpan.FromSeconds(2)
        };
        var (scheduler, entity, _) = Setup("policy-rejection", storage, settings);
        var command = new ReminderProtocol.ScheduleReminder(entity, new ReminderKey("work"), VirtualTime.Now.AddSeconds(1), "payload",
            RepeatInterval: scenario == "recurring" ? TimeSpan.FromSeconds(10) : null)
        { RetryPolicy = scenario == "unknown" ? (ReminderRetryPolicy)99 : ReminderRetryPolicy.UntilAcknowledged };
        var response = await scheduler.Ask<ReminderProtocol.ReminderScheduled>(command, ReplyTimeout, Ct);
        Assert.Equal(ReminderScheduleResponseCode.Error, response.ResponseCode);
        Assert.Empty(await inner.GetRemindersForEntityAsync(entity, ct: Ct));
        // An ordinary bounded one-off still works through the same provider.
        await ScheduleAsync(scheduler, entity, new ReminderKey("bounded"), VirtualTime.Now.AddSeconds(2), null);
    }
    [Theory(DisplayName = "Should_HonorRetryTimeWithoutBlockingNewWork_When_SlippageExceedsBackoff")]
    [InlineData("inmem")]
    [InlineData("sqlite")]
    public async Task Should_HonorRetryTimeWithoutBlockingNewWork_When_SlippageExceedsBackoff(string kind)
    {
        var storage = CreateStorage(kind);
        var t0 = VirtualTime.Now;
        var dead = new ScheduledReminder(new ReminderEntity("missing-region", "e1"), new ReminderKey("retry"), t0.AddSeconds(2),
            "old", AttemptCount: 1, OccurrenceDueTimeUtc: t0.AddYears(-1)) { RetryPolicy = ReminderRetryPolicy.UntilAcknowledged };
        await storage.UpsertReminderOccurrencesAsync([dead], Ct);
        var settings = DedicatedSettings(1) with
        {
            MaxBatchSize = 1,
            MaxSlippage = TimeSpan.FromSeconds(30),
            RetryBackoffBase = TimeSpan.FromSeconds(1),
            MaxRetryBackoff = TimeSpan.FromSeconds(2)
        };
        var (scheduler, entity, region) = Setup("slippage", storage, settings);
        await ScheduleAsync(scheduler, entity, new ReminderKey("new"), t0.AddSeconds(1), null);
        VirtualTime.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        var current = await region.ExpectMsgAsync<ReminderEnvelope<string>>(ReplyTimeout, cancellationToken: Ct);
        Assert.Equal(new ReminderKey("new"), current.Key);
        Assert.Equal(1, (await StatusAsync(scheduler, dead.Entity, dead.Key, dead.DueTimeUtc))?.AttemptCount);
        VirtualTime.Advance(TimeSpan.FromSeconds(1));
        await AwaitAssertAsync(async () => Assert.Equal(2,
            (await StatusAsync(scheduler, dead.Entity, dead.Key, dead.DueTimeUtc))?.AttemptCount),
            ReplyTimeout, TimeSpan.FromMilliseconds(20), cancellationToken: Ct);
        var pending = Assert.Single(await storage.GetRemindersForEntityAsync(dead.Entity, ct: Ct));
        Assert.Equal(VirtualTime.Now.AddSeconds(2), pending.When);
    }

}
