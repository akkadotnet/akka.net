using System.Collections.Concurrent;

namespace Akka.Reminders.Storage;

/// <summary>
/// In-memory implementation of <see cref="IReminderStorage"/>.
/// </summary>
/// <remarks>
/// Thread-safe implementation using concurrent collections. Suitable for testing and single-node scenarios.
/// Not suitable for distributed scenarios as state is not shared across nodes.
/// </remarks>
public sealed class InMemoryReminderStorage : IConditionalReminderMutationStorage
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), ScheduledReminder> _pendingReminders = new();
    private readonly ConcurrentDictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), (ScheduledReminder Reminder, AwaitingAckReminder State)> _awaitingAckReminders = new();
    private readonly ConcurrentDictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), CompletedReminder> _completedReminders = new();
    private readonly ConcurrentDictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), ScheduledReminder> _completedReminderDetails = new();

    private static (ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc) ToKey(ScheduledReminder reminder)
        => (reminder.Entity, reminder.Key, reminder.DueTimeUtc);

    private static (ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc) ToKey(AwaitingAckReminder reminder)
        => (reminder.Entity, reminder.Key, reminder.DueTimeUtc);

    private static (ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc) ToKey(CompletedReminder reminder)
        => (reminder.Entity, reminder.Key, reminder.DueTimeUtc);

    /// <summary>
    /// A pending occurrence is still the scheduler's work while inside its window; a pending recurring
    /// occurrence stays visible after its deadline so the scheduler can roll the series forward.
    /// </summary>
    private static bool IsActivePending(ScheduledReminder reminder, DateTimeOffset now)
        => reminder.RepeatInterval.HasValue || !reminder.Deadline.IsExpired(now);

    /// <inheritdoc />
    public Task<ReminderProtocol.ReminderScheduled> ScheduleReminderAsync(
        ScheduledReminder reminder,
        CancellationToken ct = default)
    {
        lock (_sync)
        {
            var matchingPending = _pendingReminders.Keys
                .Where(k => k.Entity.Equals(reminder.Entity) && k.Key.Equals(reminder.Key))
                .ToList();
            foreach (var key in matchingPending)
            {
                if (_pendingReminders.TryRemove(key, out var pending))
                    _completedReminderDetails[key] = pending;
                _completedReminders[key] = new CompletedReminder(
                    key.Entity,
                    key.Key,
                    key.DueTimeUtc,
                    DateTimeOffset.UtcNow,
                    ReminderCompletionStatus.Cancelled);
            }

            var matchingAwaiting = _awaitingAckReminders.Keys
                .Where(k => k.Entity.Equals(reminder.Entity) && k.Key.Equals(reminder.Key))
                .ToList();
            foreach (var key in matchingAwaiting)
            {
                if (_awaitingAckReminders.TryRemove(key, out var awaiting))
                    _completedReminderDetails[key] = awaiting.Reminder;
                _completedReminders[key] = new CompletedReminder(
                    key.Entity,
                    key.Key,
                    key.DueTimeUtc,
                    DateTimeOffset.UtcNow,
                    ReminderCompletionStatus.Cancelled);
            }

            var reminderKey = ToKey(reminder);
            _completedReminders.TryRemove(reminderKey, out _);
            _completedReminderDetails.TryRemove(reminderKey, out _);
            _pendingReminders[reminderKey] = reminder;
        }

        return Task.FromResult(new ReminderProtocol.ReminderScheduled(
            reminder.ToScheduleReminder(),
            ReminderScheduleResponseCode.Success));
    }

    /// <inheritdoc />
    public Task<bool> UpsertReminderOccurrencesAsync(
        IEnumerable<ScheduledReminder> reminders,
        CancellationToken ct = default)
    {
        lock (_sync)
        {
            foreach (var reminder in reminders)
            {
                var key = ToKey(reminder);
                _pendingReminders[key] = reminder;
                _awaitingAckReminders.TryRemove(key, out _);
            }
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> CommitReminderMutationsAsync(ReminderMutationBatch mutationBatch, CancellationToken ct = default)
    {
        if (mutationBatch.IsEmpty)
            return Task.FromResult(true);

        lock (_sync)
        {
            var pending = new Dictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), ScheduledReminder>(_pendingReminders);
            var awaiting = new Dictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), (ScheduledReminder Reminder, AwaitingAckReminder State)>(_awaitingAckReminders);
            var completed = new Dictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), CompletedReminder>(_completedReminders);
            var completedDetails = new Dictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), ScheduledReminder>(_completedReminderDetails);

            foreach (var reminder in mutationBatch.PendingInserts)
            {
                var key = ToKey(reminder);
                if (!pending.ContainsKey(key) && !awaiting.ContainsKey(key) && !completed.ContainsKey(key))
                    pending[key] = reminder;
            }

            foreach (var reminder in mutationBatch.PendingUpserts)
            {
                var key = ToKey(reminder);
                pending[key] = reminder;
                awaiting.Remove(key);
                completed.Remove(key);
                completedDetails.Remove(key);
            }

            foreach (var reminder in mutationBatch.CompletedReminders)
            {
                var key = ToKey(reminder);
                if (pending.TryGetValue(key, out var pendingReminder))
                    completedDetails[key] = pendingReminder;
                else if (awaiting.TryGetValue(key, out var awaitingReminder))
                    completedDetails[key] = awaitingReminder.Reminder;
                pending.Remove(key);
                awaiting.Remove(key);
                completed[key] = reminder;
            }

            foreach (var reminder in mutationBatch.ActiveCompletions)
            {
                var key = ToKey(reminder);
                if (pending.Remove(key, out var pendingReminder))
                    completedDetails[key] = pendingReminder;
                else if (awaiting.Remove(key, out var awaitingReminder))
                    completedDetails[key] = awaitingReminder.Reminder;
                else
                    continue;
                completed[key] = reminder;
            }

            foreach (var reminder in mutationBatch.AwaitingAckReminders)
            {
                var key = ToKey(reminder);
                if (!pending.TryGetValue(key, out var pendingReminder))
                    return Task.FromResult(false);

                pending.Remove(key);
                awaiting[key] = (pendingReminder, reminder);
            }

            ReplaceContents(_pendingReminders, pending);
            ReplaceContents(_awaitingAckReminders, awaiting);
            ReplaceContents(_completedReminders, completed);
            ReplaceContents(_completedReminderDetails, completedDetails);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        CancellationToken ct = default)
    {
        var cancelledKeys = new List<ReminderKey>();

        lock (_sync)
        {
            var pendingKeys = _pendingReminders.Keys
                .Where(k => k.Entity.Equals(entity) && k.Key.Equals(key))
                .ToList();
            foreach (var activeKey in pendingKeys)
            {
                if (_pendingReminders.TryRemove(activeKey, out var pending))
                {
                    _completedReminderDetails[activeKey] = pending;
                    _completedReminders[activeKey] = new CompletedReminder(
                        entity,
                        key,
                        activeKey.DueTimeUtc,
                        DateTimeOffset.UtcNow,
                        ReminderCompletionStatus.Cancelled);
                    cancelledKeys.Add(key);
                }
            }

            var awaitingKeys = _awaitingAckReminders.Keys
                .Where(k => k.Entity.Equals(entity) && k.Key.Equals(key))
                .ToList();
            foreach (var activeKey in awaitingKeys)
            {
                if (_awaitingAckReminders.TryRemove(activeKey, out var awaiting))
                {
                    _completedReminderDetails[activeKey] = awaiting.Reminder;
                    _completedReminders[activeKey] = new CompletedReminder(
                        entity,
                        key,
                        activeKey.DueTimeUtc,
                        DateTimeOffset.UtcNow,
                        ReminderCompletionStatus.Cancelled);
                    cancelledKeys.Add(key);
                }
            }
        }

        if (cancelledKeys.Count == 0)
        {
            return Task.FromResult(new ReminderProtocol.RemindersCancelled(
                entity,
                ReminderCancelResponseCode.NotFound,
                []));
        }

        return Task.FromResult(new ReminderProtocol.RemindersCancelled(
            entity,
            ReminderCancelResponseCode.Success,
            cancelledKeys.Distinct().ToList()));
    }

    /// <inheritdoc />
    public Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersForEntityAsync(
        ReminderEntity entity,
        CancellationToken ct = default)
    {
        var cancelledKeys = new HashSet<ReminderKey>();

        lock (_sync)
        {
            var pendingKeys = _pendingReminders.Keys
                .Where(k => k.Entity.Equals(entity))
                .ToList();
            foreach (var activeKey in pendingKeys)
            {
                if (_pendingReminders.TryRemove(activeKey, out var pending))
                {
                    _completedReminderDetails[activeKey] = pending;
                    _completedReminders[activeKey] = new CompletedReminder(
                        activeKey.Entity,
                        activeKey.Key,
                        activeKey.DueTimeUtc,
                        DateTimeOffset.UtcNow,
                        ReminderCompletionStatus.Cancelled);
                    cancelledKeys.Add(activeKey.Key);
                }
            }

            var awaitingKeys = _awaitingAckReminders.Keys
                .Where(k => k.Entity.Equals(entity))
                .ToList();
            foreach (var activeKey in awaitingKeys)
            {
                if (_awaitingAckReminders.TryRemove(activeKey, out var awaiting))
                {
                    _completedReminderDetails[activeKey] = awaiting.Reminder;
                    _completedReminders[activeKey] = new CompletedReminder(
                        activeKey.Entity,
                        activeKey.Key,
                        activeKey.DueTimeUtc,
                        DateTimeOffset.UtcNow,
                        ReminderCompletionStatus.Cancelled);
                    cancelledKeys.Add(activeKey.Key);
                }
            }
        }

        if (cancelledKeys.Count == 0)
        {
            return Task.FromResult(new ReminderProtocol.RemindersCancelled(
                entity,
                ReminderCancelResponseCode.NotFound,
                []));
        }

        return Task.FromResult(new ReminderProtocol.RemindersCancelled(
            entity,
            ReminderCancelResponseCode.Success,
            cancelledKeys.ToList()));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduledReminder>> GetRemindersForEntityAsync(
        ReminderEntity entity,
        int take = 10,
        int skip = 0,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var reminders = _pendingReminders.Values
            .Where(r => IsActivePending(r, now))
            .Concat(_awaitingAckReminders.Values
                .Select(v => v.Reminder)
                .Where(r => !r.Deadline.IsExpired(now)))
            .Where(r => r.Entity.Equals(entity))
            .OrderBy(r => r.When)
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult<IReadOnlyList<ScheduledReminder>>(reminders);
    }

    /// <inheritdoc />
    public Task<ReminderOverview> GetRemindersOverviewAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var pending = _pendingReminders.Values
            .Where(r => IsActivePending(r, now))
            .OrderBy(r => r.When)
            .ToList();

        if (pending.Count == 0)
            return Task.FromResult(ReminderOverview.Empty);

        return Task.FromResult(new ReminderOverview
        {
            TotalPendingReminders = pending.Count,
            TimeUntilNext = pending[0].When - now
        });
    }

    /// <inheritdoc />
    public Task<PendingRemindersWithSummary> GetNextRemindersAsync(
        DateTimeOffset untilDeadline,
        DateTimeOffset now,
        ReminderBatchSize maxCount,
        CancellationToken ct = default)
    {
        var dueReminders = _pendingReminders.Values
            .Where(r => IsActivePending(r, now) && r.When <= untilDeadline)
            .OrderBy(r => r.When)
            .Take(maxCount.Value)
            .ToList();

        var fetchedKeys = new HashSet<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc)>(
            dueReminders.Select(ToKey));

        var remainingPending = _pendingReminders
            .Where(kvp => !fetchedKeys.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .Where(r => IsActivePending(r, now))
            .OrderBy(r => r.When)
            .ToList();

        var overview = remainingPending.Count == 0
            ? ReminderOverview.Empty
            : new ReminderOverview
            {
                TotalPendingReminders = remainingPending.Count,
                TimeUntilNext = remainingPending[0].When - now
            };

        return Task.FromResult(new PendingRemindersWithSummary(dueReminders, overview));
    }

    /// <inheritdoc />
    public Task<bool> MarkRemindersAsCompletedAsync(
        IEnumerable<CompletedReminder> completedReminders,
        CancellationToken ct = default)
    {
        lock (_sync)
        {
            foreach (var completed in completedReminders)
            {
                var key = ToKey(completed);
                if (_pendingReminders.TryRemove(key, out var pending))
                    _completedReminderDetails[key] = pending;
                else if (_awaitingAckReminders.TryRemove(key, out var awaiting))
                    _completedReminderDetails[key] = awaiting.Reminder;
                _completedReminders[key] = completed;
            }
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> CleanUpCompletedRemindersAsync(
        DateTimeOffset olderThan,
        CancellationToken ct = default)
    {
        var keysToRemove = _completedReminders
            .Where(kvp => kvp.Value.CompletedAt < olderThan)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _completedReminders.TryRemove(key, out _);
            _completedReminderDetails.TryRemove(key, out _);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<int> ExpireRemindersAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var expiredCount = 0;

        foreach (var kvp in _pendingReminders.ToArray())
        {
            // Pending recurring occurrences are left for the scheduler, which rolls the series forward.
            if (kvp.Value.RepeatInterval.HasValue)
                continue;

            if (kvp.Value.Deadline.IsExpired(now))
            {
                if (_pendingReminders.TryRemove(kvp.Key, out var pending))
                {
                    _completedReminderDetails[kvp.Key] = pending;
                    _completedReminders[kvp.Key] = new CompletedReminder(
                        kvp.Value.Entity,
                        kvp.Value.Key,
                        kvp.Value.DueTimeUtc,
                        now,
                        ReminderCompletionStatus.Expired);
                    expiredCount++;
                }
            }
        }

        foreach (var kvp in _awaitingAckReminders.ToArray())
        {
            if (kvp.Value.Reminder.Deadline.IsExpired(now))
            {
                if (_awaitingAckReminders.TryRemove(kvp.Key, out var awaiting))
                {
                    _completedReminderDetails[kvp.Key] = awaiting.Reminder;
                    _completedReminders[kvp.Key] = new CompletedReminder(
                        kvp.Value.Reminder.Entity,
                        kvp.Value.Reminder.Key,
                        kvp.Value.Reminder.DueTimeUtc,
                        now,
                        ReminderCompletionStatus.Expired);
                    expiredCount++;
                }
            }
        }

        return Task.FromResult(expiredCount);
    }

    /// <inheritdoc />
    public Task<bool> MarkRemindersAsAwaitingAckAsync(
        IEnumerable<AwaitingAckReminder> reminders,
        CancellationToken ct = default)
    {
        var success = true;

        lock (_sync)
        {
            foreach (var reminder in reminders)
            {
                var key = ToKey(reminder);
                if (!_pendingReminders.TryRemove(key, out var pending))
                {
                    success = false;
                    continue;
                }

                _awaitingAckReminders[key] = (pending, reminder);
            }
        }

        return Task.FromResult(success);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduledReminder>> GetTimedOutAckRemindersAsync(
        DateTimeOffset now,
        ReminderBatchSize maxCount,
        CancellationToken ct = default)
    {
        var timedOut = _awaitingAckReminders.Values
            .Where(v => v.State.AckDeadline <= now)
            .OrderBy(v => v.State.AckDeadline)
            .Take(maxCount.Value)
            .Select(v => v.Reminder)
            .ToList();

        return Task.FromResult<IReadOnlyList<ScheduledReminder>>(timedOut);
    }

    /// <inheritdoc />
    public Task<ScheduledReminder?> GetAwaitingAckReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
    {
        var occurrenceKey = (entity, key, dueTimeUtc.ToUniversalTime());
        return Task.FromResult(
            _awaitingAckReminders.TryGetValue(occurrenceKey, out var awaiting)
                ? awaiting.Reminder
                : null);
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetNextAwaitingAckDeadlineAsync(CancellationToken ct = default)
    {
        DateTimeOffset? nextDeadline;

        lock (_sync)
        {
            nextDeadline = _awaitingAckReminders.Count == 0
                ? null
                : _awaitingAckReminders.Values.Min(v => v.State.AckDeadline);
        }

        return Task.FromResult(nextDeadline);
    }

    /// <inheritdoc />
    public async Task<AckResult> AcknowledgeReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        DateTimeOffset ackedAt,
        CancellationToken ct = default)
        => (await AcknowledgeRemindersAsync([new ReminderAcknowledgement(entity, key, dueTimeUtc, ackedAt)], ct))[0];

    /// <inheritdoc />
    public Task<IReadOnlyList<AckResult>> AcknowledgeRemindersAsync(
        IEnumerable<ReminderAcknowledgement> acknowledgements,
        CancellationToken ct = default)
    {
        var results = new List<AckResult>();

        lock (_sync)
        {
            foreach (var acknowledgement in acknowledgements)
            {
                var reminderKey = (acknowledgement.Entity, acknowledgement.Key, acknowledgement.DueTimeUtc);

                if (!_awaitingAckReminders.TryRemove(reminderKey, out var awaiting))
                {
                    results.Add(new AckResult(
                        acknowledgement.Entity,
                        acknowledgement.Key,
                        acknowledgement.DueTimeUtc,
                        ReminderAckStorageStatus.NotFound,
                        "Reminder occurrence was not awaiting acknowledgement or was already stale."));
                    continue;
                }

                if (awaiting.Reminder.Deadline.IsExpired(acknowledgement.AckedAt))
                {
                    _completedReminderDetails[reminderKey] = awaiting.Reminder;
                    _completedReminders[reminderKey] = new CompletedReminder(
                        acknowledgement.Entity,
                        acknowledgement.Key,
                        acknowledgement.DueTimeUtc,
                        acknowledgement.AckedAt,
                        ReminderCompletionStatus.Expired);
                    results.Add(new AckResult(
                        acknowledgement.Entity,
                        acknowledgement.Key,
                        acknowledgement.DueTimeUtc,
                        ReminderAckStorageStatus.NotFound,
                        "Reminder occurrence was not awaiting acknowledgement or was already stale."));
                    continue;
                }

                _completedReminders[reminderKey] = new CompletedReminder(
                    acknowledgement.Entity,
                    acknowledgement.Key,
                    acknowledgement.DueTimeUtc,
                    acknowledgement.AckedAt,
                    ReminderCompletionStatus.Delivered);
                _completedReminderDetails[reminderKey] = awaiting.Reminder;

                results.Add(new AckResult(
                    acknowledgement.Entity,
                    acknowledgement.Key,
                    acknowledgement.DueTimeUtc,
                    ReminderAckStorageStatus.Success));
            }
        }

        return Task.FromResult<IReadOnlyList<AckResult>>(results);
    }

    /// <inheritdoc />
    public Task<ReminderOccurrenceStatus?> GetReminderOccurrenceStatusAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
    {
        var occurrenceKey = (entity, key, dueTimeUtc.ToUniversalTime());
        if (_pendingReminders.TryGetValue(occurrenceKey, out var pending))
        {
            return Task.FromResult<ReminderOccurrenceStatus?>(new ReminderOccurrenceStatus(
                entity,
                key,
                occurrenceKey.Item3,
                pending.When,
                pending.AttemptCount,
                pending.LastFailureReason,
                ReminderCompletionStatus.Pending,
                pending.DeliveryDeadlineUtc));
        }

        if (_awaitingAckReminders.TryGetValue(occurrenceKey, out var awaiting))
        {
            return Task.FromResult<ReminderOccurrenceStatus?>(new ReminderOccurrenceStatus(
                entity,
                key,
                occurrenceKey.Item3,
                null,
                awaiting.Reminder.AttemptCount,
                awaiting.Reminder.LastFailureReason,
                ReminderCompletionStatus.AwaitingAck,
                awaiting.Reminder.DeliveryDeadlineUtc,
                awaiting.State.DeliveredAt,
                awaiting.State.AckDeadline));
        }

        if (_completedReminders.TryGetValue(occurrenceKey, out var completed))
        {
            _completedReminderDetails.TryGetValue(occurrenceKey, out var details);
            return Task.FromResult<ReminderOccurrenceStatus?>(new ReminderOccurrenceStatus(
                entity,
                key,
                occurrenceKey.Item3,
                null,
                details?.AttemptCount ?? 0,
                details?.LastFailureReason,
                completed.Status,
                details?.DeliveryDeadlineUtc,
                CompletedAtUtc: completed.CompletedAt));
        }

        return Task.FromResult<ReminderOccurrenceStatus?>(null);
    }

    private static void ReplaceContents<TValue>(
        ConcurrentDictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), TValue> target,
        Dictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), TValue> source)
    {
        target.Clear();
        foreach (var pair in source)
            target[pair.Key] = pair.Value;
    }
}
