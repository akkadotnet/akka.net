using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using NBench;

namespace Akka.Persistence.Performance.TestKit
{
    public abstract class RawJournalWriteSpec
    {
        #region internals 

        private const int Timeout = 30000;
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

        protected ActorSystem System;
        protected IActorRef JournalRef;
        private IActorRef _pref;
        private WriteMessages[] _messages;
        private Task _finished;

        [PerfSetup]
        public virtual void Setup()
        {
            System = ActorSystem.Create("RawJournalWriteSpec", Configuration);
            JournalRef = Persistence.Instance.Apply(System).JournalFor(null);
            _messages = new WriteMessages[EventCount];

            var completion = new TaskCompletionSource<int>();
            _pref = System.ActorOf(Props.Create(() => new CompletionActor(EventCount, completion)));

            for (int i = 1; i <= EventCount; i++)
            {
                var e = new IPersistentEnvelope[] {new AtomicWrite(new Persistent("e-"+i, i, "p-1", sender: _pref)),};
                _messages[i-1] = new WriteMessages(e, _pref, 1);
            }

            _finished = completion.Task;
        }

        [PerfCleanup]
        public virtual void Cleanup()
        {
            System.Dispose();
        }

        #endregion
        
        protected abstract Config Configuration { get; }

        [PerfBenchmark]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = Timeout)]
        public void WriteMessagesThroughtput()
        {
            for (var i = 0; i < EventCount; i++)
            {
                var msg = _messages[i];
                JournalRef.Tell(msg, _pref);
            }

            _finished.Wait();
        }
    }
}