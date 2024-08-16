﻿//-----------------------------------------------------------------------
// <copyright file="SqliteConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Sql.TestKit;
using Akka.Persistence.Sql.Common;
using Akka.Persistence.Sql.Common.Extensions;
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
            Assert.Equal("unspecified", config.GetString("read-isolation-level"));
            Assert.Equal("unspecified", config.GetString("write-isolation-level"));
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
            settings.ConnectionTimeout.Should().Be(TimeSpan.FromSeconds(30));
            settings.JournalTableName.Should().Be("event_journal");
            settings.SchemaName.Should().BeNull();
            settings.MetaTableName.Should().Be("journal_metadata");
            settings.TimestampProvider.Should().Be("Akka.Persistence.Sql.Common.Journal.DefaultTimestampProvider, Akka.Persistence.Sql.Common");
            settings.ReadIsolationLevel.Should().Be(IsolationLevel.Unspecified);
            settings.WriteIsolationLevel.Should().Be(IsolationLevel.Unspecified);
            settings.AutoInitialize.Should().BeFalse();

            // values should reflect configuration
            settings.ConnectionString.Should().Be(config.GetString("connection-string"));
            settings.ConnectionStringName.Should().Be(config.GetString("connection-string-name"));
            settings.ConnectionTimeout.Should().Be(config.GetTimeSpan("connection-timeout"));
            settings.JournalTableName.Should().Be(config.GetString("table-name"));
            settings.SchemaName.Should().Be(config.GetString("schema-name", null));
            settings.MetaTableName.Should().Be(config.GetString("metadata-table-name"));
            settings.TimestampProvider.Should().Be(config.GetString("timestamp-provider"));
            settings.ReadIsolationLevel.Should().Be(config.GetIsolationLevel("read-isolation-level"));
            settings.WriteIsolationLevel.Should().Be(config.GetIsolationLevel("write-isolation-level"));
            settings.AutoInitialize.Should().Be(config.GetBoolean("auto-initialize"));
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
            Assert.Equal("unspecified", config.GetString("read-isolation-level"));
            Assert.Equal("unspecified", config.GetString("write-isolation-level"));
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
            settings.ConnectionTimeout.Should().Be(TimeSpan.FromSeconds(30));
            settings.SchemaName.Should().BeNull();
            settings.TableName.Should().Be("snapshot");
            settings.AutoInitialize.Should().BeFalse();
#pragma warning disable CS0618
            settings.DefaultSerializer.Should().BeNull();
#pragma warning restore CS0618
            settings.ReadIsolationLevel.Should().Be(IsolationLevel.Unspecified);
            settings.WriteIsolationLevel.Should().Be(IsolationLevel.Unspecified);
            settings.FullTableName.Should().Be(settings.TableName);

            // values should reflect configuration
            settings.ConnectionString.Should().Be(config.GetString("connection-string"));
            settings.ConnectionStringName.Should().Be(config.GetString("connection-string-name"));
            settings.ConnectionTimeout.Should().Be(config.GetTimeSpan("connection-timeout"));
            settings.SchemaName.Should().Be(config.GetString("schema-name", null));
            settings.TableName.Should().Be(config.GetString("table-name"));
            settings.ReadIsolationLevel.Should().Be(config.GetIsolationLevel("read-isolation-level"));
            settings.WriteIsolationLevel.Should().Be(config.GetIsolationLevel("write-isolation-level"));
            settings.AutoInitialize.Should().Be(config.GetBoolean("auto-initialize"));
#pragma warning disable CS0618
            settings.DefaultSerializer.Should().Be(config.GetString("serializer", null));
#pragma warning restore CS0618
        }

    }
}
