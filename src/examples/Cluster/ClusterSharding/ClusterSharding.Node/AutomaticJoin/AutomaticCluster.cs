//-----------------------------------------------------------------------
// <copyright file="AutomaticCluster.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster;
using Akka.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace ClusterSharding.Node.AutomaticJoin
{
    /// <summary>
    /// Extension for Akka.NET cluster to establish cluster automatically through shared sqlite store.
    /// </summary>
    public class AutomaticCluster
    {
        private readonly ActorSystem _system;
        private readonly Cluster _cluster;
        private readonly SqlitePersistence _persistence;
        private readonly DbHelper _dbHelper;

        public AutomaticCluster(ActorSystem system)
        {
            _system = system;        
            _cluster = Cluster.Get(system);
            _persistence = SqlitePersistence.Get(system);
            _dbHelper = new DbHelper(() =>
            {
                var str = _system.Settings.Config.GetString("akka.persistence.journal.sqlite.connection-string", null);
                var conn = new SqliteConnection(str);
                conn.Open();
                return conn;
            });
        }

        public void Join()
        {
            _dbHelper.InitializeNodesTable();

            var members = _dbHelper.GetClusterMembers().ToImmutableList();
            if (members.Any())
            {
                _cluster.JoinSeedNodes(members);
                _dbHelper.AddClusterMember(_cluster.SelfAddress);
            }
            else
            {
                var self = _cluster.SelfAddress;
                _dbHelper.AddClusterMember(self);
                _cluster.JoinSeedNodes(ImmutableList.Create(self));
            }
        }

        public void Leave()
        {
            _dbHelper.RemoveClusterMember(_cluster.SelfAddress);
        }
    }
}
