//-----------------------------------------------------------------------
// <copyright file="ReceiveOnlyBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Dispatch
{
    public class ReceiveOnlyBenchmark
    {
        protected ActorSystem System;
        protected IActorRef TestActor;
        protected Counter MsgReceived;
        protected ManualResetEventSlim ResetEvent = new ManualResetEventSlim(false);
        protected const int ExpectedMessages = 500000;


        public static readonly Config Config = ConfigurationFactory.ParseString(@"
            calling-thread-dispatcher{
                executor=""" + typeof(CallingThreadExecutorConfigurator).AssemblyQualifiedName + @"""
                throughput = 100
            }
        ");

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            MsgReceived = context.GetCounter("MsgReceived");
            System = ActorSystem.Create("PerfSys", Config);
            int count = 0;
            Action<IActorDsl> actor = d => d.ReceiveAny((o, c) =>
            {
                MsgReceived.Increment();
                count++;
                if(count == ExpectedMessages)
                    ResetEvent.Set();
            });
            TestActor = System.ActorOf(Props.Create(() => new Act(actor)).WithDispatcher("calling-thread-dispatcher"), "testactor");

            SpinWait.SpinUntil(() => TestActor.AsInstanceOf<RepointableActorRef>().IsStarted);

            // force initialization of the actor
            for(var i = 0; i < ExpectedMessages-1;i++)
                TestActor.AsInstanceOf<RepointableActorRef>().Underlying.AsInstanceOf<ActorCell>().Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.Nobody)); // queue all of the messages into the actor
        }

        [PerfBenchmark(NumberOfIterations = 10, TestMode = TestMode.Measurement, RunMode = RunMode.Iterations, RunTimeMilliseconds = 1000, Skip = "Causes StackoverflowExceptions when coupled with CallingThreadDispatcher")]
        [CounterMeasurement("MsgReceived")]
        public void ActorMessagesPerSecond(BenchmarkContext context)
        {
            TestActor.Tell("hit");
            ResetEvent.Wait();
        }

        [PerfCleanup]
        public void TearDown()
        {
            System.Terminate().Wait();
        }
    }
}

