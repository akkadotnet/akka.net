using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Model;

/// <summary>A check failed. The first line names the rule.</summary>
public sealed class ModelViolation(string message) : Exception(message)
{
    public override string ToString() => Message; // CsCheck prints this; a stack trace would bury it
}

/// <summary>How an entity answers a delivery.</summary>
public enum Recipient
{
    Ack,
    Nack,
    Ignore,
}

/// <summary>
/// An application that uses reminders, for tests: a real <see cref="ReminderScheduler"/> on a virtual
/// clock, three entities (0 and 1 in shard region 0, 2 in region 1) with two reminder keys each, and a
/// <see cref="Journal"/> of everything the application did and saw. It judges nothing, except that the
/// scheduler must answer and must go idle when time stands still.
/// The scheduler starts on the first call. <see cref="Stop"/> ends the run.
/// </summary>
public sealed class ReminderApp
{
    public const int Entities = 3;
    public const int Keys = 2;
    public const int Regions = 2;

    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentQueue<ReminderApp> Stopped = new();

    private readonly IModelStorageFactory _factory;
    private readonly HarnessSignals _signals = new();
    private readonly Recipient[] _recipients;
    private readonly List<Delivered> _unanswered = [];
    private ModelHost? _host;
    private RecordingShardRegionResolver _resolver = null!;
    private IReminderStorage _inner = null!;
    private FaultyRecordingStorage _storage = null!;
    private IActorRef _scheduler = ActorRefs.Nobody;
    private IActorRef _supervisor = ActorRefs.Nobody;
    private int _deliveriesSeen;
    private int _nextId = 1;
    private bool _stopped;

    /// <param name="settings">Scheduler settings.</param>
    /// <param name="answer">How entities answer a delivery until <see cref="SetRecipient"/> says otherwise.</param>
    /// <param name="storage">Where reminders are stored; in memory when null.</param>
    public ReminderApp(ModelSettings settings, Recipient answer = Recipient.Ack, IModelStorageFactory? storage = null)
    {
        Settings = settings;
        _factory = storage ?? InMemoryModelStorageFactory.Instance;
        _recipients = Enumerable.Repeat(answer, Entities).ToArray();
        Journal = new Journal(() => _host?.Clock.Now ?? VirtualClock.Origin);
    }

    public ModelSettings Settings { get; }
    public Journal Journal { get; }
    private VirtualClock Clock => _host!.Clock;

    public static ReminderEntity EntityOf(int index) => new(index == 2 ? "region-1" : "region-0", index == 1 ? "e1" : "e0");
    public static int RegionOf(int entity) => entity == 2 ? 1 : 0;
    public static ReminderKey KeyOf(int index) => new($"k{index}");
    public static int IndexOf(ReminderEntity entity) => Enumerable.Range(0, Entities).First(i => EntityOf(i) == entity);
    public static int IndexOf(ReminderKey key) => Enumerable.Range(0, Keys).First(i => KeyOf(i) == key);

    // ------------------------------------------------------------------ what an application does

    /// <summary>Schedules a one-off reminder due <paramref name="dueIn"/> from now. Returns the reply.</summary>
    public Task<ReminderScheduleResponseCode> ScheduleOnce(int entity, int key, TimeSpan dueIn, TimeSpan? window = null) =>
        Schedule(entity, key, dueIn, null, window);

    /// <summary>Schedules a recurring reminder first due <paramref name="dueIn"/> from now. Returns the reply.</summary>
    public Task<ReminderScheduleResponseCode> ScheduleRecurring(int entity, int key, TimeSpan dueIn, TimeSpan every, TimeSpan? window = null) =>
        Schedule(entity, key, dueIn, every, window);

    private Task<ReminderScheduleResponseCode> Schedule(int entity, int key, TimeSpan dueIn, TimeSpan? every, TimeSpan? window) => Do(async () =>
    {
        var id = _nextId++; // the payload: a delivery carries it, so it names the call that asked for it
        var firstDue = Clock.Now + dueIn;
        var reply = await AskAsync<ReminderProtocol.ReminderScheduled>(new ReminderProtocol.ScheduleReminder(
            EntityOf(entity), KeyOf(key), firstDue, Journal.Payload(id), every, window));
        return Journal.Add(new Scheduled(id, entity, key, firstDue, every, window, reply.ResponseCode)).Reply;
    });

