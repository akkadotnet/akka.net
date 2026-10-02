//-----------------------------------------------------------------------
// <copyright file="SqliteTestDb.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Tests
{
    /// <summary>One temp SQLite file per test class. Delete it after the actor system shut down.</summary>
    public sealed class SqliteTestDb : IDisposable
    {
        private static readonly string TempDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "akka-embedded-tests");

        public SqliteTestDb()
        {
            Directory.CreateDirectory(TempDirectory);
            FilePath = System.IO.Path.Combine(TempDirectory, $"{Guid.NewGuid():N}.db");
        }

        public string FilePath { get; }

        public string ConnectionString => $"Data Source={FilePath}";

        /// <summary>The connection string as it goes into HOCON (forward slashes, so no escaping is needed).</summary>
        public string HoconConnectionString => ConnectionString.Replace('\\', '/');

        /// <summary>Opens a short-lived read-only connection and runs <paramref name="work"/> on it.</summary>
        public T Read<T>(Func<SqliteConnection, T> work)
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = FilePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            return work(connection);
        }

        /// <summary>Opens a short-lived read-write connection. For tests that build or break a schema by hand.</summary>
        public void Execute(string sql)
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = false };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public List<object?[]> Query(string sql)
        {
            return Read(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                using var reader = command.ExecuteReader();
                var rows = new List<object?[]>();
                while (reader.Read())
                {
                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < row.Length; i++)
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }

                return rows;
            });
        }

        public void Dispose()
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = FilePath };
            using (var connection = new SqliteConnection(builder.ConnectionString))
                SqliteConnection.ClearPool(connection);

            foreach (var file in new[] { FilePath, FilePath + "-journal", FilePath + "-wal", FilePath + "-shm" })
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (File.Exists(file))
                            File.Delete(file);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(100);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Thread.Sleep(100);
                    }
                }
            }
        }
    }
}
