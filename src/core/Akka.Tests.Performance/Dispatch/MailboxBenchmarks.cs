//-----------------------------------------------------------------------
// <copyright file="MailboxBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Dispatch
{
    /// <summary>
    /// Tests the speed of just the <see cref="Mailbox"/>
    /// </summary>
    public class MailboxBenchmarks
    {
        protected ActorSystem System;
        protected Mailbox Mailbox;
        protected IActorRef TestActor;
        protected Counter MsgReceived;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            MsgReceived = context.GetCounter("MsgReceived");
            System = ActorSystem.Create("PerfSys");
            Action<IActorDsl> actor = d => d.ReceiveAny((o, c) =>
            {
                MsgReceived.Increment();
            });
            TestActor = System.ActorOf(Props.Create(() => new Act(actor)), "testactor");
            var id = TestActor.Ask<ActorIdentity>(new Identify(null), TimeSpan.FromSeconds(3)).Result;

            Mailbox = new Mailbox(new UnboundedMessageQueue());
            Mailbox.SetActor(TestActor.AsInstanceOf<RepointableActorRef>().Underlying.AsInstanceOf<ActorCell>());
        }

        [PerfBenchmark(NumberOfIterations = 10, TestMode = TestMode.Measurement, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1500)]
        [CounterMeasurement("MsgReceived")]
        public void MailboxRawRunPerf(BenchmarkContext context)
        {
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.Run();
        }

        [PerfBenchmark(NumberOfIterations = 10, TestMode = TestMode.Measurement, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1500)]
        [CounterMeasurement("MsgReceived")]
        public void MailboxBatchRunPerf(BenchmarkContext context)
        {
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.MessageQueue.Enqueue(TestActor, new Envelope("hit", ActorRefs.NoSender));
            Mailbox.Run();
        }

        [PerfBenchmark(NumberOfIterations = 10, TestMode = TestMode.Measurement, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1500)]
        [CounterMeasurement("MsgReceived")]
        public void MailboxCanBeScheduledForExecutionPerf(BenchmarkContext context)
        {
            Mailbox.CanBeScheduledForExecution(true, false);
            MsgReceived.Increment();
        }

        [PerfBenchmark(NumberOfIterations = 10, TestMode = TestMode.Measurement, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1500)]
        [CounterMeasurement("MsgReceived")]
        public void MailboxSetAsScheduledPerf(BenchmarkContext context)
        {
            Mailbox.SetAsScheduled();
            MsgReceived.Increment();
        }

        [PerfCleanup]
        public void TearDown()
        {
            System.Terminate().Wait();
        }
    }
}

