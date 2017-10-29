﻿//-----------------------------------------------------------------------
// <copyright file="BatchingSqliteCurrentEventsByPersistenceIdSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.Sql;
using Akka.Util.Internal;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Batching
{
    public class BatchingSqliteCurrentEventsByPersistenceIdSpec : CurrentEventsByPersistenceIdSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);
        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.sqlite""
            akka.persistence.journal.sqlite {{
                class = ""Akka.Persistence.Sqlite.Journal.BatchingSqliteJournal, Akka.Persistence.Sqlite""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                table-name = event_journal
                metadata-table-name = journal_metadata
                auto-initialize = on
                connection-string = ""Datasource=memdb-journal-batch-currenteventsbypersistenceid-{id}.db;Mode=Memory;Cache=Shared""
                refresh-interval = 1s
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(SqlReadJournal.DefaultConfiguration());

        public BatchingSqliteCurrentEventsByPersistenceIdSpec(ITestOutputHelper output) : base(Config(Counter.GetAndIncrement()), nameof(BatchingSqliteCurrentEventsByPersistenceIdSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
        }
    }
}