    /// <summary>Cancels one reminder, or every reminder of the entity when <paramref name="key"/> is null.</summary>
    public Task<ReminderCancelResponseCode> Cancel(int entity, int? key) => Do(async () =>
    {
        var reply = await AskAsync<ReminderProtocol.RemindersCancelled>(key is { } k
            ? new ReminderProtocol.CancelReminder(EntityOf(entity), KeyOf(k))
            : new ReminderProtocol.CancelAllReminders(EntityOf(entity)));
        return Journal.Add(new CancelAnswered(entity, key, reply.ResponseCode)).Reply;
    });

    public Task List(int entity) => Do(async () =>
    {
        var reply = await AskAsync<ReminderProtocol.RemindersForEntity>(new ReminderProtocol.GetReminders(EntityOf(entity)));
        Journal.Add(new Listed(entity, reply.ResponseCode,
            reply.Reminders.Select(r => (IndexOf(r.Key), Journal.IdOf(r.Message), r.DueTimeUtc)).ToList()));
    });

    /// <summary>Acks every delivery that got no answer yet (all entities, or one).</summary>
    public Task AckUnanswered(int? entity = null) => Do(async () =>
    {
        var late = _unanswered.Where(d => entity is null || d.Entity == entity).GroupBy(d => (d.Entity, d.Key, d.Due)).Select(g => g.Last()).ToList();
        _unanswered.RemoveAll(d => entity is null || d.Entity == entity);
        // In batches: a long-silent entity can owe thousands of acks, and each ask has a real-time limit.
        foreach (var batch in late.Chunk(200))
            await Task.WhenAll(batch.Select(AckAsync));
    });

    /// <summary>Acks a particular observed delivery, including a late or duplicate acknowledgement.</summary>
    public Task Acknowledge(Delivered delivery) => Do(() => AckAsync(delivery));

    /// <summary>From now on this entity acks at once, nacks at once, or stays silent.</summary>
    public Task SetRecipient(int entity, Recipient mode) => Do(() => _recipients[entity] = mode);

    // ------------------------------------------------------------------ what happens around it

    /// <summary>Time passes with the scheduler keeping up: stop at every timer on the way and let it run.</summary>
    public Task Tick(TimeSpan by) => Do(async () =>
    {
        var target = Clock.Now + by;
        for (var steps = 0; Clock.NextUserDue() is { } next && next <= target; steps++)
        {
            if (steps > 50_000)
                throw new ModelViolation($"TimersRunAway: more than 50000 timers fired in a {by.TotalSeconds}s tick");
            Clock.AdvanceTo(next);
            await SettleAndRespondAsync();
        }

        Clock.AdvanceTo(target);
    });

    /// <summary>The scheduler was stalled: time jumps and no timer fires on the way.</summary>
    public Task Lag(TimeSpan by) => Do(() =>
    {
        Journal.Stalled(by + Settings.RecoveryTime);
        Clock.Advance(by);
        Journal.Add(new Lagged());
    });

    /// <summary>Stops the scheduler and starts a new one on the same storage.</summary>
    public Task Restart() => Do(async () =>
    {
        await StopSchedulerAsync();
        await StartSchedulerAsync();
    });

    public Task SetRegion(int region, bool up) => Do(() =>
    {
        _resolver.SetPresent(region, up);
        if (up)
            Journal.RegionUp(region, Settings.RecoveryTime);
        else if (!Journal.Read().Troubles.Any(t => t.Region == region && t.Until == DateTimeOffset.MaxValue))
            Journal.RegionDown(region);
        Journal.Add(new RegionSet(region, up));
    });

    /// <summary>Makes the next <paramref name="count"/> storage calls of one kind misbehave.</summary>
    public Task InjectFault(StorageCall call, FaultKind kind, int count, TimeSpan delay, bool fireTimersWhileSlow) =>
        Do(() => _storage.AddFault(call, kind, count, delay, fireTimersWhileSlow));

