# Akka.Reminders

Durable, re-entrant reminders system for Akka.Cluster.Sharding - designed for scheduling and delivering time-based messages to sharded entities with automatic persistence and recovery.

## Overview

Akka.Reminders provides a reliable way to schedule reminders (time-delayed messages) for your sharded actors. Unlike standard Akka.NET scheduling, reminders are:

- **Durable**: Persisted to storage and survive actor restarts
- **Re-entrant**: Automatically rescheduled after delivery
- **Cluster-aware**: Run as a cluster singleton with proper failover
- **Scalable**: Handle thousands of reminders with minimal overhead

## Features

- ✅ **Single and recurring reminders** - Schedule one-time or repeating time-based messages
- ✅ **SQL storage backends** - Production-ready SQL Server, PostgreSQL, and SQLite support
- ✅ **Automatic retries** - Failed deliveries retry with exponential backoff
- ✅ **Cluster singleton** - Reminder scheduler with automatic failover
- ✅ **Akka.Hosting integration** - First-class configuration API
- ✅ **Testable** - Uses `ITimeProvider` abstraction for deterministic testing
- ✅ **Automatic cleanup** - Periodic pruning of completed/cancelled reminders
- ✅ **Delivery tracking** - Monitor attempts, failures, and completion status

## Table of Contents

