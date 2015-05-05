using System.Data.Common;
using System.Data.SqlClient;
using Akka.Persistence.Sql.Common;
using Akka.Persistence.Sql.Common.Snapshot;

namespace Akka.Persistence.SqlServer.Snapshot
{
    /// <summary>
    /// Actor used for storing incoming snapshots into persistent snapshot store backed by SQL Server database.
    /// </summary>
    public class SqlServerSnapshotStore : DbSnapshotStore
    {
        private readonly SqlServerSnapshotSettings _settings;

        public SqlServerSnapshotStore() : base()
        {
            _settings = SqlServerPersistence.Instance.Apply(Context.System).SnapshotStoreSettings;
            QueryBuilder = new DefaultSnapshotQueryBuilder(_settings.SchemaName, _settings.TableName);
        }

        protected override SnapshotStoreSettings Settings { get { return _settings; } }

        protected override DbConnection CreateDbConnection()
        {
            return new SqlConnection(Settings.ConnectionString);
        }
    }
}