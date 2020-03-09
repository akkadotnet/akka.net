//-----------------------------------------------------------------------
// <copyright file="SqliteConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Sql.TestKit;
using Akka.Persistence.Sql.Common;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Persistence.Sqlite.Tests
{
    public class SqliteConfigSpec : Akka.TestKit.Xunit2.TestKit
    {
        public SqliteConfigSpec(ITestOutputHelper output) : base(output: output)
        {
        }

        [Fact]
        public void Should_sqlite_journal_has_default_config()
        {
            SqlitePersistence.Get(Sys);

            var config = Sys.Settings.Config.GetConfig("akka.persistence.journal.sqlite");

            Assert.False(config.IsNullOrEmpty());
            Assert.Equal("Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite", config.GetString("class"));
            Assert.Equal("akka.actor.default-dispatcher", config.GetString("plugin-dispatcher"));
            Assert.Equal(string.Empty, config.GetString("connection-string"));
            Assert.Equal(string.Empty, config.GetString("connection-string-name"));
            Assert.Equal(TimeSpan.FromSeconds(30), config.GetTimeSpan("connection-timeout"));
            Assert.Equal("event_journal", config.GetString("table-name"));
            Assert.Equal("journal_metadata", config.GetString("metadata-table-name"));
            Assert.False(config.GetBoolean("auto-initialize"));
            Assert.Equal("Akka.Persistence.Sql.Common.Journal.DefaultTimestampProvider, Akka.Persistence.Sql.Common", config.GetString("timestamp-provider"));
            Assert.False(config.HasPath("schema-name"));
        }

        [Fact]
        public void Sqlite_JournalSettings_default_should_contain_default_config()
        {
            var config = SqlitePersistence.Get(Sys).DefaultJournalConfig;
            var settings = new JournalSettings(config);

            // values should be correct
            settings.ConnectionString.Should().Be(string.Empty);
            settings.ConnectionStringName.Should().Be(string.Empty);
            settings.ConnectionTimeout.Should().Equals(TimeSpan.FromSeconds(30));
            settings.JournalTableName.Should().Equals("event_journal");
            settings.SchemaName.Should().BeNull();
            settings.MetaTableName.Should().Equals("journal_metadata");
            settings.TimestampProvider.Should().Be("Akka.Persistence.Sql.Common.Journal.DefaultTimestampProvider, Akka.Persistence.Sql.Common");
            settings.AutoInitialize.Should().BeFalse();

            // values should reflect configuration
            settings.ConnectionString.Should().Equals(config.GetString("connection-string"));
            settings.ConnectionStringName.Should().Equals(config.GetString("connection-string-name"));
            settings.ConnectionTimeout.Should().Equals(config.GetTimeSpan("connection-timeout"));
            settings.JournalTableName.Should().Equals(config.GetString("table-name"));
            settings.SchemaName.Should().Equals(config.GetString("schema-name", null));
            settings.MetaTableName.Should().Equals(config.GetString("metadata-table-name"));
            settings.TimestampProvider.Should().Be(config.GetString("timestamp-provider"));
            settings.AutoInitialize.Should().Equals(config.GetBoolean("auto-initialize"));
        }

        [Fact]
        public void Should_sqlite_snapshot_has_default_config()
        {
            SqlitePersistence.Get(Sys);

            var config = Sys.Settings.Config.GetConfig("akka.persistence.snapshot-store.sqlite");

            Assert.False(config.IsNullOrEmpty());
            Assert.Equal("Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite", config.GetString("class", null));
            Assert.Equal("akka.actor.default-dispatcher", config.GetString("plugin-dispatcher", null));
            Assert.Equal(string.Empty, config.GetString("connection-string", null));
            Assert.Equal(string.Empty, config.GetString("connection-string-name", null));
            Assert.Equal(TimeSpan.FromSeconds(30), config.GetTimeSpan("connection-timeout", null));
            // This is changed from "snapshot-store" to "snapshot"
            Assert.Equal("snapshot", config.GetString("table-name", null));
            Assert.False(config.GetBoolean("auto-initialize", false));
        }

        [Fact]
        public void Sqlite_SnapshotStoreSettings_default_should_contain_default_config()
        {
            var config = SqlitePersistence.Get(Sys).DefaultSnapshotConfig;
            var settings = new SnapshotStoreSettings(config);

            // values should be correct
            settings.ConnectionString.Should().Be(string.Empty);
            settings.ConnectionStringName.Should().Be(string.Empty);
            settings.ConnectionTimeout.Should().Equals(TimeSpan.FromSeconds(30));
            settings.SchemaName.Should().BeNull();
            settings.TableName.Should().Equals("snapshot");
            settings.AutoInitialize.Should().BeFalse();
            settings.DefaultSerializer.Should().BeNull();
            settings.FullTableName.Should().Equals(settings.TableName);

            // values should reflect configuration
            settings.ConnectionString.Should().Equals(config.GetString("connection-string"));
            settings.ConnectionStringName.Should().Equals(config.GetString("connection-string-name"));
            settings.ConnectionTimeout.Should().Equals(config.GetTimeSpan("connection-timeout"));
            settings.SchemaName.Should().Equals(config.GetString("schema-name", null));
            settings.TableName.Should().Equals(config.GetString("table-name"));
            settings.AutoInitialize.Should().Equals(config.GetBoolean("auto-initialize"));
            settings.DefaultSerializer.Should().Equals(config.GetString("serializer", null));
        }

    }
}
