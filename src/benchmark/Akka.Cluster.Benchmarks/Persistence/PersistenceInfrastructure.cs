// -----------------------------------------------------------------------
//  <copyright file="PersistenceInfrastructure.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence;
using Akka.Util.Internal;

namespace Akka.Cluster.Benchmarks.Persistence;

public sealed class Init
{
    public static readonly Init Instance = new();

    private Init()
    {
    }
}

public sealed class Finish
{
    public static readonly Finish Instance = new();

    private Finish()
    {
    }
}

public sealed class Done
{
    public static readonly Done Instance = new();

    private Done()
    {
    }
}

public sealed class Finished
{
    public readonly long State;

    public Finished(long state)
    {
        State = state;
    }
}

public sealed class RecoveryFinished
{
    public static readonly RecoveryFinished Instance = new();

    private RecoveryFinished()
    {
    }
}

public sealed class Store
{
    public readonly int Value;

    public Store(int value)
    {
        Value = value;
    }
}

public sealed class Stored
{
    public readonly int Value;

    public Stored(int value)
    {
        Value = value;
    }
}

/// <summary>
///     Query to <see cref="BenchmarkDoneActor" /> that will be used to signal termination
/// </summary>
public sealed class IsFinished
{
    public static readonly IsFinished Instance = new();

    private IsFinished()
    {
    }
}

public sealed class BenchmarkDoneActor : ReceiveActor
{
    private IActorRef _asker;
    private int _expected;

    public BenchmarkDoneActor(int expected)
    {
        Receive<IsFinished>(_ =>
        {
            _expected = expected;
            _asker = Sender;
        });

        Receive<Finished>(_ =>
        {
            // this will terminate the benchmark
            if (--_expected <= 0)
                _asker.Tell(Done.Instance);
        });

        Receive<RecoveryFinished>(_ =>
        {
            // this will terminate the benchmark
            if (--_expected <= 0)
                _asker?.Tell(RecoveryFinished.Instance);
        });
    }
}

public sealed class PerformanceTestActor : PersistentActor
{
    private readonly IActorRef _doneActor;
    private readonly long _target;
    private long _state;

    public PerformanceTestActor(string persistenceId, IActorRef doneActor, long target)
    {
        _doneActor = doneActor;
        PersistenceId = persistenceId;
        _target = target;
    }

    public override string PersistenceId { get; }

    protected override bool ReceiveRecover(object message)
    {
        switch (message)
        {
            case Stored s:
                _state += s.Value;
                break;
            default:
                return false;
        }

        return true;
    }

    protected override void OnReplaySuccess()
    {
        _doneActor.Tell(RecoveryFinished.Instance);
    }

    protected override bool ReceiveCommand(object message)
    {
        switch (message)
        {
            case Store store:
                PersistAsync(new Stored(store.Value), s =>
                {
                    _state += s.Value;
                    if (_state >= _target)
                        _doneActor.Tell(new Finished(_state));
                });
                break;
            case Init _:
                var sender = Sender;
                PersistAsync(new Stored(0), s =>
                {
                    _state += s.Value;
                    sender.Tell(Done.Instance);
                });
                break;
            case Finish _:
                Sender.Tell(new Finished(_state));
                break;
            default:
                return false;
        }

        return true;
    }
}

public static class PersistenceInfrastructure
{
    public static readonly AtomicCounter DbCounter = new(0);

    public static Config GenerateJournalConfig()
    {
        var config = ConfigurationFactory.ParseString(@"
            akka {
                persistence.journal {
                    plugin = ""akka.persistence.journal.inmem""
                    # In-memory journal plugin.
                    akka.persistence.journal.inmem {
                        # Class name of the plugin.
                        class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                        # Dispatcher for the plugin actor.
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                    }
                }
            }");

        return config;
    }
}