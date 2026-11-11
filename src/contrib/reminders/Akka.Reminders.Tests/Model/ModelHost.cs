using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Configuration;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Model;

/// <summary>
/// An actor system running on a <see cref="VirtualClock"/>. Hosts are pooled: CsCheck runs scenarios on
/// several threads, and each scenario borrows a whole host (a host's clock is not shared).
/// </summary>
public sealed class ModelHost : IAsyncDisposable
{
    private static readonly ConcurrentBag<ModelHost> Pool = [];
    private static int _hostCounter;
    private int _actorCounter;

    private ModelHost()
    {
        var logLevel = Environment.GetEnvironmentVariable("REMINDERS_CSCHECK_LOGLEVEL") ?? "OFF";
        var config = ConfigurationFactory.ParseString($$"""
            akka.scheduler.implementation = "{{typeof(VirtualClock).AssemblyQualifiedName}}"
            akka.loglevel = {{logLevel}}
            akka.stdout-loglevel = OFF
            akka.log-dead-letters = off
            akka.log-dead-letters-during-shutdown = off
            """);
        System = ActorSystem.Create($"model-{Interlocked.Increment(ref _hostCounter)}", config);
        Clock = (VirtualClock)System.Scheduler;
    }

    public ActorSystem System { get; }

    public VirtualClock Clock { get; }

    public string NextActorName() => $"reminder-scheduler-{Interlocked.Increment(ref _actorCounter)}";

    public static ModelHost Rent() => Pool.TryTake(out var host) ? host : new ModelHost();

    public static void Return(ModelHost host) => Pool.Add(host);

    public async ValueTask DisposeAsync() => await System.Terminate();
}

/// <summary>
/// Shard-region resolver for the model: regions can be switched on and off, and every delivery is
/// written to the <see cref="Journal"/> (with the virtual time) instead of being sent to an actor.
/// </summary>
public sealed class RecordingShardRegionResolver(Journal journal, HarnessSignals signals) : IShardRegionResolver
{
    private readonly bool[] _present = Enumerable.Repeat(true, ReminderApp.Regions).ToArray();

    public void SetPresent(int region, bool present)
    {
        lock (_present)
            _present[region] = present;
    }

    public IActorRef? TryResolve(ReminderEntity entity)
    {
        lock (_present)
            return _present[ReminderApp.RegionOf(ReminderApp.IndexOf(entity))] ? ActorRefs.Nobody : null;
    }

    public void DeliverReminder(ReminderEntity entity, ReminderEnvelope envelope, IActorRef? sender = null)
    {
        signals.Touch();
        journal.Add(new Delivered(Journal.IdOf(envelope.Message), ReminderApp.IndexOf(entity), ReminderApp.IndexOf(envelope.Key),
            envelope.DueTimeUtc, envelope.Deadline.UtcDateTime));
    }
}

/// <summary>Creates a fresh, empty storage for each scenario.</summary>
public interface IModelStorageFactory
{
    string Name { get; }

    Task<IReminderStorage> CreateAsync(ActorSystem system);

    Task DestroyAsync(IReminderStorage storage);
}

public sealed class InMemoryModelStorageFactory : IModelStorageFactory
{
    public static InMemoryModelStorageFactory Instance { get; } = new();

    public string Name => "InMemory";

    public Task<IReminderStorage> CreateAsync(ActorSystem system) => Task.FromResult<IReminderStorage>(new InMemoryReminderStorage());

    public Task DestroyAsync(IReminderStorage storage) => Task.CompletedTask;
}

/// <summary>
/// Parent of the scheduler under test. Restarts it when it throws (as the default supervisor would) and
/// tells the harness, because a restarted scheduler loads its state again before it answers anything.
/// </summary>
public sealed class SchedulerSupervisor : UntypedActor
{
    private readonly Props _child;
    private readonly HarnessSignals _log;
    private readonly TaskCompletionSource<IActorRef> _started;

    public SchedulerSupervisor(Props child, HarnessSignals log, TaskCompletionSource<IActorRef> started)
    {
        _child = child;
        _log = log;
        _started = started;
    }

    protected override void PreStart() => _started.TrySetResult(Context.ActorOf(_child, "scheduler"));

    protected override SupervisorStrategy SupervisorStrategy() => new OneForOneStrategy(ex =>
    {
        lock (_log.Lock)
        {
            _log.Crashes++;
            _log.InitPhase = true;
            _log.InitFailed = false;
        }

        return Directive.Restart;
    });

    protected override void OnReceive(object message) => Unhandled(message);
}
