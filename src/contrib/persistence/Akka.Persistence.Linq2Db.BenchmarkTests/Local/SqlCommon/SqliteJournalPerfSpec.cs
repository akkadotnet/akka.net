using Akka.Configuration;
using Akka.Persistence.Linq2Db.BenchmarkTests.Local.Linq2Db;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.SqlCommon
{
    public class SqliteJournalPerfSpec : L2dbJournalPerfSpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);

        public SqliteJournalPerfSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:memdb-journal-" + counter.IncrementAndGet() + ".db;Mode=Memory;Cache=Shared"), "SqliteJournalSpec", output,eventsCount: TestConstants.NumMessages)
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
                            #plugin-dispatcher = ""akka.actor.default-dispatcher""
                            plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
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