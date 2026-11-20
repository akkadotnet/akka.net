namespace Akka.Reminders.Tests.Model;

/// <summary>What one occurrence of a reminder is doing, as the application can tell.</summary>
public enum Phase
{
    Waiting,      // not delivered yet
    AwaitingAck,  // delivered; the application has AckTimeout to answer
    RetryPending, // the next attempt has a time and that time has not come
    RetryOverdue, // the next attempt should have arrived by now
    Done,         // acked, expired, out of attempts, cancelled, or replaced by a newer occurrence
}

/// <summary>What the model knows about acknowledgement of this occurrence, not about its whole lifecycle.</summary>
public enum AcknowledgementState
{
    NotAcknowledged,
    Acknowledged,
    AcceptanceUnknown,
}

/// <summary>One occurrence (one due time) of a reminder.</summary>
public sealed class Slot
{
    public List<DateTimeOffset> Deliveries { get; } = [];
    public DateTimeOffset? RequiredAt { get; set; } // the first delivery must arrive by then; null = not required
    public DateTimeOffset? RetryAt { get; set; }    // retry time promised by the reply to a nack
    public AcknowledgementState Acknowledgement { get; set; }
    public bool NackEndedIt { get; set; }           // a nack answered Failed or Expired
}

/// <summary>One successful schedule call. A new call for the same entity and key replaces it.</summary>
public sealed class Reminder(int id, int entity, int key, DateTimeOffset firstDue, TimeSpan? interval, TimeSpan? window)
{
    public int Id => id; // carried in the payload, so a delivery names the call that asked for it
    public int Entity => entity;
    public int Key => key;
    public DateTimeOffset FirstDue => firstDue;
    public TimeSpan? Interval => interval;
    public DateTimeOffset SavedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; } // cancelled or replaced
    public bool InDoubt { get; set; }            // a reply was Error: it may or may not exist
    public DateTimeOffset CoveredTo { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset NewestDelivered { get; set; } = DateTimeOffset.MinValue;
    public SortedDictionary<DateTimeOffset, Slot> Slots { get; } = new();

    public bool Live => EndedAt is null;

    /// <summary>How long an occurrence may be delivered: MaxDeliveryWindow, cut off by the next due time.</summary>
    public TimeSpan Life => window is { } w && (interval is null || w < interval) ? w : interval ?? TimeSpan.MaxValue;

    public DateTimeOffset Deadline(DateTimeOffset due) => Life == TimeSpan.MaxValue ? DateTimeOffset.MaxValue : due + Life;

    public bool IsSlot(DateTimeOffset due) => interval is { } i
        ? due >= firstDue && (due - firstDue).Ticks % i.Ticks == 0
        : due == firstDue;

    public Slot Slot(DateTimeOffset due) => Slots.TryGetValue(due, out var s) ? s : Slots[due] = new Slot();

    /// <summary>Due times d with after &lt; d &lt;= upTo.</summary>
    public IEnumerable<DateTimeOffset> DueIn(DateTimeOffset after, DateTimeOffset upTo)
    {
        if (interval is not { } i)
            return after < firstDue && firstDue <= upTo ? [firstDue] : [];
        var first = after < firstDue ? 0 : (after - firstDue).Ticks / i.Ticks + 1;
        return Enumerable.Range(0, int.MaxValue).Select(n => firstDue.AddTicks((first + n) * i.Ticks)).TakeWhile(d => d <= upTo);
    }
}

/// <summary>
/// What reminders must do when storage is healthy. No Akka, no storage, no threads: the test tells it
/// what the application did and saw, and asks what must be true.
/// The scheduler is "awake" except during a Lag (a stall). An occurrence must be delivered at the first
/// awake moment at or after its due time, unless its deadline has passed by then.
/// </summary>
public sealed partial class ReminderModel(ModelSettings settings)
{
    private readonly List<(DateTimeOffset From, DateTimeOffset To)> _awake = [(VirtualClock.Origin, VirtualClock.Origin)];

    public List<Reminder> Reminders { get; } = []; // every schedule call that may have been saved, oldest first
    public List<(Reminder Reminder, DateTimeOffset Due)> Owed { get; } = []; // first deliveries not yet checked off (Liveness)
    public bool[] RegionUp { get; } = Enumerable.Repeat(true, ReminderApp.Regions).ToArray();
    public DateTimeOffset Now => _awake[^1].To;

