﻿//-----------------------------------------------------------------------
// <copyright file="SqliteJournalSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Serialization;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Serialization
{
    public class SqliteJournalSerializationSpec : JournalSerializationSpec
    {
        private static AtomicCounter Counter { get; } = new AtomicCounter(0);

        public SqliteJournalSerializationSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:serialization-journal-" + Counter.IncrementAndGet() + ".db;Mode=Memory;Cache=Shared"), "SqliteJournalSerializationSpec", output)
        {
            SqlitePersistence.Get(Sys);
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
