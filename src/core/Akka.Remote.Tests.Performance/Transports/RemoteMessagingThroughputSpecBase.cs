//-----------------------------------------------------------------------
// <copyright file="RemoteMessagingThroughputSpecBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Internal;
using NBench;

namespace Akka.Remote.Tests.Performance.Transports
{
    /// <summary>
    /// Used to test the performance of remote messaging in Akka.Remote using various transports
    /// </summary>
    public abstract class RemoteMessagingThroughputSpecBase
    {
        private const string RemoteMessageCounterName = "RemoteMessageReceived";
        private const int RemoteMessageCount = 10000;
        private const int NumClients = 10;
        private Counter _remoteMessageThroughput;
        private List<Task> _tasks = new List<Task>();
        private readonly List<IActorRef> _receivers = new List<IActorRef>();
        private IActorRef _echo;
        private IActorRef _remoteEcho;
        private IActorRef _remoteReceiver;

        private static readonly AtomicCounter ActorSystemNameCounter = new AtomicCounter(0);
        protected ActorSystem System1;
        protected ActorSystem System2;

        protected static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(8);

        private class AllStartedActor : UntypedActor
        {
            public class AllStarted { }

            private readonly HashSet<IActorRef> _actors = new HashSet<IActorRef>();
            private int _correlationId = 0;

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case IActorRef a:
                        _actors.Add(a);
                        break;
                    case AllStarted a:
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var s = Sender;
                        var count = _actors.Count;
                        var c = _correlationId++;
                        var t = Task.WhenAll(_actors.Select(
                            x => x.Ask<ActorIdentity>(new Identify(c), cts.Token)));
                        t.ContinueWith(tr =>
                        {
                            return tr.Result.Length == count && tr.Result.All(x => x.MessageId.Equals(c));
                        }, TaskContinuationOptions.OnlyOnRanToCompletion).PipeTo(s);
                        break;
                }
            }
        }

        private class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }

        private class BenchmarkActor : UntypedActor
        {
            private readonly Counter _counter;
            private readonly long _maxExpectedMessages;
            private readonly IActorRef _echo;
            private long _currentMessages = 0;
            private readonly TaskCompletionSource<long> _completion;

            public BenchmarkActor(Counter counter, long maxExpectedMessages, TaskCompletionSource<long> completion, IActorRef echo)
            {
                _counter = counter;
                _maxExpectedMessages = maxExpectedMessages;
                _completion = completion;
                _echo = echo;
            }
            protected override void OnReceive(object message)
            {
                if (_currentMessages < _maxExpectedMessages)
                {
                    _currentMessages++;
                    _echo.Tell(message);
                    _counter.Increment(); // TODO: need to add settable counter calls to NBench
                    _counter.Increment();
                }
                else
                {
                    _completion.TrySetResult(_maxExpectedMessages);
                }
            }
        }

        /// <summary>
        /// Used to create a HOCON <see cref="Config"/> object for each <see cref="ActorSystem"/>
        /// participating in this throughput test.
        /// 
        /// This method is responsible for selecting the correct <see cref="Transport.Transport"/> implementation.
        /// </summary>
        /// <param name="actorSystemName">The name of the <see cref="ActorSystem"/>. Needed for <see cref="Transport.TestTransport"/>.</param>
        /// <param name="ipOrHostname">The address this system will be bound to</param>
        /// <param name="port">The port this system will be bound to</param>
        /// <returns>A populated <see cref="Config"/> object.</returns>
        public abstract Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port);

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _remoteMessageThroughput = context.GetCounter(RemoteMessageCounterName);
            System1 = ActorSystem.Create("SystemA" + ActorSystemNameCounter.Next(), CreateActorSystemConfig("SystemA" + ActorSystemNameCounter.Current, "127.0.0.1", 0));

            System2 = ActorSystem.Create("SystemB" + ActorSystemNameCounter.Next(), CreateActorSystemConfig("SystemB" + ActorSystemNameCounter.Current, "127.0.0.1", 0));

            var system1Address = RARP.For(System1).Provider.Transport.DefaultAddress;
            var system2Address = RARP.For(System2).Provider.Transport.DefaultAddress;

            var canStart = System2.ActorOf(Props.Create(() => new AllStartedActor()), "canStart");
            var echoProps = Props.Create(() => new EchoActor()).WithDeploy(new Deploy(new RemoteScope(system1Address)));

            for (var i = 0; i < NumClients; i++)
            {
                var echo = System2.ActorOf(echoProps, "echo" + i);
                var ts = new TaskCompletionSource<long>();
                _tasks.Add(ts.Task);
                var receiver =
                    System2.ActorOf(
                        Props.Create(() => new BenchmarkActor(_remoteMessageThroughput, RemoteMessageCount, ts, echo)),
                        "benchmark" + i);

                _receivers.Add(receiver);

                canStart.Tell(echo);
                canStart.Tell(receiver);
            }

            try
            {
                var testReady = canStart.Ask<bool>(new AllStartedActor.AllStarted(), TimeSpan.FromSeconds(10)).Result;
                if (!testReady)
                {
                    throw new NBenchException("Received report that 1 or more remote actor is unable to begin the test. Aborting run.");
                }
            }
            catch (Exception ex)
            {
                context.Trace.Error(ex, "error occurred during setup.");
                throw; // re-throw the error to blow up the benchmark
            }
        }

        [PerfBenchmark(
           Description =
               "Measures the throughput of Akka.Remote over a particular transport using one-way messaging",
           RunMode = RunMode.Iterations, NumberOfIterations = 3, TestMode = TestMode.Measurement,
           RunTimeMilliseconds = 1000)]
        [CounterMeasurement(RemoteMessageCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void PingPong(BenchmarkContext context)
        {
            _receivers.ForEach(c =>
            {
                c.Tell("hit");
            });
            var waiting = Task.WhenAll(_tasks);
            SpinWait.SpinUntil(() => waiting.IsCompleted); // TODO: would get more accurate results if we could AWAIT and not block here.
        }

        [PerfCleanup]
        public virtual void Cleanup()
        {
            System1.Terminate().Wait();
            System2.Terminate().Wait();
        }
    }
}
