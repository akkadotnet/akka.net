using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Perfolizer.Horology;

namespace Akka.Reminders.Benchmarks;

/// <summary>
/// Custom BenchmarkDotNet config for reminder benchmarks.
/// Uses reduced iterations since benchmarks involve real database I/O.
/// </summary>
public sealed class ReminderBenchmarkConfig : ManualConfig
{
    public ReminderBenchmarkConfig()
    {
        AddJob(Job.MediumRun
            .WithStrategy(RunStrategy.Monitoring)
            .WithWarmupCount(2)
            .WithIterationCount(5));

        AddExporter(MarkdownExporter.GitHub);
        AddColumn(new RemindersPerSecondColumn());
    }

    private sealed class RemindersPerSecondColumn : IColumn
    {
        public string Id => "RemindersPerSec";
        public string ColumnName => "Reminders/sec";
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Custom;
        public int PriorityInCategory => 0;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "Reminders processed per second (derived from ReminderCount parameter and mean time)";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            return GetValue(summary, benchmarkCase, SummaryStyle.Default);
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
        {
            var report = summary[benchmarkCase];
            if (report?.ResultStatistics == null)
                return "N/A";

            var meanNs = report.ResultStatistics.Mean;
            if (meanNs <= 0)
                return "N/A";

            // Read ReminderCount from the benchmark parameters to compute per-reminder throughput
            var reminderCountParam = benchmarkCase.Parameters.Items
                .FirstOrDefault(p => p.Name == "ReminderCount");
            if (reminderCountParam == null)
                return "N/A";

            var reminderCount = (int)reminderCountParam.Value;
            var meanSeconds = meanNs / 1_000_000_000.0;
            var remindersPerSecond = reminderCount / meanSeconds;
            return remindersPerSecond.ToString("N0");
        }

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public bool IsAvailable(Summary summary) => true;
    }
}
