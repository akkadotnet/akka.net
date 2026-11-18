using Akka.Actor;
using Akka.Configuration;
using Akka.Reminders;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.Storage;
using Akka.Reminders.Tests.Golden;
using Microsoft.Data.Sqlite;

namespace Akka.Reminders.Tests.Golden.Generator;

/// <summary>
/// Writes the two golden databases with the released Aaron.Akka.Reminders 1.5.73 packages.
/// Usage: dotnet run -c Release -- &lt;output-directory&gt;
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: dotnet run -c Release -- <output-directory>");
            return 2;
        }

        var outputDirectory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outputDirectory);

        var rows = GoldenRows.Build();
        Console.WriteLine($"Akka {typeof(ActorSystem).Assembly.GetName().Version}, " +
                          $"Akka.Reminders {typeof(ReminderKey).Assembly.GetName().Version}, " +
                          $"{rows.Count} rows");

        foreach (var provider in new[] { GoldenProvider.Local, GoldenProvider.Cluster })
        {
            var path = Path.Combine(outputDirectory, GoldenConfig.FileName(provider));
            await WriteAsync(provider, path, rows);
        }

        return 0;
    }

    private static async Task WriteAsync(GoldenProvider provider, string path, IReadOnlyList<GoldenRow> rows)
    {
        Console.WriteLine();
        Console.WriteLine($"== {provider} provider -> {path}");

        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            if (File.Exists(path + suffix))
                File.Delete(path + suffix);
        }

        var system = ActorSystem.Create("golden", ConfigurationFactory.ParseString(GoldenConfig.For(provider)));
        try
        {
            // Pooling off, so no connection keeps the file open once a call returns.
            var connectionString = $"Data Source={path};Pooling=False";
            var storage = new SqliteReminderStorage(SqliteReminderStorageSettings.Create(connectionString), system);

            // Phase 1: rows that end up Expired. The database holds nothing else yet, so each
            // ExpireRemindersAsync call only touches the rows meant for it.
            foreach (var row in rows.Where(r => r.Path == GoldenPath.Expire))
                await ScheduleAsync(storage, row);
            foreach (var at in new[] { GoldenRows.ExpireAtFirst, GoldenRows.ExpireAtSecond })
                await storage.ExpireRemindersAsync(at);

            // Phase 2: everything else, each row moved to its final state right after it is scheduled.
            foreach (var row in rows.Where(r => r.Path != GoldenPath.Expire))
            {
                await ScheduleAsync(storage, row);
                await ApplyPathAsync(storage, row);
            }

            await VerifyAsync(system, connectionString, rows);
            await ReportAsync(connectionString, rows);
        }
        finally
        {
            await system.Terminate();
        }

        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM;";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "PRAGMA journal_mode;";
            Console.WriteLine($"journal_mode: {await command.ExecuteScalarAsync()}");
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            if (File.Exists(path + suffix))
                throw new InvalidOperationException($"Unexpected side file {path + suffix}");
        }

        Console.WriteLine($"size: {new FileInfo(path).Length} bytes");
    }

    private static async Task ScheduleAsync(SqliteReminderStorage storage, GoldenRow row)
    {
        var result = await storage.ScheduleReminderAsync(row.ToScheduledReminder());
        if (result.ResponseCode != ReminderScheduleResponseCode.Success)
            throw new InvalidOperationException($"Schedule failed for {row.Id}: {result.ResponseCode} {result.Message}");
    }

    private static async Task ApplyPathAsync(SqliteReminderStorage storage, GoldenRow row)
    {
        switch (row.Path)
        {
            case GoldenPath.Open:
                break;

            case GoldenPath.AwaitingAck:
                Require(row, await storage.MarkRemindersAsAwaitingAckAsync(
                [
                    new AwaitingAckReminder(row.Entity, row.Key, row.DueTime, row.DeliveredAt!.Value, row.AckDeadline!.Value)
                ]));
                break;

            case GoldenPath.MarkCompleted:
                Require(row, await storage.MarkRemindersAsCompletedAsync(
                [
                    new CompletedReminder(row.Entity, row.Key, row.DueTime, row.CompletedAt!.Value, row.Status)
                ]));
                break;

            case GoldenPath.Acknowledged:
                var completedAt = row.CompletedAt!.Value;
                Require(row, await storage.MarkRemindersAsAwaitingAckAsync(
                [
                    new AwaitingAckReminder(row.Entity, row.Key, row.DueTime, completedAt.AddSeconds(-1), completedAt.AddMinutes(1))
                ]));
                var ack = await storage.AcknowledgeReminderAsync(row.Entity, row.Key, row.DueTime, completedAt);
                Require(row, ack.Success);
                break;

            default:
                throw new InvalidOperationException($"Unexpected path {row.Path} for {row.Id}");
        }
    }

    private static void Require(GoldenRow row, bool ok)
    {
        if (!ok)
            throw new InvalidOperationException($"Storage call failed for {row.Id} ({row.Path})");
    }

    // 1.5.73 must read its own output before the file is accepted as a fixture.
    private static async Task VerifyAsync(ActorSystem system, string connectionString, IReadOnlyList<GoldenRow> rows)
    {
        var raw = await GoldenRawRows.ReadAsync(connectionString);
        if (raw.Count != rows.Count)
            throw new InvalidOperationException($"Expected {rows.Count} rows, found {raw.Count}");

        var expected = rows.ToDictionary(r => r.Id);
        foreach (var r in raw)
        {
            var row = expected[r.Id];
            var actual = system.Serialization.Deserialize(r.Payload, r.SerializerId, r.Manifest ?? string.Empty);
            if (GoldenPayloads.Describe(actual) != GoldenPayloads.Describe(row.Payload))
                throw new InvalidOperationException(
                    $"1.5.73 read back {GoldenPayloads.Describe(actual)} for {row.Id}, expected {GoldenPayloads.Describe(row.Payload)}");

            if (r.CompletionStatus != row.Status.ToString())
                throw new InvalidOperationException($"{row.Id} stored as {r.CompletionStatus}, expected {row.Status}");
        }

        var storage = new SqliteReminderStorage(SqliteReminderStorageSettings.Create(connectionString), system);
        var pending = await storage.GetNextRemindersAsync(
            new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero), GoldenRows.Past, new ReminderBatchSize(100_000));
        var expectedPending = rows.Count(r => r.Status == ReminderCompletionStatus.Pending);
        if (pending.Reminders.Count != expectedPending)
            throw new InvalidOperationException($"Expected {expectedPending} pending, read {pending.Reminders.Count}");
    }

    private static async Task ReportAsync(string connectionString, IReadOnlyList<GoldenRow> rows)
    {
        var kinds = rows.ToDictionary(r => r.Id, r => r.Kind);
        var raw = await GoldenRawRows.ReadAsync(connectionString);

        Console.WriteLine("kind | serializer_id | manifest | rows");
        foreach (var group in raw
                     .GroupBy(r => (Kind: kinds[r.Id], r.SerializerId, r.Manifest))
                     .OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.SerializerId).ThenBy(g => g.Key.Manifest))
        {
            Console.WriteLine($"{group.Key.Kind} | {group.Key.SerializerId} | {group.Key.Manifest ?? "<NULL>"} | {group.Count()}");
        }

        Console.WriteLine("status | rows");
        foreach (var group in raw.GroupBy(r => r.CompletionStatus).OrderBy(g => g.Key))
            Console.WriteLine($"{group.Key} | {group.Count()}");
    }
}