    public Reminder? Live(int entity, int key) => Reminders.LastOrDefault(r => r.Entity == entity && r.Key == key && r.Live);

    /// <summary>First awake moment at or after <paramref name="t"/>.</summary>
    public DateTimeOffset FirstAwake(DateTimeOffset t)
    {
        var span = _awake.FirstOrDefault(s => s.To >= t, (From: t, To: t));
        return span.From > t ? span.From : t;
    }

    // ---- time ----

    /// <summary>Time passes with the scheduler keeping up (Tick).</summary>
    public void RunTo(DateTimeOffset to)
    {
        var from = Now;
        _awake[^1] = (_awake[^1].From, to);
        foreach (var r in Reminders.Where(r => r.Live))
            Require(r, from, to);
    }

    /// <summary>The scheduler was stalled until <paramref name="to"/> (Lag): it is awake again only then.</summary>
    public void WakeAt(DateTimeOffset to)
    {
        _awake.Add((to, to));
        foreach (var r in Reminders.Where(r => r.Live))
            Require(r, to, to);
    }

    /// <summary>
    /// Marks the occurrences that must be delivered while the scheduler is awake from..to.
    /// Example, every 10 s from 10:00:00, no delivery window, so each occurrence lives 10 s:
    ///   Tick, awake 10:00:05..10:00:25 -> 10:00:10 is owed at 10:00:10 and 10:00:20 at 10:00:20.
    ///   Lag, awake again only at 10:00:25 -> 10:00:10 is dead (its deadline 10:00:20 has passed),
    ///   10:00:20 is owed at 10:00:25, and nothing is owed for 10:00:30 yet.
    /// </summary>
    private void Require(Reminder r, DateTimeOffset from, DateTimeOffset to)
    {
        // An occurrence whose deadline is at or before `from` is dead: missed slots are skipped, not replayed.
        var dead = r.Life == TimeSpan.MaxValue ? DateTimeOffset.MinValue : from - r.Life;
        foreach (var due in r.DueIn(r.CoveredTo > dead ? r.CoveredTo : dead, to))
        {
            r.Slot(due).RequiredAt = due > from ? due : from;
            Owed.Add((r, due));
        }

        r.CoveredTo = to;
    }

    // ---- operations ----

    /// <summary>Returns false when the shard region is down: the call is refused and nothing changes.</summary>
    public bool Schedule(Reminder r)
    {
        if (!RegionUp[ReminderApp.RegionOf(r.Entity)])
            return false;
        Cancel(r.Entity, r.Key);
        r.SavedAt = Now;
        Reminders.Add(r);
        Require(r, Now, Now);
        return true;
    }

    /// <summary>Returns true if there was work to cancel, false if not, null if the model cannot tell.</summary>
    public bool? Cancel(int entity, int key)
    {
        var r = Live(entity, key);
        var hadWork = HasWork(r);
        if (r is not null)
            r.EndedAt = Now;
        return hadWork;
    }

    /// <summary>A reply was Error: the model no longer knows which reminder this key has, if any.</summary>
    public void Doubt(int entity, int key)
    {
        foreach (var r in Reminders.Where(r => r.Entity == entity && r.Key == key && (r.Live || r.EndedAt == Now)))
            r.InDoubt = true;
    }

    /// <summary>True if the reminder still has something to deliver or to wait for; null if the model cannot tell.</summary>
    public bool? HasWork(Reminder? r)
    {
        if (r is null)
            return false;
        if (r.Interval is not null)
            return true; // a recurring reminder always has a next occurrence
        var slot = r.Slot(r.FirstDue);
        if (slot.Acknowledgement == AcknowledgementState.Acknowledged || slot.NackEndedIt)
            return false;
        if (PhaseOf(r, r.FirstDue) != Phase.Done)
            return true;
        // Past its deadline: cleanup is best effort, so it may or may not still count as work.
        return r.Deadline(r.FirstDue) <= Now ? null : false;
    }

    /// <summary>The application received this occurrence at <paramref name="at"/>.</summary>
    public void Delivered(int id, DateTimeOffset due, DateTimeOffset at)
    {
        if (Reminders.FirstOrDefault(x => x.Id == id) is not { } r)
            return;
        r.Slot(due).Deliveries.Add(at);
        if (r.Slot(due).Acknowledgement == AcknowledgementState.AcceptanceUnknown)
            r.Slot(due).Acknowledgement = AcknowledgementState.NotAcknowledged;
        r.Slot(due).RetryAt = null;
        if (due > r.NewestDelivered)
            r.NewestDelivered = due;
    }

