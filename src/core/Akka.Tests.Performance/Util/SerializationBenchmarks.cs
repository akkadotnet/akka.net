﻿// -----------------------------------------------------------------------
//  <copyright file="SerializationBenchmarks.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Routing;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Util;

public class SerializationBenchmarks
{
    private const string CreateThroughputCounter = "FindSerializerForTypeThroughput";
    private Counter _findSerializerForTypeThroughput;
    private Serialization.Serialization _serialization;
    protected ActorSystem System;

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
        for (var i = 0; i < 1000000; i++)
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