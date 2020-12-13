using System;
using Akka.Configuration;
using Akka.Util.Internal;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public class
        SQLiteSqlCommonSnapshotCompatibilitySpec :
            SqlCommonSnapshotCompatibilitySpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);
        //private static string  connString = "FullUri=file:memdb"+counter.IncrementAndGet() +"?mode=memory&cache=shared";
        private static string connString =
            "Filename=file:memdb-journal-" + counter.IncrementAndGet() +
            ".db;Mode=Memory;Cache=Shared";
        private static SqliteConnection heldSqliteConnection =
            new SqliteConnection(connString);
        public SQLiteSqlCommonSnapshotCompatibilitySpec(ITestOutputHelper outputHelper) : base(outputHelper)
        {
            {
                heldSqliteConnection.Open();
            }
            //catch{}
            
            GC.KeepAlive(heldSqliteConnection);
        }

        protected override Config Config =>
            SQLiteCompatibilitySpecConfig.InitSnapshotConfig("snapshot_compat",
                connString);

        protected override string OldSnapshot =>
            "akka.persistence.snapshot-store.sqlite";

        protected override string NewSnapshot =>
            "akka.persistence.snapshot-store.linq2db";
    }
}