using System.Security.Cryptography;
using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Reminders.Sharding;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.Storage;
using Akka.Reminders.Tests.Golden;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Akka.Reminders.Tests;

/// <summary>
/// A copy of a checked-in golden database. The fixture file itself is never opened for writing.
/// </summary>
internal sealed class GoldenTempDatabase : IDisposable
{
    private GoldenTempDatabase(string sourcePath, string path)
    {
        SourcePath = sourcePath;
        Path = path;
    }

    public string SourcePath { get; }

    public string Path { get; }

    // Pooling off, so no pooled handle keeps the temp file open after a test finishes.
    public string ConnectionString => $"Data Source={Path};Pooling=False";

    public static string FixturePath(GoldenProvider provider)
        => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-1.5.73", GoldenConfig.FileName(provider));

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static GoldenTempDatabase Create(GoldenProvider provider)
    {
        var source = FixturePath(provider);
        File.Exists(source).Should().BeTrue($"the golden database {source} must be copied to the test output");

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"akka-reminders-golden-{Guid.NewGuid():N}.db");
        File.Copy(source, path);
        return new GoldenTempDatabase(source, path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            try
            {
                File.Delete(Path + suffix);
            }
            catch (IOException)
            {
                // best effort: a temp file left behind is harmless
            }
        }
    }
}

/// <summary>
/// Reads databases written by the released 1.5.73 packages with the current build. See
/// Fixtures/golden-1.5.73/README.md. The two subclasses run every test on a local-provider
/// and on a cluster-provider system, the two shapes that picked different serializers in 1.5.
/// </summary>
public abstract class GoldenDatabaseReadSpecs : Akka.Hosting.TestKit.TestKit
{
    private static readonly IReadOnlyList<GoldenRow> Rows = GoldenRows.Build();

