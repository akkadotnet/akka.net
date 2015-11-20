//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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