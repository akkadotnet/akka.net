//-----------------------------------------------------------------------
// <copyright file="BatchingSqliteJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Batching
{
    public class BatchingSqliteJournalSpec : JournalSpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);

        public BatchingSqliteJournalSpec(ITestOutputHelper output)
            : base(CreateSpecConfig($"Datasource=memdb-journal-batch-{counter.IncrementAndGet()}.db;Mode=Memory;Cache=Shared"), "BatchingSqliteJournalSpec", output)
        {
            SqlitePersistence.Get(Sys);

            Initialize();
        }

        private static Config CreateSpecConfig(string connectionString)
        {
            return ConfigurationFactory.ParseString(@"
                akka.persistence {
                    publish-plugin-commands = on
                    journal {
                        plugin = ""akka.persistence.journal.sqlite""
                        sqlite {
                            class = ""Akka.Persistence.Sqlite.Journal.BatchingSqliteJournal, Akka.Persistence.Sqlite""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            table-name = event_journal
                            metadata-table-name = journal_metadata
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
        }

        // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
        protected override bool SupportsSerialization => false;
    }
}
