using Akka.Reminders.Storage;
using BenchmarkDotNet.Attributes;

namespace Akka.Reminders.Benchmarks;

/// <summary>
/// Benchmarks the scheduler's atomic storage commit path for mixed reminder mutations.
/// </summary>
[Config(typeof(ReminderBenchmarkConfig))]
public class ReminderMutationBenchmarks : SqlReminderBenchmarkBase
{
    private ReminderMutationBatch _mutationBatch = ReminderMutationBatch.Empty;

    [Params(1000, 5000)]
    public int ReminderCount { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        var now = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var dueAt = now.AddMinutes(-5);
        var completedAt = now;
        var ackDeadline = now.AddMinutes(5);
        var nextDueAt = now.AddMinutes(10);
        var deliveryDeadline = nextDueAt.AddMinutes(5);

        var completedCount = ReminderCount / 3;
        var awaitingAckCount = ReminderCount / 3;
        var upsertCount = ReminderCount - completedCount - awaitingAckCount;
        var existingReminderCount = completedCount + awaitingAckCount;

        PopulateReminders(existingReminderCount, dueAt).GetAwaiter().GetResult();

        var completedReminders = Enumerable.Range(0, completedCount)
            .Select(i => CreateCompletedReminder(i, dueAt, completedAt))
            .ToList();

        var awaitingAckReminders = Enumerable.Range(completedCount, awaitingAckCount)
            .Select(i => CreateAwaitingAckReminder(i, dueAt, completedAt, ackDeadline))
            .ToList();

        var pendingUpserts = Enumerable.Range(existingReminderCount, upsertCount)
            .Select(i => CreateReminder(
                i,
                nextDueAt,
                dueTimeUtc: nextDueAt,
                maxDeliveryWindow: TimeSpan.FromMinutes(5),
                deliveryDeadlineUtc: deliveryDeadline))
            .ToList();

        _mutationBatch = new ReminderMutationBatch(pendingUpserts, completedReminders, awaitingAckReminders);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        ResetReminders().GetAwaiter().GetResult();
    }

    [Benchmark(OperationsPerInvoke = 1)]
    public Task<bool> CommitReminderMutations()
        => Storage.CommitReminderMutationsAsync(_mutationBatch);
}
