//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
