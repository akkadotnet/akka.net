//-----------------------------------------------------------------------
// <copyright file="SqliteJournalPerfSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.Sqlite.Tests.Performance
{
    // Skip performance test. Commented out for now, we'll add a specific environment variable controlled skipable fact later
    /*
    public class SqliteJournalPerfSpec : JournalPerfSpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);

        public SqliteJournalPerfSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:memdb-journal-" + counter.IncrementAndGet() + ".db;Mode=Memory;Cache=Shared"), "SqliteJournalSpec", output)
        {
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
    */
}
