//-----------------------------------------------------------------------
// <copyright file="ConnectionContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Sqlite
{
    /// <summary>
    /// This class has been made to make memory connections safe. In SQLite shared memory database exists as long, as there exists at least one opened connection to it.
    /// </summary>
    internal static class ConnectionContext
    {
        private static readonly ConcurrentDictionary<string, SqliteConnection> Remembered = new ConcurrentDictionary<string, SqliteConnection>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connectionString">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="connectionString"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public static SqliteConnection Remember(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString), "No connection string with connection to remember");

            var conn = Remembered.GetOrAdd(connectionString, s => new SqliteConnection(connectionString));

            if (conn.State != ConnectionState.Open)
                conn.Open();

            return conn;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connectionString">TBD</param>
        public static void Forget(string connectionString)
        {
            SqliteConnection conn;
            if (Remembered.TryRemove(connectionString, out conn))
            {
                conn.Dispose();
            }
        }
    }
}
