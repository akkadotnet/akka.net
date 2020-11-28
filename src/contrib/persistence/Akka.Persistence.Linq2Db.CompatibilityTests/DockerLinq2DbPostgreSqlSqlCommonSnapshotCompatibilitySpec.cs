// //-----------------------------------------------------------------------
// // <copyright file="DockerLinq2DbPostgreSqlSqlCommonSnapshotCompatibilitySpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.PostgreSql.Snapshot;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Snapshot;
using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using LinqToDB;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    [Collection("PostgreSQLSpec")]
    public class DockerLinq2DbPostgreSqlSqlCommonSnapshotCompatibilitySpec : SqlCommonSnapshotCompatibilitySpec
    {
        public static string _snapshotBaseConfig = @"
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
                        provider-name = """ + LinqToDB.ProviderName.PostgreSQL95 + @"""
                        use-clone-connection = true
                        table-compatibility-mode = ""postgres""
                        tables.snapshot {{ 
                           auto-init = true
                           table-name = ""{2}"" 
                           schema-name = ""public""
                           metadata-table-name = ""{3}""
                           }}
                    }}
                    postgresql {{
                                class = """+typeof(PostgreSqlSnapshotStore).AssemblyQualifiedName +@"""
                                #plugin-dispatcher = ""akka.actor.default-dispatcher""
                                plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                                table-name = ""{2}""
                                metadata-table-name = ""{3}""
                                schema-name = public
                                auto-initialize = on
                                connection-string = ""{1}""
                            }}
                }}
            }}
        ";
        
        public static Configuration.Config Create(string connString)
        {
            return ConfigurationFactory.ParseString(
                string.Format(_snapshotBaseConfig,
                    typeof(Linq2DbSnapshotStore).AssemblyQualifiedName,
                    connString,"snapshot_compat","metadata"));
        }

        protected override Configuration.Config Config { get; }

        protected override string OldSnapshot =>
            "akka.persistence.snapshot-store.postgresql";

        protected override string NewSnapshot =>
            "akka.persistence.snapshot-store.testspec";


        public DockerLinq2DbPostgreSqlSqlCommonSnapshotCompatibilitySpec(ITestOutputHelper output,
            PostgreSQLFixture fixture) : base( output)
        {
            //DebuggingHelpers.SetupTraceDump(output);
            Config = InitConfig(fixture);
            var connFactory = new AkkaPersistenceDataConnectionFactory(new SnapshotConfig(Create(DockerDbUtils.ConnectionString).GetConfig("akka.persistence.snapshot-store.testspec")));
            using (var conn = connFactory.GetConnection())
            {
                try
                {
                    conn.GetTable<SnapshotRow>().Delete();
                }
                catch (Exception e)
                {
                }
                
            }
        }
            
        public static Configuration.Config InitConfig(PostgreSQLFixture fixture)
        {
            //need to make sure db is created before the tests start
            //DbUtils.Initialize(fixture.ConnectionString);
            

            return Create(fixture.ConnectionString);
        }  
        protected void Dispose(bool disposing)
        {
            //base.Dispose(disposing);
//            DbUtils.Clean();
        }

    }
}