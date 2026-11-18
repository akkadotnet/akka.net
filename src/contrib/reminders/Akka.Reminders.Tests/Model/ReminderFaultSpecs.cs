using CsCheck;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Akka.Reminders.Tests.Model;

/// <summary>
/// CsCheck generates and shrinks command lists. The application runs each command; the model checks
/// its observations and outstanding obligations. Fault outcomes are observed, not guessed in advance.
/// CsCheck_Iter, CsCheck_Time, CsCheck_Seed and CsCheck_Threads are the library's own controls.
/// </summary>
public sealed class ReminderFaultSpecs(ITestOutputHelper output)
{
    /// <summary>Fixed regressions also wait for recovery, as they did before the generated runner changed.</summary>
    public static Task<History> ReplayAsync(ModelSettings settings, params Op[] commands) =>
        RunAsync(settings, InMemoryModelStorageFactory.Instance, commands.Append(new HealAndWait()));

    private static async Task<History> RunAsync(ModelSettings settings, IModelStorageFactory storage, IEnumerable<Op> commands, Recipient recipient = Recipient.Ack)
    {
        var app = new ReminderApp(settings, recipient, storage);
        var model = new ReminderModel(settings);
        try
        {
            foreach (var command in commands)
            {
                await command.Run(app);
                model.Observe(app.Journal.Read());
            }

            return app.Journal.Read();
        }
        finally
        {
            app.Stop();
            await ReminderApp.DisposeStoppedAsync();
        }
    }

    private Task SampleAsync(IModelStorageFactory storage, long iterations, bool trouble = true)
    {
        // CsCheck owns argument/sequence shrinking and seed replay; the callback runs the input.
        var cases = SettingsGen.Select(Commands(trouble).Array);
        var iter = Environment.GetEnvironmentVariable("CsCheck_Iter") is null &&
                   Environment.GetEnvironmentVariable("CsCheck_Time") is null ? iterations : -1;
        return cases.SampleAsync((settings, commands) => RunAsync(settings, storage, commands),
            iter: iter, writeLine: output.WriteLine,
            print: sample => $"{sample.Item1} on {storage.Name}\nCommands: {Check.Print(sample.Item2)}");
    }

    // These are individual commands, not scenarios. The weights and argument generators retain the
    // existing exploration: arbitrary ordering, replacement/cancellation of absent keys, late acks,
    // and combinations of faults. Even a no-op command can expose an incorrect reply.
    private static Gen<Op> Commands(bool trouble)
    {
        (int Weight, IGen<Op> Generator)[] healthy =
        [
            (10, ScheduleOnceGen),
            (12, ScheduleRecurringGen),
            (5, Gen.Select(Entity, Key, (e, k) => new Cancel(e, k))),
            (2, Entity.Select(e => new CancelAll(e))),
            (4, Entity.Select(e => new ListReminders(e))),
            (30, TickGen),
            (6, LagGen),
            (3, Gen.Const(new Restart())),
            (6, Gen.Select(Entity, Gen.Enum<Recipient>(), (e, mode) => new SetRecipient(e, mode))),
            (3, Gen.Const(new AckOutstanding())),
            (1, Gen.Const(new HealAndWait())),
        ];
        return Gen.Frequency(trouble ? [.. healthy,
            (4, Gen.Select(Gen.Int[0, ReminderApp.Regions - 1], Gen.Bool, (r, up) => new SetRegion(r, up))),
            (8, FaultGen)] : healthy);
    }

    [Fact(DisplayName = "Should_MatchTheModelAndKeepEveryRule_When_RunningGeneratedSequences_InMemory")]
    public Task InMemory() => SampleAsync(InMemoryModelStorageFactory.Instance, 120);

    // No storage faults and no missing shard regions: nothing loosens the model, so every delivery and
    // every reply must match it exactly.
    [Fact(DisplayName = "Should_MatchTheModelExactly_When_StorageIsHealthy_InMemory")]
    public Task InMemoryHealthy() => SampleAsync(InMemoryModelStorageFactory.Instance, 60, trouble: false);

