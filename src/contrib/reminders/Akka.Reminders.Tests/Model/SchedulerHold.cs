using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Model;

/// <summary>
/// Keeps the scheduler from reading its mailbox while the harness queues a group of messages behind it.
/// <para>
/// The scheduler runs each storage call in <c>RunTask</c>, which suspends its mailbox until the task ends.
/// The hold is such a call: a status query for a harness-only key, whose storage call (see
/// <see cref="FaultyRecordingStorage"/>) does not return until <see cref="Release"/>. What the harness
/// sends meanwhile waits in the mailbox, and the scheduler reads it in send order once the call returns.
/// Without a hold, the scheduler can read the first of several acks and queue its flush before the harness
/// has sent the second, so which acks share a storage call depends on timing.
/// </para>
/// <para>
/// Only user messages wait. System messages (a stop, and the task's own continuation, which is how the
/// scheduler wakes up after <see cref="Release"/>) still run on a suspended mailbox. The hold never blocks
/// a thread: the held call is an <c>await</c>.
/// </para>
/// </summary>
internal sealed class SchedulerHold
{
    private readonly TaskCompletionSource<ReminderOccurrenceStatus?> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the scheduler is inside the held storage call, which means its mailbox is suspended.</summary>
    public Task Entered => _entered.Task;

    /// <summary>The scheduler's answer to the hold query. It comes after <see cref="Release"/>.</summary>
    public Task Reply { get; internal set; } = Task.CompletedTask;

    /// <summary>Lets the held storage call return, so the scheduler reads what the harness queued. Safe to call twice.</summary>
    public void Release() => _released.TrySetResult(null);

    /// <summary>Called by the storage wrapper when the scheduler makes the held call.</summary>
    internal Task<ReminderOccurrenceStatus?> Enter()
    {
        _entered.TrySetResult(true);
        return _released.Task;
    }
}
