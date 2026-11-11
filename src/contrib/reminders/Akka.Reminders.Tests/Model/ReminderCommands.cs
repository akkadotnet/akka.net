namespace Akka.Reminders.Tests.Model;

/// <summary>One application command. All durations and offsets are milliseconds.</summary>
public abstract record Op
{
    public Task Run(ReminderApp app) => this switch
    {
        ScheduleOnce x => app.ScheduleOnce(x.Entity, x.Key, Time(x.DueOffsetMs), Time(x.WindowMs)),
        ScheduleRecurring x => app.ScheduleRecurring(x.Entity, x.Key, Time(x.AnchorOffsetMs), Time(x.IntervalMs), Time(x.WindowMs)),
        Cancel x => app.Cancel(x.Entity, x.Key),
        CancelAll x => app.Cancel(x.Entity, null),
        ListReminders x => app.List(x.Entity),
        Tick x => app.Tick(Time(x.Ms)),
        Lag x => app.Lag(Time(x.Ms)),
        Restart => app.Restart(),
        SetRecipient x => app.SetRecipient(x.Entity, x.Mode),
        AckOutstanding x => app.AckUnanswered(x.Entity),
        SetRegion x => app.SetRegion(x.Region, x.Present),
        InjectFault x => app.InjectFault(x.Call, x.Kind, x.Count, Time(x.DelayMs), x.FireTimersWhileSlow),
        HealAndWait => app.HealAndWait(),
        _ => throw new InvalidOperationException($"No application action for {GetType().Name}"),
    };

    private static TimeSpan Time(int ms) => TimeSpan.FromMilliseconds(ms);
    private static TimeSpan? Time(int? ms) => ms is { } value ? Time(value) : null;
}

public sealed record ScheduleOnce(int Entity, int Key, int DueOffsetMs, int? WindowMs) : Op;
public sealed record ScheduleRecurring(int Entity, int Key, int AnchorOffsetMs, int IntervalMs, int? WindowMs) : Op;
public sealed record Cancel(int Entity, int Key) : Op;
public sealed record CancelAll(int Entity) : Op;
public sealed record ListReminders(int Entity) : Op;
public sealed record Tick(int Ms) : Op;
public sealed record Lag(int Ms) : Op;
public sealed record Restart : Op;
public sealed record SetRecipient(int Entity, Recipient Mode) : Op;
public sealed record AckOutstanding(int? Entity = null) : Op;
public sealed record SetRegion(int Region, bool Present) : Op;
public sealed record InjectFault(StorageCall Call, FaultKind Kind, int Count, int DelayMs, bool FireTimersWhileSlow) : Op;
public sealed record HealAndWait : Op;
