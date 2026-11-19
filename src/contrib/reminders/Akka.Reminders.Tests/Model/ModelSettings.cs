namespace Akka.Reminders.Tests.Model;

/// <summary>
/// Scheduler settings for one run, in milliseconds. <c>AckGroup</c> is not a scheduler setting: it is how the
/// harness splits the acks of one round. It sends them in groups of this many, holding the scheduler while it
/// queues each group (see <see cref="SchedulerHold"/>), so the scheduler flushes a group together; 0 sends the
/// whole round as one group. Together with <c>AckFlushBatch</c> this decides which acks share a storage call.
/// </summary>
public sealed record ModelSettings(int MaxSlippageMs, int AckTimeoutMs, int BackoffBaseMs, int MaxBackoffMs, int MaxAttempts,
    int MaxBatch = 1000, int Chunk = 100, int AckFlushBatch = 256, int AckGroup = 0)
{
    public static ModelSettings Default { get; } = new(1000, 10_000, 1000, 10_000, 3);

    public static readonly TimeSpan StorageTimeout = TimeSpan.FromSeconds(5);

    public TimeSpan MaxSlippage => TimeSpan.FromMilliseconds(MaxSlippageMs);
    public TimeSpan AckTimeout => TimeSpan.FromMilliseconds(AckTimeoutMs);
    public TimeSpan MaxBackoff => TimeSpan.FromMilliseconds(MaxBackoffMs);

    /// <summary>
    /// Test observation window after storage or region trouble: allowance for an ack timeout, retry
    /// backoff, and a recovery tick. Later faults or stalls extend the window. This is not a production
    /// wall-clock delivery guarantee (docs: Storage read failure and automatic recovery).
    /// </summary>
    public TimeSpan RecoveryTime => AckTimeout + MaxBackoff + StorageTimeout * 2;

    /// <summary>Backoff before retry number <paramref name="attemptCount"/> + 1 (same formula as the scheduler).</summary>
    public TimeSpan Backoff(int attemptCount) => TimeSpan.FromMilliseconds(Math.Min(BackoffBaseMs * Math.Pow(2, attemptCount), MaxBackoffMs));

    public ReminderSettings ToReminderSettings() => new()
    {
        MaxSlippage = MaxSlippage,
        StorageTimeout = StorageTimeout,
        MaxDeliveryAttempts = MaxAttempts,
        RetryBackoffBase = TimeSpan.FromMilliseconds(BackoffBaseMs),
        MaxRetryBackoff = MaxBackoff,
        AckTimeout = AckTimeout,
        MaxBatchSize = MaxBatch,
        DeliveryCommitChunkSize = Chunk,
        AckFlushBatchSize = AckFlushBatch,
        PruneOlderThan = TimeSpan.FromDays(3650),
    };

    public override string ToString() =>
        $"new ModelSettings({MaxSlippageMs}, {AckTimeoutMs}, {BackoffBaseMs}, {MaxBackoffMs}, {MaxAttempts}, {MaxBatch}, {Chunk}, {AckFlushBatch}, {AckGroup})";
}