    private static readonly DateTimeOffset EndOfTime = new(2200, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // What 1.5.73 stored for each kind: (serializer_id, manifest). An empty manifest is the
    // empty string, not NULL. Local-provider systems wrote JSON (1) for string/int/long and for
    // Protobuf; cluster-provider systems wrote primitive (17) and proto (2).
    private static readonly IReadOnlyDictionary<GoldenKind, (int Id, string Manifest)> LocalStored =
        new Dictionary<GoldenKind, (int, string)>
        {
            [GoldenKind.Poco] = (1, ""),
            [GoldenKind.String] = (1, ""),
            [GoldenKind.Int] = (1, ""),
            [GoldenKind.Long] = (1, ""),
            [GoldenKind.Bytes] = (4, ""),
            [GoldenKind.Bool] = (1, ""),
            [GoldenKind.Double] = (1, ""),
            [GoldenKind.CustomManifestV1] = (7001, "rp-v1"),
            [GoldenKind.CustomManifestV2] = (7001, "rp-v2"),
            [GoldenKind.TypeManifest] = (7002, "Akka.Reminders.Tests.Golden.TypedPayload, Akka.Reminders.Tests"),
            [GoldenKind.NoManifest] = (7003, ""),
            [GoldenKind.ProtoTimestamp] = (1, ""),
            [GoldenKind.ProtoInt64] = (1, "")
        };

    private static readonly IReadOnlyDictionary<GoldenKind, (int Id, string Manifest)> ClusterStored =
        new Dictionary<GoldenKind, (int, string)>(LocalStored)
        {
            [GoldenKind.String] = (17, "System.String, System.Private.CoreLib"),
            [GoldenKind.Int] = (17, "System.Int32, System.Private.CoreLib"),
            [GoldenKind.Long] = (17, "System.Int64, System.Private.CoreLib"),
            [GoldenKind.ProtoTimestamp] = (2, "Google.Protobuf.WellKnownTypes.Timestamp, Google.Protobuf"),
            [GoldenKind.ProtoInt64] = (2, "Google.Protobuf.WellKnownTypes.Int64Value, Google.Protobuf")
        };

    private static readonly string[] ExpectedColumns =
    [
        "shard_region_name", "entity_id", "reminder_key", "when_utc", "due_time_utc", "repeat_interval_ticks",
        "serializer_id", "manifest", "payload", "attempt_count", "last_failure_reason",
        "max_delivery_window_ticks", "delivery_deadline_utc", "is_completed", "completed_at_utc",
        "completion_status", "delivered_at_utc", "ack_deadline_utc"
    ];

    private readonly GoldenProvider _provider;

    protected GoldenDatabaseReadSpecs(GoldenProvider provider, ITestOutputHelper output)
        : base(output: output)
    {
        _provider = provider;
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.AddHocon(ConfigurationFactory.ParseString(GoldenConfig.SerializerHocon), HoconAddMode.Prepend);

        if (_provider == GoldenProvider.Cluster)
            builder.WithClustering(new ClusterOptions { SeedNodes = Array.Empty<string>() });
    }

    private SqliteReminderStorage OpenStorage(GoldenTempDatabase db)
        => new(SqliteReminderStorageSettings.Create(db.ConnectionString), Sys);

    private static string Snapshot(RawReminderRow r)
        => string.Join("|", r.Id, r.WhenUtc.ToString("O"), r.RepeatIntervalTicks, r.SerializerId, r.Manifest,
            Convert.ToHexString(r.Payload), r.AttemptCount, r.LastFailureReason, r.MaxDeliveryWindowTicks,
            r.DeliveryDeadlineUtc?.ToString("O"), r.IsCompleted, r.CompletedAtUtc?.ToString("O"),
            r.CompletionStatus, r.DeliveredAtUtc?.ToString("O"), r.AckDeadlineUtc?.ToString("O"));

    private static string IdOf(ScheduledReminder r)
        => $"{r.Entity.ShardRegionName}/{r.Entity.EntityId}/{r.Key.Name}@{r.DueTimeUtc:O}";

    private static IEnumerable<string> Differences(ScheduledReminder actual, GoldenRow expected)
    {
        if (actual.When != expected.When)
            yield return $"When {actual.When:O} != {expected.When:O}";
        if (actual.DueTimeUtc != expected.DueTime)
            yield return $"DueTimeUtc {actual.DueTimeUtc:O} != {expected.DueTime:O}";
        if (actual.RepeatInterval != expected.RepeatInterval)
            yield return $"RepeatInterval {actual.RepeatInterval} != {expected.RepeatInterval}";
        if (actual.AttemptCount != expected.AttemptCount)
            yield return $"AttemptCount {actual.AttemptCount} != {expected.AttemptCount}";
        if (actual.LastFailureReason != expected.LastFailureReason)
            yield return $"LastFailureReason '{actual.LastFailureReason}' != '{expected.LastFailureReason}'";
        if (actual.MaxDeliveryWindow != expected.MaxDeliveryWindow)
            yield return $"MaxDeliveryWindow {actual.MaxDeliveryWindow} != {expected.MaxDeliveryWindow}";
        if (actual.DeliveryDeadlineUtc != expected.DeliveryDeadline)
            yield return $"DeliveryDeadlineUtc {actual.DeliveryDeadlineUtc:O} != {expected.DeliveryDeadline:O}";
        if (actual.Message.GetType() != expected.Payload.GetType())
            yield return $"Message type {actual.Message.GetType()} != {expected.Payload.GetType()}";
        if (GoldenPayloads.Describe(actual.Message) != GoldenPayloads.Describe(expected.Payload))
            yield return $"Message {GoldenPayloads.Describe(actual.Message)} != {GoldenPayloads.Describe(expected.Payload)}";
    }

    private static IReadOnlyList<string> Compare(IEnumerable<ScheduledReminder> actual, IEnumerable<GoldenRow> expected)
    {
        var problems = new List<string>();
        var byId = expected.ToDictionary(r => r.Id);
        var seen = new HashSet<string>();
        foreach (var reminder in actual)
        {
            var id = IdOf(reminder);
            if (!byId.TryGetValue(id, out var row))
            {
                problems.Add($"unexpected row {id}");
                continue;
            }

            seen.Add(id);
            problems.AddRange(Differences(reminder, row).Select(d => $"{id}: {d}"));
        }

        problems.AddRange(byId.Keys.Where(id => !seen.Contains(id)).Select(id => $"missing row {id}"));
        return problems;
    }

    [Fact(DisplayName = "Should_RunOnTheExpectedProvider_When_SpecStarts")]
    public void Should_RunOnTheExpectedProvider_When_SpecStarts()
    {
        var provider = ((ExtendedActorSystem)Sys).Provider.GetType().Name;
        if (_provider == GoldenProvider.Cluster)
            provider.Should().Be("ClusterActorRefProvider");
        else
            provider.Should().Be("LocalActorRefProvider");
    }

    [Fact(DisplayName = "Should_DocumentWhatRelease1573Wrote_When_ReadingStoredSerializerIdsAndManifests")]
    public async Task Should_DocumentWhatRelease1573Wrote_When_ReadingStoredSerializerIdsAndManifests()
    {
        using var db = GoldenTempDatabase.Create(_provider);
        var raw = await GoldenRawRows.ReadAsync(db.ConnectionString, TestContext.Current.CancellationToken);
        var kinds = Rows.ToDictionary(r => r.Id, r => r.Kind);
        var table = _provider == GoldenProvider.Cluster ? ClusterStored : LocalStored;

        raw.Should().HaveCount(Rows.Count);
        var problems = raw
            .Where(r => (r.SerializerId, r.Manifest ?? "<NULL>") != table[kinds[r.Id]])
            .Select(r => $"{r.Id} ({kinds[r.Id]}): stored ({r.SerializerId}, {r.Manifest ?? "<NULL>"}), expected {table[kinds[r.Id]]}")
            .ToList();
        problems.Should().BeEmpty();

        // every kind is present, so no row of the table above goes unchecked
        raw.Select(r => kinds[r.Id]).Distinct().Should().BeEquivalentTo(Enum.GetValues<GoldenKind>());
    }

    [Fact(DisplayName = "Should_DeserializeEveryPayload_When_ResolvedBySerializerIdAndManifest")]
    public async Task Should_DeserializeEveryPayload_When_ResolvedBySerializerIdAndManifest()
    {
        using var db = GoldenTempDatabase.Create(_provider);
        var raw = await GoldenRawRows.ReadAsync(db.ConnectionString, TestContext.Current.CancellationToken);
        var expected = Rows.ToDictionary(r => r.Id);

        // Completed rows (Delivered, Cancelled, Expired, Failed) are never read back through the
        // storage API, so resolve them the way the storage does: by stored id and manifest.
        var problems = new List<string>();
        foreach (var r in raw)
        {
            var row = expected[r.Id];
            try
            {
                var actual = Sys.Serialization.Deserialize(r.Payload, r.SerializerId, r.Manifest ?? string.Empty);
                if (actual.GetType() != row.Payload.GetType())
                    problems.Add($"{r.Id}: type {actual.GetType()} != {row.Payload.GetType()}");
                else if (GoldenPayloads.Describe(actual) != GoldenPayloads.Describe(row.Payload))
                    problems.Add($"{r.Id}: {GoldenPayloads.Describe(actual)} != {GoldenPayloads.Describe(row.Payload)}");
            }
            catch (Exception ex)
            {
                problems.Add($"{r.Id} ({row.Kind}, id {r.SerializerId}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_ReadEveryPendingRowWithAllSchedulingFields_When_StorageFetchesDueReminders")]
    public async Task Should_ReadEveryPendingRowWithAllSchedulingFields_When_StorageFetchesDueReminders()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = GoldenTempDatabase.Create(_provider);
        var storage = OpenStorage(db);
        var before = (await GoldenRawRows.ReadAsync(db.ConnectionString, ct)).Select(Snapshot).ToList();

        var batch = await storage.GetNextRemindersAsync(EndOfTime, DateTimeOffset.UtcNow, new ReminderBatchSize(100_000), ct);

        var pending = Rows.Where(r => r.Status == ReminderCompletionStatus.Pending).ToList();
        pending.Should().NotBeEmpty();
        Compare(batch.Reminders, pending).Should().BeEmpty();
        batch.NextOverview.TotalPendingReminders.Should().Be(0, "every pending row fit in the batch");

        // nothing was ended as Failed because it could not be read, and nothing else changed
        var after = (await GoldenRawRows.ReadAsync(db.ConnectionString, ct)).Select(Snapshot).ToList();
        after.Should().Equal(before);

        var overview = await storage.GetRemindersOverviewAsync(DateTimeOffset.UtcNow, ct);
        overview.TotalPendingReminders.Should().Be(pending.Count);
        overview.TimeUntilNext.Should().BeLessThan(TimeSpan.Zero, "the overdue rows are the next to fire");
    }

    [Fact(DisplayName = "Should_ReadEveryOpenRowPerEntity_When_ListingRemindersForEntities")]
    public async Task Should_ReadEveryOpenRowPerEntity_When_ListingRemindersForEntities()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = GoldenTempDatabase.Create(_provider);
        var storage = OpenStorage(db);

        // Pending and AwaitingAck rows, which is everything that is not completed.
        var open = Rows.Where(r => r.IsOpen).ToList();
        var listed = new List<ScheduledReminder>();
        foreach (var entity in open.Select(r => r.Entity).Distinct())
            listed.AddRange(await storage.GetRemindersForEntityAsync(entity, take: 100_000, cancellationToken: ct));

        Compare(listed, open).Should().BeEmpty();

        foreach (var row in open.Where(r => r.Status == ReminderCompletionStatus.AwaitingAck))
        {
            var reminder = await storage.GetAwaitingAckReminderAsync(row.Entity, row.Key, row.DueTime, ct);
            reminder.Should().NotBeNull($"{row.Id} is awaiting an ack");
            Differences(reminder!, row).Should().BeEmpty($"{row.Id} is read back through the ack path");
        }
    }

    [Fact(DisplayName = "Should_ReportStatusAttemptsAndFailureReason_When_QueryingEveryOccurrence")]
    public async Task Should_ReportStatusAttemptsAndFailureReason_When_QueryingEveryOccurrence()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = GoldenTempDatabase.Create(_provider);
        var storage = OpenStorage(db);

        var problems = new List<string>();
        foreach (var row in Rows)
        {
            var status = await storage.GetReminderOccurrenceStatusAsync(row.Entity, row.Key, row.DueTime, ct);
            if (status is null)
            {
                problems.Add($"{row.Id}: no status");
                continue;
            }

            void Check<T>(string name, T actual, T expected)
            {
                if (!EqualityComparer<T>.Default.Equals(actual, expected))
                    problems.Add($"{row.Id}: {name} {actual} != {expected}");
            }

            Check("CompletionStatus", status.CompletionStatus, row.Status);
            Check("AttemptCount", status.AttemptCount, row.AttemptCount);
            Check("LastFailureReason", status.LastFailureReason, row.LastFailureReason);
            Check("DeliveryDeadlineUtc", status.DeliveryDeadlineUtc, row.DeliveryDeadline);
            Check("CompletedAtUtc", status.CompletedAtUtc, row.CompletedAt);
            Check("DeliveredAtUtc", status.DeliveredAtUtc, row.DeliveredAt);
            Check("AckDeadlineUtc", status.AckDeadlineUtc, row.AckDeadline);
            Check("NextAttemptAtUtc", status.NextAttemptAtUtc,
                row.Status == ReminderCompletionStatus.Pending ? row.When : (DateTimeOffset?)null);
        }

        problems.Should().BeEmpty();

        // all six statuses the 1.5.73 API can produce are in the fixture
        Rows.Select(r => r.Status).Distinct().Should().BeEquivalentTo(Enum.GetValues<ReminderCompletionStatus>());
    }

