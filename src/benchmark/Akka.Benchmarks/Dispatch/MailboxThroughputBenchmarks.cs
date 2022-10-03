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

        private class CompletionActor : ReceiveActor
        {
            private readonly TaskCompletionSource<int> _tcs;
            private readonly int _target;
            private int _count = 0;

            public CompletionActor(int target, TaskCompletionSource<int> tcs)
            {
                _target = target;
                _tcs = tcs;

                ReceiveAny(_ =>
                {
                    if (++_count == _target)
                    {
                        _tcs.TrySetResult(0);
                    }
                });
            }
        }
        
        public static readonly Config Config = ConfigurationFactory.ParseString(@"
            calling-thread-dispatcher{
                executor=""" + typeof(CallingThreadExecutorConfigurator).AssemblyQualifiedName + @"""
                #throughput = 100
            }
        ");
        
        [Params(10_000, 100_000)] // higher values will cause the CallingThreadDispatcher to stack overflow
        public int MsgCount { get; set; }
        
        [Params(true, false)]
        public bool UseCallingThreadDispatcher { get; set; }
        
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
            var props = Props.Create(() => new CompletionActor(MsgCount, _completionSource));
            var finalProps = UseCallingThreadDispatcher ? props.WithDispatcher("calling-thread-dispatcher") : props;
            _testActor = _system.ActorOf(finalProps);

            var repointableActorRef = _testActor.AsInstanceOf<RepointableActorRef>();
            if (UseCallingThreadDispatcher) {
                // have to perform the work of the supervisor ourselves
                
                repointableActorRef.Point();
            }
            

            // have to force actor to start before we acquire cell
            var id = _testActor.Ask<ActorIdentity>(new Identify(null), TimeSpan.FromSeconds(3)).Result;
            
            _enqueueMailbox = new Mailbox(new UnboundedMessageQueue());
            _enqueueMailbox.SetActor(repointableActorRef.Underlying.AsInstanceOf<ActorCell>());

            _runMailbox = new Mailbox(new UnboundedMessageQueue());
            _runMailbox.SetActor(repointableActorRef.Underlying.AsInstanceOf<ActorCell>());
            
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
            await _completionSource.Task;
        }
    }
}