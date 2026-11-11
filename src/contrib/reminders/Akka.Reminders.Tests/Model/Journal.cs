using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Model;

/// <summary>Something the application did or saw, or trouble the test caused. <c>Seq</c> orders them.</summary>
public abstract record Event
{
    public long Seq { get; set; }
    public DateTimeOffset At { get; set; }

    protected abstract string Text { get; }

    public sealed override string ToString() => Text;

    protected static string Name(int entity, int? key) => key is null ? $"entity {entity}" : $"entity {entity}, key {key}";
}

/// <summary>A schedule call and its reply. <c>Id</c> is the payload, so deliveries can be traced to the call.</summary>
public sealed record Scheduled(int Id, int Entity, int Key, DateTimeOffset FirstDue, TimeSpan? Interval, TimeSpan? Window,
    ReminderScheduleResponseCode Reply) : Event
{
    public Reminder Definition { get; } = new(Id, Entity, Key, FirstDue, Interval, Window);

    protected override string Text =>
        $"schedule reminder {Id}: {Name(Entity, Key)}, first due {Journal.T(FirstDue)}{(Interval is { } i ? $", every {i.TotalSeconds}s" : "")}{(Window is { } w ? $", window {w.TotalSeconds}s" : "")} -> {Reply}";
}

/// <summary>A cancel call and its reply. <c>Key</c> is null for cancel-all.</summary>
public sealed record CancelAnswered(int Entity, int? Key, ReminderCancelResponseCode Reply) : Event
{
    protected override string Text => $"cancel {Name(Entity, Key)} -> {Reply}";
}

/// <summary>A reminder message that reached the application.</summary>
public sealed record Delivered(int Id, int Entity, int Key, DateTimeOffset Due, DateTimeOffset EnvelopeDeadline) : Event
{
    protected override string Text => $"reminder {Id} delivered: {Name(Entity, Key)}, due {Journal.T(Due)}";
}

/// <summary>An ack and its reply. <c>Asked</c> is the journal position when the ack was sent; <c>Id</c> is the delivery it answers.</summary>
public sealed record AckAnswered(long Asked, int Id, int Entity, int Key, DateTimeOffset Due, ReminderAckResponseCode Reply) : Event
{
    protected override string Text => $"ack reminder {Id} due {Journal.T(Due)} -> {Reply}";
}

public sealed record NackAnswered(long Asked, int Id, int Entity, int Key, DateTimeOffset Due, ReminderNackResponseCode Reply, DateTimeOffset? RetryAt) : Event
{
    protected override string Text => $"nack reminder {Id} due {Journal.T(Due)} -> {Reply}, retry {Journal.T(RetryAt)}";
}

public sealed record Listed(int Entity, FetchRemindersResponseCode Reply, IReadOnlyList<(int Key, int Id, DateTimeOffset Due)> Items) : Event
{
    protected override string Text => $"list entity {Entity} -> {Reply}: {string.Join("; ", Items.Select(i => $"reminder {i.Id} key {i.Key} due {Journal.T(i.Due)}"))}";
}

/// <summary>
/// Trouble the test caused: a storage call that failed or was slow, or a shard region that was down.
/// From <c>At</c> until <c>Until</c> the scheduler may be late. <c>Region</c> is null for storage trouble.
/// </summary>
public sealed record Trouble(DateTimeOffset Until, int? Region, StorageCall? Call, FaultKind? Kind) : Event
{
    public DateTimeOffset Until { get; set; } = Until;

    /// <summary>Ack inputs targeted by this injected fault, never storage results or persisted rows.</summary>
    public IReadOnlyList<ReminderAcknowledgement> AckTargets { get; init; } = [];

    public bool Touches(int entity) => Region is null || Region == ReminderApp.RegionOf(entity);

    protected override string Text => (Region is { } r ? $"region {r} down" : Call is null ? "stall during trouble" : $"storage {Call} {Kind}") +
                                      $": trouble until {Journal.T(Until)}";
}

/// <summary>The scheduler was stalled until now (a Lag).</summary>
public sealed record Lagged : Event
{
    protected override string Text => "the scheduler was stalled until now";
}

/// <summary>A shard region went down or came back.</summary>
public sealed record RegionSet(int Region, bool Up) : Event
{
    protected override string Text => $"region {Region} is {(Up ? "up" : "down")}";
}

/// <summary>One operation is over and the scheduler is idle.</summary>
public sealed record Done : Event
{
    protected override string Text => "(idle)";
}

