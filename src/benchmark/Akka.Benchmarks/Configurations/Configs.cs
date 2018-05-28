//-----------------------------------------------------------------------
// <copyright file="Configs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;

namespace Akka.Benchmarks.Configurations
{
    public class MicroBenchmarkConfig : ManualConfig
    {
        public MicroBenchmarkConfig()
        {
            this.Add(new MemoryDiagnoser());
            this.Add(MarkdownExporter.GitHub);
        }
    }
    public class MonitoringConfig : ManualConfig
    {
        public MonitoringConfig()
        {
            this.Add(MarkdownExporter.GitHub);
        }
    }
}