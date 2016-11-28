using System.Data.SQLite;
using Akka.Configuration;
using Akka.Persistence.Performance.TestKit;
using Akka.Util;
using NBench;

namespace Akka.Persistence.Sqlite.Tests.Performance.BatchingJournal
{
    public class BatchingSqliteSingleActorWriteSpec : SingleActorWriteSpec
    {
        public readonly string DbName = $"test-{ThreadLocalRandom.Current.Next()}.db";

        protected override Config Configuration => ConfigurationFactory.ParseString(@"
            akka.persistence.journal {
                plugin = ""akka.persistence.journal.sqlite""
                sqlite {
                    class = ""Akka.Persistence.Sqlite.Journal.BatchingSqliteJournal, Akka.Persistence.Sqlite""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    table-name = event_journal
                    metadata-table-name = journal_metadata
                    auto-initialize = on
                    connection-string = ""Data Source=""" + DbName+@""";Version=3;""
                }
            }");

        [PerfSetup]
        public override void Setup()
        {
            base.Setup();
        }

        [PerfCleanup]
        public override void Cleanup()
        {
            base.Cleanup();
            SQLiteConnection.ClearAllPools();
            //File.Delete(DbName);
        }
    }
}