﻿//-----------------------------------------------------------------------
// <copyright file="JsonBatchingSqliteJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Batching.Json
{
    public class JsonBatchingSqliteJournalSpec : JournalSpec
    {
        private static AtomicCounter counter = new AtomicCounter(300);

        public JsonBatchingSqliteJournalSpec(ITestOutputHelper output)
            : base(CreateSpecConfig($"Datasource=memdb-journal-batch-json-{counter.IncrementAndGet()}.db;Mode=Memory;Cache=Shared"), "JsonBatchingSqliteJournalSpec", output)
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
                            stored-as = json
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
        }
    }
}