    [Fact(DisplayName = "Should_MatchTheModel_When_HealthyRecipientsAcknowledgeManually")]
    public Task InMemoryManualAcknowledgements()
    {
        // The old healthy model's five commands and timing ranges, checked by the shared model.
        // Recipients stay silent until an explicit per-entity acknowledgement command is generated.
        var settings = new ModelSettings(1000, 3000, 100, 100, 10);
        var delay = Gen.Int[1, 10].Select(seconds => seconds * 1000);
        var interval = Gen.OneOfConst(5000, 10_000, 20_000);
        var commands = Gen.Frequency<Op>(
            (1, Gen.Select(Entity, Key, delay, (e, k, due) => new ScheduleOnce(e, k, due, null))),
            (1, Gen.Select(Entity, Key, delay, interval, (e, k, due, every) => new ScheduleRecurring(e, k, due, every, null))),
            (1, Gen.Select(Entity, Key, (e, k) => new Cancel(e, k))),
            (1, delay.Select(ms => new Tick(ms))),
            (1, Entity.Select(e => new AckOutstanding(e))));
        return commands.Array.SampleAsync(async trace =>
        {
            var history = await RunAsync(settings, InMemoryModelStorageFactory.Instance, trace, Recipient.Ignore);
            RequireHealthyDeliveries(history);
        }, writeLine: output.WriteLine);
    }

    // This profile has no faults, stalls, past anchors or automatic acknowledgements. Keep its
    // independent per-registration check: a same-due replacement cannot borrow the old payload's delivery.
    internal static void RequireHealthyDeliveries(History history)
    {
        var now = history.All.LastOrDefault()?.At ?? VirtualClock.Origin;
        var onTime = history.Deliveries.Where(d => d.At <= d.Due).Select(d => (d.Id, d.Due)).ToHashSet();
        foreach (var call in history.Calls)
        {
            var until = history.EndOf(call)?.At ?? now;
            for (var due = call.FirstDue; due <= until; due += call.Interval.GetValueOrDefault())
            {
                if (!onTime.Contains((call.Id, due)))
                    throw new ModelViolation($"HealthyDelivery: reminder {call.Id} due {Journal.T(due)} did not arrive on time\n{history.Timeline()}");
                if (call.Interval is null)
                    break;
            }
        }
    }

    [Fact(DisplayName = "Should_MatchTheModelAndKeepEveryRule_When_RunningGeneratedSequences_Sqlite")]
    public async Task Sqlite()
    {
        var factory = new SqliteModelStorageFactory();
        try
        {
            await SampleAsync(factory, 30);
        }
        finally
        {
            factory.Cleanup();
        }
    }