/// <summary>The ordered list of events of one run. Thread-safe; the rules read a <see cref="History"/> of it.</summary>
public sealed class Journal(Func<DateTimeOffset> now)
{
    private readonly List<Event> _events = [];
    private readonly List<Delivered> _deliveries = [];
    private long _seq;

    public T Add<T>(T e) where T : Event
    {
        lock (_events)
        {
            e.Seq = ++_seq;
            e.At = now();
            _events.Add(e);
            if (e is Delivered d)
                _deliveries.Add(d);
        }

        return e;
    }

    public long Seq => Interlocked.Read(ref _seq);

    /// <summary>Deliveries after the first <paramref name="seen"/>; moves <paramref name="seen"/> past them.</summary>
    public List<Delivered> NewDeliveries(ref int seen)
    {
        lock (_events)
        {
            var fresh = _deliveries.Skip(seen).ToList();
            seen = _deliveries.Count;
            return fresh;
        }
    }

    public History Read()
    {
        lock (_events)
            return new History(_events.ToList());
    }

    /// <summary>A shard region went down: trouble for its entities until it is back and has had time to recover.</summary>
    public void RegionDown(int region) => Add(new Trouble(DateTimeOffset.MaxValue, region, null, null));

    public void RegionUp(int region, TimeSpan recovery)
    {
        foreach (var t in Read().Troubles.Where(t => t.Region == region && t.Until == DateTimeOffset.MaxValue))
            t.Until = now() + recovery;
    }

    /// <summary>
    /// A stall or a slow storage call while the scheduler is getting over a failure or a missing region
    /// starts the recovery wait again when it ends.
    /// </summary>
    public void Stalled(TimeSpan stallPlusRecovery)
    {
        var at = now();
        var recovering = Read().Troubles.Where(t => t.Kind != FaultKind.Slow && t.At <= at && t.Until >= at && t.Until != DateTimeOffset.MaxValue);
        foreach (var region in recovering.Select(t => t.Region).Distinct().ToList())
            Add(new Trouble(at + stallPlusRecovery, region, null, null));
    }

    public static string Payload(int id) => "m" + id;

    public static int IdOf(object message) => message is string s && s.StartsWith('m') && int.TryParse(s.AsSpan(1), out var id) ? id : -1;

    public static string T(DateTimeOffset t) => t == DateTimeOffset.MaxValue
        ? "never"
        : (t - VirtualClock.Origin).TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "s";

    public static string T(DateTimeOffset? t) => t is null ? "none" : T(t.Value);
}

/// <summary>A snapshot of the journal, with the few lookups the rules share.</summary>
public sealed class History(List<Event> all)
{
    private readonly Dictionary<int, Scheduled> _calls = all.OfType<Scheduled>().ToDictionary(c => c.Id);

    public List<Event> All => all;
    public List<Scheduled> Calls { get; } = all.OfType<Scheduled>().ToList();
    public List<Delivered> Deliveries { get; } = all.OfType<Delivered>().ToList();
    public List<AckAnswered> Acks { get; } = all.OfType<AckAnswered>().ToList();
    public List<NackAnswered> Nacks { get; } = all.OfType<NackAnswered>().ToList();
    public List<Listed> Lists { get; } = all.OfType<Listed>().ToList();
    public List<Trouble> Troubles { get; } = all.OfType<Trouble>().ToList();

    /// <summary>Everything the application did and saw, one line each, with the virtual time.</summary>
    public string Timeline(int last = int.MaxValue) => "What the application saw:\n" +
        string.Join("\n", all.Where(e => e is not (Done or Trouble)).TakeLast(last).Select(e => $"  {Journal.T(e.At),7}  {e}"));

    /// <summary>The schedule call whose payload a delivery carries, or null.</summary>
    public Scheduled? Call(int id) => _calls.GetValueOrDefault(id);

    /// <summary>The answered cancel or the successful re-schedule that ended this call's reminder, or null.</summary>
    public Event? EndOf(Scheduled c) => all.FirstOrDefault(e => e.Seq > c.Seq && e switch
    {
        CancelAnswered x => x.Reply != ReminderCancelResponseCode.Error && x.Entity == c.Entity && (x.Key ?? c.Key) == c.Key,
        Scheduled n => n.Reply == ReminderScheduleResponseCode.Success && n.Entity == c.Entity && n.Key == c.Key,
        _ => false,
    });

    /// <summary>True when no trouble touched this entity between the two times.</summary>
    public bool Calm(int entity, DateTimeOffset from, DateTimeOffset to) =>
        !Troubles.Any(t => t.Touches(entity) && t.At <= to && t.Until >= from);