    [Fact(DisplayName = "Should_StoreSchedulingFieldsExactly_When_ReadingRawColumns")]
    public async Task Should_StoreSchedulingFieldsExactly_When_ReadingRawColumns()
    {
        using var db = GoldenTempDatabase.Create(_provider);
        var raw = await GoldenRawRows.ReadAsync(db.ConnectionString, TestContext.Current.CancellationToken);
        var expected = Rows.ToDictionary(r => r.Id);

        raw.Select(r => r.Id).Should().BeEquivalentTo(expected.Keys);

        var problems = new List<string>();
        foreach (var r in raw)
        {
            var row = expected[r.Id];
            void Check<T>(string name, T actual, T want)
            {
                if (!EqualityComparer<T>.Default.Equals(actual, want))
                    problems.Add($"{r.Id}: {name} {actual} != {want}");
            }

            Check("when_utc", r.WhenUtc, row.When);
            Check("repeat_interval_ticks", r.RepeatIntervalTicks, row.RepeatInterval?.Ticks);
            Check("max_delivery_window_ticks", r.MaxDeliveryWindowTicks, row.MaxDeliveryWindow?.Ticks);
            Check("delivery_deadline_utc", r.DeliveryDeadlineUtc, row.DeliveryDeadline);
            Check("attempt_count", r.AttemptCount, row.AttemptCount);
            Check("last_failure_reason", r.LastFailureReason, row.LastFailureReason);
            Check("completion_status", r.CompletionStatus, row.Status.ToString());
            Check("is_completed", r.IsCompleted, !row.IsOpen);
            Check("completed_at_utc", r.CompletedAtUtc, row.CompletedAt);
            Check("delivered_at_utc", r.DeliveredAtUtc, row.DeliveredAt);
            Check("ack_deadline_utc", r.AckDeadlineUtc, row.AckDeadline);
        }

        problems.Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_AcceptSchemaAsIsAndKeepEveryRow_When_StorageInitializes")]
    public async Task Should_AcceptSchemaAsIsAndKeepEveryRow_When_StorageInitializes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = GoldenTempDatabase.Create(_provider);
        var fixtureHashBefore = GoldenTempDatabase.Hash(db.SourcePath);
        var before = (await GoldenRawRows.ReadAsync(db.ConnectionString, ct)).Select(Snapshot).ToList();
        var schemaBefore = await ReadSchemaAsync(db.ConnectionString, ct);

        schemaBefore.Columns.Should().Equal(ExpectedColumns, "1.5.73 wrote this schema");

        // Any storage call runs CREATE TABLE/INDEX IF NOT EXISTS (AutoInitialize defaults to on).
        var storage = OpenStorage(db);
        await storage.GetRemindersOverviewAsync(DateTimeOffset.UtcNow, ct);
        await storage.ExpireRemindersAsync(DateTimeOffset.UtcNow, ct);

        var schemaAfter = await ReadSchemaAsync(db.ConnectionString, ct);
        schemaAfter.Columns.Should().Equal(schemaBefore.Columns, "the current build adds no column to a 1.5.73 file");
        schemaAfter.Indexes.Should().BeEquivalentTo(schemaBefore.Indexes, "no index is added, dropped or renamed");

        var after = (await GoldenRawRows.ReadAsync(db.ConnectionString, ct)).Select(Snapshot).ToList();
        after.Should().Equal(before, "initializing the storage must not lose or rewrite a row");

        GoldenTempDatabase.Hash(db.SourcePath).Should().Be(fixtureHashBefore, "the checked-in fixture is never written");
    }

