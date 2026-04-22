//-----------------------------------------------------------------------
// <copyright file="Configs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Akka.Benchmarks.Configurations
{
    public class RequestsPerSecondColumn : IColumn
    {
        public string Id => nameof(RequestsPerSecondColumn);
        public string ColumnName => "Req/sec";

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase, null);
        public bool IsAvailable(Summary summary) => true;
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Custom;
        public int PriorityInCategory => -1;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "Requests per Second";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
        {
            var benchmarkAttribute = benchmarkCase.Descriptor.WorkloadMethod.GetCustomAttribute<BenchmarkAttribute>();
            var totalOperations = benchmarkAttribute?.OperationsPerInvoke ?? 1;

            if (!summary.HasReport(benchmarkCase)) 
                return "<not found>";
            
            var report = summary[benchmarkCase];
            var statistics = report?.ResultStatistics;
            if(statistics is null) 
                return "<not found>";
            
            var nsPerOperation = statistics.Mean;
            var operationsPerSecond = 1 / (nsPerOperation / 1e9);

            return operationsPerSecond.ToString("N2");  // or format as you like

        }
    }

    
    /// <summary>
    /// Basic BenchmarkDotNet configuration used for microbenchmarks.
    /// </summary>
    public class MicroBenchmarkConfig : ManualConfig
    {
        public MicroBenchmarkConfig()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddExporter(MarkdownExporter.GitHub);
            AddLogger(ConsoleLogger.Default);
        }
    }

    /// <summary>
    /// BenchmarkDotNet configuration for throughput benchmarks.
    /// Includes requests/sec column for easier comparison.
    /// </summary>
    public class ThroughputBenchmarkConfig : ManualConfig
    {
        public ThroughputBenchmarkConfig()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddExporter(MarkdownExporter.GitHub);
            AddLogger(ConsoleLogger.Default);
            AddColumn(new RequestsPerSecondColumn());
        }
    }

    /// <summary>
    /// BenchmarkDotNet configuration used for monitored jobs (not for microbenchmarks).
    /// </summary>
    public class MonitoringConfig : ManualConfig
    {
        public MonitoringConfig()
        {
            AddExporter(MarkdownExporter.GitHub);
            AddColumn(new RequestsPerSecondColumn());
        }
    }

    public class MacroBenchmarkConfig : ManualConfig
    {
        public MacroBenchmarkConfig()
        {
            AddExporter(MarkdownExporter.GitHub);
            AddColumn(new RequestsPerSecondColumn());
            AddColumn(new CategoriesColumn());
            AddLogger(ConsoleLogger.Default);
            
            int processorCount = Environment.ProcessorCount;
            IntPtr affinityMask = (IntPtr)((1 << processorCount) - 1);
            
            AddJob(Job.LongRun
                .WithGcMode(new GcMode { Server = true, Concurrent = true })
                .WithWarmupCount(3) // Reduced from 25
                .WithIterationCount(10) // Reduced from 50
                .RunOncePerIteration()
                .WithStrategy(RunStrategy.Monitoring)
                // .WithAffinity(affinityMask) // Optional
            );
        }
    }
}
