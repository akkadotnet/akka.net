using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;
using Npgsql;

namespace Akka.Reminders.Benchmarks;

// Scheduler throughput harness: run the identical source on each revision being compared.
public sealed class SchedulerThroughput : SqlReminderBenchmarkBase
{
    public static async Task RunAsync(string[] args)
    {
        var scenario = args[1];
        var count = int.Parse(args[2]);
        var repeats = int.Parse(args[3]);
        var extraLatencyMs = args.Length > 4 ? int.Parse(args[4]) : 0;
        if (scenario is not ("one-off" or "due-recurring" or "early-recurring" or "recurring-retry"))
            throw new ArgumentException($"Unknown scheduler scenario: {scenario}");
        if (count < 1 || repeats < 1 || extraLatencyMs < 0)
            throw new ArgumentOutOfRangeException(nameof(args), "Count and repeats must be positive, and latency nonnegative.");
        var bench = new SchedulerThroughput();
        await bench.GlobalSetup();
        using var system = ActorSystem.Create("scheduler-throughput", ConfigurationFactory.ParseString("akka.loglevel = OFF\nakka.stdout-loglevel = OFF"));
        try
        {
            for (var run = 0; run < repeats + 1; run++)
            {
                await bench.ResetReminders();
                var clock = new FixedClock();
                var resolver = new CountingResolver(system.DeadLetters);
                // The optional conditional-mutation marker exists only on newer revisions.
                // Forward it when present so the same harness also builds against the dev baseline.
                var storageInterface = typeof(IReminderStorage).Assembly.GetType(
                    "Akka.Reminders.Storage.IConditionalReminderMutationStorage") ?? typeof(IReminderStorage);
                var storage = (IReminderStorage)DispatchProxy.Create(storageInterface, typeof(CountingStorage));
                var proxy = (CountingStorage)(object)storage;
                proxy.Inner = bench.Storage;
                proxy.ExtraLatencyMs = extraLatencyMs;
                var settings = new ReminderSettings
                {
                    MaxSlippage = TimeSpan.FromSeconds(5),
                    MaxBatchSize = 1000,
                    DeliveryCommitChunkSize = 100,
                    AckTimeout = TimeSpan.FromHours(2),
                    StorageTimeout = TimeSpan.FromMinutes(5)
                };
                var type = typeof(ReminderSettings).Assembly.GetType("Akka.Reminders.ReminderScheduler", true)!;
                var actor = system.ActorOf(Props.Create(type, settings, resolver, storage, clock));
                var query = new ReminderProtocol.GetReminders(CreateEntity(0));
                await actor.Ask<ReminderProtocol.RemindersForEntity>(query, TimeSpan.FromSeconds(30));
                var due = scenario == "early-recurring" ? clock.Now.AddSeconds(2) : clock.Now;
                if (scenario == "recurring-retry") due = clock.Now.AddMinutes(-1);
                await bench.PopulateReminders(count, due);
                if (scenario != "one-off")
                {
                    await using var conn = new NpgsqlConnection(ConnectionString);
                    await conn.OpenAsync();
                    await using var command = conn.CreateCommand();
                    command.CommandText = "UPDATE reminders.scheduled_reminders SET repeat_interval_ticks = @interval, delivery_deadline_utc = due_time_utc + interval '1 hour', when_utc = @when, attempt_count = @attempt";
                    command.Parameters.AddWithValue("interval", TimeSpan.FromHours(1).Ticks);
                    command.Parameters.AddWithValue("when", scenario == "recurring-retry" ? clock.Now : due);
                    command.Parameters.AddWithValue("attempt", scenario == "recurring-retry" ? 1 : 0);
                    await command.ExecuteNonQueryAsync();
                    if (scenario == "recurring-retry")
                    {
                        var next = Enumerable.Range(0, count).Select(i => CreateReminder(i, due.AddHours(1), repeatInterval: TimeSpan.FromHours(1), deliveryDeadlineUtc: due.AddHours(2))).ToList();
                        if (!await bench.Storage.UpsertReminderOccurrencesAsync(next)) throw new InvalidOperationException("Successor setup failed");
                    }
                }
                proxy.StatusReads = 0;
                proxy.Fetches = 0;
                proxy.Commits = 0;
                proxy.Overviews = 0;
                var fetchType = type.GetNestedType("FetchReminders", BindingFlags.NonPublic)!;
                var fetch = fetchType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
                var watch = Stopwatch.StartNew();
                actor.Tell(fetch);
                // Same sender queues this behind Fetch; RunTask suspends the mailbox until the pass ends.
                var result = await actor.Ask<ReminderProtocol.RemindersForEntity>(query, TimeSpan.FromMinutes(5));
                watch.Stop();
                if (result.ResponseCode != FetchRemindersResponseCode.Success || resolver.Deliveries != count)
                    throw new InvalidOperationException($"Expected {count} deliveries, got {resolver.Deliveries}; query {result.ResponseCode}");
                await actor.GracefulStop(TimeSpan.FromSeconds(30));
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    scenario, count, run, warmup = run == 0, extraLatencyMs,
                    elapsedMs = watch.Elapsed.TotalMilliseconds,
                    statusReads = proxy.StatusReads, fetches = proxy.Fetches,
                    commits = proxy.Commits, overviews = proxy.Overviews,
                    deliveries = resolver.Deliveries
                }));
            }
        }
        finally
        {
            await system.Terminate();
            await bench.GlobalCleanup();
        }
    }

    private sealed class FixedClock : ITimeProvider
    {
        public DateTimeOffset Now { get; } = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        public TimeSpan MonotonicClock => TimeSpan.Zero;
        public TimeSpan HighResMonotonicClock => TimeSpan.Zero;
    }

    private sealed class CountingResolver(IActorRef target) : IShardRegionResolver
    {
        public int Deliveries { get; private set; }
        public IActorRef TryResolve(ReminderEntity entity) => target;
        public void DeliverReminder(ReminderEntity entity, ReminderEnvelope envelope, IActorRef? sender = null) => Deliveries++;
    }
}

public class CountingStorage : DispatchProxy
{
    public IReminderStorage Inner { get; set; } = null!;
    public int ExtraLatencyMs { get; set; }
    public int StatusReads;
    public int Fetches;
    public int Commits;
    public int Overviews;

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case nameof(IReminderStorage.GetReminderOccurrenceStatusAsync):
                StatusReads++;
                return ReadStatusAsync(args!);
            case nameof(IReminderStorage.GetNextRemindersAsync): Fetches++; break;
            case nameof(IReminderStorage.CommitReminderMutationsAsync): Commits++; break;
            case nameof(IReminderStorage.GetRemindersOverviewAsync): Overviews++; break;
        }
        return method.Invoke(Inner, args);
    }

    private async Task<ReminderOccurrenceStatus?> ReadStatusAsync(object?[] args)
    {
        var ct = (CancellationToken)args[3]!;
        // Intentional simulated transport latency, outside the SQL implementation.
        if (ExtraLatencyMs > 0) await Task.Delay(ExtraLatencyMs, ct);
        return await Inner.GetReminderOccurrenceStatusAsync((ReminderEntity)args[0]!, (ReminderKey)args[1]!, (DateTimeOffset)args[2]!, ct);
    }
}