    /// <summary>Storage works again and every shard region is up; then enough time passes to recover.</summary>
    public async Task HealAndWait()
    {
        await Do(() => _storage.ClearFaults());
        for (var region = 0; region < Regions; region++)
            await SetRegion(region, true);
        await Tick(TimeSpan.FromSeconds(30) + (Journal.Read().Troubles.Count > 0 ? Settings.RecoveryTime : TimeSpan.Zero));
    }

    /// <summary>Ends the run and returns everything the application did and saw.</summary>
    public History Stop()
    {
        if (!_stopped && _host is not null)
            Stopped.Enqueue(this); // stopping is async; the next run to start, or DisposeStoppedAsync, finishes it
        _stopped = true;
        return Journal.Read();
    }

    /// <summary>Finishes stopping every run that has ended. Call it after sampling.</summary>
    public static async Task DisposeStoppedAsync()
    {
        while (Stopped.TryDequeue(out var app))
        {
            await app.StopSchedulerAsync();
            await app._factory.DestroyAsync(app._inner);
            if (app._signals.Stuck)
                await app._host!.DisposeAsync();
            else
                ModelHost.Return(app._host!);
        }
    }

    // ------------------------------------------------------------------ plumbing

    private Task Do(Action action) => Do(() =>
    {
        action();
        return Task.FromResult(0);
    });

    private Task Do(Func<Task> action) => Do(async () =>
    {
        await action();
        return 0;
    });

