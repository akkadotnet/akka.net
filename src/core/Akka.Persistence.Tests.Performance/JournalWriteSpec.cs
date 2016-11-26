using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using NBench;

namespace Akka.Persistence.Tests.Performance
{
    public abstract class JournalWriteSpec
    {
        #region internals 

        private const int TenSeconds = 10000;
        private const int EventCount = 100000;

        class CompletionActor : ReceiveActor
        {
            public CompletionActor(int state, TaskCompletionSource<int> completion)
            {
                Receive<WriteMessageSuccess>(success =>
                {
                    state--;
                    if (state == 0) completion.SetResult(0);
                });
                Receive<WriteMessagesSuccessful>(_ => { });
                Receive<WriteMessagesFailed>(failed => completion.SetException(failed.Cause));
                Receive<WriteMessageFailure>(failure => completion.SetException(failure.Cause));
            }
        }

        #endregion

        #region init

        private ActorSystem _system;
        private IActorRef _journalRef;
        private IActorRef _pref;
        private WriteMessages[] _messages;
        private Task _finished;

        [PerfSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("perf-system", Configuration);
            _journalRef = Persistence.Instance.Apply(_system).JournalFor(null);
            _messages = new WriteMessages[EventCount];

            var completion = new TaskCompletionSource<int>();
            _pref = _system.ActorOf(Props.Create(() => new CompletionActor(EventCount, completion)));

            for (int i = 1; i <= EventCount; i++)
            {
                var e = new IPersistentEnvelope[] {new AtomicWrite(new Persistent("e-"+i, i, "p-1", sender: _pref)),};
                _messages[i] = new WriteMessages(e, _pref, 1);
            }

            _finished = completion.Task;
        }

        [PerfCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        #endregion
        
        protected abstract Config Configuration { get; }

        [PerfBenchmark]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = TenSeconds)]
        public void WriteMessagesThroughtput()
        {
            var i = 0;
            for (; i < EventCount; i++)
            {
                var msg = _messages[i];
                _journalRef.Tell(msg, _pref);
            }

            _finished.Wait();
        }
    }
}