//-----------------------------------------------------------------------
// <copyright file="DBHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Microsoft.Data.Sqlite;

namespace ClusterSharding.Node.AutomaticJoin
{
    public class DbHelper
    {
        private readonly Func<SqliteConnection> _connectionFactory;

        public DbHelper(Func<SqliteConnection> connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void InitializeNodesTable()
        {
            using (var cmd = new SqliteCommand("", _connectionFactory()))
            {
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS cluster_nodes (
                    member_address VARCHAR(255) NOT NULL PRIMARY KEY
                );";

                cmd.ExecuteNonQuery();
            }
        }

        public IEnumerable<Address> GetClusterMembers()
        {
            using (var cmd = new SqliteCommand(@"SELECT member_address from cluster_nodes", _connectionFactory()))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    var result = new List<Address>();
                    while (reader.Read())
                    {
                        var addr = reader.GetString(0);
                        result.Add(Address.Parse(addr));
                    }
                    return result;
                }
            }
        }

        public void AddClusterMember(Address address)
        {
            using (var cmd = new SqliteCommand(@"INSERT INTO cluster_nodes(member_address) VALUES (@addr)", _connectionFactory()))
            using (var tx = cmd.Connection.BeginTransaction())
            {
                cmd.Transaction = tx;
                var addr = address.ToString();
                cmd.Parameters.Add("@addr", SqliteType.Text);
                cmd.Parameters["@addr"].Value = addr;

                cmd.ExecuteNonQuery();
                tx.Commit();
            }
        }

        public void RemoveClusterMember(Address address)
        {
            using (var cmd = new SqliteCommand(@"DELETE FROM cluster_nodes WHERE member_address = @addr", _connectionFactory()))
            using (var tx = cmd.Connection.BeginTransaction())
            {
                cmd.Transaction = tx;
                var addr = address.ToString();
                cmd.Parameters.Add("@addr", SqliteType.Text);
                cmd.Parameters["@addr"].Value = addr;

                cmd.ExecuteNonQuery();
                tx.Commit();
            }
        }
    }
}
