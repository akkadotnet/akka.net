//-----------------------------------------------------------------------
// <copyright file="BackwardsCompatSqliteJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.AssemblyVersioning
{
    public class BackwardsCompatSqliteJournalSpec : Akka.TestKit.Xunit2.TestKit
    {
        public BackwardsCompatSqliteJournalSpec(ITestOutputHelper output) : base(CreateSpecConfig(@"Filename=file:AssemblyVersioning/samples/memdb-journal-1-v123-altered.db;"), "BackwardsCompatSqliteSnapshotStoreSpec", output)
        {
        }

        [Fact]
        public void Can_read_events_from_v123_sqlitedb()
        {
            var journalRef = Persistence.Instance.Apply(Sys).JournalFor(null);
            var receiverProbe = CreateTestProbe();
            journalRef.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, "p-1", receiverProbe.Ref));
            for (int i = 1; i <= 5; i++)
                receiverProbe.ExpectMsg<ReplayedMessage>(m => m.Persistent.PersistenceId == "p-1"
                                                              && m.Persistent.Payload.ToString() == "a-" + i
                                                              && m.Persistent.SequenceNr == i);
        }

        private static Config CreateSpecConfig(string connectionString)
        {
            return ConfigurationFactory.ParseString(@"
                akka.persistence {
                    publish-plugin-commands = on
                    journal {
                        plugin = ""akka.persistence.journal.sqlite""
                        sqlite {
                            class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            table-name = event_journal
                            metadata-table-name = journal_metadata
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
        }
    }
}