    private static async Task<(IReadOnlyList<string> Columns, IReadOnlyList<string> Indexes)> ReadSchemaAsync(
        string connectionString, CancellationToken ct)
    {
        var columns = new List<string>();
        var indexes = new List<string>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info(\"{GoldenRawRows.TableName}\");";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                columns.Add(reader.GetString(1));
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $t AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            command.Parameters.AddWithValue("$t", GoldenRawRows.TableName);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                indexes.Add(reader.GetString(0));
        }

        return (columns, indexes);
    }
}

public sealed class GoldenDatabaseLocalReadSpecs : GoldenDatabaseReadSpecs
{
    public GoldenDatabaseLocalReadSpecs(ITestOutputHelper output) : base(GoldenProvider.Local, output)
    {
    }
}

public sealed class GoldenDatabaseClusterReadSpecs : GoldenDatabaseReadSpecs
{
    public GoldenDatabaseClusterReadSpecs(ITestOutputHelper output) : base(GoldenProvider.Cluster, output)
    {
    }
}

/// <summary>
/// Starts the real <see cref="ReminderScheduler"/> on a copy of a 1.5.73 database and checks it
/// delivers the overdue rows with the right payload, then completes them in storage.
/// </summary>
public abstract class GoldenDatabaseDeliverySpecs : Akka.Hosting.TestKit.TestKit
{
    private static readonly IReadOnlyList<GoldenRow> Rows = GoldenRows.Build();

