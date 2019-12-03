// //-----------------------------------------------------------------------
// // <copyright file="ConflictNameResolver.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Sqlite.Snapshot
{
    /// <summary>
    /// Check if there is a conflict with snapshot table name
    /// </summary>
    internal class ConflictNameResolver
    {
        /// <summary>
        /// Tries to resolve the conflict of snapshot table names
        /// </summary>
        /// <param name="connection">sqlite connection</param>
        /// <param name="configuration">sqlite query configuration</param>
        /// <returns>Returns true if has the conflict</returns>
        public async Task<bool> Resolve(SqliteConnection connection, SqliteQueryConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(configuration.DefaultSnapshotTableName)
                || string.Equals(
                    configuration.DefaultSnapshotTableName, 
                    configuration.SnapshotTableName, StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }

            var sql = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{configuration.DefaultSnapshotTableName}';";
            using (var command = new SqliteCommand(sql, connection))
            {
                var originalName = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                var hasConflict = string.Equals(
                    originalName, 
                    configuration.DefaultSnapshotTableName, 
                    StringComparison.InvariantCultureIgnoreCase);

                if (hasConflict)
                {
                    configuration.UseDefaultSnapshotTableName();
                }
                
                return hasConflict;
            }
        }
    }
}