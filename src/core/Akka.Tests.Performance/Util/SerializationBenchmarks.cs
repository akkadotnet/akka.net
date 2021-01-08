//-----------------------------------------------------------------------
// <copyright file="SerializationBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Routing;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Util
{
    public class SerializationBenchmarks
    {
        protected ActorSystem System;
        private Serialization.Serialization _serialization;
        private Counter _findSerializerForTypeThroughput;
        private const string CreateThroughputCounter = "FindSerializerForTypeThroughput";

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            System = ActorSystem.Create("SerializationBenchmarks");
            _serialization = new Serialization.Serialization(System.AsInstanceOf<ExtendedActorSystem>());
            _findSerializerForTypeThroughput = context.GetCounter(CreateThroughputCounter);
        }

        [PerfBenchmark(RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void FindSerializerForTypePerf(BenchmarkContext context)
        {
            for (int i = 0; i < 1000000; i++)
            {
                _serialization.FindSerializerForType(typeof(Dictionary<string, int>));
                _serialization.FindSerializerForType(typeof(List<string>));
                _serialization.FindSerializerForType(typeof(RoundRobinPool));
                _serialization.FindSerializerForType(typeof(string));
                _serialization.FindSerializerForType(typeof(RoundRobinGroup));
                _serialization.FindSerializerForType(typeof(byte[]));
                _findSerializerForTypeThroughput.Increment();
            }
        }

        [PerfCleanup]
        public void TearDown()
        {
            System.Terminate().Wait();
        }
    }
}

