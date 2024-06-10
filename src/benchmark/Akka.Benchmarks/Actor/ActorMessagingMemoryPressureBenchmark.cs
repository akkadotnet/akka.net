//-----------------------------------------------------------------------
// <copyright file="ActorMessagingMemoryPressureBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Routing;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MonitoringConfig))]
    public class ActorMessagingMemoryPressureBenchmark
    {
        #region Classes
        public sealed class StopActor
        {
            private StopActor(){}
            public static readonly StopActor Instance = new();
        }
        
        public sealed class MyActor : ReceiveActor
        {
            public MyActor()
            {
                Receive<StopActor>(str =>
                {
                    Context.Stop(Self);
                    Sender.Tell(str);
                });
                
                Receive<string>(str =>
                {
                    Sender.Tell(str);
                });
            }
        }
        #endregion
        
        private ActorSystem _sys;
        private IActorRef _actorEntryPoint;

        private const string Msg = "hit";

        public const int MsgCount = 100_000;
        
        [Params(10, 100)]
        public int ActorCount { get; set; }

        private Task[] _askTasks;
        
        [GlobalSetup]
        public void Setup()
        {
            _sys = ActorSystem.Create("Bench", @"akka.log-dead-letters = off");
        }
        
        [GlobalCleanup]
        public async Task CleanUp()
        {
            await _sys.Terminate();
        }

        [IterationCleanup]
        public void PerInvokeCleanup()
        {
            _actorEntryPoint.GracefulStop(TimeSpan.FromSeconds(5)).Wait();
        }

        [IterationSetup]
        public void PerInvokeSetup()
        {
            _actorEntryPoint = _sys.ActorOf(Props.Create<MyActor>().WithRouter(new BroadcastPool(ActorCount)));
            _askTasks = new Task[MsgCount];
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = MsgCount)]
        public Task PushMsgs()
        {
            for (var i = 0; i < MsgCount; i++)
            {
               _actorEntryPoint.Tell(Msg);
            }

            return Task.CompletedTask;
        }
        
        [Benchmark(OperationsPerInvoke = MsgCount)]
        public Task AskMsgs()
        {
            for (var i = 0; i < MsgCount; i++)
            {
                _askTasks[i] = _actorEntryPoint.Ask<string>(Msg);
            }
            
            return Task.WhenAll(_askTasks);
        }
    }
}
