using System;
using System.Data;
using System.Data.SQLite;
using Akka.Persistence.Sql.Linq2Db;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Tests;
using Akka.Util.Internal;
using LinqToDB;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.Linq2Db
{
    public class SystemDataSQLiteLinq2DbJournalPerfSpec : L2dbJournalPerfSpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);
        
        //private static string  connString = "FullUri=file:memdb"+counter.IncrementAndGet() +"?mode=memory&cache=shared";
        //private static string connString =
        //    "Data Source=:memory:file:memdb"+ counter.IncrementAndGet() +"?mode=memory&cache=shared";
        private static string  connString = "FullUri=file:memdb"+counter.IncrementAndGet() +"?mode=memory&cache=shared";

        private static SQLiteConnection heldSqliteConnection =
            new SQLiteConnection(connString);

        public static void InitWALForFileDb()
        {
            var c = new SQLiteConnection(connString);
            c.Open();
            var walCommand = c.CreateCommand();
            walCommand.CommandText =
                @"
    PRAGMA journal_mode = 'wal'
";
            walCommand.ExecuteNonQuery();
        }
            
        public SystemDataSQLiteLinq2DbJournalPerfSpec(ITestOutputHelper output)
            : base(SQLiteJournalSpecConfig.Create(connString, ProviderName.SQLiteClassic), "SqliteJournalSpec", output,eventsCount: TestConstants.NumMessages)
        {

            if (heldSqliteConnection.State != ConnectionState.Open)
            {
                heldSqliteConnection.Open();
            }

            //InitWALForFileDb();
            var conf = new JournalConfig(
                SQLiteJournalSpecConfig.Create(connString, ProviderName.SQLiteClassic).GetConfig("akka.persistence.journal.testspec"));
            
            var connFactory = new AkkaPersistenceDataConnectionFactory(conf);
            using (var conn = connFactory.GetConnection())
            {
                
                try
                {
                    
                    conn.GetTable<JournalRow>().Delete();
                    
                }
                catch (Exception e)
                {
                }
                
            }
        }
        
    }
}