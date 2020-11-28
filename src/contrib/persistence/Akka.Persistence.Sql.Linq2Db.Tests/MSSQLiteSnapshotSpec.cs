using System;
using Akka.Persistence.TCK.Snapshot;
using Akka.Util.Internal;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    public class MSSQLiteSnapshotSpec : SnapshotStoreSpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);
        //private static string  connString = "FullUri=file:memdb"+counter.IncrementAndGet() +"?mode=memory&cache=shared";
        private static string connString =
            "Filename=file:memdb-journal-" + counter.IncrementAndGet() +
            ".db;Mode=Memory;Cache=Shared";
        private static SqliteConnection heldSqliteConnection =
            new SqliteConnection(connString);

        public MSSQLiteSnapshotSpec(ITestOutputHelper outputHelper) : base(SQLiteSnapshotSpecConfig.Create(connString, ProviderName.SQLiteMS),
            "linq2dbJournalSpec",
            output: outputHelper)
        {
            //try
            {
                heldSqliteConnection.Open();
            }
            //catch{}
            //DataConnection.OnTrace = info =>
            //{
            //    outputHelper.WriteLine(info.SqlText);
            //    if (info.Exception != null)
            //    {
            //        outputHelper.WriteLine(info.Exception.ToString());
            //    }
            //
            //    if (!string.IsNullOrWhiteSpace(info.CommandText))
            //    {
            //        outputHelper.WriteLine(info.CommandText);
            //    }
            //};
            Initialize();
            GC.KeepAlive(heldSqliteConnection);
        }
        // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
        protected override bool SupportsSerialization => false;
    }
}