    [Fact(DisplayName = "Should_MatchTheModelAndKeepEveryRule_When_RunningGeneratedSequences_PostgreSql")]
    [Trait("Category", "ModelSql")]
    public async Task PostgreSql()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("REMINDERS_CSCHECK_SQL") == "1", "Set REMINDERS_CSCHECK_SQL=1 to run the model against PostgreSQL.");
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync(TestContext.Current.CancellationToken);
        await SampleAsync(new PostgreSqlModelStorageFactory(container.GetConnectionString()), 10);
    }

    [Fact(DisplayName = "Should_MatchTheModelAndKeepEveryRule_When_RunningGeneratedSequences_SqlServer")]
    [Trait("Category", "ModelSql")]
    public async Task SqlServer()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("REMINDERS_CSCHECK_SQL") == "1", "Set REMINDERS_CSCHECK_SQL=1 to run the model against SQL Server.");
        await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").WithPassword("yourStrong(!)Password").Build();
        await container.StartAsync(TestContext.Current.CancellationToken);
        await SampleAsync(new SqlServerModelStorageFactory(container.GetConnectionString()), 10);
    }

    // Argument domains are unchanged: near-boundary values plus broad times, settings and faults.

    private static Gen<int> Entity => Gen.Int[0, ReminderApp.Entities - 1];
    private static Gen<int> Key => Gen.Int[0, ReminderApp.Keys - 1];

    // Due offsets cluster around "now" (where the interesting boundaries are) but reach hours away.
    private static Gen<int> DueOffset => Gen.Frequency(
        (6, Gen.Int[-15_000, 30_000]),
        (2, Gen.Int[-3_600_000, 3_600_000]),
        (1, Gen.OneOfConst(0, -1, 1, -1_000, 1_000)));

    private static Gen<int> Interval => Gen.Frequency(
        (6, Gen.OneOfConst(1_000, 2_000, 3_000, 5_000, 7_000, 10_000)),
        (3, Gen.OneOfConst(15_000, 30_000, 60_000, 300_000)),
        (1, Gen.OneOfConst(1_800_000, 3_600_000)),
        (1, Gen.Int[500, 20_000]));

    private static Gen<int?> OneOffWindow => Gen.Frequency(
        (3, Gen.Const((int?)null)),
        (3, Gen.Int[1, 60_000].Select(i => (int?)i)),
        (1, Gen.OneOfConst<int?>(1_000, 3_600_000)));

    private static Gen<int?> RecurringWindow(int interval) => Gen.Frequency(
        (3, Gen.Const((int?)null)),
        (3, Gen.Int[1, Math.Max(1, interval - 1)].Select(i => (int?)i)),
        (1, Gen.Int[interval, interval * 3].Select(i => (int?)i)));

    private static Gen<ScheduleOnce> ScheduleOnceGen =>
        from e in Entity
        from k in Key
        from due in DueOffset
        from w in OneOffWindow
        select new ScheduleOnce(e, k, due, w);

    private static Gen<ScheduleRecurring> ScheduleRecurringGen =>
        from e in Entity
        from k in Key
        from interval in Interval
        from anchor in Gen.Frequency(
            (5, DueOffset),
            (2, Gen.Int[-20, 20].Select(m => m * interval)),
            (1, Gen.Int[-3, 3].Select(m => m * interval + 1)))
        from w in RecurringWindow(interval)
        select new ScheduleRecurring(e, k, anchor, interval, w);

    private static Gen<Tick> TickGen => Gen.Frequency(
        (5, Gen.Int[0, 5_000]),
        (3, Gen.Int[5_000, 30_000]),
        (1, Gen.Int[30_000, 180_000])).Select(ms => new Tick(ms));

    private static Gen<Lag> LagGen => Gen.Frequency(
        (3, Gen.Int[1_000, 30_000]),
        (2, Gen.Int[30_000, 600_000]),
        (1, Gen.Int[600_000, 6 * 3_600_000])).Select(ms => new Lag(ms));

    private static Gen<InjectFault> FaultGen =>
        from call in Gen.Frequency(
            (8, Gen.Const(StorageCall.Commit)),
            (3, Gen.Const(StorageCall.Fetch)),
            (2, Gen.Const(StorageCall.TimedOutAcks)),
            (2, Gen.Const(StorageCall.Ack)),
            (1, Gen.Const(StorageCall.Expire)),
            (1, Gen.Const(StorageCall.Overview)),
            (1, Gen.Const(StorageCall.NextAckDeadline)),
            (1, Gen.Const(StorageCall.GetAwaitingAck)),
            (1, Gen.Const(StorageCall.Schedule)),
            (1, Gen.Const(StorageCall.Cancel)))
        from kind in Gen.Frequency(
            (4, Gen.Const(FaultKind.Fail)),
            (4, Gen.Const(FaultKind.Slow)),
            (2, Gen.Const(FaultKind.Timeout)),
            (2, Gen.Const(FaultKind.AppliedThenFail)))
        from count in Gen.Int[1, 3]
        from delay in Gen.Frequency(
            (3, Gen.Int[1, 2_000]),
            (2, Gen.Int[2_000, 15_000]),
            (1, Gen.Int[15_000, 120_000]))
        from fire in Gen.Bool
        select new InjectFault(call, kind, count, kind == FaultKind.Slow ? delay : 0, fire);

    private static Gen<ModelSettings> SettingsGen =>
        from slip in Gen.OneOfConst(0, 1, 500, 1_000, 5_000)
        from ack in Gen.OneOfConst(500, 2_000, 5_000, 10_000, 30_000)
        from backoff in Gen.OneOfConst(100, 1_000, 5_000, 60_000)
        from maxBackoff in Gen.OneOfConst(1_000, 10_000, 600_000)
        from attempts in Gen.OneOfConst(1, 2, 3, 5, 10)
        from batch in Gen.Frequency((2, Gen.Int[1, 4]), (1, Gen.Const(1000)))
        from chunk in Gen.Frequency((2, Gen.Int[1, 3]), (1, Gen.Const(100)))
        from ackBatch in Gen.OneOfConst(1, 2, 256)
        select new ModelSettings(slip, ack, backoff, Math.Max(maxBackoff, backoff), attempts, batch, chunk, ackBatch);
}
