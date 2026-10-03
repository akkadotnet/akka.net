//-----------------------------------------------------------------------
// <copyright file="EmbeddedHostingSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
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
                    journal => journal
                        .AddWriteEventAdapter("red-tagger", static _ => new RedTagger(), typeof(HostingEvent))
                        .WithHealthCheck(),
                    configureSnapshot: snapshot => snapshot.WithHealthCheck());

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
                    journal => journal.AddWriteEventAdapter("red-tagger", static _ => new RedTagger(), typeof(HostingEvent)),
                    pluginIdentifier: "first")
                .WithEmbeddedPersistence(
                    secondDb.ConnectionString,
                    journal => journal.AddWriteEventAdapter("red-tagger", static _ => new RedTagger(), typeof(HostingEvent)),
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
                    journal => journal
                        .AddWriteEventAdapter<RedTagger>("red-tagger", [typeof(HostingEvent)])
                        .WithHealthCheck(),
                    configureSnapshot: snapshot => snapshot.WithHealthCheck());

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
            config.GetString("akka.persistence.journal.embedded.tag-write-mode").Should().Be("TagTable");
            config.GetBoolean("akka.persistence.journal.embedded.auto-initialize").Should().BeTrue();
            config.GetString("akka.persistence.journal.plugin").Should().Be("akka.persistence.journal.embedded");
            config.GetString("akka.persistence.snapshot-store.plugin").Should().Be("akka.persistence.snapshot-store.embedded");
            config.GetString("akka.persistence.query.journal.embedded.write-plugin").Should().Be("akka.persistence.journal.embedded");
        }

        [Fact(DisplayName = "Should_start_two_read_journals_with_gap_tracking_When_their_ids_differ")]
        public async Task Should_start_two_read_journals_with_gap_tracking_When_their_ids_differ()
        {
            using var db = new TempDb();
            await using var hosted = await HostedSystem.StartAsync(true, builder => builder
                .WithCustomSerializer("hosting-test", [typeof(HostingEvent), typeof(HostingSnapshot)], system => new HostingSerializer(system))
                .WithEmbeddedPersistence(
                    new EmbeddedJournalOptions { ConnectionString = db.ConnectionString },
                    new EmbeddedSnapshotOptions { ConnectionString = db.ConnectionString },
                    new EmbeddedReadJournalOptions { JournalSequenceRetrievalEnabled = true })
                .WithEmbeddedReadJournal(new EmbeddedReadJournalOptions("second") { JournalSequenceRetrievalEnabled = true }));
            var system = hosted.System;

            var actor = system.ActorOf(Props.Create(() => new HostingActor("two-readers", null, null)));
            (await actor.Ask<int>(new Persist("only"), TimeSpan.FromSeconds(10))).Should().Be(1);

            // both trackers started: they used to share one actor name, and the second read journal failed to start
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

    public class OptionsSpec
    {
        [Fact(DisplayName = "Should_write_every_setting_When_journal_options_are_turned_into_config")]
        public void Should_write_every_setting_When_journal_options_are_turned_into_config()
        {
            var options = new EmbeddedJournalOptions(isDefaultPlugin: false, identifier: "custom")
            {
                ConnectionString = "Data Source=a \"quoted\".db",
                TagWriteMode = TagWriteMode.Both,
                TagSeparator = "|",
                DeleteCompatibilityMode = true,
                UseWriterUuidColumn = false,
                JournalTableName = "events",
                BatchSize = 7
            };

            var config = options.ToConfig().GetConfig("akka.persistence.journal.custom");

            config.GetString("connection-string").Should().Be("Data Source=a \"quoted\".db");
            config.GetString("tag-write-mode").Should().Be("Both");
            config.GetString("tag-separator").Should().Be("|");
            config.GetBoolean("delete-compatibility-mode").Should().BeTrue();
            config.GetBoolean("default.journal.use-writer-uuid-column").Should().BeFalse();
            config.GetString("default.journal.table-name").Should().Be("events");
            config.GetInt("batch-size").Should().Be(7);
            options.ToConfig().HasPath("akka.persistence.journal.plugin").Should().BeFalse("it is not the default plugin");
            options.DefaultConfig.GetString("akka.persistence.journal.custom.class").Should().Contain("SqliteWriteJournal");
        }

        [Fact(DisplayName = "Should_throw_When_connection_string_is_missing")]
        public void Should_throw_When_connection_string_is_missing()
        {
            Assert.Throws<ArgumentException>(() => new AkkaConfigurationBuilder(new ServiceCollection(), "x").WithEmbeddedPersistence(""));
        }
    }
}
