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
            return JournalSettings.Create(config.GetConfig(JournalPath), JournalPath, config);
        }

        [Fact(DisplayName = "Should_read_reference_defaults_When_only_connection_string_is_set")]
        public void Should_read_reference_defaults_When_only_connection_string_is_set()
        {
            var settings = Journal("");

            settings.TagWriteMode.Should().Be(TagWriteMode.TagTable);
            settings.TagSeparator.Should().Be(";");
            settings.AutoInitialize.Should().BeTrue();
            settings.DeleteCompatibilityMode.Should().BeFalse();
            settings.UseWriterUuid.Should().BeTrue();
            settings.DefaultSerializer.Should().BeNull();
            (settings.BufferSize, settings.BatchSize, settings.ReplayBatchSize, settings.ReadThreads).Should().Be((5000, 100, 1000, 2));
            settings.Tables.Journal.Should().Be("journal");
            settings.Tables.TagTable.Should().Be("tags");
            settings.Tables.Metadata.Should().Be("journal_metadata");
            settings.Warnings.Should().BeEmpty();
        }

        [Theory(DisplayName = "Should_throw_When_table_mapping_is_not_default")]
        [InlineData("table-mapping = sqlite", "sqlite")]
        [InlineData("table-compatibility-mode = sqlite", "sqlite")]
        public void Should_throw_When_table_mapping_is_not_default(string setting, string value)
        {
            var config = Full($"{JournalPath}.{setting}");

            var failure = Assert.Throws<ConfigurationException>(() => JournalSettings.Create(config.GetConfig(JournalPath), JournalPath, config));

            failure.Message.Should().Be(
                $"[{JournalPath}] only supports table-mapping = default (the Akka.Persistence.Sql default schema). " +
                $"Found [{value}]. Legacy table mappings are not supported by Akka.Persistence.Embedded.");
        }

        [Fact(DisplayName = "Should_throw_When_schema_name_is_set")]
        public void Should_throw_When_schema_name_is_set()
        {
            var config = Full($"{SnapshotPath}.default.schema-name = dbo");

            var failure = Assert.Throws<ConfigurationException>(() => SnapshotSettings.Create(config.GetConfig(SnapshotPath), SnapshotPath, config));

            failure.Message.Should().Be($"[{SnapshotPath}.default.schema-name] must be null: SQLite has no schemas. Found [dbo].");
        }

        [Fact(DisplayName = "Should_log_one_warning_listing_ignored_keys_When_Sql_only_keys_are_set")]
        public void Should_log_one_warning_listing_ignored_keys_When_Sql_only_keys_are_set()
        {
            var settings = Journal($"{JournalPath} {{ parallelism = 3, provider-name = \"SQLite.MS\", sqlite {{ x = 1 }} }}");

            settings.Warnings.Should().ContainSingle().Which.Should().Be(
                $"[{JournalPath}] ignores Akka.Persistence.Sql setting(s) [provider-name, parallelism, sqlite]: they have no meaning for Akka.Persistence.Embedded.");
        }

        [Fact(DisplayName = "Should_throw_When_connection_string_is_private_memory_database")]
        public void Should_throw_When_connection_string_is_private_memory_database()
        {
            foreach (var connectionString in new[] { "Data Source=:memory:", "Data Source=scratch;Mode=Memory" })
            {
                var config = Full($"{JournalPath}.connection-string = \"{connectionString}\"");

                var failure = Assert.Throws<ConfigurationException>(() => JournalSettings.Create(config.GetConfig(JournalPath), JournalPath, config));

                failure.Message.Should().StartWith($"[{JournalPath}.connection-string] uses a private in-memory database");
            }

            // a named shared in-memory database is allowed
            Journal($"{JournalPath}.connection-string = \"Data Source=shared;Mode=Memory;Cache=Shared\"").Should().NotBeNull();
        }

        [Fact(DisplayName = "Should_throw_When_identifier_is_not_plain")]
        public void Should_throw_When_identifier_is_not_plain()
        {
            var config = Full($"{JournalPath}.default.journal.table-name = \"journal; DROP TABLE x\"");

            var failure = Assert.Throws<ConfigurationException>(() => JournalSettings.Create(config.GetConfig(JournalPath), JournalPath, config));

            failure.Message.Should().Be($"[{JournalPath}.default.journal.table-name] = [journal; DROP TABLE x] is not a plain SQL identifier.");
        }

        [Fact(DisplayName = "Should_throw_When_connection_string_is_empty")]
        public void Should_throw_When_connection_string_is_empty()
        {
            var config = ConfigurationFactory.Empty.WithFallback(SqlitePersistence.DefaultConfiguration);

            var failure = Assert.Throws<ConfigurationException>(() => JournalSettings.Create(config.GetConfig(JournalPath), JournalPath, config));

            failure.Message.Should().Be($"[{JournalPath}.connection-string] is required.");
        }

        [Fact(DisplayName = "Should_throw_When_tag_write_mode_is_unknown")]
        public void Should_throw_When_tag_write_mode_is_unknown()
        {
            var failure = Assert.Throws<ConfigurationException>(() => Journal($"{JournalPath}.tag-write-mode = Bogus"));

            failure.Message.Should().Be($"[{JournalPath}.tag-write-mode] must be Csv, TagTable or Both. Found [Bogus].");
        }

        [Fact(DisplayName = "Should_keep_serializer_null_When_core_journal_fallback_says_json")]
        public void Should_keep_serializer_null_When_core_journal_fallback_says_json()
        {
            // what core hands a journal: its section with akka.persistence.journal-plugin-fallback underneath
            var system = Full("").WithFallback(Persistence.DefaultConfig());
            var section = system.GetConfig(JournalPath).WithFallback(system.GetConfig("akka.persistence.journal-plugin-fallback"));

            JournalSettings.Create(section, JournalPath, system).DefaultSerializer.Should().BeNull("the reference config's null hides core's json");
        }

        [Fact(DisplayName = "Should_throw_When_Sql_Common_root_keys_are_set")]
        public void Should_throw_When_Sql_Common_root_keys_are_set()
        {
            var failure = Assert.Throws<ConfigurationException>(() => Journal($"{JournalPath}.table-name = event_journal"));

            failure.Message.Should().Be(
                $"[{JournalPath}.table-name] is an Akka.Persistence.Sqlite (Sql.Common) setting. " +
                $"This plugin uses the Akka.Persistence.Sql schema; set table names under {JournalPath}.default.* instead. See the migration guide.");
        }

        [Fact(DisplayName = "Should_resolve_tag_read_mode_from_write_mode_When_set_to_auto")]
        public void Should_resolve_tag_read_mode_from_write_mode_When_set_to_auto()
        {
            QueryModeFor("Csv").Should().Be(TagReadMode.Csv);
            QueryModeFor("TagTable").Should().Be(TagReadMode.TagTable);
            QueryModeFor("Both").Should().Be(TagReadMode.TagTable);
        }

        [Fact(DisplayName = "Should_throw_When_query_tag_read_mode_is_Both")]
        public void Should_throw_When_query_tag_read_mode_is_Both()
        {
            var config = Full($"{SqlitePersistence.QueryPluginId}.tag-read-mode = Both");

            Assert.Throws<ConfigurationException>(
                () => QuerySettings.Create(config.GetConfig(SqlitePersistence.QueryPluginId), SqlitePersistence.QueryPluginId, config));
        }

        [Fact(DisplayName = "Should_throw_When_query_write_plugin_is_not_this_journal")]
        public void Should_throw_When_query_write_plugin_is_not_this_journal()
        {
            var config = Full($$"""
                akka.persistence.journal.other { class = "Some.Other.Journal, Some.Other" }
                {{SqlitePersistence.QueryPluginId}}.write-plugin = "akka.persistence.journal.other"
                """);

            var failure = Assert.Throws<ConfigurationException>(
                () => QuerySettings.Create(config.GetConfig(SqlitePersistence.QueryPluginId), SqlitePersistence.QueryPluginId, config));

            failure.Message.Should().Contain("is not the Akka.Persistence.Embedded journal");
        }

        private static TagReadMode QueryModeFor(string writeMode)
        {
            var config = Full($"{JournalPath}.tag-write-mode = {writeMode}");
            return QuerySettings.Create(config.GetConfig(SqlitePersistence.QueryPluginId), SqlitePersistence.QueryPluginId, config).TagReadMode;
        }
    }
}
