// -----------------------------------------------------------------------
//  <copyright file="GetMailboxTypeSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Dispatch;
using NBench;
using NBench.Util;

namespace Akka.Tests.Performance.Dispatch;

public class GetMailboxTypeSpec
{
    private const string CreateThroughputCounter = "GetMailboxTypeFootprint";
    private const int GetMailboxTypeNumber = 1000000;

    private static readonly AtomicCounter Counter = new();
    private Counter _createActorThroughput;
    private Mailboxes _mailboxes;
    private MessageDispatcher _messageDispatcher;

    private ActorSystem _system;

    [PerfSetup]
    public void Setup(BenchmarkContext context)
    {
        _system = ActorSystem.Create($"GetMailboxTypeSpec{Counter.GetAndIncrement()}");
        _messageDispatcher = _system.Dispatchers.Lookup(EchoActor.Props.Dispatcher);
        _mailboxes = new Mailboxes(_system);
        _createActorThroughput = context.GetCounter(CreateThroughputCounter);
    }

    [PerfBenchmark(Description = "Measures the amount of memory and GC pressure on GetMailboxType method",
        RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
    [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
    [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
    [CounterMeasurement(CreateThroughputCounter)]
    public void GetMailboxType_memory_footprint(BenchmarkContext context)
    {
        for (var i = 0; i < GetMailboxTypeNumber; i++)
        {
            _mailboxes.GetMailboxType(EchoActor.Props, _messageDispatcher.Configurator.Config);
            _createActorThroughput.Increment();
        }
    }

    [PerfCleanup]
    public void Teardown(BenchmarkContext context)
    {
        _system.Terminate().Wait(TimeSpan.FromSeconds(2.0d));
        _system = null;
    }

    internal class EchoActor : UntypedActor
    {
        public static Props Props { get; } = Props.Create(() => new EchoActor());

        protected override void OnReceive(object message)
        {
            Sender.Tell(message);
        }
    }
}