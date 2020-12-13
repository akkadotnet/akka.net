// //-----------------------------------------------------------------------
// // <copyright file="SQLiteCompatibilitySpecConfig.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Snapshot;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public class SQLiteCompatibilitySpecConfig
    {
        public static Config InitSnapshotConfig(string tablename, string connectionString)
        {
            //need to make sure db is created before the tests start
            //DbUtils.Initialize(connString);
            var specString = $@"
                    akka.persistence {{
                        publish-plugin-commands = on
                        snapshot-store {{
		sqlite {{
			# qualified type name of the SQL Server persistence journal actor
			class = ""Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite""
			# dispatcher used to drive journal actor
			plugin-dispatcher = ""akka.actor.default-dispatcher""
			# connection string used for database access
			connection-string = ""{connectionString}""
			# default SQL commands timeout
			connection-timeout = 30s
			# SQL server schema name to table corresponding with persistent journal
			schema-name = dbo
			# SQL server table corresponding with persistent journal
			table-name = ""{tablename}""
			# should corresponding journal table be initialized automatically
			auto-initialize = on
			
			# Recommended: change default circuit breaker settings
			# By uncommenting below and using Connection Timeout + Command Timeout
			# circuit-breaker.call-timeout=30s
		}}
	
                        linq2db {{
                        class = ""{typeof(Linq2DbSnapshotStore).AssemblyQualifiedName}""
                        plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
#plugin-dispatcher = ""akka.actor.default-dispatcher""
                        connection-string = ""{connectionString}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = """ + LinqToDB.ProviderName.SQLiteMS + $@"""
                        #use-clone-connection = true
                        table-compatibility-mode = sqlite
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
        public static Config InitJournalConfig(string tablename, string metadatatablename, string connectionString)
        {
            //need to make sure db is created before the tests start
            //DbUtils.Initialize(connString);
            var specString = $@"
                    akka.persistence {{
                        publish-plugin-commands = on
                        journal {{
                            plugin = ""akka.persistence.journal.sqlite""
                            sqlite {{
                                class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
                                plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                                table-name = ""{tablename}""
                                metadata-table-name = ""{metadatatablename}""
                                schema-name = dbo
                                auto-initialize = on
                                connection-string = ""{connectionString}""
                            }}
                               testspec {{
                        class = ""{typeof(Linq2DbWriteJournal).AssemblyQualifiedName}""
                        plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
#plugin-dispatcher = ""akka.actor.default-dispatcher""
                        connection-string = ""{connectionString}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = ""{LinqToDB.ProviderName.SQLiteMS}""
                        parallelism = 3
                        table-compatibility-mode = ""sqlite""
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