using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Snapshot;
using LinqToDB;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    public static class SQLiteSnapshotSpecConfig
    {
        public static string _journalBaseConfig = @"
            akka.persistence {{
                publish-plugin-commands = on
                snapshot-store {{
                    plugin = ""akka.persistence.snapshot-store.testspec""
                    testspec {{
                        class = ""{0}""
                        #plugin-dispatcher = ""akka.actor.default-dispatcher""
plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                        connection-string = ""{1}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = ""{2}""
                        parallelism = 1
                        max-row-by-row-size = 100
                        tables.journal {{ auto-init = true }}
                        use-clone-connection = ""{3}""
                    }}
                }}
            }}
        ";
        
        public static Configuration.Config Create(string connString, string providerName)
        {
            return ConfigurationFactory.ParseString(
                string.Format(_journalBaseConfig,
                    typeof(Linq2DbSnapshotStore).AssemblyQualifiedName,
                    connString, providerName, (providerName == ProviderName.SQLiteMS).ToString().ToLower()));
        }
    }
    public static class SQLiteJournalSpecConfig
    {
        public static string _journalBaseConfig = @"
            akka.persistence {{
                publish-plugin-commands = on
                journal {{
                    plugin = ""akka.persistence.journal.testspec""
                    testspec {{
                        class = ""{0}""
                        #plugin-dispatcher = ""akka.actor.default-dispatcher""
plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                        connection-string = ""{1}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = ""{2}""
                        parallelism = 1
                        max-row-by-row-size = 100
                        tables.journal {{ auto-init = true }}
                        use-clone-connection = ""{3}""
                    }}
                }}
            }}
        ";
        
        public static Configuration.Config Create(string connString, string providerName)
        {
            return ConfigurationFactory.ParseString(
                string.Format(_journalBaseConfig,
                    typeof(Linq2DbWriteJournal).AssemblyQualifiedName,
                    connString, providerName, (providerName == ProviderName.SQLiteMS).ToString().ToLower()));
        }
    }
}