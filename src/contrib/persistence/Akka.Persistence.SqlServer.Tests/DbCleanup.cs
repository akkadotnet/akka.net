using System;
using System.Data.SqlClient;

namespace Akka.Persistence.SqlServer.Tests
{
    public static class DbCleanup
    {
        private static readonly string ConnectionString = @"Data Source=(LocalDB)\v11.0;AttachDbFilename=|DataDirectory|\Resources\AkkaPersistenceSqlServerSpecDb.mdf;Integrated Security=True";

        static DbCleanup()
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", AppDomain.CurrentDomain.BaseDirectory);
        }

        public static void Clean()
        {
            using (var conn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand())
            {
                conn.Open();
                cmd.CommandText = @"
                    IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'EventJournal') BEGIN DELETE FROM dbo.EventJournal END;
                    IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'SnapshotStore') BEGIN DELETE FROM dbo.SnapshotStore END";
                cmd.Connection = conn;
                cmd.ExecuteNonQuery();
            }
        }
    }
}