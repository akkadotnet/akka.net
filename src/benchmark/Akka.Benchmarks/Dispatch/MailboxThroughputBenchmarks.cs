//-----------------------------------------------------------------------
// <copyright file="MailboxThroughput.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Util.Internal;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Dispatch
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class MailboxThroughputBenchmarks
    {
        private ActorSystem _system;
        private Mailbox _enqueueMailbox;
        private Mailbox _runMailbox;
        private IActorRef _testActor;
        private TaskCompletionSource<int> _completionSource;
        private Envelope _msg;
        
        public static readonly Config Config = ConfigurationFactory.ParseString(@"
            calling-thread-dispatcher{
                executor=""" + typeof(CallingThreadExecutorConfigurator).AssemblyQualifiedName + @"""
                throughput = 100
            }
        ");
        
        [Params(10_000)]
        public int MsgCount { get; set; }
        
        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("Bench", Config);
            _msg = new Envelope("hit", ActorRefs.NoSender);
        }

        [IterationSetup]
        public void IterationSetup()
        {
            _completionSource = new TaskCompletionSource<int>();
            _testActor = _system.ActorOf(act =>
            {
                var i = 0;
                act.ReceiveAny((o, context) =>
                {
                    i++;
                    _completionSource.TrySetResult(i);
                });
            }); // .WithDispatcher("calling-thread-dispatcher")
            
            _enqueueMailbox = new Mailbox(new UnboundedMessageQueue());
            _enqueueMailbox.SetActor(_testActor.AsInstanceOf<RepointableActorRef>().Underlying.AsInstanceOf<ActorCell>());

            _runMailbox = new Mailbox(new UnboundedMessageQueue());
            _runMailbox.SetActor(_testActor.AsInstanceOf<RepointableActorRef>().Underlying.AsInstanceOf<ActorCell>());
            
            for(var i = 0; i < MsgCount; i++)
                _runMailbox.MessageQueue.Enqueue(_testActor, _msg);
        }

        [IterationCleanup]
        public void Cleanup()
        {
            _testActor.GracefulStop(TimeSpan.FromSeconds(3)).Wait();
        }
        
        [Benchmark]
        public void EnqueuePerformance()
        {
            for(var i = 0; i < MsgCount; i++)
                _enqueueMailbox.MessageQueue.Enqueue(_testActor, _msg);
        }

        [Benchmark]
        public async Task RunPerformance()
        {
            _runMailbox.Run();
        }
    }
}