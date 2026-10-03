//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.Query;
using Akka.Persistence.Query.InMemory;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    /// Builds the system the way an Akka.Hosting app does, with the switch off: the plugin options supply their factories,
    /// the event adapter and the read journal come from the Hosting builders, and no HOCON names a plugin class.
    /// Then it persists, recovers, snapshots and queries. The journal and snapshot store are not built-in types, the way a
    /// third-party plugin is not, and the built-in <c>inmem</c> plugins run beside them.
    /// </summary>
    private static async Task RunPersistenceAsync()
    {
        const string label = "persistence";
        Console.WriteLine($"[canary-persistence] creating ActorSystem '{label}' ...");

        // Serialization and Mailboxes log-and-continue when a configured type name does not resolve, so a
        // silent boot is not proof of anything. This watchdog turns any WARNING or ERROR that reaches the
        // stdout logger into a failed run.
        var watchdog = new LogWatchdogFilter();

        var host = Host.CreateApplicationBuilder();
        host.Logging.ClearProviders();
        host.Services.AddAkka(label, builder => builder
            .WithJournalAndSnapshot(new CanaryJournalOptions(), new CanarySnapshotOptions(),
                configureJournal: journal => journal.AddWriteEventAdapter("canary-tagger", _ => new CanaryTagger(), typeof(CanaryEvent)),
                configureSnapshot: null)
            .WithInMemoryJournal(_ => { }, journalId: "inmem", isDefaultPlugin: false)
            .WithInMemorySnapshotStore("inmem", isDefaultPlugin: false)
            .WithReadJournal(InMemoryReadJournal.Identifier,
                static (system, config) => new InMemoryReadJournalProvider(system, config),
                InMemoryReadJournal.DefaultConfiguration().GetConfig(InMemoryReadJournal.Identifier))
            .WithCustomSerializer("canary", new[] { typeof(CanaryEvent), typeof(CanarySnapshot) }, static system => new CanarySerializer(system))
            .AddSetup(new LogFilterSetup([watchdog])));

        using var app = host.Build();
        await app.StartAsync();
        var system = app.Services.GetRequiredService<ActorSystem>();
        try
        {
            watchdog.ThrowIfAnyProblems(label, "startup");

            AssertSerializer(system);

            // send a and b together, so the actor stashes b while a's write is in flight; snapshot at 2; then send c
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
            AssertRegisteredPluginsAreUsed(system);
            await AssertBuiltInPluginsAsync(label, system);

            system.Log.Info("[canary-persistence] {0}: round-trip complete", label);
            watchdog.ThrowIfAnyProblems(label, "post-boot");
        }
        finally
        {
            await app.StopAsync();
        }

        Console.WriteLine($"[canary-persistence] {label}: terminated");
    }

    /// <summary>
    /// The default journal and snapshot store are the registered ones, built by the
    /// factories, once each, with the default config in the journal's section. The persist, recovery and queries above only work when they ran.
    /// </summary>
    private static void AssertRegisteredPluginsAreUsed(ActorSystem system)
    {
        const string label = "persistence";
        var persistence = Persistence.Instance.Apply(system);

        // the shipped default configurator resolves from the built-in table
        Require(label, ReferenceEquals(persistence.DefaultInternalStashOverflowStrategy, ThrowOverflowExceptionStrategy.Instance),
            "the built-in stash overflow configurator did not resolve");

        Require(label, CanaryJournal.Instances == 1, $"the registered journal was built {CanaryJournal.Instances} times, not 1");
        Require(label, CanaryJournal.Marker == "from-default", $"the registered journal got marker [{CanaryJournal.Marker}] from its default config");
        Require(label, CanarySnapshotStore.Instances == 1, $"the registered snapshot store was built {CanarySnapshotStore.Instances} times, not 1");
        Console.WriteLine($"[canary-persistence] {label}: registered journal and snapshot store are in use, and the built-in stash overflow strategy resolves");
    }

    /// <summary>
    /// The built-in plugins still start by name with the switch off, next to the registered ones.
    /// </summary>
    private static async Task AssertBuiltInPluginsAsync(string label, ActorSystem system)
    {
        var actor = system.ActorOf(Props.Create(() => new CanaryPersistentActor(
            "canary-builtin", "akka.persistence.journal.inmem", "akka.persistence.snapshot-store.inmem")), "canary-builtin");

        Require(label, await actor.Ask<int>("a", AskTimeout) == 1, "the built-in inmem journal did not persist");
        Require(label, await actor.Ask<long>(new SaveNow(), AskTimeout) == 1, "the built-in inmem snapshot store did not save");
        Console.WriteLine($"[canary-persistence] {label}: the built-in inmem journal and snapshot store work too");
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
    /// A journal whose options supply no factory must fail at start with the switch off, and say why.
    /// </summary>
    private static async Task RunUnregisteredAsync()
    {
        const string label = "unregistered";

        var host = Host.CreateApplicationBuilder();
        host.Logging.ClearProviders();
        host.Services.AddAkka(label, builder => builder.WithJournal(new UnregisteredJournalOptions()));

        using var app = host.Build();
        await app.StartAsync();
        try
        {
            var system = app.Services.GetRequiredService<ActorSystem>();
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

            Require(label, thrown is not null, "a journal with no factory started with Akka.DynamicTypeLoading off");
            Require(label, thrown!.Message.Contains("Akka.DynamicTypeLoading", StringComparison.Ordinal)
                           && thrown.Message.Contains("akka.persistence.journal.unregistered.class", StringComparison.Ordinal)
                           && thrown.Message.Contains("Akka.Persistence.Hosting", StringComparison.Ordinal),
                $"the exception did not name the setting, the switch and Akka.Persistence.Hosting: [{thrown.Message}]");
            Console.WriteLine($"[canary-persistence] {label}: a journal with no factory failed at start as designed");
        }
        finally
        {
            await app.StopAsync();
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