    private readonly GoldenProvider _provider;

    protected GoldenDatabaseDeliverySpecs(GoldenProvider provider, ITestOutputHelper output)
        : base(output: output)
    {
        _provider = provider;
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.AddHocon(ConfigurationFactory.ParseString(GoldenConfig.SerializerHocon), HoconAddMode.Prepend);

        if (_provider == GoldenProvider.Cluster)
            builder.WithClustering(new ClusterOptions { SeedNodes = Array.Empty<string>() });
    }

    [Fact(DisplayName = "Should_DeliverOverdueRowsWithTheirPayloads_When_SchedulerStartsOnA1573Database")]
    public async Task Should_DeliverOverdueRowsWithTheirPayloads_When_SchedulerStartsOnA1573Database()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = GoldenTempDatabase.Create(_provider);
        var storage = new SqliteReminderStorage(SqliteReminderStorageSettings.Create(db.ConnectionString), Sys);

        var overdue = Rows.Where(r => r.Group == "overdue").ToList();
        overdue.Should().HaveCount(3);

        var probe = CreateTestProbe();
        var resolver = new TestShardRegionResolver();
        resolver.RegisterShardRegion(GoldenRows.LiveRegion, probe);

        var settings = new ReminderSettings
        {
            MaxSlippage = TimeSpan.FromMilliseconds(100),
            StorageTimeout = TimeSpan.FromSeconds(10),
            MaxDeliveryAttempts = 3,
            RetryBackoffBase = TimeSpan.FromMilliseconds(100)
        };

