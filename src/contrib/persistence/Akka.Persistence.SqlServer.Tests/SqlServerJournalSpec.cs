using Akka.Configuration;
using Akka.Persistence.TestKit.Journal;

namespace Akka.Persistence.SqlServer.Tests
{
    public class SqlServerJournalSpec : JournalSpec
    {
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString(@"
        akka.persistence {
            publish-plugin-commands = on
            journal {
                plugin = ""akka.persistence.journal.sql-server""
                sql-server {
                    class = ""Akka.Persistence.SqlServer.Journal.SqlServerJournal, Akka.Persistence.SqlServer""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    table-name = EventJournal
                    schema-name = dbo
                    auto-initialize = on
                    connection-string = ""Data Source=(LocalDB)\\v11.0;AttachDbFilename=|DataDirectory|\\Resources\\AkkaPersistenceSqlServerSpecDb.mdf;Integrated Security=True""
                }
            }
        }");

        public SqlServerJournalSpec()
            : base(SpecConfig, "SqlServerJournalSpec")
        {
            DbCleanup.Clean();
            Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbCleanup.Clean();
        }
    }
}