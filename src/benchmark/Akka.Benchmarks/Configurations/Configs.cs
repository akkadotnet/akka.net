//-----------------------------------------------------------------------
// <copyright file="Configs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
#pragma warning disable CS0618 // Type or member is obsolete
            this.Add(MemoryDiagnoser.Default);
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
            this.Add(MarkdownExporter.GitHub);
#pragma warning restore CS0618 // Type or member is obsolete
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
#pragma warning disable CS0618 // Type or member is obsolete
            this.Add(MarkdownExporter.GitHub);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
