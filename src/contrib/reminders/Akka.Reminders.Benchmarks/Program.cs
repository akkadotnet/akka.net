using System.Reflection;
using Akka.Reminders.Benchmarks;
using BenchmarkDotNet.Running;
using Npgsql;

if (args.Length > 0 && args[0] == "--scheduler-throughput")
{
    if (args.Length < 4)
        throw new ArgumentException("Usage: --scheduler-throughput <one-off|due-recurring|early-recurring|recurring-retry> <count> <repeats> [status-read-latency-ms]");
    await SchedulerThroughput.RunAsync(args);
    return;
}

Console.WriteLine("Akka.Reminders Benchmarks");
Console.WriteLine("-------------------------");
Console.WriteLine("IMPORTANT: Make sure PostgreSQL is running via 'docker compose up -d' before running benchmarks.");
Console.WriteLine();

// Try to connect to PostgreSQL to give early warning
try
{
    await using var conn = new NpgsqlConnection(SqlReminderBenchmarkBase.ConnectionString);
    await conn.OpenAsync();
}
catch (Exception)
{
    Console.WriteLine("ERROR: Could not connect to the configured PostgreSQL benchmark database.");
    Console.WriteLine("Please make sure PostgreSQL is running via 'docker compose up -d'");
    return;
}

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
