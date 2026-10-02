//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Query;
using Akka.Persistence.Query.InMemory;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace Akka.Persistence.AOT.App;

internal static class Program
{
    private const string PersistenceId = "canary-1";
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TerminateTimeout = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
        // a crash on a pool thread would otherwise kill the process with no diagnosis at all
        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
        {
            PrintFailure(e.ExceptionObject as Exception);
            Console.Out.Flush();
        };

        try
        {
            await RunPersistenceAsync();
            await RunUnregisteredAsync();

            Console.WriteLine("[canary-persistence] OK");
            return 0;
        }
        catch (Exception ex)
        {
            PrintFailure(ex);
            return 1;
        }
    }

    /// <summary>
    /// Boots with the switch off and everything registered in code, then persists, recovers, snapshots and queries.
    /// </summary>
    private static async Task RunPersistenceAsync()
    {
        const string label = "persistence";
        Console.WriteLine($"[canary-persistence] creating ActorSystem '{label}' ...");

        var config = ConfigurationFactory.ParseString($$"""
                akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
                akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
                akka.persistence.journal.inmem {
                    event-adapters {
                        canary-tagger = "{{typeof(CanaryTagger).FullName}}, {{typeof(CanaryTagger).Assembly.GetName().Name}}"
                    }
                    event-adapter-bindings {
                        "{{typeof(CanaryEvent).FullName}}, {{typeof(CanaryEvent).Assembly.GetName().Name}}" = canary-tagger
                    }
                }
                """)
            // PersistenceQuery injects this by reflection when the switch is on. With it off the app supplies it.
            .WithFallback(InMemoryReadJournal.DefaultConfiguration());

        var setup = BootstrapSetup.Create().WithConfig(config)
            .And(PersistencePluginSetup.Empty
                .WithEventAdapter(static _ => new CanaryTagger())
                .WithEventAdapterBinding<CanaryEvent>())
            .And(PersistenceQuerySetup.Empty
                .WithReadJournal(static (system, journalConfig) => new InMemoryReadJournalProvider(system, journalConfig)))
            .And(SerializationSetup.Create(static system => ImmutableHashSet.Create(
                SerializerDetails.Create("canary", new CanarySerializer(system),
                    ImmutableHashSet.Create(typeof(CanaryEvent), typeof(CanarySnapshot))))));

        // Serialization and Mailboxes log-and-continue when a configured type name does not resolve, so a
        // silent boot is not proof of anything. This watchdog turns any WARNING or ERROR that reaches the
        // stdout logger into a failed run.
        var watchdog = new LogWatchdogFilter();
        var system = ActorSystem.Create(label, setup.And(new LogFilterSetup([watchdog])));
        try
        {
            watchdog.ThrowIfAnyProblems(label, "startup");

            AssertSerializer(system);

            // persist three events in one burst (the actor stashes while each write is in flight), snapshot at 2
            var first = system.ActorOf(Props.Create(() => new CanaryPersistentActor(PersistenceId)), "canary-1");
            var counts = await Task.WhenAll(
                first.Ask<int>("a", AskTimeout),
                first.Ask<int>("b", AskTimeout));
            Require(label, counts.SequenceEqual([1, 2]), $"first two persists replied [{string.Join(',', counts)}]");

            var snapshotSequenceNr = await first.Ask<long>(new SaveNow(), AskTimeout);
            Require(label, snapshotSequenceNr == 2, $"snapshot was saved at sequence number {snapshotSequenceNr}, not 2");

            Require(label, await first.Ask<int>("c", AskTimeout) == 3, "third persist did not reply 3");
            Console.WriteLine($"[canary-persistence] {label}: persisted 3 events, snapshot at 2");

            await first.GracefulStop(AskTimeout);

            // a new incarnation recovers from the snapshot plus the event after it
            var second = system.ActorOf(Props.Create(() => new CanaryPersistentActor(PersistenceId)), "canary-1-again");
            var state = await second.Ask<CanaryState>(new GetState(), AskTimeout);
            Require(label, state.Values.SequenceEqual(["a", "b", "c"]), $"recovered state was [{string.Join(',', state.Values)}] snapshot {state.SnapshotSequenceNr}");
            Require(label, state.SnapshotSequenceNr == 2, $"recovery used snapshot {state.SnapshotSequenceNr}, not 2");
            Console.WriteLine($"[canary-persistence] {label}: recovered [{string.Join(',', state.Values)}] from the snapshot at {state.SnapshotSequenceNr}");

            await AssertQueriesAsync(label, system);

            system.Log.Info("[canary-persistence] {0}: round-trip complete", label);
            watchdog.ThrowIfAnyProblems(label, "post-boot");
        }
        finally
        {
            await system.Terminate().WaitAsync(TerminateTimeout);
        }

        Console.WriteLine($"[canary-persistence] {label}: terminated");
    }

    private static void AssertSerializer(ActorSystem system)
    {
        const string label = "persistence";
        var evt = new CanaryEvent("x");
        var serializer = system.Serialization.FindSerializerFor(evt);
        Require(label, serializer is CanarySerializer, $"CanaryEvent is bound to [{serializer.GetType().FullName}]");

        var bytes = serializer.ToBinary(evt);
        var roundTripped = system.Serialization.Deserialize(bytes, serializer.Identifier, serializer.Manifest(evt));
        Require(label, Equals(roundTripped, evt), "CanaryEvent did not survive a serializer round-trip");
    }

    private static async Task AssertQueriesAsync(string label, ActorSystem system)
    {
        var readJournal = PersistenceQuery.Get(system).ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
        var materializer = system.Materializer();

        var byPersistenceId = await readJournal
            .CurrentEventsByPersistenceId(PersistenceId, 0, long.MaxValue)
            .RunWith(Sink.Seq<EventEnvelope>(), materializer)
            .WaitAsync(AskTimeout);
        Require(label, byPersistenceId.Count == 3, $"CurrentEventsByPersistenceId returned {byPersistenceId.Count} envelopes, not 3");
        Require(label, byPersistenceId.Select(e => ((CanaryEvent)e.Event).Value).SequenceEqual(["a", "b", "c"]),
            "CurrentEventsByPersistenceId returned the events out of order or the wrong events");

        // the tag is added by CanaryTagger, so this also proves the registered event adapter ran
        var byTag = await readJournal
            .CurrentEventsByTag("canary", Offset.NoOffset())
            .RunWith(Sink.Seq<EventEnvelope>(), materializer)
            .WaitAsync(AskTimeout);
        Require(label, byTag.Count == 3, $"CurrentEventsByTag returned {byTag.Count} envelopes, not 3 - the event adapter did not run");

        Console.WriteLine($"[canary-persistence] {label}: CurrentEventsByPersistenceId and CurrentEventsByTag both returned 3 events");
    }

    /// <summary>
    /// A journal that nothing registers must fail at start with the switch off, and say why.
    /// </summary>
    private static async Task RunUnregisteredAsync()
    {
        const string label = "unregistered";
        var config = ConfigurationFactory.ParseString($$"""
            akka.persistence.journal.plugin = "akka.persistence.journal.unregistered"
            akka.persistence.journal.unregistered {
                class = "{{typeof(UnregisteredJournal).FullName}}, {{typeof(UnregisteredJournal).Assembly.GetName().Name}}"
                plugin-dispatcher = "akka.actor.default-dispatcher"
            }
            """);

        var system = ActorSystem.Create(label, config);
        try
        {
            var persistence = Persistence.Instance.Apply(system);
            ConfigurationException? thrown = null;
            try
            {
                persistence.JournalFor("akka.persistence.journal.unregistered");
            }
            catch (ConfigurationException ex)
            {
                thrown = ex;
            }

            Require(label, thrown is not null, "an unregistered journal started with Akka.DynamicTypeLoading off");
            Require(label, thrown!.Message.Contains("Akka.DynamicTypeLoading", StringComparison.Ordinal)
                           && thrown.Message.Contains("akka.persistence.journal.unregistered.class", StringComparison.Ordinal),
                $"the exception did not name the setting and the switch: [{thrown.Message}]");
            Console.WriteLine($"[canary-persistence] {label}: an unregistered journal failed at start as designed");
        }
        finally
        {
            await system.Terminate().WaitAsync(TerminateTimeout);
        }

        Console.WriteLine($"[canary-persistence] {label}: terminated");
    }

    private static void Require(string label, bool condition, string problem)
    {
        if (!condition)
            throw new InvalidOperationException($"{label}: {problem}");
    }

    private static void PrintFailure(Exception? ex)
    {
        if (ex is null)
        {
            Console.WriteLine("[canary-persistence] FAILED: unhandled non-exception throw");
            return;
        }

        Console.WriteLine($"[canary-persistence] FAILED: {ex.GetType().FullName}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);

        var inner = ex.InnerException;
        var depth = 0;
        while (inner is not null && depth++ < 10)
        {
            Console.WriteLine($"[canary-persistence]  --> inner: {inner.GetType().FullName}: {inner.Message}");
            Console.WriteLine(inner.StackTrace);
            inner = inner.InnerException;
        }
    }
}