    /// <summary>Returns true when the ack must succeed. An ack names (entity, key, due time), nothing else.</summary>
    public bool Ack(int entity, int key, DateTimeOffset due)
    {
        if (Live(entity, key) is not { } r || PhaseOf(r, due) != Phase.AwaitingAck)
            return false;
        r.Slot(due).Acknowledgement = AcknowledgementState.Acknowledged;
        return true;
    }

    /// <summary>Returns the reply a nack must get, and the retry time when one is promised.</summary>
    public (ReminderNackResponseCode Code, DateTimeOffset? RetryAt) Nack(int entity, int key, DateTimeOffset due)
    {
        if (Live(entity, key) is not { } r || PhaseOf(r, due) != Phase.AwaitingAck)
            return (ReminderNackResponseCode.NotFound, null);
        var slot = r.Slot(due);
        var retryAt = Now + settings.Backoff(slot.Deliveries.Count - 1);
        slot.NackEndedIt = slot.Deliveries.Count >= settings.MaxAttempts || retryAt >= r.Deadline(due);
        if (slot.NackEndedIt)
            return (slot.Deliveries.Count >= settings.MaxAttempts ? ReminderNackResponseCode.Failed : ReminderNackResponseCode.Expired, null);
        slot.RetryAt = retryAt;
        return (ReminderNackResponseCode.RetryScheduled, retryAt);
    }

    // ---- questions ----

    /// <summary>
    /// Where one occurrence stands now, worked out from its deliveries and the answers they got.
    /// Example, a one-off with AckTimeout 10 s, backoff 1 s, then 2 s, MaxDeliveryAttempts 3, never acked:
    ///   delivered 10:00:00           -> AwaitingAck until 10:00:10
    ///   10:00:10, no ack             -> RetryPending: the retry is due 10:00:11 (10 s + 1 s backoff)
    ///   10:00:11, nothing arrived    -> RetryOverdue (Liveness reports it)
    ///   delivered 10:00:11           -> AwaitingAck until 10:00:21, then retry due 10:00:23 (2 s backoff)
    ///   delivered 10:00:23 (third)   -> AwaitingAck until 10:00:33, then Done: no attempts left
    /// An ack while AwaitingAck makes it Done at once. So does a newer occurrence being delivered,
    /// a passed deadline, or a cancel.
    /// </summary>
    public Phase PhaseOf(Reminder r, DateTimeOffset due)
    {
        var slot = r.Slot(due);
        var deadline = r.Deadline(due);
        if (!r.Live || slot.Acknowledgement == AcknowledgementState.Acknowledged || slot.NackEndedIt || r.NewestDelivered > due) // latest-only: a newer delivery ends it
            return Phase.Done;
        if (slot.Deliveries.Count == 0)
            return deadline > Now ? Phase.Waiting : Phase.Done;

        var retryAt = slot.RetryAt;
        if (retryAt is null)
        {
            var ackDeadline = slot.Deliveries[^1] + settings.AckTimeout;
            if (Now < ackDeadline)
                return deadline > Now ? Phase.AwaitingAck : Phase.Done;
            // No answer in time: retry after a backoff, if attempts and the deadline allow.
            var noticed = FirstAwake(ackDeadline);
            retryAt = noticed + settings.Backoff(slot.Deliveries.Count - 1);
            if (slot.Deliveries.Count >= settings.MaxAttempts || deadline <= noticed || retryAt >= deadline)
                return Phase.Done;
        }

        var sendAt = FirstAwake(retryAt.Value);
        if (sendAt > Now)
            return Phase.RetryPending;
        return deadline <= sendAt ? Phase.Done : Phase.RetryOverdue;
    }

    /// <summary>True if an older schedule call for the same key already delivered this due time.</summary>
    public bool OlderCallDelivered(Reminder r, DateTimeOffset due) => Reminders.Any(o =>
        o != r && o.Entity == r.Entity && o.Key == r.Key && o.Slots.TryGetValue(due, out var s) && s.Deliveries.Count > 0);
}
