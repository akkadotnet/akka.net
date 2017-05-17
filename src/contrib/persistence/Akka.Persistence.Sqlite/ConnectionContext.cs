﻿//-----------------------------------------------------------------------
// <copyright file="ConnectionContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SQLite;

namespace Akka.Persistence.Sqlite
{
    /// <summary>
    /// This class has been made to make memory connections safe. In SQLite shared memory database exists as long, as there exists at least one opened connection to it.
    /// </summary>
    internal static class ConnectionContext
    {
        private static readonly ConcurrentDictionary<string, SQLiteConnection> Remembered = new ConcurrentDictionary<string, SQLiteConnection>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connectionString">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="connectionString"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public static SQLiteConnection Remember(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString), "No connection string with connection to remember");

            var conn = Remembered.GetOrAdd(connectionString, s => new SQLiteConnection(connectionString));

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
            SQLiteConnection conn;
            if (Remembered.TryRemove(connectionString, out conn))
            {
                conn.Dispose();
            }
        }
    }
}