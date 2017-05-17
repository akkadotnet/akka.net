﻿//-----------------------------------------------------------------------
// <copyright file="RemoteMessagingThroughputSpecBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        private const int RemoteMessageCount = 50000;
        private Counter _remoteMessageThroughput;
        private readonly ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);
        private IActorRef _receiver;
        private IActorRef _echo;
        private IActorRef _remoteEcho;
        private IActorRef _remoteReceiver;

        private static readonly AtomicCounter ActorSystemNameCounter = new AtomicCounter(0);
        protected ActorSystem System1;
        protected ActorSystem System2;

        protected static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(8);

        private class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }

        private class BenchmarkActor : ReceiveActor
        {
            private readonly Counter _counter;
            private readonly long _maxExpectedMessages;
            private long _currentMessages = 0;
            private readonly ManualResetEventSlim _resetEvent;

            public BenchmarkActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent)
            {
                _counter = counter;
                _maxExpectedMessages = maxExpectedMessages;
                _resetEvent = resetEvent;
                ReceiveAny(o =>
                {
                    _counter.Increment();
                    if (++_currentMessages == _maxExpectedMessages)
                        _resetEvent.Set();
                });
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
            _echo = System1.ActorOf(Props.Create(() => new EchoActor()), "echo");

            System2 = ActorSystem.Create("SystemB" + ActorSystemNameCounter.Next(), CreateActorSystemConfig("SystemB" + ActorSystemNameCounter.Current, "127.0.0.1", 0));
            _receiver =
                System2.ActorOf(
                    Props.Create(() => new BenchmarkActor(_remoteMessageThroughput, RemoteMessageCount, _resetEvent)),
                    "benchmark");

            var system1Address = RARP.For(System1).Provider.Transport.DefaultAddress;
            var system2Address = RARP.For(System2).Provider.Transport.DefaultAddress;

            var system1EchoActorPath = new RootActorPath(system1Address) / "user" / "echo";
            var system2RemoteActorPath = new RootActorPath(system2Address) / "user" / "benchmark";

            try
            {
                // set the timeout high here to avoid timeouts
                // TL;DR; - on slow machines it can take longer than 2 seconds to form the association, do the handshake, and reply back
                // using the in-memory transport.
                _remoteReceiver =
                    System1.ActorSelection(system2RemoteActorPath).ResolveOne(TimeSpan.FromSeconds(30)).Result;
                _remoteEcho =
                    System2.ActorSelection(system1EchoActorPath).ResolveOne(TimeSpan.FromSeconds(2)).Result;
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
        public void OneWay(BenchmarkContext context)
        {
            Parallel.For(0, RemoteMessageCount, _ => _remoteReceiver.Tell("foo")); // send a remote message

            if (!_resetEvent.Wait(WaitTimeout))
            {
                context.Trace.Warning($"Timed out after {WaitTimeout}s");
            } 
        }

        [PerfBenchmark(
           Description =
               "Measures the throughput of Akka.Remote over a particular transport using two-way messaging",
           RunMode = RunMode.Iterations, NumberOfIterations = 3, TestMode = TestMode.Measurement,
           RunTimeMilliseconds = 1000)]
        [CounterMeasurement(RemoteMessageCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void TwoWay(BenchmarkContext context)
        {

            Parallel.For(0, RemoteMessageCount, _ => _remoteEcho.Tell("foo", _receiver)); // send a remote message

            if (!_resetEvent.Wait(WaitTimeout))
            {
                context.Trace.Warning($"Timed out after {WaitTimeout}s");
            }
        }

        [PerfCleanup]
        public virtual void Cleanup()
        {
            _resetEvent.Dispose();
            System1.Terminate().Wait();
            System2.Terminate().Wait();
        }
    }
}