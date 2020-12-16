//-----------------------------------------------------------------------
// <copyright file="SqliteCurrentEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Linq2Db.Journal.Query.Tests;
using Akka.Persistence.Query;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Query;
using Akka.Persistence.TCK.Query;
using Akka.Util.Internal;
using LinqToDB;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Query
{
    public class SqliteCurrentEventsByTagSpec : CurrentEventsByTagSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);

        public static Config Config(int id)
        {
            var connectionString =
                 $"Filename=file:memdb-l2db-journal-currenteventsbytag-{id}.db;Mode=Memory;Cache=Shared";
            ConnectionContext.Remember(connectionString);
         return    ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal{{
                plugin = ""akka.persistence.journal.linq2db""
            
                linq2db {{
                class = ""{typeof(Linq2DbWriteJournal).AssemblyQualifiedName}""
                        
                event-adapters {{
                  color-tagger  = ""Akka.Persistence.TCK.Query.ColorFruitTagger, Akka.Persistence.TCK""
                }}
                event-adapter-bindings = {{
                  ""System.String"" = color-tagger
                }}
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                table-name = event_journal
                metadata-table-name = journal_metadata
                auto-initialize = on
                provider-name = ""{ProviderName.SQLiteMS}""
                connection-string = ""{connectionString}""
                refresh-interval = 1s
                tables{{
                  journal{{
                       auto-init = true
                  }}
                }}
            }}
            }}
            akka.persistence.query.journal.linq2db
            {{
                provider-name = ""{ProviderName.SQLiteMS}""
                connection-string = ""{connectionString}""
                table-name = event_journal
                metadata-table-name = journal_metadata
            }}
            akka.test.single-expect-default = 10s")
                .WithFallback(Linq2DbWriteJournal.DefaultConfiguration)
                .WithFallback(Linq2DbReadJournal.DefaultConfiguration);
        }

        public SqliteCurrentEventsByTagSpec(ITestOutputHelper output) : base(Config(Counter.GetAndIncrement()), nameof(SqliteCurrentEventsByTagSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<Linq2DbReadJournal>(Linq2DbReadJournal.Identifier);
        }

        //[Fact(Skip = "Test work good, but raises an exception")]
        //public override void ReadJournal_query_CurrentEventsByTag_should_complete_when_no_events()
        //{
        //}

    }
}
