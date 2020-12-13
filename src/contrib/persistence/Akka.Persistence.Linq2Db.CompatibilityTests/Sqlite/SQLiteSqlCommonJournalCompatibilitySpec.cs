using System;
using Akka.Persistence.Sql.Linq2Db.Tests;
using Akka.Util.Internal;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public class SQLiteSqlCommonJournalCompatibilitySpec : SqlCommonJournalCompatibilitySpec
    {
        private static AtomicCounter counter = new AtomicCounter(0);
        //private static string  connString = "FullUri=file:memdb"+counter.IncrementAndGet() +"?mode=memory&cache=shared";
        private static string connString =
            "Filename=file:memdb-journal-" + counter.IncrementAndGet() +
            ".db;Mode=Memory;Cache=Shared";
        private static SqliteConnection heldSqliteConnection =
            new SqliteConnection(connString);
        public SQLiteSqlCommonJournalCompatibilitySpec(ITestOutputHelper outputHelper) : base(outputHelper)
        {
            //DebuggingHelpers.SetupTraceDump(outputHelper);
            {
                heldSqliteConnection.Open();
            }
            //catch{}
            
            GC.KeepAlive(heldSqliteConnection);
        }

        protected override string OldJournal =>
            "akka.persistence.journal.sqlite";

        protected override string NewJournal =>
            "akka.persistence.journal.testspec";

        protected override Configuration.Config Config =>
            SQLiteCompatibilitySpecConfig.InitJournalConfig("journal_compat",
                "journal_metadata_compat", connString);
    }
}