- [Installation](#installation)
- [Supported Storage Backends](#supported-storage-backends)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
  - [SQL Server Storage](#sql-server-storage)
  - [PostgreSQL Storage](#postgresql-storage)
  - [SQLite Storage](#sqlite-storage)
  - [In-Memory Storage](#in-memory-storage)
  - [Reminder Settings](#reminder-settings)
- [Usage Examples](#usage-examples)
- [API Reference](#api-reference)
- [Testing](#testing)
- [Native AOT](#native-aot)
- [Architecture](#architecture)
- [Design Documents](#design-documents)

## Installation

**Core Package:**
```bash
dotnet add package Akka.Reminders
```

**SQL Server Storage:**
```bash
dotnet add package Akka.Reminders.SqlServer
```

**PostgreSQL Storage:**
```bash
dotnet add package Akka.Reminders.PostgreSql
```

**SQLite Storage:**
```bash
dotnet add package Akka.Reminders.Sqlite
```

**Legacy Compatibility Package (all providers):**
```bash
dotnet add package Akka.Reminders.Sql
```

## Supported Storage Backends

| Storage Backend | Package | Auto-Initialize | Documentation |
|----------------|---------|-----------------|---------------|
| In-Memory | `Akka.Reminders` | N/A | Built-in (development/testing only) |
| SQL Server | `Akka.Reminders.SqlServer` | Yes | [SQL Server Schema](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/Akka.Reminders.SqlServer/Scripts/SqlServer-Create.sql) |
| PostgreSQL | `Akka.Reminders.PostgreSql` | Yes | [PostgreSQL Schema](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/Akka.Reminders.PostgreSql/Scripts/PostgreSql-Create.sql) |
| SQLite | `Akka.Reminders.Sqlite` | Yes | [SQLite Schema](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/Akka.Reminders.Sqlite/Scripts/Sqlite-Create.sql) |

> **Compatibility:** `Akka.Reminders.Sql` remains available and fully functional as a compatibility package, but new projects should prefer provider-specific packages (`SqlServer`, `PostgreSql`, or `Sqlite`).

> **Note:** For production deployments, you can manually create database schemas using the provided SQL scripts instead of using auto-initialization.

## Quick Start

### Basic Setup with In-Memory Storage

```csharp
using Akka.Hosting;
using Akka.Reminders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkka("MySystem", (configBuilder, provider) =>
{
    configBuilder
        .WithClustering()
        .WithShardRegion<MyEntity>(
            "my-entities",
            (system, registry, resolver) => entityId =>
                Props.Create(() => new MyEntityActor(entityId)),
            new MyMessageExtractor(),
            new ShardOptions())
        .WithReminders("reminder-host", reminders => reminders
            .WithStorage(_ => new InMemoryReminderStorage()));
});
```

### Production Setup with SQL Server

```csharp
using Akka.Hosting;
using Akka.Reminders;
using Akka.Reminders.SqlServer.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkka("MySystem", (configBuilder, provider) =>
{
    configBuilder
        .WithClustering()
        .WithShardRegion<MyEntity>(
            "my-entities",
            (system, registry, resolver) => entityId =>
                Props.Create(() => new MyEntityActor(entityId)),
            new MyMessageExtractor(),
            new ShardOptions())
        .WithReminders("reminder-host", reminders => reminders
            .WithSqlServerStorage(
                connectionString: builder.Configuration.GetConnectionString("Reminders"),
                schemaName: "dbo",
                tableName: "akka_reminders",
                autoInitialize: true));
});
```

### Using Reminders in Actors

```csharp
public class MyEntityActor : ReceiveActor
{
    private readonly IReminderClient _reminders;

    public MyEntityActor(string entityId)
    {
        var extension = Context.System.ReminderClient();
        _reminders = extension.CreateClient("my-entities", entityId);

        ReceiveAsync<ScheduleReminder>(async msg =>
        {
            // Schedule a one-time reminder that expires if it is more than 30 seconds late.
            var result = await _reminders.ScheduleSingleReminderAsync(
                new ReminderKey("my-reminder"),
                DateTimeOffset.UtcNow.AddMinutes(5),
                new DoSomething(),
                maxDeliveryWindow: TimeSpan.FromSeconds(30));

            if (result.ResponseCode == ReminderScheduleResponseCode.Success)
            {
                // Reminder scheduled successfully
            }
        });

        ReceiveAsync<ScheduleRecurring>(async msg =>
        {
            // Schedule a recurring reminder that fires every hour
            await _reminders.ScheduleRecurringReminderAsync(
                new ReminderKey("hourly-check"),
                DateTimeOffset.UtcNow.AddHours(1),
                TimeSpan.FromHours(1),
                new PerformHealthCheck());
        });

        ReceiveAsync<ReminderEnvelope<DoSomething>>(async envelope =>
        {
            if (envelope.Deadline.IsExpired())
            {
                await _reminders.AckAsync(envelope);
                return;
            }

            // Handle the reminder when it fires
            Console.WriteLine("Reminder received!");

            // Acknowledge receipt so the scheduler marks it delivered.
            // If AckAsync fails or times out, the reminder will be retried.
            await _reminders.AckAsync(envelope);
        });
    }
}
```

## Configuration

### SQL Server Storage

`using Akka.Reminders.SqlServer.Hosting;`
`using Akka.Reminders.SqlServer.Configuration;`

The `WithSqlServerStorage` extension method configures SQL Server as the reminder storage backend:

```csharp
.WithReminders("reminder-host", reminders => reminders
    .WithSqlServerStorage(
        connectionString: "Server=localhost;Database=Reminders;User Id=sa;Password=YourPassword;",
        schemaName: "dbo",
        tableName: "akka_reminders",
        autoInitialize: true))
```

**Parameters:**
- `connectionString`: SQL Server connection string
- `schemaName`: Database schema name (default: "reminders")
- `tableName`: Table name for reminders (default: "scheduled_reminders")
- `autoInitialize`: Auto-create schema/table if missing (default: true)

**Advanced Configuration:**

```csharp
.WithReminders("reminder-host", reminders => reminders
    .WithSqlServerStorage(new SqlServerReminderStorageSettings
    {
        ConnectionString = "Server=localhost;...",
        SchemaName = "custom_schema",
        TableName = "my_reminders",
        CommandTimeout = TimeSpan.FromSeconds(60),
        AutoInitialize = false // Manual schema management
    }))
```

**Connection Factory (Azure AD access tokens):**

When a connection needs settings a connection string can't carry, such as an Azure AD access token, pass a connection factory instead of a connection string:

```csharp
.WithReminders("reminder-host", reminders => reminders
    .WithSqlServerStorage(
        connectionFactory: async ct => new SqlConnection(connectionString)
        {
            AccessToken = await tokenProvider.GetTokenAsync(ct)
        },
        autoInitialize: true))
```

The factory must return a new, unopened connection. The storage opens and disposes it, and calls the factory once per operation (schema auto-initialization included), so each call can return a fresh token. The overload takes the same optional `schemaName`, `tableName` and `autoInitialize` parameters. You can also set `SqlServerReminderStorageSettings.ConnectionFactory`; set exactly one of `ConnectionString` or `ConnectionFactory`, or `Validate()` throws. No process-wide `SqlAuthenticationProvider` is needed.

**Manual Schema Setup:**

For production environments, you may prefer to manually create the database schema. Use the provided SQL script:

📄 [SQL Server Schema Script](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/Akka.Reminders.SqlServer/Scripts/SqlServer-Create.sql)

```sql
-- Run this script against your database
-- Creates schema, table, and indexes
```

### PostgreSQL Storage

`using Akka.Reminders.PostgreSql.Hosting;`
`using Akka.Reminders.PostgreSql.Configuration;`

The `WithPostgreSqlStorage` extension method configures PostgreSQL as the reminder storage backend:

```csharp
.WithReminders("reminder-host", reminders => reminders
    .WithPostgreSqlStorage(
        connectionString: "Host=localhost;Database=reminders;Username=postgres;Password=postgres",
        schemaName: "public",
        tableName: "akka_reminders",
        autoInitialize: true))
```

**Parameters:**
- `connectionString`: PostgreSQL connection string
- `schemaName`: Database schema name (default: "reminders")
- `tableName`: Table name for reminders (default: "scheduled_reminders")
- `autoInitialize`: Auto-create schema/table if missing (default: true)

> Backward compatibility: default schema remains `reminders` unless you explicitly override it.

**Advanced Configuration:**

```csharp
.WithReminders("reminder-host", reminders => reminders
    .WithPostgreSqlStorage(new PostgreSqlReminderStorageSettings
    {
        ConnectionString = "Host=localhost;...",
        SchemaName = "custom_schema",
        TableName = "my_reminders",
        CommandTimeout = TimeSpan.FromSeconds(60),
        AutoInitialize = false // Manual schema management
    }))
```

**Manual Schema Setup:**

For production environments, you may prefer to manually create the database schema. Use the provided SQL script:

📄 [PostgreSQL Schema Script](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/Akka.Reminders.PostgreSql/Scripts/PostgreSql-Create.sql)

```sql
-- Run this script against your database
-- Creates schema, table, and indexes
```

**HOCON Configuration:**

```hocon
akka.reminders.postgresql {
  connection-string = "Host=localhost;Database=reminders;Username=postgres;Password=postgres"
  schema-name = "public"
  table-name = "scheduled_reminders"
  auto-initialize = true
  command-timeout = 30s
}
```

```csharp
.WithReminders("reminder-host", reminders => reminders
    .WithPostgreSqlStorageFromConfig())
```

### SQLite Storage

`using Akka.Reminders.Sqlite.Hosting;`

The `WithSqliteStorage` extension method configures SQLite as the reminder storage backend:

```csharp
.WithReminders("reminder-host", reminders => reminders
    .WithSqliteStorage(
        connectionString: "Data Source=reminders.db;Mode=ReadWriteCreate;Cache=Shared",
        tableName: "akka_reminders",
        autoInitialize: true))
```

**Parameters:**
- `connectionString`: SQLite connection string
- `tableName`: Table name for reminders (default: "scheduled_reminders")
- `autoInitialize`: Auto-create table if missing (default: true)

**Manual Schema Setup:**

📄 [SQLite Schema Script](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/Akka.Reminders.Sqlite/Scripts/Sqlite-Create.sql)

### In-Memory Storage

For development and testing, use the built-in in-memory storage:

```csharp
// Explicit convenience method (recommended)
.WithReminders("reminder-host", reminders => reminders
    .WithInMemoryStorage())

// Or manually instantiate
.WithReminders("reminder-host", reminders => reminders
    .WithStorage(_ => new InMemoryReminderStorage()))
```

> ⚠️ **Warning:** In-memory storage is not durable and should only be used for development/testing.
>
> 💡 **Tip:** For unit tests, consider using `WithLocalReminders()` instead for instant startup without cluster bootstrap delays. See the [Testing](#testing) section for details.

### Reminder Settings

Configure reminder behavior and performance characteristics:

```csharp
.WithReminders("reminder-host", reminders => reminders
    .WithSqlServerStorage(connectionString: "...")
    .WithSettings(new ReminderSettings
    {
        // Maximum time drift allowed before immediate delivery
        MaxSlippage = TimeSpan.FromSeconds(5),

        // Timeout for storage operations
        StorageTimeout = TimeSpan.FromSeconds(5),

        // How frequently to prune completed/cancelled reminders
        PruneInterval = TimeSpan.FromHours(12),

        // Age threshold for pruning reminders
        PruneOlderThan = TimeSpan.FromDays(30),

        // Maximum delivery attempts before marking as permanently failed
        // Applies to both infrastructure failures and ack timeouts
        MaxDeliveryAttempts = 10,

        // Default window an occurrence stays actionable after its due time.
        // null (default) = no deadline: retries continue until MaxDeliveryAttempts.
        // Strongly recommended to set this so stale reminders expire instead of replaying.
        DefaultMaxDeliveryWindow = TimeSpan.FromHours(24),

        // Maximum reminders fetched per batch (limits query size under load)
        MaxBatchSize = 1000,

        // Maximum reminders delivered before writes are attempted.
        // Lower values reduce duplicate blast radius during write outages.
        DeliveryCommitChunkSize = 100,

        // Base delay for exponential backoff on retries
        // Retries are also bounded by each occurrence's delivery deadline.
        RetryBackoffBase = TimeSpan.FromSeconds(60),

        // Cap on exponential backoff to prevent absurdly long intervals
        MaxRetryBackoff = TimeSpan.FromMinutes(10),

        // How long to wait for a recipient ack before retrying delivery.
        // This also controls the Deadline on delivered ReminderEnvelope<T> messages —
        // when another retry is possible, the envelope deadline equals the ack timeout
        // so recipients know exactly how long until the next delivery replaces this one.
        AckTimeout = TimeSpan.FromSeconds(10)
    }))
```

> ⚠️ **Warning:** By default a reminder has **no delivery deadline** — it is retried until `MaxDeliveryAttempts` is exhausted, no matter how far past its due time it drifts. After a restart, upgrade, or prolonged outage this can cause a large backlog of stale reminders to fire at once. Set `DefaultMaxDeliveryWindow` (or pass `maxDeliveryWindow` per call) to how long a reminder stays actionable after its due time — for example 24 hours — so overdue occurrences expire instead of replaying.

## Usage Examples

### Health Check Pattern

```csharp
public class HealthCheckActor : ReceiveActor
{
    private readonly IReminderClient _reminders;

    public HealthCheckActor(string entityId)
    {
        _reminders = Context.System.ReminderClient()
            .CreateClient("health-checks", entityId);

        ReceiveAsync<Initialize>(async _ =>
        {
            // Schedule health check every 5 minutes
            await _reminders.ScheduleRecurringReminderAsync(
                new ReminderKey("health-check"),
                DateTimeOffset.UtcNow.AddMinutes(5),
                TimeSpan.FromMinutes(5),
                new PerformHealthCheck());
        });

        ReceiveAsync<ReminderEnvelope<PerformHealthCheck>>(async envelope =>
        {
            if (envelope.Deadline.IsExpired())
            {
                await _reminders.AckAsync(envelope);
                return;
            }

            var isHealthy = await CheckHealth();
            if (!isHealthy)
            {
                Self.Tell(new Restart());
            }

            await _reminders.AckAsync(envelope);
        });
    }
}
```

### Delayed Order Processing

```csharp
public class OrderActor : ReceiveActor
{
    private readonly IReminderClient _reminders;

    public OrderActor(string orderId)
    {
        _reminders = Context.System.ReminderClient()
            .CreateClient("orders", orderId);

        ReceiveAsync<PlaceOrder>(async order =>
        {
            await ProcessOrder(order);

            // Schedule payment check in 24 hours
            await _reminders.ScheduleSingleReminderAsync(
                new ReminderKey("payment-check"),
                DateTimeOffset.UtcNow.AddHours(24),
                new CheckPayment());
        });

        ReceiveAsync<ReminderEnvelope<CheckPayment>>(async envelope =>
        {
            if (envelope.Deadline.IsExpired())
            {
                await _reminders.AckAsync(envelope);
                return;
            }

            if (!await PaymentReceived())
            {
                Self.Tell(new CancelOrder());
            }

            await _reminders.AckAsync(envelope);
        });
    }
}
```

## API Reference

### IReminderClient

The `IReminderClient` interface provides the primary API for scheduling and managing reminders:

```csharp
// Get client instance for an entity
var extension = Context.System.ReminderClient();
var client = extension.CreateClient("shard-region-name", "entity-id");
```

#### Schedule Single Reminder
```csharp
Task<ReminderScheduled> ScheduleSingleReminderAsync(
    ReminderKey key,
    DateTimeOffset when,
    object message,
    TimeSpan? maxDeliveryWindow = null,
    CancellationToken cancellationToken = default)
```

Schedules a one-time reminder that fires at the specified time. When `maxDeliveryWindow` is provided,
the occurrence expires after `when + maxDeliveryWindow` and will not be retried beyond that deadline.

#### Schedule Recurring Reminder
```csharp
Task<ReminderScheduled> ScheduleRecurringReminderAsync(
    ReminderKey key,
    DateTimeOffset firstOccurrence,
    TimeSpan interval,
    object message,
    TimeSpan? maxDeliveryWindow = null,
    CancellationToken cancellationToken = default)
```

Schedules a recurring reminder that fires repeatedly at the specified interval. Recurring reminders are
latest-only: each occurrence expires when the next occurrence is delivered or becomes due, or sooner if `maxDeliveryWindow`
produces an earlier deadline.

#### Cancel Reminder
```csharp
Task<ReminderCancelled> CancelReminderAsync(
    ReminderKey key,
    CancellationToken cancellationToken = default)
```

Cancels a specific reminder by key.

#### Cancel All Reminders
```csharp
Task<RemindersCancelled> CancelAllRemindersAsync(
    CancellationToken cancellationToken = default)
```

Cancels all reminders for the current entity.

#### List Reminders
```csharp
Task<FetchedReminders> ListRemindersAsync(
    CancellationToken cancellationToken = default)
```

Lists all active reminders for the current entity.

#### Acknowledge Reminder
```csharp
Task<ReminderAckResponse> AckAsync(
    ReminderEnvelope envelope,
    CancellationToken cancellationToken = default)
```

Acknowledges receipt of a delivered reminder. Must be called after processing a `ReminderEnvelope<T>`. If this call faults or times out, the scheduler will redeliver the reminder after `AckTimeout` elapses, subject to the occurrence deadline.

#### Reject Reminder

```csharp
var nack = await client.NackAsync(envelope, "Downstream service was unavailable");
if (nack.ResponseCode == ReminderNackResponseCode.RetryScheduled)
{
    Log.Info("Retry {0} is scheduled for {1}", nack.AttemptCount, nack.NextAttemptAtUtc);
}
```

`NackAsync` uses the current retry budget, deadline, and exponential backoff.
It returns `Failed` or `Expired` when the occurrence cannot retry.

#### Get Occurrence Status

```csharp
var status = await client.GetOccurrenceStatusAsync(
    envelope.Key,
    envelope.DueTimeUtc);
```

The status query returns terminal rows until normal pruning removes them.
Official storage providers support status queries without a schema migration.

For 0.7 recurring reminders, custom storage providers must implement `IConditionalReminderMutationStorage` and
honor the insert-only successors and active-only completions in `ReminderMutationBatch` atomically. Existing
`IReminderStorage` providers continue to support one-off reminders. See [the storage contract](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/docs/design/failure-modes.md#custom-storage-providers).

The new delivery-control messages use new serializer manifests. Upgrade the
reminder scheduler before a consumer calls this API. A 0.6 scheduler cannot
read the 0.7 negative acknowledgement or status messages.
Custom providers must implement the new `IReminderStorage` query operations.

`MaxDeliveryAttempts` applies to one occurrence. A recurring reminder creates a
new occurrence with a new retry budget. A failed occurrence does not disable the recurring reminder.

### Acknowledgement Protocol

Reminders are wrapped in `ReminderEnvelope<T>` before delivery. The envelope carries the original payload alongside the `ReminderEntity`, `ReminderKey`, occurrence `DueTimeUtc`, and a non-null `Deadline` value object.

The `Deadline` tells the recipient how long the current delivery is relevant:

- **When another retry is possible** (attempts remain and the next backoff fits within the occurrence deadline): the deadline equals the ack timeout for this attempt (`now + AckTimeout`). A new delivery will replace this one when the deadline passes.
- **When this is the final attempt** (max attempts reached, or backoff would exceed the occurrence deadline): the deadline equals the occurrence-level delivery deadline.
- **When there is no occurrence deadline and no more retries**: the deadline is `ReminderDeadline.Infinite`.

Use `envelope.Deadline.TimeRemaining()` to know how long you have, or `envelope.Deadline.IsExpired()` to check whether a new delivery has likely already replaced this one.

**Receiving and acknowledging a reminder:**

```csharp
ReceiveAsync<ReminderEnvelope<DoSomething>>(async envelope =>
{
    // Drop stale work before doing side effects.
    if (envelope.Deadline.IsExpired())
    {
        await _reminders.AckAsync(envelope);
        return;
    }

    // Process the reminder payload first (idempotently).
    // Use DueTimeUtc as your occurrence identity for dedupe.
    await DoWork(envelope.Message);

    // Then acknowledge. If this fails, the reminder is retried.
    var ack = await _reminders.AckAsync(envelope);
    if (ack.ResponseCode != ReminderAckResponseCode.Success)
    {
        // Log the failure; the scheduler will retry after AckTimeout
        Log.Warning("Ack failed: {0}", ack.Message);
    }
});
```

**What happens on each outcome:**

| Outcome | Scheduler behavior |
|---------|-------------------|
| `AckAsync` succeeds | Current occurrence marked Delivered |
| `NackAsync` succeeds | Current attempt fails and the normal retry policy runs immediately |
| `AckAsync` times out or returns Error | Reminder retried after `AckTimeout` (default: 10s) with exponential backoff while it is still before the occurrence deadline |
| Retry attempts exhausted (`MaxDeliveryAttempts`) | Reminder marked Failed |
| Reminder exceeds its deadline | Reminder marked Expired and will not be retried |
| Ack received for a stale or superseded occurrence | Scheduler returns `NotFound`; the ack is a harmless no-op |

Because duplicates are possible whenever an ack is lost or times out, **consumers must be idempotent**.
For recurring reminders, dedupe by `(ReminderEntity, ReminderKey, DueTimeUtc)`.

### Response Codes

**ReminderScheduleResponseCode:**
- `Success`: Reminder scheduled successfully
- `ShardRegionNotFound`: Target shard region doesn't exist
- `Error`: Unexpected error occurred

**ReminderCancelResponseCode:**
- `Success`: Reminder(s) cancelled successfully
- `NotFound`: Reminder not found
- `Error`: Unexpected error occurred

### Reminder States

Reminders progress through the following states:
- **Pending**: Scheduled but not yet delivered
- **AwaitingAck**: Delivered via `Tell`; waiting for recipient to call `AckAsync`
- **Delivered**: Recipient acknowledged the occurrence
- **Failed**: Permanently failed after `MaxDeliveryAttempts` exhausted
- **Expired**: Deadline passed before the occurrence could be successfully acknowledged
- **Cancelled**: Manually cancelled by user

## Testing

### Local Reminders for Fast Testing

For unit and integration tests, use `WithLocalReminders()` to avoid the 30+ second ClusterSingleton bootstrap delay:

```csharp
using Akka.Hosting;
using Akka.Reminders;

public class ReminderTests : Akka.Hosting.TestKit.TestKit
{
    private readonly TestShardRegionResolver _resolver = new();

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // Configure local reminders - starts instantly without cluster
        builder.WithLocalReminders(reminders => reminders
            .WithInMemoryStorage()
            .WithResolver(_resolver)
            .WithSettings(new ReminderSettings
            {
                MaxSlippage = TimeSpan.FromMilliseconds(100),
                MaxDeliveryAttempts = 3
            }));
    }

    [Fact]
    public async Task Reminder_ShouldDeliver_ToRegisteredShardRegion()
    {
        // Arrange
        var targetActor = CreateTestProbe("billing-actor");
        _resolver.RegisterShardRegion("billing-shard", targetActor);

        var client = Sys.ReminderClient().CreateClient("billing-shard", "customer-123");

        // Act - Schedule reminder
        await client.ScheduleSingleReminderAsync(
            new ReminderKey("retry-payment"),
            DateTimeOffset.UtcNow.AddMilliseconds(200),
            new PaymentRetry());

        // Assert - Reminder delivered wrapped in ReminderEnvelope
        var shardEnvelope = targetActor.ExpectMsg<ShardingEnvelope>(TimeSpan.FromSeconds(2));
        Assert.Equal("customer-123", shardEnvelope.EntityId);
        var reminderEnvelope = Assert.IsType<ReminderEnvelope<PaymentRetry>>(shardEnvelope.Message);
    }
}
```

**Key Differences:**

| Feature | `WithReminders()` (Production) | `WithLocalReminders()` (Testing) |
|---------|-------------------------------|----------------------------------|
| **Startup Time** | 30+ seconds (ClusterSingleton bootstrap) | Instant (regular actor) |
| **Clustering** | Required | Not required |
| **Shard Resolver** | Uses real ClusterSharding | Manual registration via `TestShardRegionResolver` |
| **Storage** | Configurable (SQL, in-memory) | Defaults to in-memory |
| **Use Case** | Production deployments | Unit/integration tests |

**Why Use Local Reminders?**

- ✅ **No Cluster Bootstrap Delay**: Tests start instantly
- ✅ **Simple Setup**: Register test probes as shard regions
- ✅ **Full Functionality**: All reminder features work in local mode
- ✅ **Better Test Isolation**: No shared cluster state between tests

### Time-Based Testing with TestScheduler

Akka.Reminders uses the `ITimeProvider` abstraction (mapped to `IScheduler`) for time operations, making it fully testable with `TestScheduler`:

```csharp
[Fact]
public async Task Reminder_should_fire_at_scheduled_time()
{
    // Arrange
    var testScheduler = new TestScheduler();
    var system = ActorSystem.Create("test", config);

    // Schedule reminder
    var reminderTime = testScheduler.Now.AddMinutes(5);
    await client.ScheduleSingleReminderAsync(
        new ReminderKey("test"),
        reminderTime,
        new TestMessage());

    // Act - Advance time
    testScheduler.Advance(TimeSpan.FromMinutes(5));

    // Assert - Reminder fires (wrapped in ReminderEnvelope)
    testProbe.ExpectMsg<ReminderEnvelope<TestMessage>>();
}
```

## Native AOT

Local mode (`WithLocalReminders`) works under .NET Native AOT. All five packages are marked `IsAotCompatible` and produce no trim or AOT warnings of their own. The repository's AOT canary ([src/aot/Akka.Reminders.AOT.App](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/reminders/src/aot/Akka.Reminders.AOT.App)) has an independent `Native AOT canary (linux-x64)` CI job: it publishes with `PublishAot`, schedules, delivers and acks reminders with in-memory and SQLite storage, and restarts to deliver a reminder restored from the SQLite file.

Clustered mode (`WithReminders`, which runs the scheduler as a cluster singleton and delivers through Cluster.Sharding) is **not supported** under Native AOT yet, because Akka.Cluster itself is not AOT-ready in Akka.NET 1.6.

Start with the [Akka.NET v1.6 getting started guide](https://github.com/akkadotnet/akka.net/blob/1.6.0-beta3/GETTING_STARTED_V1.6.md), also linked from Akka.NET's 1.6 release notes. Its [Native AOT](https://github.com/akkadotnet/akka.net/blob/1.6.0-beta3/GETTING_STARTED_V1.6.md#native-aot-and-trimming) and [source-generated serializer](https://github.com/akkadotnet/akka.net/blob/1.6.0-beta3/GETTING_STARTED_V1.6.md#source-generated-serializer-akkaserializationv2) sections explain the runtime and serialization setup.

### Register your message types

The scheduler delivers each reminder as a `ReminderEnvelope<T>`, where `T` is the runtime type of the message. On the JIT it builds that type by reflection. Native AOT cannot, so register every message type you schedule:

```csharp
builder
    .WithCustomSerializer("billing", [typeof(PaymentRetry)], system => new BillingSerializer(system))
    .WithLocalReminders(reminders => reminders
        .WithReminderMessage<PaymentRetry>()
        .WithStorage(system => new SqliteReminderStorage(
            SqliteReminderStorageSettings.Create("Data Source=reminders.db"), system))
        .WithResolver(_ => resolver));
```

- Register the exact runtime type. A registration for a base type does not cover subclasses.
- This also covers reminders restored from storage after a restart, which no code in the new process ever scheduled.
- If a type is not registered, `ScheduleSingleReminderAsync` and `ScheduleRecurringReminderAsync` return `ReminderScheduleResponseCode.Error` with a message that names the type and the `WithReminderMessage<T>()` call, and nothing is stored. A stored reminder whose type is not registered logs an error at delivery and follows the normal ack-timeout retry path.
- On the JIT, registration is optional and changes nothing: unregistered types still go through reflection.
- Outside Akka.Hosting, call `ReminderMessageTypes.Register<PaymentRetry>()` at startup. Registration is process-wide and safe to repeat.

### Use an AOT-compatible payload serializer

Akka.NET turns its `Akka.DynamicTypeLoading` feature switch off for AOT publishes. **Reflection-based serializers do not work under Native AOT**, including Akka.NET's default Newtonsoft.Json fallback, which is unavailable with the switch off. SQL storage serializes each reminder's payload through Akka.NET serialization; in-memory storage does not serialize payloads.

For durable storage, use one of these options:

- Akka.NET 1.6's source-generated serializer in `Akka.Serialization.V2`, which generates MessagePack serialization code at compile time.
- An AOT-compatible built-in Akka.NET serializer, where it supports your payload type.
- An AOT-compatible custom serializer, registered with `WithCustomSerializer` as in the example above. For example, a `SerializerWithStringManifest` implementation can map stable manifest strings to generated Protobuf parsers explicitly.

Using `SerializerWithStringManifest` or Protobuf alone is not sufficient: the implementation must avoid runtime type discovery and code generation. Resolve manifests explicitly instead of relying on `Type.GetType` to reconstruct a payload type.

`WithReminderMessage<T>()` only registers the `ReminderEnvelope<T>` factory. It does not register a payload serializer or change the serializer ID, manifest, or bytes of existing stored reminders.

**Check existing stored reminders before switching to Native AOT.** The 1.5-to-1.6 read compatibility applies to the ordinary JIT runtime with dynamic type loading enabled. Old rows written through the JSON fallback are not automatically readable under AOT. Provide an AOT-compatible reader for their existing serializer IDs, manifests, and payload format, or migrate them before switching the store to Native AOT. Registering a different serializer for future writes does not migrate old rows.

To test this behavior on the JIT before you publish, turn the switch off in your project file:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Akka.DynamicTypeLoading" Value="false" Trim="true" />
</ItemGroup>
```

### Environment notes

- **SQL Server**: `Microsoft.Data.SqlClient` needs `<InvariantGlobalization>false</InvariantGlobalization>` under Native AOT.
- **Expected warnings**: an AOT publish reports about 70 trim/AOT warnings from Akka.Remote, Akka.Cluster, Akka.DistributedData and Akka.Persistence. They appear because this package references Akka.Cluster.Hosting; local mode does not reach that code. [src/aot/Akka.Reminders.AOT.App/aot-warnings.baseline.txt](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/src/aot/Akka.Reminders.AOT.App/aot-warnings.baseline.txt) lists them.

## Architecture

### Components

- **ReminderScheduler**: Cluster singleton actor that manages reminder scheduling and delivery
- **ReminderClient**: Client API for scheduling and managing reminders
- **ReminderClientExtension**: ActorSystem extension for accessing the reminder system
- **IReminderStorage**: Pluggable storage abstraction for persistence backends
- **IShardRegionResolver**: Resolves shard region names to actor references

### Message Flow

1. Entity actor requests reminder via `IReminderClient`
2. Request routed to ReminderScheduler singleton (via cluster singleton proxy)
3. Reminder persisted to storage backend
4. Scheduler tracks next due reminder time
5. When due, the scheduler persists the occurrence's delivery state (and any next recurring occurrence) before sending
6. Message wrapped in `ReminderEnvelope<T>` and delivered to the target shard region via `Tell`
7. Recipient calls `IReminderClient.AckAsync(envelope)` to confirm receipt for that specific `DueTimeUtc`
8. If ack times out: delivery retried with exponential backoff while the occurrence remains before deadline
9. Recurring reminders are latest-only; older occurrences expire when the next occurrence is delivered or becomes due
10. Completed/cancelled/expired reminders are pruned periodically based on PruneInterval setting

### Reliability Features

- **At-least-once delivery with acknowledgement**: Reminders are wrapped in `ReminderEnvelope<T>` and delivered via `Tell`. Recipients call `IReminderClient.AckAsync(envelope)` to confirm receipt. Unacknowledged reminders are retried with exponential backoff until they expire or exhaust attempts. Consumers must be idempotent.
- **Durable persistence**: Reminders survive actor restarts and cluster failures
- **Deadline-bounded retries**: Failed deliveries retry with exponential backoff inside each occurrence's absolute delivery deadline
- **Cluster singleton**: Single scheduler instance with automatic failover
- **Occurrence identity**: Reminders track delivery attempts, deadlines, and due-time identity per occurrence
- **Periodic pruning**: Automatic cleanup of old completed/cancelled reminders
- **Write circuit breaker**: Automatically pauses batch delivery when database writes fail, probing with a single reminder until writes recover. Prevents duplicate delivery storms during database outages.
- **Bounded first-failure blast radius**: Interleaved deliver/persist chunking caps duplicate deliveries on the first failed tick to `DeliveryCommitChunkSize`.

For a detailed analysis of failure modes and design trade-offs, see [Failure Modes and Design Decisions](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/docs/design/failure-modes.md).

## Design Documents

- [Failure Modes and Design Decisions](https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/docs/design/failure-modes.md) - Delivery semantics, threat model, circuit breaker design, and accepted trade-offs

## Building from Source

```bash
# Restore tools
dotnet tool restore

# Build
dotnet build

# Run tests
dotnet test

# Pack NuGet package
dotnet pack -c Release
```

## Contributing

Contributions are welcome! Please ensure:
- All tests pass
- Code follows existing style
- New features include tests
- Public APIs are documented

## License

Apache 2.0 License - see LICENSE file for details.
