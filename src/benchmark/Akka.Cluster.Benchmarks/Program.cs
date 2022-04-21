using System;
using System.Reflection;
using Akka.Cluster.Benchmarks.Persistence;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Akka.Cluster.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
#if (DEBUG)
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
                .Run(args, new DebugInProcessConfig());
#else
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
#endif

        }
    }
}