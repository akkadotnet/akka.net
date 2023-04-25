//-----------------------------------------------------------------------
// <copyright file="Configs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;

namespace Akka.Benchmarks.Configurations
{
    /// <summary>
    /// Basic BenchmarkDotNet configuration used for microbenchmarks.
    /// </summary>
    public class MicroBenchmarkConfig : ManualConfig
    {
        public MicroBenchmarkConfig()
        {
            this.Add(MemoryDiagnoser.Default);
            this.Add(MarkdownExporter.GitHub);
            AddLogger(ConsoleLogger.Default);
        }
    }

    /// <summary>
    /// BenchmarkDotNet configuration used for monitored jobs (not for microbenchmarks).
    /// </summary>
    public class MonitoringConfig : ManualConfig
    {
        public MonitoringConfig()
        {
            this.Add(MarkdownExporter.GitHub);
        }
    }
}
