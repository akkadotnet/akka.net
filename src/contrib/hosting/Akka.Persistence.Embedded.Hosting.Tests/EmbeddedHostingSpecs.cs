//-----------------------------------------------------------------------
// <copyright file="EmbeddedHostingSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Hosting;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Akka.Persistence.Embedded.Hosting.Tests
{
    /// <summary>
    /// AppContext switches are process-wide, so a spec that flips <c>Akka.DynamicTypeLoading</c> never runs beside another.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DynamicTypeLoadingCollection
    {
        public const string Name = "Akka.DynamicTypeLoading";
    }

    /// <summary>
    /// A plain Microsoft host with Akka.Hosting, no TestKit: the TestKit itself loads types by name, so it cannot run
    /// with <c>Akka.DynamicTypeLoading</c> off. Flips the switch around the host's life.
    /// </summary>
    internal sealed class HostedSystem : IAsyncDisposable
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";
        private readonly IHost _host;
        private readonly bool _hadSwitch;
        private readonly bool _previous;

        private HostedSystem(IHost host, bool hadSwitch, bool previous)
        {
            _host = host;
            _hadSwitch = hadSwitch;
            _previous = previous;
        }

        public ActorSystem System => _host.Services.GetRequiredService<ActorSystem>();

        public IServiceProvider Services => _host.Services;

        public static async Task<HostedSystem> StartAsync(bool dynamicTypeLoading, Action<AkkaConfigurationBuilder> configure)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
            try
            {
                var appBuilder = Host.CreateApplicationBuilder();
                appBuilder.Logging.ClearProviders();
                appBuilder.Services.AddHealthChecks();
                appBuilder.Services.AddAkka("embedded-hosting", (builder, _) =>
                {
                    // the stdout logger is named in HOCON; with the switch off Akka logs through the host instead
                    builder.ConfigureLoggers(logger =>
                    {
                        logger.ClearLoggers();
                        logger.AddLoggerFactory();
                    });
                    configure(builder);
                });

                var host = appBuilder.Build();
                await host.StartAsync();
                return new HostedSystem(host, hadSwitch, previous);
            }
            catch
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(30));
                _host.Dispose();
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !_hadSwitch || _previous);
            }
        }
    }

    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DynamicTypeLoadingOffSpec
    {
        private static void Configure(AkkaConfigurationBuilder builder, string connectionString)
            => builder
                .WithCustomSerializer("hosting-test", [typeof(HostingEvent), typeof(HostingSnapshot)], system => new HostingSerializer(system))
                .WithEmbeddedPersistence(
                    connectionString,
                    // the factory overload builds the adapter in code, so no type name is resolved
                    journalBuilder: journal => journal
                        .AddWriteEventAdapter<RedTagger>("red-tagger", [typeof(HostingEvent)])
                        .WithHealthCheck(),
                    snapshotBuilder: snapshot => snapshot.WithHealthCheck());

        [Fact(DisplayName = "Should_persist_recover_snapshot_and_query_by_tag_When_dynamic_type_loading_is_off")]
        public async Task Should_persist_recover_snapshot_and_query_by_tag_When_dynamic_type_loading_is_off()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => Configure(builder, db.ConnectionString));

            AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var on).Should().BeTrue();
            on.Should().BeFalse();

            await Scenario.RunAsync(hosted.System, "off-1");
        }

        [Fact(DisplayName = "Should_report_healthy_When_journal_and_snapshot_store_run_with_dynamic_type_loading_off")]
        public async Task Should_report_healthy_When_journal_and_snapshot_store_run_with_dynamic_type_loading_off()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => Configure(builder, db.ConnectionString));

            var result = await hosted.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

            result.Status.Should().Be(HealthStatus.Healthy);
            result.Entries.Keys.Should().Contain("akka.persistence.journal.embedded").And.Contain("akka.persistence.snapshot-store.embedded");
        }

        [Fact(DisplayName = "Should_keep_two_embedded_journals_apart_When_called_twice_with_different_ids")]
        public async Task Should_keep_two_embedded_journals_apart_When_called_twice_with_different_ids()
        {
            using var firstDb = new TempDb();
            using var secondDb = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => builder
                .WithCustomSerializer("hosting-test", [typeof(HostingEvent), typeof(HostingSnapshot)], system => new HostingSerializer(system))
                .WithEmbeddedPersistence(
                    firstDb.ConnectionString,
                    journalBuilder: journal => journal.AddWriteEventAdapter<RedTagger>("red-tagger", [typeof(HostingEvent)]),
                    pluginIdentifier: "first")
                .WithEmbeddedPersistence(
                    secondDb.ConnectionString,
                    journalBuilder: journal => journal.AddWriteEventAdapter<RedTagger>("red-tagger", [typeof(HostingEvent)]),
                    pluginIdentifier: "second",
                    isDefaultPlugin: false));
            var system = hosted.System;

            await Scenario.RunAsync(system, "in-first", readJournalId: "akka.persistence.query.journal.first");
            await Scenario.RunAsync(
                system, "in-second", "akka.persistence.journal.second", "akka.persistence.snapshot-store.second",
                "akka.persistence.query.journal.second");

            var materializer = system.Materializer();
            var first = system.ReadJournalFor<SqliteReadJournal>("akka.persistence.query.journal.first");
            var second = system.ReadJournalFor<SqliteReadJournal>("akka.persistence.query.journal.second");
            var firstIds = await first.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), materializer).WaitAsync(TimeSpan.FromSeconds(10));
            var secondIds = await second.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), materializer).WaitAsync(TimeSpan.FromSeconds(10));

            firstIds.Should().Equal("in-first");
            secondIds.Should().Equal("in-second");
            system.Settings.Config.GetString("akka.persistence.journal.plugin").Should().Be("akka.persistence.journal.first");
        }
    }

    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DynamicTypeLoadingOnSpec
    {
        private static void Configure(AkkaConfigurationBuilder builder, string connectionString)
            => builder
                .WithCustomSerializer("hosting-test", [typeof(HostingEvent), typeof(HostingSnapshot)], system => new HostingSerializer(system))
                .WithEmbeddedPersistence(
                    connectionString,
                    // the type overload writes the adapter into HOCON and also registers it in code
                    journalBuilder: journal => journal
                        .AddWriteEventAdapter<RedTagger>("red-tagger", [typeof(HostingEvent)])
                        .WithHealthCheck(),
                    snapshotBuilder: snapshot => snapshot.WithHealthCheck());

        [Fact(DisplayName = "Should_behave_the_same_When_dynamic_type_loading_is_on")]
        public async Task Should_behave_the_same_When_dynamic_type_loading_is_on()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(true, builder => Configure(builder, db.ConnectionString));

            await Scenario.RunAsync(hosted.System, "on-1");

            var result = await hosted.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            result.Status.Should().Be(HealthStatus.Healthy);
        }

        [Fact(DisplayName = "Should_write_the_options_into_config_When_the_extension_runs")]
        public async Task Should_write_the_options_into_config_When_the_extension_runs()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(true, builder => Configure(builder, db.ConnectionString));
            var config = hosted.System.Settings.Config;

            config.GetString("akka.persistence.journal.embedded.connection-string").Should().Contain(db.FilePath);
            config.GetBoolean("akka.persistence.journal.embedded.auto-initialize").Should().BeTrue();
            config.GetString("akka.persistence.journal.plugin").Should().Be("akka.persistence.journal.embedded");
            config.GetString("akka.persistence.snapshot-store.plugin").Should().Be("akka.persistence.snapshot-store.embedded");
            config.GetString("akka.persistence.query.journal.embedded.write-plugin").Should().Be("akka.persistence.journal.embedded");
        }

        [Fact(DisplayName = "Should_start_two_read_journals_When_their_ids_differ")]
        public async Task Should_start_two_read_journals_When_their_ids_differ()
        {
            using var firstDb = new TempDb();
            using var secondDb = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(true, builder => builder
                .WithCustomSerializer("hosting-test", [typeof(HostingEvent), typeof(HostingSnapshot)], system => new HostingSerializer(system))
                .WithEmbeddedPersistence(
                    new EmbeddedJournalOptions { ConnectionString = firstDb.ConnectionString },
                    new EmbeddedSnapshotOptions { ConnectionString = firstDb.ConnectionString })
                .WithEmbeddedPersistence(
                    new EmbeddedJournalOptions(false, "second") { ConnectionString = secondDb.ConnectionString }));
            var system = hosted.System;

            var first = system.ActorOf(Props.Create(() => new HostingActor("two-readers", null, null)));
            (await first.Ask<int>(new Persist("only"), TimeSpan.FromSeconds(10))).Should().Be(1);
            var second = system.ActorOf(Props.Create(() => new HostingActor("two-readers", "akka.persistence.journal.second", null)));
            (await second.Ask<int>(new Persist("only"), TimeSpan.FromSeconds(10))).Should().Be(1);

            // both read journals start and read their own journal
            foreach (var id in new[] { "akka.persistence.query.journal.embedded", "akka.persistence.query.journal.second" })
            {
                var journal = system.ReadJournalFor<SqliteReadJournal>(id);
                var events = await journal.CurrentEventsByPersistenceId("two-readers", 0, long.MaxValue)
                    .RunWith(Sink.Seq<EventEnvelope>(), system.Materializer()).WaitAsync(TimeSpan.FromSeconds(10));
                events.Should().HaveCount(1);
            }
        }

        [Fact(DisplayName = "Should_register_only_the_snapshot_store_When_mode_is_SnapshotStore")]
        public async Task Should_register_only_the_snapshot_store_When_mode_is_SnapshotStore()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(true, builder => builder.WithEmbeddedPersistence(db.ConnectionString, mode: PersistenceMode.SnapshotStore));
            var config = hosted.System.Settings.Config;

            config.HasPath("akka.persistence.snapshot-store.embedded.connection-string").Should().BeTrue();
            config.HasPath("akka.persistence.journal.embedded.connection-string").Should().BeFalse();
            config.HasPath("akka.persistence.query.journal.embedded.write-plugin").Should().BeFalse();
        }
    }


    /// <summary>The ways to call WithEmbeddedPersistence, with <c>Akka.DynamicTypeLoading</c> off.</summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class HostingSurfaceSpec
    {
        private static AkkaConfigurationBuilder WithSerializer(AkkaConfigurationBuilder builder)
            => builder.WithCustomSerializer("hosting-test", [typeof(HostingEvent), typeof(HostingSnapshot)], system => new HostingSerializer(system));

        private static void AddTagger(AkkaPersistenceJournalBuilder journal)
            => journal.AddWriteEventAdapter<RedTagger>("red-tagger", [typeof(HostingEvent)]);

        private static async Task<string[]> TablesAsync(string path)
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
            var names = new System.Collections.Generic.List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
            return names.ToArray();
        }

        [Fact(DisplayName = "Should_run_the_scenario_When_options_objects_are_passed")]
        public async Task Should_run_the_scenario_When_options_objects_are_passed()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => WithSerializer(builder)
                .WithEmbeddedPersistence(
                    new EmbeddedJournalOptions { ConnectionString = db.ConnectionString, QueryRefreshInterval = TimeSpan.FromMilliseconds(100) },
                    new EmbeddedSnapshotOptions { ConnectionString = db.ConnectionString },
                    AddTagger));

            await Scenario.RunAsync(hosted.System, "options-1");
        }

        [Fact(DisplayName = "Should_run_the_scenario_When_configurator_delegates_are_passed")]
        public async Task Should_run_the_scenario_When_configurator_delegates_are_passed()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => WithSerializer(builder)
                .WithEmbeddedPersistence(
                    journal =>
                    {
                        journal.ConnectionString = db.ConnectionString;
                        journal.JournalTableName = "configured_events";
                    },
                    snapshot => snapshot.ConnectionString = db.ConnectionString));

            // the delegate overload takes no adapters, so this checks persistence, snapshots and the table name only
            var system = hosted.System;
            var actor = system.ActorOf(Props.Create(() => new HostingActor("configurator-1", null, null)));
            (await actor.Ask<int>(new Persist("a"), TimeSpan.FromSeconds(10))).Should().Be(1);
            (await actor.Ask<long>(new Snapshot(), TimeSpan.FromSeconds(10))).Should().Be(1);

            (await TablesAsync(db.FilePath)).Should().Contain("configured_events", "the table name was set in the configurator");
        }

        [Fact(DisplayName = "Should_throw_When_both_configurator_delegates_are_null")]
        public void Should_throw_When_both_configurator_delegates_are_null()
        {
            var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "x");
            Assert.Throws<ArgumentException>(() => builder.WithEmbeddedPersistence((Action<EmbeddedJournalOptions>?)null, null));
            Assert.Throws<ArgumentException>(() => builder.WithEmbeddedPersistence((EmbeddedJournalOptions?)null, null));
        }

        [Fact(DisplayName = "Should_persist_recover_and_query_When_only_the_journal_is_registered")]
        public async Task Should_persist_recover_and_query_When_only_the_journal_is_registered()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => WithSerializer(builder)
                .WithEmbeddedPersistence(db.ConnectionString, PersistenceMode.Journal, journalBuilder: j =>
                {
                    AddTagger(j);
                    j.WithHealthCheck();
                }));
            var system = hosted.System;

            var first = system.ActorOf(Props.Create(() => new HostingActor("journal-only", null, null)));
            (await first.Ask<int>(new Persist("plain-1"), TimeSpan.FromSeconds(10))).Should().Be(1);
            (await first.Ask<int>(new Persist("red-2"), TimeSpan.FromSeconds(10))).Should().Be(2);
            await first.GracefulStop(TimeSpan.FromSeconds(10));

            var second = system.ActorOf(Props.Create(() => new HostingActor("journal-only", null, null)));
            (await second.Ask<State>(new GetState(), TimeSpan.FromSeconds(10))).Values.Should().Equal("plain-1", "red-2");

            var byTag = await system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier)
                .CurrentEventsByTag("red", Offset.NoOffset()).RunWith(Sink.Seq<EventEnvelope>(), system.Materializer()).WaitAsync(TimeSpan.FromSeconds(10));
            byTag.Should().HaveCount(1);

            (await TablesAsync(db.FilePath)).Should().NotContain("snapshot");
            var health = await hosted.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            health.Entries.Keys.Should().Contain("akka.persistence.journal.embedded").And.NotContain("akka.persistence.snapshot-store.embedded");
        }

        [Fact(DisplayName = "Should_store_and_load_snapshots_When_only_the_snapshot_store_is_registered")]
        public async Task Should_store_and_load_snapshots_When_only_the_snapshot_store_is_registered()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => WithSerializer(builder)
                .WithEmbeddedPersistence(
                    journalOptions: null,
                    snapshotOptions: new EmbeddedSnapshotOptions { ConnectionString = db.ConnectionString },
                    snapshotBuilder: s => s.WithHealthCheck())
                .WithInMemoryJournal());
            var system = hosted.System;

            var first = system.ActorOf(Props.Create(() => new HostingActor("snapshot-only", null, null)));
            (await first.Ask<int>(new Persist("a"), TimeSpan.FromSeconds(10))).Should().Be(1);
            (await first.Ask<long>(new Snapshot(), TimeSpan.FromSeconds(10))).Should().Be(1);
            await first.GracefulStop(TimeSpan.FromSeconds(10));

            var second = system.ActorOf(Props.Create(() => new HostingActor("snapshot-only", null, null)));
            var state = await second.Ask<State>(new GetState(), TimeSpan.FromSeconds(10));
            state.SnapshotSequenceNr.Should().Be(1);
            state.Values.Should().Equal("a");
            (await TablesAsync(db.FilePath)).Should().Contain("snapshot").And.NotContain("journal");
        }

        [Fact(DisplayName = "Should_leave_the_database_empty_and_fail_writes_When_autoInitialize_is_false")]
        public async Task Should_leave_the_database_empty_and_fail_writes_When_autoInitialize_is_false()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => WithSerializer(builder)
                .WithEmbeddedPersistence(db.ConnectionString, autoInitialize: false, journalBuilder: AddTagger));
            var system = hosted.System;

            var actor = system.ActorOf(Props.Create(() => new HostingActor("no-tables", null, null)));
            var attempt = () => actor.Ask<int>(new Persist("lost"), TimeSpan.FromSeconds(3));

            await attempt.Should().ThrowAsync<Exception>("the journal table does not exist");
            (await TablesAsync(db.FilePath)).Should().NotContain("journal");
            system.Settings.Config.GetBoolean("akka.persistence.journal.embedded.auto-initialize").Should().BeFalse();
        }

        [Fact(DisplayName = "Should_read_the_journal_through_its_own_read_journal_id_When_its_identifier_is_not_embedded")]
        public async Task Should_read_the_journal_through_its_own_read_journal_id_When_its_identifier_is_not_embedded()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => WithSerializer(builder)
                .WithEmbeddedPersistence(db.ConnectionString, journalBuilder: AddTagger, pluginIdentifier: "custom"));

            // the read journal id follows the identifier: akka.persistence.query.journal.{id}
            await Scenario.RunAsync(hosted.System, "alias-1", readJournalId: "akka.persistence.query.journal.custom");
            var viaCustom = await hosted.System.ReadJournalFor<SqliteReadJournal>("akka.persistence.query.journal.custom")
                .CurrentPersistenceIds().RunWith(Sink.Seq<string>(), hosted.System.Materializer()).WaitAsync(TimeSpan.FromSeconds(10));
            viaCustom.Should().Equal("alias-1");
        }

        [Fact(DisplayName = "Should_apply_query_settings_from_the_journal_options_When_the_read_journal_starts")]
        public async Task Should_apply_query_settings_from_the_journal_options_When_the_read_journal_starts()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => WithSerializer(builder)
                .WithEmbeddedPersistence(
                    new EmbeddedJournalOptions
                    {
                        ConnectionString = db.ConnectionString,
                        QueryRefreshInterval = TimeSpan.FromMilliseconds(250),
                        QueryMaxBufferSize = 3,
                        QueryThreads = 2
                    },
                    null,
                    AddTagger));
            var system = hosted.System;
            var config = system.Settings.Config.GetConfig(SqliteReadJournal.Identifier);

            config.GetTimeSpan("refresh-interval").Should().Be(TimeSpan.FromMilliseconds(250));
            config.GetInt("max-buffer-size").Should().Be(3);
            config.GetInt("query-threads").Should().Be(2);

            // the settings reach the read journal: 10 events come back through pages of 3
            var actor = system.ActorOf(Props.Create(() => new HostingActor("paged", null, null)));
            for (var i = 1; i <= 10; i++)
                (await actor.Ask<int>(new Persist($"e-{i}"), TimeSpan.FromSeconds(10))).Should().Be(i);
            var events = await system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier)
                .CurrentEventsByPersistenceId("paged", 0, long.MaxValue)
                .RunWith(Sink.Seq<EventEnvelope>(), system.Materializer()).WaitAsync(TimeSpan.FromSeconds(10));
            events.Select(e => e.SequenceNr).Should().Equal(Enumerable.Range(1, 10).Select(i => (long)i));
        }

        [Fact(DisplayName = "Should_use_custom_table_names_When_they_are_set_in_the_journal_and_snapshot_options")]
        public async Task Should_use_custom_table_names_When_they_are_set_in_the_journal_and_snapshot_options()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(false, builder => WithSerializer(builder)
                .WithEmbeddedPersistence(
                    new EmbeddedJournalOptions
                    {
                        ConnectionString = db.ConnectionString,
                        JournalTableName = "events",
                        TagTableName = "event_tags"
                    },
                    new EmbeddedSnapshotOptions { ConnectionString = db.ConnectionString, TableName = "snaps" },
                    AddTagger));

            await Scenario.RunAsync(hosted.System, "names-1");

            var tables = await TablesAsync(db.FilePath);
            tables.Should().Contain(["events", "event_tags", "snaps"]);
            tables.Should().NotContain(["journal", "tags", "snapshot"]);
        }
    }

    public class OptionsSpec
    {
        [Fact(DisplayName = "Should_write_every_setting_When_journal_options_are_turned_into_config")]
        public void Should_write_every_setting_When_journal_options_are_turned_into_config()
        {
            var options = new EmbeddedJournalOptions(isDefaultPlugin: false, identifier: "custom")
            {
                ConnectionString = "Data Source=a \"quoted\".db",
                AutoInitialize = false,
                JournalTableName = "events",
                TagTableName = "event_tags",
                BufferSize = 5,
                BatchSize = 7,
                ReplayBatchSize = 9,
                ReadThreads = 3,
                QueryRefreshInterval = TimeSpan.FromMilliseconds(250),
                QueryMaxBufferSize = 11,
                QueryThreads = 6
            };

            var all = options.ToConfig();
            var config = all.GetConfig("akka.persistence.journal.custom");

            config.GetString("connection-string").Should().Be("Data Source=a \"quoted\".db");
            config.GetBoolean("auto-initialize").Should().BeFalse();
            config.GetString("table-name").Should().Be("events");
            config.GetString("tag-table-name").Should().Be("event_tags");
            config.GetInt("buffer-size").Should().Be(5);
            config.GetInt("batch-size").Should().Be(7);
            config.GetInt("replay-batch-size").Should().Be(9);
            config.GetInt("read-threads").Should().Be(3);
            var query = all.GetConfig("akka.persistence.query.journal.custom");
            query.GetString("write-plugin").Should().Be("akka.persistence.journal.custom");
            query.GetTimeSpan("refresh-interval").Should().Be(TimeSpan.FromMilliseconds(250));
            query.GetInt("max-buffer-size").Should().Be(11);
            query.GetInt("query-threads").Should().Be(6);
            all.HasPath("akka.persistence.journal.plugin").Should().BeFalse("it is not the default plugin");
            options.DefaultConfig.GetString("akka.persistence.journal.custom.class").Should().Contain("SqliteWriteJournal");
        }

        [Fact(DisplayName = "Should_write_the_table_name_When_snapshot_options_are_turned_into_config")]
        public void Should_write_the_table_name_When_snapshot_options_are_turned_into_config()
        {
            var options = new EmbeddedSnapshotOptions { ConnectionString = "Data Source=a.db", TableName = "snaps" };

            options.ToConfig().GetString("akka.persistence.snapshot-store.embedded.table-name").Should().Be("snaps");
        }

        [Fact(DisplayName = "Should_keep_the_reference_settings_When_options_are_left_null")]
        public void Should_keep_the_reference_settings_When_options_are_left_null()
        {
            var options = new EmbeddedJournalOptions { ConnectionString = "Data Source=a.db" };

            var merged = options.ToConfig().WithFallback(options.DefaultConfig).GetConfig("akka.persistence.journal.embedded");

            merged.GetInt("batch-size").Should().Be(100);
            merged.GetString("table-name").Should().Be("journal");
            merged.GetString("tag-table-name").Should().Be("tags");
            options.ToConfig().HasPath("akka.persistence.journal.embedded.batch-size").Should().BeFalse("a null property writes nothing");
        }

        [Fact(DisplayName = "Should_throw_When_connection_string_is_missing")]
        public void Should_throw_When_connection_string_is_missing()
        {
            var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "x");
            Assert.Throws<ArgumentNullException>(() => builder.WithEmbeddedPersistence(""));
            Assert.Throws<ArgumentNullException>(() => new EmbeddedJournalOptions().ToConfig());
            Assert.Throws<ArgumentNullException>(() => new EmbeddedSnapshotOptions().ToConfig());
        }

        [Fact(DisplayName = "Should_throw_When_a_builder_is_given_for_a_plugin_the_mode_leaves_out")]
        public void Should_throw_When_a_builder_is_given_for_a_plugin_the_mode_leaves_out()
        {
            var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "x");
            Assert.Throws<Exception>(() => builder.WithEmbeddedPersistence("Data Source=a.db", PersistenceMode.SnapshotStore, journalBuilder: _ => { }));
            Assert.Throws<Exception>(() => builder.WithEmbeddedPersistence("Data Source=a.db", PersistenceMode.Journal, snapshotBuilder: _ => { }));
        }
    }
}
