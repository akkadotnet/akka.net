namespace Akka.Reminders.Tests.Model;

/// <summary>
/// What must have arrived by now. Checked after every operation; the model says what is owed.
/// With healthy storage "by now" is exact. Trouble (a failed or slow storage call, a missing shard
/// region) loosens it in one way only: what was due during the trouble is owed when the trouble is
/// over. A slow call is over when it returns. A failed call or a missing region is over
/// <see cref="ModelSettings.RecoveryTime"/> later; a stall or a slow call during that wait starts it again.
/// </summary>
public static class Liveness
{
    public static readonly (string Name, string Statement, string Source, Func<ReminderModel, History, ModelSettings, IEnumerable<string>> Broken)[] All =
    [
        ("DeliveredOnTime",
            "Every occurrence is delivered at the first moment the scheduler is awake at or after its due time (up to MaxSlippage early), unless its deadline has passed by then.",
            "docs: Scheduler tick; Scheduler lag longer than the repeat interval; Meaning of a scheduling error; Storage read failure and automatic recovery",
            DeliveredOnTime),
        ("RetriedOnTime",
            "A delivery that gets no ack within AckTimeout is sent again after the backoff, and a nacked one at the time the nack reply gave, while attempts and the deadline allow.",
            "docs: Ack lost or recipient crashes before acking; Negative acknowledgement handler",
            RetriedOnTime),
    ];

    private static IEnumerable<string> DeliveredOnTime(ReminderModel model, History h, ModelSettings s)
    {
        var late = new List<string>();
        // Each owed delivery is judged once, when it falls due, and then taken off the list.
        model.Owed.RemoveAll(owed =>
        {
            var (r, due) = owed;
            var slot = r.Slots[due];
            var at = slot.RequiredAt!.Value;
            var by = h.TroubleOver(r.Entity, at);
            if (by > model.Now)
                return false; // not owed yet
            var arrived = slot.Deliveries.Count > 0 && slot.Deliveries[0] <= by;
            var excused = r.InDoubt ||
                          (by > at && (r.EndedAt <= by || r.Deadline(due) <= by || LostToTrouble(h, r.Entity, FirstPossibleAttempt(r, due, s), by, s))) ||
                          model.OlderCallDelivered(r, due); // not ruled: may a new schedule call deliver a due time the old one already delivered?
            if (!arrived && !excused)
                late.Add($"reminder {r.Id} (entity {r.Entity}, key {r.Key}) due {Journal.T(due)} was owed by {Journal.T(by)} and " +
                         (slot.Deliveries.Count == 0 ? "has not arrived" : $"arrived at {Journal.T(slot.Deliveries[0])}"));
            return true;
        });
        return late;
    }

    /// <summary>
    /// Trouble may cost an occurrence for good, as the design documents: each send that was committed
    /// but reported failure counts as one attempt, and so does each try to reach a missing shard region.
    /// </summary>
    private static bool LostToTrouble(History h, int entity, DateTimeOffset from, DateTimeOffset to, ModelSettings s, int observedAttempts = 0)
    {
        var during = h.Troubles.Where(t => t.Touches(entity) && t.At <= to && t.Until >= from).ToList();
        return during.Any(t => t.Region is not null) ||
               AmbiguousAttempts(h, entity, from, to) >= s.MaxAttempts - observedAttempts;
    }

    private static int AmbiguousAttempts(History h, int entity, DateTimeOffset from, DateTimeOffset to) =>
        h.Troubles.Count(t => t.Touches(entity) && t.At >= from && t.At <= to &&
                             t is { Call: StorageCall.Commit, Kind: FaultKind.AppliedThenFail });

    private static DateTimeOffset FirstPossibleAttempt(Reminder r, DateTimeOffset due, ModelSettings s) =>
        r.SavedAt > due - s.MaxSlippage ? r.SavedAt : due - s.MaxSlippage;

    private static DateTimeOffset AfterSlowCalls(History h, int entity, DateTimeOffset at)
    {
        // Storage work suspends this scheduler's mailbox. An ack timeout that elapses during a slow
        // call cannot be noticed until that call returns; the retry backoff starts when it is noticed.
        while (h.Troubles.Where(t => t.Touches(entity) && t.Kind == FaultKind.Slow && t.At <= at && t.Until > at)
                   .Select(t => (DateTimeOffset?)t.Until).Max() is { } until)
            at = until;
        return at;
    }

    private static IEnumerable<string> RetriedOnTime(ReminderModel model, History h, ModelSettings s)
    {
        // Only the newest delivered occurrence of a reminder can still be waiting for a retry.
        foreach (var r in model.Reminders.Where(r => r.Live && !r.InDoubt))
        {
            var due = r.NewestDelivered;
            if (!r.Slots.TryGetValue(due, out var slot) || slot.Acknowledgement == AcknowledgementState.AcceptanceUnknown || model.PhaseOf(r, due) != Phase.RetryOverdue)
                continue;
            var attemptStart = FirstPossibleAttempt(r, due, s);
            var possibleUnsent = AmbiguousAttempts(h, r.Entity, attemptStart, model.Now);
            // The application sees fewer attempts when committed sends report failure. Their exact
            // count is uncertain, so use the largest backoff consistent with the injected faults.
            // A nack's promised time is already durable and does not get another backoff after a stall.
            var retryAt = slot.RetryAt ?? AfterSlowCalls(h, r.Entity, model.FirstAwake(slot.Deliveries[^1] + s.AckTimeout)) +
                s.Backoff(slot.Deliveries.Count - 1 + possibleUnsent);
            var by = h.TroubleOver(r.Entity, model.FirstAwake(retryAt));
            if (by > model.Now || r.Deadline(due) <= by ||
                LostToTrouble(h, r.Entity, attemptStart, by, s, slot.Deliveries.Count))
                continue;
            yield return $"reminder {r.Id} (entity {r.Entity}, key {r.Key}) due {Journal.T(due)} was last sent at {Journal.T(slot.Deliveries[^1])}, got no ack, and its retry owed by {Journal.T(by)} has not arrived";
        }
    }
}
