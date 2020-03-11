//-----------------------------------------------------------------------
// <copyright file="BatchingSqliteEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.Sql;
using Akka.Persistence.TCK.Query;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Batching
{
    public class BatchingSqliteEventsByTagSpec : EventsByTagSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);
        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.sqlite""
            akka.persistence.journal.sqlite {{
                event-adapters {{
                  color-tagger  = ""Akka.Persistence.TCK.Query.ColorFruitTagger, Akka.Persistence.TCK""
                }}
                event-adapter-bindings = {{
                  ""System.String"" = color-tagger
                }}
                class = ""Akka.Persistence.Sqlite.Journal.BatchingSqliteJournal, Akka.Persistence.Sqlite""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                table-name = event_journal
                metadata-table-name = journal_metadata
                auto-initialize = on
                connection-string = ""Datasource=memdb-journal-batch-eventsbytag-{id}.db;Mode=Memory;Cache=Shared""
                refresh-interval = 1s
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(SqlReadJournal.DefaultConfiguration());

        public BatchingSqliteEventsByTagSpec(ITestOutputHelper output) : base(Config(Counter.GetAndIncrement()), nameof(BatchingSqliteEventsByTagSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
        }
    }
}
