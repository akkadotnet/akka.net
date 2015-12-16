using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Internal;
using Google.ProtocolBuffers;
using NBench;

namespace Akka.Remote.Tests.Performance
{
    /// <summary>
    /// Specs used to verify the performance of the <see cref="IInboundMessageDispatcher"/>,
    /// one of the potential bottlenecks inside the remoting system
    /// </summary>
    public class InboundMessageDispatcherSpec
    {
        private const string MessageDispatcherThroughputCounterName = "InboundMessageDispatch";
        private SerializedMessage _message;

        private Counter _inboundMessageDispatcherCounter;
        private static readonly AtomicCounter Counter = new AtomicCounter(0);

        private ActorSystem _actorSystem;
        private Address _systemAddress;
        private IInboundMessageDispatcher _dispatcher;

        private IInternalActorRef _targetActorRef;

        /// <summary>
        /// Not thread-safe, but called by a single thread in the benchmark
        /// </summary>
        private class BenchmarkActorRef : MinimalActorRef
        {
            private readonly Counter _counter;

            public BenchmarkActorRef(Counter counter)
            {
                _counter = counter;
            }

            protected override void TellInternal(object message, IActorRef sender)
            {
                _counter.Increment();
            }

            public override ActorPath Path { get { return null; } }
            public override IActorRefProvider Provider { get { return null; } }
        }

        private static readonly Config RemoteHocon = ConfigurationFactory.ParseString(@"
             akka {
              actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""

              remote {
                log-remote-lifecycle-events = off

                enabled-transports = [
                  ""akka.remote.test"",
                ]

                test {
                  transport-class = ""Akka.Remote.Transport.TestTransport,Akka.Remote""
                  applied-adapters = []
                  registry-key = aX33k12WKg
                  maximum-payload-bytes = 128000b
                  scheme-identifier = test
                  local-address = ""test://MessageDispatcher@0.0.0.0:1111""
                }
              }
        ");

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _actorSystem = ActorSystem.Create("MessageDispatcher" + Counter.GetAndIncrement(), RemoteHocon);
            _systemAddress = RARP.For(_actorSystem).Provider.DefaultAddress;
            _inboundMessageDispatcherCounter = context.GetCounter(MessageDispatcherThroughputCounterName);
            _message = SerializedMessage.CreateBuilder().SetSerializerId(0).SetMessage(ByteString.CopyFromUtf8("foo")).Build();
            _dispatcher = new DefaultMessageDispatcher(_actorSystem, RARP.For(_actorSystem).Provider, _actorSystem.Log);
            _targetActorRef = new BenchmarkActorRef(_inboundMessageDispatcherCounter);
        }

        [PerfBenchmark(Description = "Tests the performance of the Default", RunMode = RunMode.Throughput, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [CounterMeasurement(MessageDispatcherThroughputCounterName)]
        public void DispatchThroughput(BenchmarkContext context)
        {
            _dispatcher.Dispatch(_targetActorRef, _systemAddress, _message);
        }

        [PerfCleanup]
        public void Cleanup()
        {
            _actorSystem.Shutdown();
            _actorSystem.TerminationTask.Wait();
            _actorSystem = null;
        }
    }
}
