using Akka.Configuration;
using Akka.Persistence.Sql.Common.TestKit;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests
{
    public class SqliteJournalQuerySpec : SqlJournalQuerySpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);

        public SqliteJournalQuerySpec(ITestOutputHelper output) 
            : base(CreateSpecConfig("FullUri=file:memdb-journal-query-" + counter.IncrementAndGet() + ".db?mode=memory&cache=shared;"), "SqliteJournalQuerySpec", output: output)
        {
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
                            class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            table-name = event_journal
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }" + TimestampConfig("akka.persistence.journal.sqlite"));
        }
    }
}