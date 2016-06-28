//-----------------------------------------------------------------------
// <copyright file="SqliteEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Query.Sql;
using Akka.Persistence.Query.Sql.Tests;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Query
{
    public class SqliteAllPersistenceIdsSpec : AllPersistenceIdsSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(50);
        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.sqlite""
            akka.persistence.journal.sqlite {{
                class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                table-name = event_journal
                meta-table-name = journal_metadata
                auto-initialize = on
                connection-string = ""FullUri=file:memdb-journal-{id}.db?mode=memory&cache=shared;""
                refresh-interval = 1s
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(SqlReadJournal.DefaultConfiguration());

        public SqliteAllPersistenceIdsSpec(ITestOutputHelper output) : base(Config(Counter.GetAndIncrement()), output)
        {
        }
    }
}