    /// <summary>When the trouble covering time <paramref name="t"/> is over; <paramref name="t"/> itself when there is none.</summary>
    public DateTimeOffset TroubleOver(int entity, DateTimeOffset t)
    {
        while (Troubles.Where(x => x.Touches(entity) && x.At <= t && x.Until > t).Select(x => (DateTimeOffset?)x.Until).Max() is { } later)
            t = later;
        return t;
    }

    /// <summary>True if one of these storage calls failed (slow does not count) after event <paramref name="seq"/>.</summary>
    public bool Failed(long seq, params StorageCall[] calls) =>
        Troubles.Any(t => t.Seq > seq && t.Kind is not (null or FaultKind.Slow) && calls.Contains(t.Call!.Value));

    /// <summary>A landed ack call targeting this occurrence; its storage result may still be NotFound.</summary>
    public Trouble? AppliedAcknowledgement(AckAnswered ack) => Troubles.LastOrDefault(t =>
        t.Seq > ack.Asked && t.Seq < ack.Seq &&
        t is { Call: StorageCall.Ack, Kind: FaultKind.AppliedThenFail } &&
        t.AckTargets.Any(a => a.Entity == ReminderApp.EntityOf(ack.Entity) &&
                             a.Key == ReminderApp.KeyOf(ack.Key) && a.DueTimeUtc == ack.Due));

    /// <summary>Whether observations establish that a targeted landed ack call was still eligible.</summary>
    public bool KnownLandedAcknowledgement(ModelSettings s, AckAnswered ack)
    {
        var fault = AppliedAcknowledgement(ack);
        var delivery = Deliveries.LastOrDefault(d => d.Seq <= ack.Asked &&
            d.Entity == ack.Entity && d.Key == ack.Key && d.Due == ack.Due);
        if (fault is null || delivery is null || Call(delivery.Id) is not { } call)
            return false;

        // Reaching the ack call can return NotFound. Only infer acceptance when independent
        // observations still establish AwaitingAck, before timeout/deadline and without trouble
        // or an intervening operation that could have changed this occurrence's state.
        if (fault.At >= CommittedAt(delivery.At) + s.AckTimeout || fault.At >= call.Definition.Deadline(ack.Due))
            return false;
        if (Troubles.Any(t => t.Seq > delivery.Seq && t.Seq < fault.Seq && t.Touches(ack.Entity)) ||
            Nacks.Any(n => n.Seq > delivery.Seq && n.Seq < fault.Seq &&
                          n.Entity == ack.Entity && n.Key == ack.Key && n.Due == ack.Due) ||
            Deliveries.Any(d => d.Seq > delivery.Seq && d.Seq < fault.Seq &&
                               d.Id == delivery.Id && d.Due > ack.Due) ||
            Calls.Any(c => c.Seq > delivery.Seq && c.Seq < fault.Seq &&
                          c.Entity == ack.Entity && c.Key == ack.Key) ||
            All.OfType<CancelAnswered>().Any(c => c.Seq > delivery.Seq && c.Seq < fault.Seq &&
                                                  c.Entity == ack.Entity && (c.Key is null || c.Key == ack.Key)) ||
            EndOf(call) is { } end && end.Seq < fault.Seq)
            return false;
        return true;
    }

    /// <summary>
    /// Deadlines are judged when the send is committed. If a slow commit ended at <paramref name="at"/>,
    /// this is when that commit began; otherwise <paramref name="at"/>.
    /// </summary>
    public DateTimeOffset CommittedAt(DateTimeOffset at) =>
        Troubles.Where(t => t is { Call: StorageCall.Commit, Kind: FaultKind.Slow } && t.Until == at)
            .Select(t => (DateTimeOffset?)t.At).Min() ?? at;
}

/// <summary>What the harness watches to drive a run (is the scheduler idle? did it restart?). Never used to judge.</summary>
public sealed class HarnessSignals
{
    private long _activity;

    public object Lock { get; } = new();

    /// <summary>Bumped by every storage call and every delivery.</summary>
    public long Activity => Interlocked.Read(ref _activity);

    public void Touch() => Interlocked.Increment(ref _activity);

    /// <summary>True while the scheduler loads its state at start-up.</summary>
    public bool InitPhase { get; set; }

    /// <summary>True when that load failed and the scheduler waits on its retry timer.</summary>
    public bool InitFailed { get; set; }

    /// <summary>Storage calls that reported failure; an immediate retry after each one is in spec.</summary>
    public int FailedCalls { get; set; }

    public int Crashes { get; set; }

    /// <summary>The scheduler stopped answering; its actor system is not reused.</summary>
    public bool Stuck { get; set; }
}
