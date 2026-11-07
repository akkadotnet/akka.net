//-----------------------------------------------------------------------
// <copyright file="SettingsSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using Akka.Configuration;
using Akka.Persistence.Embedded.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Settings
{
    public class SettingsSpecs
    {
        private const string JournalPath = "akka.persistence.journal.embedded";
        private const string SnapshotPath = "akka.persistence.snapshot-store.embedded";

        private static Akka.Configuration.Config Full(string extra)
            => ConfigurationFactory.ParseString($$"""
                    akka.persistence.journal.embedded.connection-string = "Data Source=/tmp/never-opened.db"
                    akka.persistence.snapshot-store.embedded.connection-string = "Data Source=/tmp/never-opened.db"
                    {{extra}}
                    """)
                .WithFallback(SqlitePersistence.DefaultConfiguration);

        private static JournalSettings Journal(string extra)
        {
            var config = Full(extra);
            return JournalSettings.Create(config.GetConfig(JournalPath), JournalPath);
        }

        private static QuerySettings Query(string extra)
        {
            var config = Full(extra);
            return QuerySettings.Create(config.GetConfig(SqlitePersistence.QueryPluginId), SqlitePersistence.QueryPluginId, config);
        }

        [Fact(DisplayName = "Should_read_reference_defaults_When_only_connection_string_is_set")]
        public void Should_read_reference_defaults_When_only_connection_string_is_set()
        {
            var journal = Journal("");

            journal.AutoInitialize.Should().BeTrue();
            (journal.BufferSize, journal.BatchSize, journal.ReplayBatchSize, journal.ReadThreads).Should().Be((5000, 100, 1000, 2));
            journal.Tables.Should().Be(new JournalTableNames("journal", "tags"));
            journal.Serializer.Should().BeNull();

            var config = Full("");
            var snapshot = SnapshotSettings.Create(config.GetConfig(SnapshotPath), SnapshotPath);
            snapshot.AutoInitialize.Should().BeTrue();
            snapshot.TableName.Should().Be("snapshot");
            snapshot.Serializer.Should().BeNull();

            var query = Query("");
            query.MaxBufferSize.Should().Be(500);
            query.RefreshInterval.Should().Be(TimeSpan.FromSeconds(1));
            query.QueryThreads.Should().Be(4);
            query.WritePluginPath.Should().Be(JournalPath);
            query.Journal.ConnectionString.Should().Be("Data Source=/tmp/never-opened.db");
        }

        [Fact(DisplayName = "Should_read_every_setting_When_the_flat_keys_are_set")]
        public void Should_read_every_setting_When_the_flat_keys_are_set()
        {
            var config = Full($$"""
                {{JournalPath}} {
                    auto-initialize = false
                    table-name = evt
                    tag-table-name = evt_tags
                    buffer-size = 11
                    batch-size = 12
                    replay-batch-size = 13
                    read-threads = 3
                }
                {{SnapshotPath}} {
                    auto-initialize = false
                    table-name = snaps
                }
                {{SqlitePersistence.QueryPluginId}} {
                    max-buffer-size = 21
                    refresh-interval = 250ms
                    query-threads = 5
                }
                """);

            var journal = JournalSettings.Create(config.GetConfig(JournalPath), JournalPath);
            journal.AutoInitialize.Should().BeFalse();
            journal.Tables.Should().Be(new JournalTableNames("evt", "evt_tags"));
            (journal.BufferSize, journal.BatchSize, journal.ReplayBatchSize, journal.ReadThreads).Should().Be((11, 12, 13, 3));

            var snapshot = SnapshotSettings.Create(config.GetConfig(SnapshotPath), SnapshotPath);
            snapshot.AutoInitialize.Should().BeFalse();
            snapshot.TableName.Should().Be("snaps");

            var query = QuerySettings.Create(config.GetConfig(SqlitePersistence.QueryPluginId), SqlitePersistence.QueryPluginId, config);
            (query.MaxBufferSize, query.RefreshInterval, query.QueryThreads).Should().Be((21, TimeSpan.FromMilliseconds(250), 5));
            query.Journal.Tables.Should().Be(new JournalTableNames("evt", "evt_tags"));
        }

        [Fact(DisplayName = "Should_ignore_unknown_keys_When_Akka_Persistence_Sql_settings_are_set")]
        public void Should_ignore_unknown_keys_When_Akka_Persistence_Sql_settings_are_set()
        {
            var journal = Journal($$"""
                {{JournalPath}} {
                    parallelism = 3
                    provider-name = "SQLite.MS"
                    tag-write-mode = Csv
                    delete-compatibility-mode = true
                    default.journal.table-name = "ignored"
                    default.journal.columns.ordering = other
                }
                """);

            journal.Tables.Should().Be(new JournalTableNames("journal", "tags"));
        }

        [Fact(DisplayName = "Should_throw_When_connection_string_is_private_memory_database")]
        public void Should_throw_When_connection_string_is_private_memory_database()
        {
            foreach (var connectionString in new[] { "Data Source=:memory:", "Data Source=scratch;Mode=Memory" })
            {
                var failure = Assert.Throws<ConfigurationException>(() => Journal($"{JournalPath}.connection-string = \"{connectionString}\""));

                failure.Message.Should().StartWith($"[{JournalPath}.connection-string] uses a private in-memory database");
            }

            // a named shared in-memory database is allowed
            Journal($"{JournalPath}.connection-string = \"Data Source=shared;Mode=Memory;Cache=Shared\"").Should().NotBeNull();
        }

        [Theory(DisplayName = "Should_throw_When_table_name_is_not_a_plain_identifier")]
        [InlineData(JournalPath, "table-name")]
        [InlineData(JournalPath, "tag-table-name")]
        [InlineData(SnapshotPath, "table-name")]
        public void Should_throw_When_table_name_is_not_a_plain_identifier(string path, string key)
        {
            var config = Full($"{path}.{key} = \"journal; DROP TABLE x\"");

            var failure = Assert.Throws<ConfigurationException>(
                () => path == JournalPath
                    ? JournalSettings.Create(config.GetConfig(path), path)
                    : SnapshotSettings.Create(config.GetConfig(path), path));

            failure.Message.Should().Be($"[{path}.{key}] = [journal; DROP TABLE x] is not a plain SQL identifier.");
        }

        [Fact(DisplayName = "Should_throw_When_connection_string_is_empty")]
        public void Should_throw_When_connection_string_is_empty()
        {
            var config = ConfigurationFactory.Empty.WithFallback(SqlitePersistence.DefaultConfiguration);

            var failure = Assert.Throws<ConfigurationException>(() => JournalSettings.Create(config.GetConfig(JournalPath), JournalPath));

            failure.Message.Should().Be($"[{JournalPath}.connection-string] is required.");
        }

        [Fact(DisplayName = "Should_throw_When_a_number_is_out_of_range")]
        public void Should_throw_When_a_number_is_out_of_range()
        {
            Assert.Throws<ConfigurationException>(() => Journal($"{JournalPath}.batch-size = 0")).Message
                .Should().Be($"[{JournalPath}.batch-size] must be between 1 and {int.MaxValue}. Found [0].");
            Assert.Throws<ConfigurationException>(() => Journal($"{JournalPath}.read-threads = 65")).Message
                .Should().Be($"[{JournalPath}.read-threads] must be between 1 and 64. Found [65].");
            Assert.Throws<ConfigurationException>(() => Query($"{SqlitePersistence.QueryPluginId}.query-threads = 0"));
        }

        [Fact(DisplayName = "Should_use_the_write_plugins_connection_string_and_tables_When_the_read_journal_starts")]
        public void Should_use_the_write_plugins_connection_string_and_tables_When_the_read_journal_starts()
        {
            var query = Query($$"""
                akka.persistence.journal.other {
                    class = "Akka.Persistence.Embedded.Journal.SqliteWriteJournal, Akka.Persistence.Embedded"
                    connection-string = "Data Source=/tmp/other.db"
                    table-name = other_events
                }
                {{SqlitePersistence.QueryPluginId}}.write-plugin = "akka.persistence.journal.other"
                """);

            query.WritePluginPath.Should().Be("akka.persistence.journal.other");
            query.Journal.ConnectionString.Should().Be("Data Source=/tmp/other.db");
            query.Journal.Tables.Should().Be(new JournalTableNames("other_events", "tags"));
        }

        [Fact(DisplayName = "Should_throw_When_query_write_plugin_is_not_this_journal")]
        public void Should_throw_When_query_write_plugin_is_not_this_journal()
        {
            var failure = Assert.Throws<ConfigurationException>(() => Query($$"""
                akka.persistence.journal.other { class = "Some.Other.Journal, Some.Other" }
                {{SqlitePersistence.QueryPluginId}}.write-plugin = "akka.persistence.journal.other"
                """));

            failure.Message.Should().Contain("is not the Akka.Persistence.Embedded journal");
        }

        [Fact(DisplayName = "Should_throw_When_query_write_plugin_does_not_exist")]
        public void Should_throw_When_query_write_plugin_does_not_exist()
        {
            var failure = Assert.Throws<ConfigurationException>(
                () => Query($"{SqlitePersistence.QueryPluginId}.write-plugin = \"akka.persistence.journal.missing\""));

            failure.Message.Should().Contain("is not a configured journal plugin section");
        }

        [Fact(DisplayName = "Should_keep_the_query_limits_fixed_When_the_read_journal_is_configured")]
        public void Should_keep_the_query_limits_fixed_When_the_read_journal_is_configured()
        {
            QuerySettings.MaxConcurrentQueries.Should().Be(100);
            QuerySettings.ThrottleTimeout.Should().Be(TimeSpan.FromSeconds(3));
            QuerySettings.WritePluginInitTimeout.Should().Be(TimeSpan.FromSeconds(10));
        }
    }
}
