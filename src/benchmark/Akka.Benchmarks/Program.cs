//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using System.Threading;
using Akka.Benchmarks.DData;
using BenchmarkDotNet.Running;

namespace Akka.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            //var w = new SerializerORDictionaryBenchmarks();
            //w.NumElements = 25;
            //w.NumNodes = 30;
            //w.SetupSystem();
            //Console.WriteLine("Running");
            //while (true)
            //{
            //    w.Serialize_ORDictionary();
            //}
            BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
        }
    }
}
