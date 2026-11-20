using Akka.Reminders.Storage;
using BenchmarkDotNet.Attributes;

namespace Akka.Reminders.Benchmarks;

/// <summary>
/// Benchmarks the critical path: fetching due reminders in batches and marking them complete.
/// Uses a real PostgreSQL instance to measure actual I/O performance.
/// </summary>
[Config(typeof(ReminderBenchmarkConfig))]
public class ReminderStorageBenchmarks : SqlReminderBenchmarkBase
{
    [Params(1000, 5000, 25000)]
    public int ReminderCount { get; set; }

    [Params(1000, 5000)]
    public int MaxBatchSize { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        // Populate N reminders all due now
        PopulateReminders(ReminderCount, DateTimeOffset.UtcNow.AddMinutes(-1)).GetAwaiter().GetResult();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        ResetReminders().GetAwaiter().GetResult();
    }

    [Benchmark(OperationsPerInvoke = 1)]
    public async Task<int> FetchAndCompleteAllReminders()
    {
        var now = DateTimeOffset.UtcNow;
        var deadline = now.AddMinutes(1);
        var totalProcessed = 0;

        while (true)
        {
            var batch = await Storage.GetNextRemindersAsync(deadline, now, maxCount: new ReminderBatchSize(MaxBatchSize));

            if (batch.Reminders.Count == 0)
                break;

            var completed = batch.Reminders
                .Select(r => new CompletedReminder(r.Entity, r.Key, r.DueTimeUtc, now))
                .ToList();

            await Storage.MarkRemindersAsCompletedAsync(completed);
            totalProcessed += completed.Count;
        }

        return totalProcessed;
    }
}
