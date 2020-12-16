using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Snapshot;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    public static class SQLServerJournalSpecConfig
    {
        public static string _journalBaseConfig = @"
akka.persistence {{
                publish-plugin-commands = on
                journal {{
                    plugin = ""akka.persistence.journal.linq2db""
                    linq2db {{
                        class = ""{0}""
                        plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
#plugin-dispatcher = ""akka.actor.default-dispatcher""
                        connection-string = ""{1}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = """ + LinqToDB.ProviderName.SqlServer2017 + @"""
                        parallelism = {4}
                        batch-size = {3}
                        #use-clone-connection = true
                        tables.journal {{ 
                           auto-init = true
                           table-name = ""{2}"" 
                           }}
                    }}
                }}
            }}
        ";
        public static Configuration.Config Create(string connString, string tableName, int batchSize = 100, int parallelism = 2)
        {
            return ConfigurationFactory.ParseString(
                string.Format(_journalBaseConfig,
                    typeof(Linq2DbWriteJournal).AssemblyQualifiedName,
                    connString,tableName,batchSize, parallelism));
        }
    }
    
    public static class SQLServerSnapshotSpecConfig
    {
        public static string _journalBaseConfig = @"
akka.persistence {{
                publish-plugin-commands = on
                snapshot-store {{
                    plugin = ""akka.persistence.snapshot-store.linq2db""
                    linq2db {{
                        class = ""{0}""
                        plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
#plugin-dispatcher = ""akka.actor.default-dispatcher""
                        connection-string = ""{1}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = """ + LinqToDB.ProviderName.SqlServer2017 + @"""
                        parallelism = {4}
                        batch-size = {3}
                        #use-clone-connection = true
                        tables.snapshot {{ 
                           auto-init = true
                           table-name = ""{2}""
                           column-names {{
                           }} 
                           }}
                    }}
                }}
            }}
        ";
        public static Configuration.Config Create(string connString, string tableName, int batchSize = 100, int parallelism = 2)
        {
            return ConfigurationFactory.ParseString(
                string.Format(_journalBaseConfig,
                    typeof(Linq2DbSnapshotStore).AssemblyQualifiedName,
                    connString,tableName,batchSize, parallelism));
        }
    }
}