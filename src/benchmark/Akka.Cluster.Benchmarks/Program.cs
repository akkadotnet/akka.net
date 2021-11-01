using System;
using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Akka.Cluster.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
#if (DEBUG)
            BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args, new DebugInProcessConfig());
#else
            BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
#endif

        }
    }
}