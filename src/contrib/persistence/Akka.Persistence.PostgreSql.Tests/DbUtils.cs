using System;
using System.Configuration;
using System.Data.SqlClient;
using Akka.Dispatch.SysMsg;
using Npgsql;

namespace Akka.Persistence.PostgreSql.Tests
{
    public static class DbUtils
    {
        public static void Initialize()
        {
            var connectionString = ConfigurationManager.ConnectionStrings["TestDb"].ConnectionString;
            var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString);

            //connect to postgres database to create a new database
            var databaseName = connectionBuilder.Database;
            connectionBuilder.Database = "postgres";
            connectionString = connectionBuilder.ToString();

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                bool dbExists;
                using (var cmd = new NpgsqlCommand())
                {
                    cmd.CommandText = string.Format(@"SELECT TRUE FROM pg_database WHERE datname='{0}'", databaseName);
                    cmd.Connection = conn;

                    var result = cmd.ExecuteScalar();
                    dbExists = result != null && Convert.ToBoolean(result);
                }

                if (dbExists)
                {
                    DoClean(conn);
                }
                else
                {
                    DoCreate(conn, databaseName);
                }
            }
        }

        public static void Clean()
        {
            var connectionString = ConfigurationManager.ConnectionStrings["TestDb"].ConnectionString;

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                DoClean(conn);
            }
        }

        private static void DoCreate(NpgsqlConnection conn, string databaseName)
        {
            using (var cmd = new NpgsqlCommand())
            {
                cmd.CommandText = string.Format(@"CREATE DATABASE {0}", databaseName);
                cmd.Connection = conn;
                cmd.ExecuteNonQuery();
            }
        }

        private static void DoClean(NpgsqlConnection conn)
        {
            using (var cmd = new NpgsqlCommand())
            {
                cmd.CommandText = @"
                    DROP TABLE IF EXISTS public.event_journal;
                    DROP TABLE IF EXISTS public.snapshot_store";
                cmd.Connection = conn;
                cmd.ExecuteNonQuery();
            }
        }
    }
}