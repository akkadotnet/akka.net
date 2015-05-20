using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Akka.Persistence.Snapshot;
using Npgsql;
using Akka.Persistence.Sql.Common.Snapshot;
using Akka.Persistence.Sql.Common;
using System;
using System.Data.Common;

namespace Akka.Persistence.PostgreSql.Snapshot
{
    /// <summary>
    /// Actor used for storing incoming snapshots into persistent snapshot store backed by PostgreSQL database.
    /// </summary>
    public class PostgreSqlSnapshotStore : DbSnapshotStore
    {
        private readonly PostgreSqlPersistenceExtension _extension;
        private readonly PostgreSqlSnapshotStoreSettings _settings;

        public PostgreSqlSnapshotStore()
        {
            _extension = PostgreSqlPersistence.Instance.Apply(Context.System);

            _settings = _extension.SnapshotStoreSettings;
            QueryBuilder = new PostgreSqlSnapshotQueryBuilder(_settings.SchemaName, _settings.TableName);
            QueryMapper = new PostgreSqlSnapshotQueryMapper(Context.System.Serialization);
        }

        protected override SnapshotStoreSettings Settings
        {
            get
            {
                return _settings;
            }
        }

        protected override DbConnection CreateDbConnection()
        {
            return new NpgsqlConnection(Settings.ConnectionString);
        }
    }
}