using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Dispatch
{
    /// <summary>
    ///     Base class used to test the performance of different <see cref="Mailbox" /> implementations
    /// </summary>
    public abstract class MailboxThroughputSpecBase
    {
        private const string MailboxCounterName = "MessageReceived";
        private static readonly Envelope TestEnvelope = new Envelope {Message = "", Sender = ActorRefs.NoSender};
        private ActorCell _actorCell;
        private MessageDispatcher _dispatcher;
        private Mailbox _mailbox;
        private Counter _mailboxThroughput;
        private IActorRef _receiver;
        protected abstract Mailbox CreateMailbox();

        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        private ActorSystem _system;

        class BenchmarkActor : UntypedActor
        {
            private Counter _counter;

            public BenchmarkActor(Counter counter)
            {
                _counter = counter;
            }

            protected override void OnReceive(object message)
            {
                _counter.Increment();
            }
        }

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _mailboxThroughput = context.GetCounter(MailboxCounterName);
            _mailbox = CreateMailbox();
            _system = ActorSystem.Create("MailboxThroughputSpecBase" + Counter.GetAndIncrement());
            _receiver = _system.ActorOf(Props.Create(() => new BenchmarkActor(_mailboxThroughput)));
            _actorCell = _receiver.AsInstanceOf<ActorRefWithCell>().Underlying.AsInstanceOf<ActorCell>();
            _dispatcher = _actorCell.Dispatcher;
            _mailbox.Setup(_actorCell.Dispatcher);
            _mailbox.SetActor(_actorCell);
            _mailbox.Start();
        }

        [PerfBenchmark(
            Description =
                "Measures the raw message throughput of a mailbox implementation with no additional configuration options",
            RunMode = RunMode.Throughput, NumberOfIterations = 13, TestMode = TestMode.Measurement,
            RunTimeMilliseconds = 1000)]
        [CounterMeasurement(MailboxCounterName)]
        public void Benchmark(BenchmarkContext context)
        {
            _receiver.Tell(string.Empty);
        }
    }
}