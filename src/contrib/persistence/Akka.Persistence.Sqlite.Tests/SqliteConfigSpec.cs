﻿//-----------------------------------------------------------------------
// <copyright file="SqliteConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Hocon;
using Akka.Persistence.Sql.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

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
            Assert.Equal("Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite", config.GetString("class", null));
            Assert.Equal("akka.actor.default-dispatcher", config.GetString("plugin-dispatcher", null));
            Assert.Equal(string.Empty, config.GetString("connection-string", null));
            Assert.Equal(string.Empty, config.GetString("connection-string-name", null));
            Assert.Equal(TimeSpan.FromSeconds(30), config.GetTimeSpan("connection-timeout", null));
            Assert.Equal("event_journal", config.GetString("table-name", null));
            Assert.Equal("journal_metadata", config.GetString("metadata-table-name", null));
            Assert.False(config.GetBoolean("auto-initialize", false));
            Assert.Equal("Akka.Persistence.Sql.Common.Journal.DefaultTimestampProvider, Akka.Persistence.Sql.Common", config.GetString("timestamp-provider", null));
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
            Assert.Equal("snapshot_store", config.GetString("table-name", null));
            Assert.False(config.GetBoolean("auto-initialize", false));
        }
    }
}
