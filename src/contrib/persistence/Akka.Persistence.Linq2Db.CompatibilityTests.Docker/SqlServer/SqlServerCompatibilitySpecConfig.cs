using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Snapshot;
using Akka.Persistence.Sql.Linq2Db.Tests;
using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using LinqToDB.Reflection;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public class SqlServerCompatibilitySpecConfig
    {
        public static Config InitSnapshotConfig(string tablename)
        {
            DbUtils.ConnectionString = DockerDbUtils.ConnectionString;
            //need to make sure db is created before the tests start
            //DbUtils.Initialize(connString);
            var specString = $@"
                    akka.persistence {{
                        publish-plugin-commands = on
                        snapshot-store {{
		sql-server {{
			# qualified type name of the SQL Server persistence journal actor
			class = ""Akka.Persistence.SqlServer.Snapshot.SqlServerSnapshotStore, Akka.Persistence.SqlServer""
			# dispatcher used to drive journal actor
			plugin-dispatcher = ""akka.actor.default-dispatcher""
			# connection string used for database access
			connection-string = ""{DbUtils.ConnectionString}""
			# default SQL commands timeout
			connection-timeout = 30s
			# SQL server schema name to table corresponding with persistent journal
			schema-name = dbo
			# SQL server table corresponding with persistent journal
			table-name = ""{tablename}""
			# should corresponding journal table be initialized automatically
			auto-initialize = on

			sequential-access = off
			
			# Recommended: change default circuit breaker settings
			# By uncommenting below and using Connection Timeout + Command Timeout
			# circuit-breaker.call-timeout=30s
		}}
	
                        linq2db {{
                        class = ""{typeof(Linq2DbSnapshotStore).AssemblyQualifiedName}""
                        plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
#plugin-dispatcher = ""akka.actor.default-dispatcher""
                        connection-string = ""{DbUtils.ConnectionString}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = """ + LinqToDB.ProviderName.SqlServer2017 + $@"""
                        #use-clone-connection = true
                        table-compatibility-mode = sqlserver
                        tables
                        {{
                        snapshot {{ 
                           auto-init = true
                           table-name = ""{tablename}""    
                          }}
                        }}
                      }}

                    }}
                    }}";

            return ConfigurationFactory.ParseString(specString);
        }
        public static Config InitJournalConfig(string tablename, string metadatatablename)
        {
            DbUtils.ConnectionString = DockerDbUtils.ConnectionString;
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