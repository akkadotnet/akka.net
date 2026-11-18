using Akka.Reminders.Storage;
using BenchmarkDotNet.Attributes;

namespace Akka.Reminders.Benchmarks;

/// <summary>
/// Benchmarks the batched acknowledgement write path for reminders that are already awaiting ack.
/// </summary>
[Config(typeof(ReminderBenchmarkConfig))]
public class ReminderAcknowledgementBenchmarks : SqlReminderBenchmarkBase
{
    private IReadOnlyList<ReminderAcknowledgement> _acknowledgements = [];

    [Params(1000, 5000)]
    public int ReminderCount { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        var dueAt = TruncateToMicroseconds(DateTimeOffset.UtcNow.AddMinutes(-5));
        var deliveredAt = dueAt.AddSeconds(1);
        var ackDeadline = dueAt.AddMinutes(10);

        PopulateReminders(ReminderCount, dueAt).GetAwaiter().GetResult();

        var awaitingAckReminders = Enumerable.Range(0, ReminderCount)
            .Select(i => CreateAwaitingAckReminder(i, dueAt, deliveredAt, ackDeadline))
            .ToList();

        if (!Storage.MarkRemindersAsAwaitingAckAsync(awaitingAckReminders).GetAwaiter().GetResult())
            throw new InvalidOperationException("Failed to prepare awaiting-ack reminders for benchmark.");

        _acknowledgements = Enumerable.Range(0, ReminderCount)
            .Select(i => CreateAcknowledgement(i, dueAt, deliveredAt.AddSeconds(1)))
            .ToList();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        ResetReminders().GetAwaiter().GetResult();
    }

    [Benchmark(OperationsPerInvoke = 1)]
    public async Task<int> AcknowledgeAllReminders()
    {
        var results = await Storage.AcknowledgeRemindersAsync(_acknowledgements);
        return results.Count(r => r.Success);
    }
}
