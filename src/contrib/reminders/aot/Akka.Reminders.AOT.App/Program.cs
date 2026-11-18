// Native AOT canary for Akka.Reminders in local mode (WithLocalReminders).
//
// Runs with Akka.DynamicTypeLoading off, so every ReminderEnvelope<T> must come from a
// WithReminderMessage<T>() registration, never from reflection. Phases:
//
//   memory   in-memory storage: schedule -> deliver -> ack, and an unregistered type is rejected
//            at schedule time with the registration hint
//   sqlite   SQLite storage: schedule -> deliver -> ack, then schedule a second reminder and stop
//            before it is due
//   restore  a NEW process opens the same SQLite file; the second reminder is restored from storage
//            and delivered -> ack (the path that needs the registration most, since nothing in this
//            process ever scheduled it)
//
// With no arguments the app runs all three, starting itself again for "restore". Exit code 0 and a
// final "AOT CANARY OK" line mean success.

using System.Diagnostics;
using System.Text;
using Akka.Actor;
using Akka.Hosting;
using Akka.Reminders;
using Akka.Reminders.Sharding;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var timeout = TimeSpan.FromSeconds(30);

try
{
    switch (args.FirstOrDefault())
    {
        case null:
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"akka-reminders-aot-{Guid.NewGuid():N}.db");
            try
            {
                await RunMemoryAsync();
                await RunSqliteAsync(dbPath);
                await RunRestoreInNewProcessAsync(dbPath);
            }
            finally
            {
                foreach (var file in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                    File.Delete(file);
            }

            Console.WriteLine("AOT CANARY OK");
            return 0;
        }
        case "memory":
            await RunMemoryAsync();
            return 0;
        case "sqlite" when args.Length == 2:
            await RunSqliteAsync(args[1]);
            return 0;
        case "restore" when args.Length == 2:
            await RunRestoreAsync(args[1]);
            return 0;
        default:
            Console.Error.WriteLine("usage: Akka.Reminders.AOT.App [memory | sqlite <db> | restore <db>]");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"AOT CANARY FAILED: {ex}");
    return 1;
}

async Task RunMemoryAsync()
{
    await using var app = await CanaryApp.StartAsync(storage: null);

    var client = app.Client("memory-1");
    var envelope = await app.ScheduleAndReceiveAsync(client, "once", new PaymentRetry("memory"), timeout);
    Check(envelope.Message.Note == "memory", $"unexpected payload [{envelope.Message.Note}]");
    await AckAsync(client, envelope);

    // An unregistered type must fail when it is scheduled, not later at delivery time.
    var rejected = await client.ScheduleSingleReminderAsync(
        new ReminderKey("unregistered"), DateTimeOffset.UtcNow.AddSeconds(1), new NotRegistered());
    Check(rejected.ResponseCode == ReminderScheduleResponseCode.Error,
        $"unregistered type was accepted: {rejected.ResponseCode}");
    Check(rejected.Message?.Contains("WithReminderMessage<NotRegistered>()") == true,
        $"rejection does not name the registration call: {rejected.Message}");

    Console.WriteLine("[memory] schedule -> deliver -> ack OK; unregistered type rejected OK");
}

async Task RunSqliteAsync(string dbPath)
{
    await using var app = await CanaryApp.StartAsync(dbPath);

    var client = app.Client("sqlite-1");
    var envelope = await app.ScheduleAndReceiveAsync(client, "once", new PaymentRetry("sqlite"), timeout);
    Check(envelope.Message.Note == "sqlite", $"unexpected payload [{envelope.Message.Note}]");
    await AckAsync(client, envelope);

    // Due after this process has stopped; the restore phase delivers it.
    var scheduled = await client.ScheduleSingleReminderAsync(
        new ReminderKey("after-restart"), DateTimeOffset.UtcNow.AddSeconds(4), new PaymentRetry("restored"));
    Check(scheduled.ResponseCode == ReminderScheduleResponseCode.Success,
        $"schedule failed: {scheduled.ResponseCode} {scheduled.Message}");

    Console.WriteLine("[sqlite] schedule -> deliver -> ack OK; second reminder stored for restart");
}

async Task RunRestoreInNewProcessAsync(string dbPath)
{
    var self = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find own executable");
    var startInfo = new ProcessStartInfo(self) { UseShellExecute = false };

    // Under `dotnet run` the process is dotnet itself; pass the app dll along.
    if (Path.GetFileNameWithoutExtension(self) == "dotnet")
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Akka.Reminders.AOT.App.dll"));

    startInfo.ArgumentList.Add("restore");
    startInfo.ArgumentList.Add(dbPath);

    using var child = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start restore process");
    await child.WaitForExitAsync().WaitAsync(timeout * 2);
    Check(child.ExitCode == 0, $"restore process exited with {child.ExitCode}");
}

async Task RunRestoreAsync(string dbPath)
{
    await using var app = await CanaryApp.StartAsync(dbPath);

    // Nothing is scheduled here: the reminder comes from the SQLite file.
    var envelope = await app.Received.Task.WaitAsync(timeout);
    Check(envelope.Key.Name == "after-restart", $"unexpected reminder [{envelope.Key.Name}]");
    Check(envelope.Message.Note == "restored", $"unexpected payload [{envelope.Message.Note}]");
    await AckAsync(app.Client(envelope.Entity.EntityId), envelope);

    Console.WriteLine("[restore] reminder restored from SQLite -> deliver -> ack OK");
}

