using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Tests;
using LinqToDB.Reflection;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public class SqlServerCompatibilitySpecConfig
    {
        public static Config InitConfig(string tablename, string metadatatablename)
        {
            DbUtils.ConnectionString = ConnectionString.Instance;
            //need to make sure db is created before the tests start
            //DbUtils.Initialize(connString);
            var specString = $@"
                    akka.persistence {{
                        publish-plugin-commands = on
                        journal {{
                            plugin = ""akka.persistence.journal.sql-server""
                            sql-server {{
                                class = ""Akka.Persistence.SqlServer.Journal.SqlServerJournal, Akka.Persistence.SqlServer""
                                plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                                table-name = ""{tablename}""
                                metadata-table-name = ""{metadatatablename}""
                                schema-name = dbo
                                auto-initialize = on
                                connection-string = ""{DbUtils.ConnectionString}""
                            }}
                               testspec {{
                        class = ""{typeof(Linq2DbWriteJournal).AssemblyQualifiedName}""
                        plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
#plugin-dispatcher = ""akka.actor.default-dispatcher""
                        connection-string = ""{DbUtils.ConnectionString}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = ""{LinqToDB.ProviderName.SqlServer2017}""
                        parallelism = 3
                        table-compatibility-mode = ""sqlserver""
                        tables.journal {{ 
                           auto-init = true
                           table-name = ""{tablename}"" 
                           metadata-table-name = ""{metadatatablename}""
                           
                           }}
                    }}

                        }}
                    }}";

            return ConfigurationFactory.ParseString(specString);
        }
    }
}