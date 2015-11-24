//-----------------------------------------------------------------------
// <copyright file="CommonSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Data.Common;

namespace Akka.Persistence.Sql.Common.Snapshot
{
    public class CommonSnapshotStore : SqlSnapshotStore
    {
        private readonly CommonPersistence _extension;

        private readonly DbProviderFactory _dbFactory;

        public CommonSnapshotStore()
        {
            _extension = CommonPersistence.Get(Context.System);
            
            _dbFactory = DbProviderFactories.GetFactory(_extension.SnapshotSettings.ProviderName);

            QueryBuilder = new CommonQueryBuilder(_dbFactory, _extension.SnapshotSettings);
            QueryMapper = new CommonQueryMapper(Context.System.Serialization);
        }


        protected override DbConnection CreateDbConnection(string connectionString)
        {
            var connection = _dbFactory.CreateConnection();
            connection.ConnectionString = connectionString;
            return connection;
        }

        protected override SnapshotStoreSettings Settings { get { return _extension.SnapshotSettings; } }
    }
}