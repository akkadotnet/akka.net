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
using Akka.Hosting;
using Akka.Persistence.Embedded;
using Akka.Persistence.Embedded.Hosting;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Hosting;
using Akka.Persistence.Query;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Akka.Persistence.Embedded.AOT.App;

internal static class Program
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LiveTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TerminateTimeout = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
        // a crash on a pool thread would otherwise kill the process with no diagnosis at all
        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
        {
            PrintFailure(e.ExceptionObject as Exception);
            Console.Out.Flush();
        };

        var files = new List<string>();
        try
        {
            CheckNativeLibrary();
            await RunTagTableAsync(NewDatabase(files));
            await RunCsvAsync(NewDatabase(files));
            await RunBothAsync(NewDatabase(files));
            await RunUnregisteredAsync(NewDatabase(files));

            Console.WriteLine("[canary-sqlite] OK");
            return 0;
        }
        catch (Exception ex)
        {
            PrintFailure(ex);
            return 1;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException e)
                {
                    // a leftover temp file does not fail the canary, but say so
                    Console.WriteLine($"[canary-sqlite] could not delete {file}: {e.Message}");
                }
            }
        }
    }

    private static string NewDatabase(List<string> files)
    {
        var path = Path.Combine(Path.GetTempPath(), $"canary-sqlite-{Guid.NewGuid():N}.db");
        files.Add(path);
        return path;
    }

    /// <summary>Opens a connection by hand: the native library must be found next to the executable.</summary>
    private static void CheckNativeLibrary()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select sqlite_version()";
        var version = (string?)command.ExecuteScalar();
        Require("native", !string.IsNullOrEmpty(version), "sqlite_version() returned nothing");
        Console.WriteLine($"[canary-sqlite] native: SQLite {version} loaded");
    }

    /// <summary>An Akka.Hosting host and the actor system it runs.</summary>
    private sealed class HostedCanary : IAsyncDisposable
    {
        private readonly IHost _host;

        public HostedCanary(IHost host)
        {
            _host = host;
        }

        public ActorSystem System => _host.Services.GetRequiredService<ActorSystem>();

        public IServiceProvider Services => _host.Services;

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync(TerminateTimeout);
            _host.Dispose();
        }
    }

    /// <summary>
    /// Builds the system the way an application does: Akka.Hosting plus <c>WithEmbeddedPersistence</c>. HOCON is not
    /// involved at all: no class names, no reference config, no loggers by name.
    /// </summary>
    private static async Task<HostedCanary> StartHostedAsync(string label, string path, TagWriteMode tagWriteMode, LogWatchdogFilter watchdog)
    {
        var appBuilder = Host.CreateApplicationBuilder();
        appBuilder.Logging.ClearProviders();
        appBuilder.Services.AddHealthChecks();
        appBuilder.Services.AddAkka(label, (builder, _) => builder
            .AddSetup(new LogFilterSetup([watchdog]))
            // With Akka.DynamicTypeLoading off core registers no fallback serializer, so the app binds its own.
            .WithCustomSerializer("canary", [typeof(CanaryEvent), typeof(CanarySnapshot)], system => new CanarySerializer(system))
            .WithEmbeddedPersistence(
                journalOptions: new EmbeddedJournalOptions
                {
                    ConnectionString = "Data Source=" + path,
                    TagStorageMode = tagWriteMode,
                    QueryRefreshInterval = TimeSpan.FromMilliseconds(100)
                },
                snapshotOptions: new EmbeddedSnapshotOptions { ConnectionString = "Data Source=" + path },
                journalBuilder: journal => journal
                    .AddWriteEventAdapter<CanaryTagger>("canary-tagger", [typeof(CanaryEvent)])
                    .WithHealthCheck(),
                snapshotBuilder: snapshot => snapshot.WithHealthCheck()));

        var host = appBuilder.Build();
        await host.StartAsync();
        return new HostedCanary(host);
    }

    /// <summary>A system without the Hosting registration: HOCON names the plugin, and with the switch off core refuses it.</summary>
    private static ActorSystem CreateUnregisteredSystem(string label, string path)
    {
        var config = ConfigurationFactory.ParseString($$"""
            akka.persistence.journal.plugin = "akka.persistence.journal.embedded"
            akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.embedded"
            akka.persistence.journal.embedded.connection-string = "Data Source={{path}}"
            akka.persistence.snapshot-store.embedded.connection-string = "Data Source={{path}}"
            """).WithFallback(SqlitePersistence.DefaultConfiguration);

        return ActorSystem.Create(label, config);
    }

    private static async Task HealthyAsync(string label, HostedCanary hosted)
    {
        var report = await hosted.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Require(label, report.Status == HealthStatus.Healthy && report.Entries.Count >= 2,
            $"the persistence health checks reported {report.Status} with {report.Entries.Count} entries");
    }

    /// <summary>
    /// Persists, snapshots, recovers, deletes and runs every query against a TagTable database.
    /// </summary>
    private static async Task RunTagTableAsync(string path)
    {
        const string label = "tagtable";
        Console.WriteLine($"[canary-sqlite] creating ActorSystem '{label}' ...");
        var watchdog = new LogWatchdogFilter();
        await using var hosted = await StartHostedAsync(label, path, TagWriteMode.TagTable, watchdog);
        var system = hosted.System;
        {
            watchdog.ThrowIfAnyProblems(label, "startup");
            await HealthyAsync(label, hosted);

            // 1. five events (two red), snapshot at 3, stop, recover
            var first = system.ActorOf(Props.Create(() => new CanaryPersistentActor("p1", true)), "p1-first");
            Require(label, await first.Ask<int>(new PersistCmd("a", false), AskTimeout) == 1, "persist a");
            Require(label, await first.Ask<int>(new PersistCmd("b", true), AskTimeout) == 2, "persist b");
            Require(label, await first.Ask<int>(new PersistCmd("c", false), AskTimeout) == 3, "persist c");
            var snapshotSequenceNr = await first.Ask<long>(new SaveNow(), AskTimeout);
            Require(label, snapshotSequenceNr == 3, $"snapshot was saved at sequence number {snapshotSequenceNr}, not 3");
            Require(label, await first.Ask<int>(new PersistCmd("d", true), AskTimeout) == 4, "persist d");
            Require(label, await first.Ask<int>(new PersistCmd("e", false), AskTimeout) == 5, "persist e");
            await first.GracefulStop(AskTimeout);

            var second = system.ActorOf(Props.Create(() => new CanaryPersistentActor("p1", true)), "p1-second");
            var state = await second.Ask<CanaryState>(new GetState(), AskTimeout);
            Require(label, state.Values.SequenceEqual(["a", "b", "c", "d", "e"]), $"recovered [{string.Join(',', state.Values)}]");
            Require(label, state.SnapshotSequenceNr == 3, $"recovery offered the snapshot at {state.SnapshotSequenceNr}, not 3");
            Console.WriteLine($"[canary-sqlite] {label}: persisted 5 events, recovered them with the snapshot at {state.SnapshotSequenceNr}");

            // 2. delete up to 2; recover from events only: 3-5 replay and the sequence number stays 5
            var deleted = await second.Ask<long>(new DeleteTo(2), AskTimeout);
            Require(label, deleted == 2, $"delete reported {deleted}");
            await second.GracefulStop(AskTimeout);
            var third = system.ActorOf(Props.Create(() => new CanaryPersistentActor("p1", false)), "p1-third");
            state = await third.Ask<CanaryState>(new GetState(), AskTimeout);
            Require(label, state.LastSequenceNr == 5, $"LastSequenceNr was {state.LastSequenceNr} after the delete, not 5");
            Require(label, state.Values.SequenceEqual(["c", "d", "e"]), $"events 3-5 were not replayed: [{string.Join(',', state.Values)}]");
            Console.WriteLine($"[canary-sqlite] {label}: after DeleteMessages(2) LastSequenceNr is {state.LastSequenceNr} and events 3-5 replayed");

            await AssertCurrentQueriesAsync(label, system);

            // 4 + 5. live queries see what is persisted after they started
            var readJournal = system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
            var materializer = system.Materializer();
            var liveTag = readJournal.EventsByTag("red", Offset.NoOffset())
                .Where(static e => e.SequenceNr == 6)
                .RunWith(Sink.First<EventEnvelope>(), materializer);
            var liveIds = readJournal.PersistenceIds()
                .Where(static id => id == "p2")
                .RunWith(Sink.First<string>(), materializer);

            Require(label, await third.Ask<int>(new PersistCmd("f", true), AskTimeout) == 4, "persist f");
            var red = await liveTag.WaitAsync(LiveTimeout);
            Require(label, red.Tags.SequenceEqual(["red"]), "the live tagged envelope lost its tag");

            var other = system.ActorOf(Props.Create(() => new CanaryPersistentActor("p2", true)), "p2");
            Require(label, await other.Ask<int>(new PersistCmd("x", false), AskTimeout) == 1, "persist on p2");
            Require(label, await liveIds.WaitAsync(LiveTimeout) == "p2", "the live persistence ids query did not report p2");
            Console.WriteLine($"[canary-sqlite] {label}: live EventsByTag and PersistenceIds delivered new events");

            // 6. delete every snapshot of p1
            var snapshotStore = Persistence.Instance.Apply(system).SnapshotStoreFor(null);
            await snapshotStore.Ask<DeleteSnapshotsSuccess>(new DeleteSnapshots("p1", SnapshotSelectionCriteria.Latest), AskTimeout);
            var loaded = await snapshotStore.Ask<LoadSnapshotResult>(new LoadSnapshot("p1", SnapshotSelectionCriteria.Latest, long.MaxValue), AskTimeout);
            Require(label, loaded.Snapshot is null, "a snapshot survived DeleteSnapshots(Latest)");
            Console.WriteLine($"[canary-sqlite] {label}: DeleteSnapshots left nothing to load");

            // 7. the registered event adapter tagged an event on its way in
            var adapted = system.ActorOf(Props.Create(() => new CanaryPersistentActor("p3", true)), "p3");
            Require(label, await adapted.Ask<int>(new PersistCmd("adapted-1", false), AskTimeout) == 1, "persist on p3");
            var adaptedFound = await readJournal.CurrentEventsByTag("adapted", Offset.NoOffset())
                .RunWith(Sink.Seq<EventEnvelope>(), materializer).WaitAsync(AskTimeout);
            Require(label, adaptedFound.Count == 1 && adaptedFound[0].PersistenceId == "p3" && adaptedFound[0].Tags.SequenceEqual(["adapted"]),
                $"CurrentEventsByTag(adapted) returned {adaptedFound.Count} envelopes - the event adapter did not run");
            Console.WriteLine($"[canary-sqlite] {label}: the registered event adapter tagged an event");

            system.Log.Info("[canary-sqlite] {0}: round-trip complete", label);
            watchdog.ThrowIfAnyProblems(label, "post-boot");
        }
        Console.WriteLine($"[canary-sqlite] {label}: terminated");
    }

    private static async Task AssertCurrentQueriesAsync(string label, ActorSystem system)
    {
        var readJournal = system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
        var materializer = system.Materializer();

        var ids = await readJournal.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), materializer).WaitAsync(AskTimeout);
        Require(label, ids.SequenceEqual(["p1"]), $"CurrentPersistenceIds returned [{string.Join(',', ids)}]");

        var byId = await readJournal.CurrentEventsByPersistenceId("p1", 0, long.MaxValue)
            .RunWith(Sink.Seq<EventEnvelope>(), materializer).WaitAsync(AskTimeout);
        Require(label, byId.Select(e => e.SequenceNr).SequenceEqual([3L, 4L, 5L]), "CurrentEventsByPersistenceId did not return events 3-5");
        Require(label, byId.All(e => e.Offset is Sequence), "an envelope did not carry a Sequence offset");

        var byTag = await readJournal.CurrentEventsByTag("red", Offset.NoOffset())
            .RunWith(Sink.Seq<EventEnvelope>(), materializer).WaitAsync(AskTimeout);
        Require(label, byTag.Count == 1 && byTag[0].SequenceNr == 4 && byTag[0].Tags.SequenceEqual(["red"]),
            $"CurrentEventsByTag(red) returned {byTag.Count} envelopes");

        var all = await readJournal.CurrentAllEvents(Offset.NoOffset())
            .RunWith(Sink.Seq<EventEnvelope>(), materializer).WaitAsync(AskTimeout);
        Require(label, all.Count == 3, $"CurrentAllEvents returned {all.Count} envelopes, not 3");

        var fromEnd = await readJournal.CurrentAllEvents(new FromEnd(1))
            .RunWith(Sink.Seq<EventEnvelope>(), materializer).WaitAsync(AskTimeout);
        Require(label, fromEnd.Count == 1 && fromEnd[0].SequenceNr == 5 && fromEnd[0].Offset is Sequence,
            $"CurrentAllEvents(FromEnd(1)) returned {fromEnd.Count} envelopes");

        Console.WriteLine($"[canary-sqlite] {label}: every current query returned what it should");
    }

    /// <summary>A second database in Csv mode: tagged events are found through the tags column.</summary>
    private static async Task RunCsvAsync(string path)
    {
        const string label = "csv";
        var watchdog = new LogWatchdogFilter();
        await using var hosted = await StartHostedAsync(label, path, TagWriteMode.Csv, watchdog);
        var system = hosted.System;
        {
            var actor = system.ActorOf(Props.Create(() => new CanaryPersistentActor("csv-1", true)), "csv-1");
            Require(label, await actor.Ask<int>(new PersistCmd("tagged", true), AskTimeout) == 1, "persist tagged");
            Require(label, await actor.Ask<int>(new PersistCmd("plain", false), AskTimeout) == 2, "persist plain");

            var readJournal = system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
            var found = await readJournal.CurrentEventsByTag("red", Offset.NoOffset())
                .RunWith(Sink.Seq<EventEnvelope>(), system.Materializer()).WaitAsync(AskTimeout);
            Require(label, found.Count == 1 && found[0].SequenceNr == 1 && found[0].Tags.SequenceEqual(["red"]),
                $"CurrentEventsByTag(red) returned {found.Count} envelopes");
            Console.WriteLine($"[canary-sqlite] {label}: found the tagged event through the Csv column");

            watchdog.ThrowIfAnyProblems(label, "run");
        }
        Console.WriteLine($"[canary-sqlite] {label}: terminated");
    }

    /// <summary>A third database in Both mode: tags go to the column and the table, and a delete cleans up the tag table.</summary>
    private static async Task RunBothAsync(string path)
    {
        const string label = "both";
        var watchdog = new LogWatchdogFilter();
        await using var hosted = await StartHostedAsync(label, path, TagWriteMode.Both, watchdog);
        var system = hosted.System;
        {
            var actor = system.ActorOf(Props.Create(() => new CanaryPersistentActor("both-1", true)), "both-1");
            Require(label, await actor.Ask<int>(new PersistCmd("plain", false), AskTimeout) == 1, "persist plain");
            Require(label, await actor.Ask<int>(new PersistCmd("tagged", true), AskTimeout) == 2, "persist tagged");
            Require(label, await actor.Ask<int>(new PersistCmd("last", true), AskTimeout) == 3, "persist last");
            Require(label, await actor.Ask<long>(new DeleteTo(2), AskTimeout) == 2, "delete up to 2");

            var readJournal = system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
            var found = await readJournal.CurrentEventsByTag("red", Offset.NoOffset())
                .RunWith(Sink.Seq<EventEnvelope>(), system.Materializer()).WaitAsync(AskTimeout);
            Require(label, found.Count == 1 && found[0].SequenceNr == 3 && found[0].Tags.SequenceEqual(["red"]),
                $"CurrentEventsByTag(red) returned {found.Count} envelopes after the delete");
            Console.WriteLine($"[canary-sqlite] {label}: tag lookup and delete work with the column and the table");

            watchdog.ThrowIfAnyProblems(label, "run");
        }
        Console.WriteLine($"[canary-sqlite] {label}: terminated");
    }

    /// <summary>Without the registration the plugin cannot start with the switch off, and the error says why.</summary>
    private static async Task RunUnregisteredAsync(string path)
    {
        const string label = "unregistered";
        var system = CreateUnregisteredSystem(label, path);
        try
        {
            ConfigurationException? thrown = null;
            try
            {
                Persistence.Instance.Apply(system).JournalFor(null);
            }
            catch (ConfigurationException ex)
            {
                thrown = ex;
            }

            Require(label, thrown is not null, "the SQLite journal started without WithEmbeddedPersistence() and with Akka.DynamicTypeLoading off");
            Require(label, thrown!.Message.Contains("Akka.DynamicTypeLoading", StringComparison.Ordinal),
                $"the exception did not name the switch: [{thrown.Message}]");
            Console.WriteLine($"[canary-sqlite] {label}: an unregistered journal failed at start as designed");
        }
        finally
        {
            await system.Terminate().WaitAsync(TerminateTimeout);
        }

        Console.WriteLine($"[canary-sqlite] {label}: terminated");
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
            Console.WriteLine("[canary-sqlite] FAILED: unhandled non-exception throw");
            return;
        }

        Console.WriteLine($"[canary-sqlite] FAILED: {ex.GetType().FullName}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);

        var inner = ex.InnerException;
        var depth = 0;
        while (inner is not null && depth++ < 10)
        {
            Console.WriteLine($"[canary-sqlite]  --> inner: {inner.GetType().FullName}: {inner.Message}");
            Console.WriteLine(inner.StackTrace);
            inner = inner.InnerException;
        }
    }
}
