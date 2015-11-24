//-----------------------------------------------------------------------
// <copyright file="ConnectionContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;

namespace Akka.Persistence.Sql.Common
{
    /// <summary>
    /// This class has been made to make memory connections safe. In SQLite shared memory database exists as long, as there exists at least one opened connection to it.
    /// </summary>
    internal static class CommonConnectionContext
    {
        private static readonly ConcurrentDictionary<string, DbConnection> Remembered = new ConcurrentDictionary<string, DbConnection>();

        public static DbConnection CreateConnection(string connectionString, String providerName)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException("connectionString", "No connection string specified");
            if (string.IsNullOrEmpty(providerName))
                throw new ArgumentNullException("providerName", "No providerName string specified");            
            
            var c = DbProviderFactories.GetFactory(providerName).CreateConnection();
            c.ConnectionString = connectionString;
            return c;
        }

        public static void Remember(string connectionString, String providerName)
        {
            if (string.IsNullOrEmpty(connectionString)) 
                throw new ArgumentNullException("connectionString", "No connection string with connection to remember");
            
            var conn = Remembered.GetOrAdd(connectionString, s => CreateConnection(connectionString, providerName));

            if (conn.State == ConnectionState.Closed)
                conn.Open();
        }

        public static void Forget(string connectionString)
        {
            DbConnection conn;
            if (Remembered.TryRemove(connectionString, out conn))
            {
                conn.Dispose();
            }
        }
    }
}