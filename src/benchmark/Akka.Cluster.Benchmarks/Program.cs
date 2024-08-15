// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Akka.Cluster.Benchmarks;

internal class Program
{
    private static void Main(string[] args)
    {
#if (DEBUG)
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
            .Run(args, new DebugInProcessConfig());
#else
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
#endif
    }
}