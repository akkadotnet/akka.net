using System.Data.Common;
using System.Data.SQLite;
using Akka.Persistence.Sql.Common;
using Akka.Persistence.Sql.Common.Snapshot;

namespace Akka.Persistence.Sqlite.Snapshot
{
    public class SqliteSnapshotStore : SqlSnapshotStore
    {
        private readonly SqlitePersistence _extension;

        public SqliteSnapshotStore()
        {
            _extension = SqlitePersistence.Get(Context.System);
            QueryBuilder = new QueryBuilder(_extension.SnapshotSettings);
            QueryMapper = new SqliteQueryMapper(Context.System.Serialization);
        }


        protected override DbConnection CreateDbConnection(string connectionString)
        {
            return new SQLiteConnection(connectionString);
        }

        protected override SnapshotStoreSettings Settings { get { return _extension.SnapshotSettings; } }
    }
}