﻿using System;
using Xunit;

namespace Akka.Persistence.Sqlite.Tests
{
    public class SqliteConfigSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public void Should_sqlite_journal_has_default_config()
        {
            SqlitePersistence.Get(Sys);

            var config = Sys.Settings.Config.GetConfig("akka.persistence.journal.sqlite");
            
            Assert.NotNull(config);
            Assert.Equal("Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite", config.GetString("class"));
            Assert.Equal("akka.actor.default-dispatcher", config.GetString("plugin-dispatcher"));
            Assert.Equal(string.Empty, config.GetString("connection-string"));
            Assert.Equal(string.Empty, config.GetString("connection-string-name"));
            Assert.Equal(TimeSpan.FromSeconds(30), config.GetTimeSpan("connection-timeout"));
            Assert.Equal("event_journal", config.GetString("table-name"));
            Assert.Equal("metadata", config.GetString("metadata-table-name"));
            Assert.Equal(false, config.GetBoolean("auto-initialize"));
            Assert.Equal("Akka.Persistence.Sql.Common.Journal.DefaultTimestampProvider, Akka.Persistence.Sql.Common", config.GetString("timestamp-provider"));
        }

        [Fact]
        public void Should_sqlite_snapshot_has_default_config()
        {
            SqlitePersistence.Get(Sys);

            var config = Sys.Settings.Config.GetConfig("akka.persistence.snapshot-store.sqlite");

            Assert.NotNull(config);
            Assert.Equal("Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite", config.GetString("class"));
            Assert.Equal("akka.actor.default-dispatcher", config.GetString("plugin-dispatcher"));
            Assert.Equal(string.Empty, config.GetString("connection-string"));
            Assert.Equal(string.Empty, config.GetString("connection-string-name"));
            Assert.Equal(TimeSpan.FromSeconds(30), config.GetTimeSpan("connection-timeout"));
            Assert.Equal("snapshot_store", config.GetString("table-name"));
            Assert.Equal(false, config.GetBoolean("auto-initialize"));
       }
    }
}