    /// <summary>Runs one operation: start on first use, do it, let the scheduler settle, mark the end.</summary>
    private async Task<T> Do<T>(Func<Task<T>> action)
    {
        try
        {
            if (_host is null)
                await StartAsync();
            var result = await action();
            await SettleAndRespondAsync();
            Journal.Add(new Done());
            return result;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private async Task StartAsync()
    {
        await DisposeStoppedAsync();
        _host = ModelHost.Rent();
        Clock.Reset();
        _resolver = new RecordingShardRegionResolver(Journal, _signals);
        _inner = await _factory.CreateAsync(_host.System);
        _storage = new FaultyRecordingStorage(_inner, Clock, Journal, _signals, Settings.RecoveryTime);
        await StartSchedulerAsync();
        await SettleAsync();
    }

    private async Task StartSchedulerAsync()
    {
        lock (_signals.Lock)
        {
            _signals.InitPhase = true;
            _signals.InitFailed = false;
        }

        var props = Props.Create(typeof(ReminderScheduler), Settings.ToReminderSettings(), _resolver, _storage, Clock);
        var started = new TaskCompletionSource<IActorRef>(TaskCreationOptions.RunContinuationsAsynchronously);
        _supervisor = _host!.System.ActorOf(Props.Create(typeof(SchedulerSupervisor), props, _signals, started), _host.NextActorName());
        _scheduler = await started.Task;

        for (var spin = 0; ; spin++)
        {
            lock (_signals.Lock)
                if (!_signals.InitPhase)
                    return;
            RetryFailedInitialLoad();
            if (spin > 2_000_000)
                throw new ModelViolation("SchedulerStarts: the scheduler never finished loading its initial state");
            if (spin < 2_000)
                await Task.Yield();
            else
                await Task.Delay(1);
        }
    }

    /// <summary>
    /// A failed initial load retries on a StorageTimeout * 2 timer. Nothing else can happen until then,
    /// so move the clock to that timer as soon as the actor has set it.
    /// </summary>
    private void RetryFailedInitialLoad()
    {
        lock (_signals.Lock)
        {
            if (!_signals.InitPhase || !_signals.InitFailed)
                return;
            if (Clock.NextUserDue() is not { } due || due > Clock.Now + ModelSettings.StorageTimeout * 2)
                return;
            _signals.InitFailed = false;
            Clock.AdvanceTo(due);
        }

        Clock.FireDue();
    }

    private async Task StopSchedulerAsync()
    {
        if (_scheduler.IsNobody())
            return;
        try
        {
            await _supervisor.GracefulStop(AskTimeout);
        }
        catch (TaskCanceledException)
        {
            // Graceful shutdown timed out. Retire the whole host during cleanup so actors from
            // this trace cannot survive into another sample; a restart cannot safely continue.
            _signals.Stuck = true;
            if (!_stopped)
                throw new ModelViolation("SchedulerStops: the scheduler did not stop before restart");
        }

        _scheduler = ActorRefs.Nobody;
    }

    private async Task<T> AskAsync<T>(object message)
    {
        try
        {
            var ask = _scheduler.Ask<T>(message, AskTimeout);
            // A scheduler that restarted and cannot reload its state answers only after its retry timer fires.
            while (!ask.IsCompleted && await Task.WhenAny(ask, Task.Delay(10)) != ask)
                RetryFailedInitialLoad();
            return await ask;
        }
        catch (AskTimeoutException ex)
        {
            _signals.Stuck = true;
            throw new ModelViolation($"SchedulerResponds: no answer to {message.GetType().Name} within {AskTimeout.TotalSeconds}s of real time, at {Journal.T(Clock.Now)}: {ex.Message}\n{Journal.Read().Timeline(15)}");
        }
    }

    private async Task AckAsync(Delivered d)
    {
        var asked = Journal.Seq;
        var reply = await AskAsync<ReminderProtocol.ReminderAckResponse>(
            new ReminderProtocol.ReminderAck(EntityOf(d.Entity), KeyOf(d.Key), d.Due));
        Journal.Add(new AckAnswered(asked, d.Id, d.Entity, d.Key, d.Due, reply.ResponseCode));
        await ProbeAsync(); // the reply is sent before the scheduler has finished the ack
    }

    private async Task NackAsync(Delivered d)
    {
        var asked = Journal.Seq;
        var reply = await AskAsync<ReminderProtocol.ReminderNackResponse>(
            new ReminderProtocol.ReminderNack(EntityOf(d.Entity), KeyOf(d.Key), d.Due, "model nack"));
        Journal.Add(new NackAnswered(asked, d.Id, d.Entity, d.Key, d.Due, reply.ResponseCode, reply.NextAttemptAtUtc));
    }

    private Task ProbeAsync() => AskAsync<ReminderProtocol.ReminderOccurrenceStatusResponse>(
        new ReminderProtocol.GetReminderOccurrenceStatus(FaultyRecordingStorage.ProbeEntity, new ReminderKey("probe"), VirtualClock.Origin));

    /// <summary>
    /// Waits until the scheduler is idle: no due timers, and two round trips through its mailbox with no
    /// storage call or delivery in between. Retrying at once after a failed storage call is in spec, so
    /// each failed call buys extra rounds; with healthy storage the scheduler may not spin.
    /// </summary>
    private async Task SettleAsync()
    {
        var quiet = 0;
        var failedAtStart = _signals.FailedCalls;
        for (var round = 0; quiet < 2; round++)
        {
            var fired = Clock.FireDue();
            var before = _signals.Activity;
            await ProbeAsync();
            quiet = fired == 0 && _signals.Activity == before ? quiet + 1 : 0;
            if (round - 20 * (_signals.FailedCalls - failedAtStart) > 400)
                throw new ModelViolation($"GoesIdle: the scheduler kept working for {round} rounds without time passing, at {Journal.T(Clock.Now)}");
        }
    }

    /// <summary>Lets the scheduler finish what it can do without time passing, and lets recipients answer.</summary>
    private async Task SettleAndRespondAsync()
    {
        for (var round = 0; ; round++)
        {
            await SettleAsync();
            var fresh = Journal.NewDeliveries(ref _deliveriesSeen);
            if (fresh.Count == 0)
                return;
            if (round > 5_000)
                throw new ModelViolation("GoesIdle: deliveries kept coming without time passing");

            // One answer per occurrence: a slow pass can deliver the same one twice before the recipient runs.
            var latest = fresh.GroupBy(d => (d.Id, d.Due)).Select(g => g.Last()).ToList();
            _unanswered.AddRange(latest.Where(d => _recipients[d.Entity] == Recipient.Ignore));
            await Task.WhenAll(latest.Where(d => _recipients[d.Entity] == Recipient.Ack).Select(AckAsync));
            foreach (var d in latest.Where(d => _recipients[d.Entity] == Recipient.Nack))
                await NackAsync(d);
        }
    }
}