        var scheduler = Sys.ActorOf(
            Props.Create(() => new ReminderScheduler(settings, resolver, storage, Sys.Scheduler)),
            $"golden-scheduler-{Guid.NewGuid():N}");

        // The scheduler is up once it answers a query; startup loads the whole file first.
        var ready = await scheduler.Ask<ReminderProtocol.RemindersForEntity>(
            new ReminderProtocol.GetReminders(new ReminderEntity("__probe__", "__probe__")), TimeSpan.FromSeconds(30), ct);
        ready.ResponseCode.Should().Be(FetchRemindersResponseCode.Success);

        var delivered = new List<ReminderEnvelope>();
        for (var i = 0; i < overdue.Count; i++)
            delivered.Add(await probe.ExpectMsgAsync<ReminderEnvelope>(TimeSpan.FromSeconds(15), cancellationToken: ct));

        foreach (var row in overdue)
        {
            var envelope = delivered.Single(e => e.Entity == row.Entity && e.Key == row.Key);
            envelope.DueTimeUtc.Should().Be(row.DueTime);
            envelope.Message.GetType().Should().Be(row.Payload.GetType());
            GoldenPayloads.Describe(envelope.Message).Should().Be(GoldenPayloads.Describe(row.Payload));

            var ack = await scheduler.Ask<ReminderProtocol.ReminderAckResponse>(
                new ReminderProtocol.ReminderAck(envelope.Entity, envelope.Key, envelope.DueTimeUtc), TimeSpan.FromSeconds(10), ct);
            ack.ResponseCode.Should().Be(ReminderAckResponseCode.Success);
        }

        // nothing else in the file was due, so nothing else is delivered
        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), ct);

        // the acks reach storage in the scheduler's next flush
        await AwaitAssertAsync(async () =>
        {
            foreach (var row in overdue)
            {
                var status = await storage.GetReminderOccurrenceStatusAsync(row.Entity, row.Key, row.DueTime, ct);
                status.Should().NotBeNull();
                status!.CompletionStatus.Should().Be(ReminderCompletionStatus.Delivered, row.Id);
            }
        }, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(100), ct);

        // and the far-future rows are untouched
        var untouched = await storage.GetNextRemindersAsync(
            new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.UtcNow, new ReminderBatchSize(100_000), ct);
        untouched.Reminders.Should().HaveCount(Rows.Count(r => r.Status == ReminderCompletionStatus.Pending && r.Group != "overdue"));
    }
}

public sealed class GoldenDatabaseLocalDeliverySpecs : GoldenDatabaseDeliverySpecs
{
    public GoldenDatabaseLocalDeliverySpecs(ITestOutputHelper output) : base(GoldenProvider.Local, output)
    {
    }
}

public sealed class GoldenDatabaseClusterDeliverySpecs : GoldenDatabaseDeliverySpecs
{
    public GoldenDatabaseClusterDeliverySpecs(ITestOutputHelper output) : base(GoldenProvider.Cluster, output)
    {
    }
}
