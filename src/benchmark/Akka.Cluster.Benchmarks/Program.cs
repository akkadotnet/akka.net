using System;
using System.Reflection;
using BenchmarkDotNet.Running;

namespace Akka.Cluster.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
        }
    }
}