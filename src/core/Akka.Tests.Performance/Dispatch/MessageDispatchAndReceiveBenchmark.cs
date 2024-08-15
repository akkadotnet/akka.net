﻿// -----------------------------------------------------------------------
//  <copyright file="MessageDispatchAndReceiveBenchmark.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using NBench;

namespace Akka.Tests.Performance.Dispatch;

public class MessageDispatchAndReceiveBenchmark
{
    public static readonly Config Config = ConfigurationFactory.ParseString(@"
            calling-thread-dispatcher{
                executor=""" + typeof(CallingThreadExecutorConfigurator).AssemblyQualifiedName + @"""
                throughput = 100
            }
        ");

    protected Counter MsgReceived;
    protected ActorSystem System;
    protected IActorRef TestActor;

    [PerfSetup]
    public void Setup(BenchmarkContext context)
    {
        MsgReceived = context.GetCounter("MsgReceived");
        System = ActorSystem.Create("PerfSys", Config);
        Action<IActorDsl> actor = d => d.ReceiveAny((_, _) => { MsgReceived.Increment(); });
        TestActor = System.ActorOf(Props.Create(() => new Act(actor)).WithDispatcher("calling-thread-dispatcher"),
            "testactor");

        // force initialization of the actor
        TestActor.Tell("warmup");
        MsgReceived.Decrement();
    }

    [PerfBenchmark(NumberOfIterations = 10, TestMode = TestMode.Measurement, RunMode = RunMode.Throughput,
        RunTimeMilliseconds = 1500, Skip = "Causes StackoverflowExceptions when coupled with CallingThreadDispatcher")]
    [CounterMeasurement("MsgReceived")]
    public void ActorMessagesPerSecond(BenchmarkContext context)
    {
        TestActor.Tell("hit");
    }

    [PerfCleanup]
    public void TearDown()
    {
        System.Terminate().Wait();
    }
}