static async Task AckAsync(IReminderClient client, ReminderEnvelope envelope)
{
    var ack = await client.AckAsync(envelope);
    Check(ack.ResponseCode == ReminderAckResponseCode.Success, $"ack failed: {ack.ResponseCode} {ack.Message}");
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

/// <summary>
/// The reminder payload. Registered with WithReminderMessage and serialized by <see cref="PaymentRetrySerializer"/>.
/// </summary>
public sealed record PaymentRetry(string Note);

/// <summary>
/// Never registered: scheduling it must fail under Native AOT.
/// </summary>
public sealed record NotRegistered;

/// <summary>
/// The application's own serializer for its reminder payload. With Akka.DynamicTypeLoading off
/// there is no JSON fallback, so durable storage needs one.
/// </summary>
public sealed class PaymentRetrySerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
{
    public override int Identifier => 9301;

    public override string Manifest(object o) => o switch
    {
        PaymentRetry => "pr",
        _ => throw new ArgumentException($"Unsupported type [{o.GetType()}]", nameof(o))
    };

    public override byte[] ToBinary(object obj) => obj switch
    {
        PaymentRetry retry => Encoding.UTF8.GetBytes(retry.Note),
        _ => throw new ArgumentException($"Unsupported type [{obj.GetType()}]", nameof(obj))
    };

    public override object FromBinary(byte[] bytes, string manifest) => manifest switch
    {
        "pr" => new PaymentRetry(Encoding.UTF8.GetString(bytes)),
        _ => throw new System.Runtime.Serialization.SerializationException($"Unknown manifest [{manifest}]")
    };
}

/// <summary>
/// Stands in for a sharded entity: receives the typed envelope.
/// </summary>
public sealed class BillingActor : ReceiveActor
{
    public BillingActor(TaskCompletionSource<ReminderEnvelope<PaymentRetry>> received)
    {
        Receive<ReminderEnvelope<PaymentRetry>>(envelope => received.TrySetResult(envelope));
    }
}

/// <summary>
/// One Akka.Hosting application, wired the way an application using local reminders wires it.
/// </summary>
public sealed class CanaryApp : IAsyncDisposable
{
    private const string Region = "billing";

    private readonly IHost _host;
    private readonly ActorSystem _system;

    private CanaryApp(IHost host, TaskCompletionSource<ReminderEnvelope<PaymentRetry>> received)
    {
        _host = host;
        _system = host.Services.GetRequiredService<ActorSystem>();
        Received = received;
    }

    /// <summary>
    /// Completes with the first envelope the billing actor receives.
    /// </summary>
    public TaskCompletionSource<ReminderEnvelope<PaymentRetry>> Received { get; }

    public static async Task<CanaryApp> StartAsync(string? storage)
    {
        var resolver = new TestShardRegionResolver();
        var received = new TaskCompletionSource<ReminderEnvelope<PaymentRetry>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
        builder.Services.AddAkka("aot-reminders", akka =>
        {
            akka
                // Registered before the scheduler starts, so a reminder restored from storage
                // and due right away has somewhere to go.
                .WithActors((system, _) => resolver.RegisterShardRegion(
                    Region, system.ActorOf(Props.Create(() => new BillingActor(received)), "billing")))
                .WithCustomSerializer("payment-retry", [typeof(PaymentRetry)], system => new PaymentRetrySerializer(system))
                .WithLocalReminders(reminders =>
                {
                    reminders
                        .WithReminderMessage<PaymentRetry>()
                        .WithResolver(_ => resolver)
                        .WithSettings(new ReminderSettings
                        {
                            MaxSlippage = TimeSpan.FromMilliseconds(100),
                            StorageTimeout = TimeSpan.FromSeconds(5)
                        });

                    if (storage is null)
                    {
                        reminders.WithInMemoryStorage();
                    }
                    else
                    {
                        reminders.WithStorage(system => new SqliteReminderStorage(
                            SqliteReminderStorageSettings.Create($"Data Source={storage}"),
                            system));
                    }
                });
        });

        var host = builder.Build();
        await host.StartAsync();

        return new CanaryApp(host, received);
    }

    public IReminderClient Client(string entityId) => _system.ReminderClient().CreateClient(Region, entityId);

    public async Task<ReminderEnvelope<PaymentRetry>> ScheduleAndReceiveAsync(
        IReminderClient client, string key, PaymentRetry message, TimeSpan timeout)
    {
        var scheduled = await client.ScheduleSingleReminderAsync(
            new ReminderKey(key), DateTimeOffset.UtcNow.AddMilliseconds(300), message);
        if (scheduled.ResponseCode != ReminderScheduleResponseCode.Success)
            throw new InvalidOperationException($"schedule failed: {scheduled.ResponseCode} {scheduled.Message}");

        return await Received.Task.WaitAsync(timeout);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
