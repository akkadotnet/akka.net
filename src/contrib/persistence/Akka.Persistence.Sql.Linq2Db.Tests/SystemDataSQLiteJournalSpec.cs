using System;
using System.Data.SQLite;
using Akka.Util.Internal;
using LinqToDB;
using LinqToDB.Data;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    public class SystemDataSQLiteJournalSpec : Akka.Persistence.TCK.Journal.JournalSpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);
        private static string  connString = "FullUri=file:memdb"+counter.IncrementAndGet() +"?mode=memory&cache=shared";
        //private static string connString =
                //"Data Source=:memory:file:memdb"+ counter.IncrementAndGet() +"?mode=memory&cache=shared";
            //"Filename=file:memdb-journal-" + counter.IncrementAndGet() +
            //".db;Mode=Memory;Cache=Shared";
        private static SQLiteConnection heldSqliteConnection =
            new SQLiteConnection(connString);

        public SystemDataSQLiteJournalSpec(ITestOutputHelper outputHelper) : base(SQLiteJournalSpecConfig.Create(connString, ProviderName.SQLiteClassic),
            "linq2dbJournalSpec",
            output: outputHelper)
        {
            try
            {
                heldSqliteConnection.Open();
            }
            catch{}
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