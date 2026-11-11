using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Reminders.Tests.Model;

/// <summary>
/// Deterministic <see cref="IScheduler"/> for the model-based tests. Time moves only when the harness
/// moves it, and due work runs only when the harness calls <see cref="FireDue"/>. Unlike
/// <see cref="Akka.TestKit.TestScheduler"/> it is thread-safe, can report the next due timer, and can be
/// reset between scenarios.
/// </summary>
public sealed class VirtualClock : IScheduler, IAdvancedScheduler
{
    /// <summary>
    /// Fixed start time for every scenario. It is far ahead of the wall clock, so storage code that
    /// still reads <see cref="DateTimeOffset.UtcNow"/> sees every virtual deadline as "in the future"
    /// and behaves the same on every run.
    /// </summary>
    public static readonly DateTimeOffset Origin = new(2050, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly object _lock = new();
    private readonly List<Item> _items = [];
    private long _seq;
    private DateTimeOffset _now = Origin;

    private sealed record Item(
        DateTimeOffset Due,
        long Seq,
        ICanTell? Receiver,
        object? Message,
        IActorRef? Sender,
        Action? Action,
        ICancelable? Cancelable,
        TimeSpan? Repeat);

    // Akka creates schedulers through this constructor shape.
    public VirtualClock(Config config, ILoggingAdapter log)
    {
    }

    public DateTimeOffset Now
    {
        get
        {
            lock (_lock)
                return _now;
        }
    }

    private static readonly System.Diagnostics.Stopwatch Stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public TimeSpan MonotonicClock => Stopwatch.Elapsed;

    public TimeSpan HighResMonotonicClock => Stopwatch.Elapsed;

    public IAdvancedScheduler Advanced => this;

    /// <summary>Clears all scheduled work and moves the clock back to <see cref="Origin"/>.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _items.Clear();
            _now = Origin;
        }
    }

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(by));
        lock (_lock)
            _now += by;
    }

    public void AdvanceTo(DateTimeOffset when)
    {
        lock (_lock)
        {
            if (when > _now)
                _now = when;
        }
    }

    /// <summary>
    /// Runs every live item due at or before now, in due order. Returns how many ran.
    /// </summary>
    public int FireDue()
    {
        List<Item> due;
        lock (_lock)
        {
            _items.RemoveAll(i => i.Cancelable?.IsCancellationRequested == true);
            due = _items.Where(i => i.Due <= _now).OrderBy(i => i.Due).ThenBy(i => i.Seq).ToList();
            foreach (var item in due)
            {
                _items.Remove(item);
                if (item.Repeat is { } repeat)
                    _items.Add(item with { Due = item.Due + repeat, Seq = ++_seq });
            }
        }

        foreach (var item in due)
        {
            if (item.Action is not null)
                item.Action();
            else
                item.Receiver!.Tell(item.Message!, item.Sender ?? ActorRefs.NoSender);
        }

        return due.Count;
    }

    /// <summary>
    /// The earliest live item sent to a user actor (the reminder scheduler), or null.
    /// </summary>
    public DateTimeOffset? NextUserDue()
    {
        lock (_lock)
        {
            _items.RemoveAll(i => i.Cancelable?.IsCancellationRequested == true);
            var next = _items
                .Where(i => i.Receiver is IActorRef r && r.Path.Elements.FirstOrDefault() == "user")
                .Select(i => (DateTimeOffset?)i.Due)
                .DefaultIfEmpty(null)
                .Min();
            return next;
        }
    }

    private void Add(TimeSpan delay, ICanTell? receiver, object? message, IActorRef? sender, Action? action,
        ICancelable? cancelable, TimeSpan? repeat)
    {
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        lock (_lock)
            _items.Add(new Item(_now + delay, ++_seq, receiver, message, sender, action, cancelable, repeat));
    }

    public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender)
        => Add(delay, receiver, message, sender, null, null, null);

    public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        => Add(delay, receiver, message, sender, null, cancelable, null);

    public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender)
        => Add(initialDelay, receiver, message, sender, null, null, interval);

    public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender,
        ICancelable cancelable)
        => Add(initialDelay, receiver, message, sender, null, cancelable, interval);

    public void ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        => Add(delay, null, null, null, action, cancelable, null);

    public void ScheduleOnce(TimeSpan delay, Action action)
        => Add(delay, null, null, null, action, null, null);

    public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
        => Add(initialDelay, null, null, null, action, cancelable, interval);

    public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        => Add(initialDelay, null, null, null, action, null, interval);

    public void ScheduleOnce(TimeSpan delay, IRunnable action, ICancelable cancelable)
        => Add(delay, null, null, null, action.Run, cancelable, null);

    public void ScheduleOnce(TimeSpan delay, IRunnable action)
        => Add(delay, null, null, null, action.Run, null, null);

    public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, IRunnable action, ICancelable cancelable)
        => Add(initialDelay, null, null, null, action.Run, cancelable, interval);

    public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, IRunnable action)
        => Add(initialDelay, null, null, null, action.Run, null, interval);
}
