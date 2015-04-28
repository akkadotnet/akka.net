using Akka.Configuration;
using Akka.Persistence.TestKit.Snapshot;

namespace Akka.Persistence.SqlServer.Tests
{
    public class SqlServerSnapshotStoreSpec : SnapshotStoreSpec
    {
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString(@"
        akka.persistence {
            publish-plugin-commands = on
            snapshot-store {
                plugin = ""akka.persistence.snapshot-store.sql-server""
                sql-server {
                    class = ""Akka.Persistence.SqlServer.Snapshot.SqlServerSnapshotStore, Akka.Persistence.SqlServer""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    table-name = SnapshotStore
                    schema-name = dbo
                    auto-initialize = on
                    connection-string = ""Data Source=(LocalDB)\\v11.0;AttachDbFilename=|DataDirectory|\\Resources\\AkkaPersistenceSqlServerSpecDb.mdf;Integrated Security=True""
                }
            }
        }");

        public SqlServerSnapshotStoreSpec()
            : base(SpecConfig, "SqlServerSnapshotStoreSpec")
        {
            DbCleanup.Clean();
            Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbCleanup.Clean();
        }
    }
}