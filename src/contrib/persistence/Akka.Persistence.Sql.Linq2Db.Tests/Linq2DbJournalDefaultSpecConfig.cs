using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Journal;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    public static class Linq2DbJournalDefaultSpecConfig
    {
        public static string customConfig(string customJournalName,
            string journalTableName, string metadatatablename,
            string providername, string connectionstring) => $@"
akka.persistence.journal.linq2db{{
  {customJournalName} {{
  class = ""Akka.Persistence.Sql.Linq2Db.Journal.Linq2DbWriteJournal, Akka.Persistence.Sql.Linq2Db""
  provider-name = ""{providername}""
    connection-string = ""{connectionstring}""
 tables{{
  journal{{
    auto-init = true
    table-name = ""{journalTableName}""
    metadata-table-name = ""{metadatatablename}""
  }}
 }}
}}
}}";
        
        
        
        public static string _journalBaseConfig(string tablename, string metadatatablename, string providername, string connectionstring) => $@"
akka.persistence.journal.linq2db {{
  provider-name = ""{providername}""
    connection-string = ""{connectionstring}""
 tables{{
  journal{{
    auto-init = true
    table-name = ""{tablename}""
    metadata-table-name = ""{metadatatablename}""
  }}
 }}
}}
";

        public static Configuration.Config GetCustomConfig(string configName,
            string journalTableName,
            string metadataTableName, string providerName,
            string connectionString, bool asDefault)
        {
            return ConfigurationFactory.ParseString(customConfig(configName,
                journalTableName, metadataTableName, providerName,
                connectionString)+(asDefault?$@"
akka{{
  persistence {{
    journal {{
      plugin = akka.persistence.journal.linq2db.{configName}
    }}      
  }}
}}":"")).WithFallback(Linq2DbWriteJournal.DefaultConfiguration);
        }

        public static Configuration.Config GetConfig(string tableName,
            string metadatatablename, string providername, string connectionString)
        {
            return ConfigurationFactory
                .ParseString(_journalBaseConfig(tableName, metadatatablename,
                    providername, connectionString))
                .WithFallback(Linq2DbWriteJournal.DefaultConfiguration);
        }
    }
}