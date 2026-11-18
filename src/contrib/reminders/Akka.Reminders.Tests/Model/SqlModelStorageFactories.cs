using Akka.Actor;
using Akka.Reminders.PostgreSql;
using Akka.Reminders.PostgreSql.Configuration;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.SqlServer;
using Akka.Reminders.SqlServer.Configuration;
using Akka.Reminders.Storage;
using Microsoft.Data.Sqlite;

namespace Akka.Reminders.Tests.Model;

/// <summary>One SQLite file per scenario, deleted when the scenario ends.</summary>
public sealed class SqliteModelStorageFactory : IModelStorageFactory
{
    // A RAM disk when there is one: every commit fsyncs, which dominates the run time on a real disk.
    private readonly string _directory = Path.Combine(
        Directory.Exists("/dev/shm") ? "/dev/shm" : Path.GetTempPath(), $"akka-reminders-model-{Guid.NewGuid():N}");
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IReminderStorage, string> _files = new();

    public string Name => "Sqlite";

    public Task<IReminderStorage> CreateAsync(ActorSystem system)
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, $"{Guid.NewGuid():N}.db");
        var storage = new SqliteReminderStorage(
            SqliteReminderStorageSettings.Create($"Data Source={file};Mode=ReadWriteCreate"), system);
        _files[storage] = file;
        return Task.FromResult<IReminderStorage>(storage);
    }

    public Task DestroyAsync(IReminderStorage storage)
    {
        if (_files.TryRemove(storage, out var file))
        {
            using (var connection = new SqliteConnection($"Data Source={file};Mode=ReadWriteCreate"))
                SqliteConnection.ClearPool(connection);
            foreach (var path in new[] { file, file + "-wal", file + "-shm", file + "-journal" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}

/// <summary>One table per scenario in a shared PostgreSQL database.</summary>
public sealed class PostgreSqlModelStorageFactory(string connectionString) : IModelStorageFactory
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IReminderStorage, string> _tables = new();

    public string Name => "PostgreSql";

    public Task<IReminderStorage> CreateAsync(ActorSystem system)
    {
        var table = $"m_{Guid.NewGuid():N}";
        var storage = new PostgreSqlReminderStorage(PostgreSqlReminderStorageSettings.Create(connectionString, tableName: table), system);
        _tables[storage] = table;
        return Task.FromResult<IReminderStorage>(storage);
    }

    public async Task DestroyAsync(IReminderStorage storage)
    {
        if (!_tables.TryRemove(storage, out var table))
            return;
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS \"reminders\".\"{table}\"";
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>One table per scenario in a shared SQL Server database.</summary>
public sealed class SqlServerModelStorageFactory(string connectionString) : IModelStorageFactory
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IReminderStorage, string> _tables = new();

    public string Name => "SqlServer";

    public Task<IReminderStorage> CreateAsync(ActorSystem system)
    {
        var table = $"m_{Guid.NewGuid():N}";
        var storage = new SqlServerReminderStorage(SqlServerReminderStorageSettings.Create(connectionString, tableName: table), system);
        _tables[storage] = table;
        return Task.FromResult<IReminderStorage>(storage);
    }

    public async Task DestroyAsync(IReminderStorage storage)
    {
        if (!_tables.TryRemove(storage, out var table))
            return;
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS [reminders].[{table}]";
        await command.ExecuteNonQueryAsync();
    }
}
