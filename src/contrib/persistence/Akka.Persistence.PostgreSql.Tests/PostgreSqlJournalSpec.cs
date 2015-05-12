using System.Configuration;
using Akka.Configuration;
using Akka.Persistence.TestKit.Journal;

namespace Akka.Persistence.PostgreSql.Tests
{
    public class PostgreSqlJournalSpec : JournalSpec
    {
        private static readonly Config SpecConfig;

        static PostgreSqlJournalSpec()
        {
            var connectionString = ConfigurationManager.ConnectionStrings["TestDb"].ConnectionString;

            var config = @"
                akka.persistence {
                    publish-plugin-commands = on
                    journal {
                        plugin = ""akka.persistence.journal.postgresql""
                        postgresql {
                            class = ""Akka.Persistence.PostgreSql.Journal.PostgreSqlJournal, Akka.Persistence.PostgreSql""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            table-name = event_journal
                            schema-name = public
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }";

            SpecConfig = ConfigurationFactory.ParseString(config);

            //need to make sure db is created before the tests start
            DbUtils.Initialize();
        }

        public PostgreSqlJournalSpec()
            : base(SpecConfig, "PostgreSqlJournalSpec")
        {
            Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean();
        }
    }
}